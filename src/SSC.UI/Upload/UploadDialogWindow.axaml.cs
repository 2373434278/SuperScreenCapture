using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Application.Upload;

namespace SSC.UI.Upload;

public partial class UploadDialogWindow : Window
{
    private readonly byte[] _data;
    private readonly string _fileName;
    private readonly UploadPipeline _pipeline;
    private readonly SettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly IReadOnlyList<UploadProviderInfo> _providers;
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private UploadSettings _upload;
    private UploadResult? _result;
    private CancellationTokenSource? _cancellation;

    public UploadDialogWindow(
        byte[] data,
        string fileName,
        UploadPipeline pipeline,
        SettingsService settings,
        ILocalizer localizer)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _providers = pipeline.Providers;
        _upload = settings.Current.Upload.Clone();

        InitializeComponent();
        ApplyLocalization();
        WireControls();
        ProviderBox.ItemsSource = _providers
            .Select(provider => _localizer[provider.DisplayNameKey])
            .ToArray();
        var providerIndex = 0;
        for (var index = 0; index < _providers.Count; index++)
        {
            if (_providers[index].Id == _upload.Provider)
            {
                providerIndex = index;
                break;
            }
        }

        ProviderBox.SelectedIndex = providerIndex;
        RebuildFields();
    }

    private void WireControls()
    {
        CloseButton.Click += (_, _) => Close();
        CancelButton.Click += (_, _) =>
        {
            if (_cancellation is not null && !_cancellation.IsCancellationRequested)
            {
                _cancellation.Cancel();
                return;
            }

            Close();
        };
        UploadButton.Click += (_, _) => _ = UploadAsync();
        CopyLinkButton.Click += (_, _) => _ = CopyLinkAsync();
        OpenLinkButton.Click += (_, _) => OpenLink();
        DeleteRemoteButton.Click += (_, _) => _ = DeleteRemoteAsync();
        ProviderBox.SelectionChanged += (_, _) =>
        {
            if (ProviderBox.SelectedIndex < 0 || ProviderBox.SelectedIndex >= _providers.Count)
            {
                return;
            }

            CaptureFields();
            _upload.Provider = _providers[ProviderBox.SelectedIndex].Id;
            RebuildFields();
        };
    }

    private void ApplyLocalization()
    {
        Title = _localizer["uploadTitle"];
        TitleText.Text = _localizer["uploadTitle"];
        NoteText.Text = _localizer["uploadNote"];
        ProviderLabel.Text = _localizer["uploadProvider"];
        EnableLabel.Text = _localizer["uploadEnable"];
        UploadButton.Content = _localizer["uploadStart"];
        CancelButton.Content = _localizer["uploadCancel"];
        CopyLinkButton.Content = _localizer["uploadCopyLink"];
        OpenLinkButton.Content = _localizer["uploadOpenLink"];
        DeleteRemoteButton.Content = _localizer["uploadDeleteRemote"];
        ResultTitle.Text = _localizer["uploadSucceeded"];
        EnableSwitch.OnContent = _localizer["on"];
        EnableSwitch.OffContent = _localizer["off"];
        EnableSwitch.IsChecked = _settings.Current.UploadEnabled;
    }

    private void RebuildFields()
    {
        _fields.Clear();
        FieldsPanel.Children.Clear();
        switch (_upload.Provider)
        {
            case "s3":
                AddField("uploadBucket", _upload.Bucket, value => _upload.Bucket = value);
                AddField("uploadRegion", _upload.Region, value => _upload.Region = value);
                AddField("uploadAccessKey", _upload.AccessKey, value => _upload.AccessKey = value);
                AddField("uploadSecretKey", _upload.SecretKey, value => _upload.SecretKey = value, secret: true);
                AddField("uploadPrefix", _upload.Prefix, value => _upload.Prefix = value);
                AddField("uploadEndpoint", _upload.Endpoint, value => _upload.Endpoint = value);
                break;
            case "custom":
                AddField("uploadEndpoint", _upload.Endpoint, value => _upload.Endpoint = value);
                AddField("uploadToken", _upload.Token, value => _upload.Token = value, secret: true);
                AddField("uploadFieldName", _upload.FieldName, value => _upload.FieldName = value);
                break;
            default:
                AddField("uploadClientId", _upload.ClientId, value => _upload.ClientId = value);
                break;
        }
    }

    private void AddField(string labelKey, string value, Action<string> assign, bool secret = false)
    {
        var box = new TextBox
        {
            Text = value,
            Classes = { "input" },
            PasswordChar = secret ? '•' : '\0'
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
        row.Children.Add(new TextBlock
        {
            Text = _localizer[labelKey],
            Classes = { "field" },
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        FieldsPanel.Children.Add(row);
        _fields[labelKey] = box;
        box.TextChanged += (_, _) => assign(box.Text ?? string.Empty);
    }

    private void CaptureFields()
    {
        foreach (var (key, box) in _fields)
        {
            var value = box.Text ?? string.Empty;
            switch (key)
            {
                case "uploadClientId":
                    _upload.ClientId = value;
                    break;
                case "uploadBucket":
                    _upload.Bucket = value;
                    break;
                case "uploadRegion":
                    _upload.Region = value;
                    break;
                case "uploadAccessKey":
                    _upload.AccessKey = value;
                    break;
                case "uploadSecretKey":
                    _upload.SecretKey = value;
                    break;
                case "uploadPrefix":
                    _upload.Prefix = value;
                    break;
                case "uploadEndpoint":
                    _upload.Endpoint = value;
                    break;
                case "uploadToken":
                    _upload.Token = value;
                    break;
                case "uploadFieldName":
                    _upload.FieldName = value;
                    break;
            }
        }
    }

    private async Task UploadAsync()
    {
        if (_cancellation is not null)
        {
            return;
        }

        CaptureFields();
        var current = _settings.Current;
        current.Upload = _upload;
        current.UploadEnabled = EnableSwitch.IsChecked == true;
        try
        {
            _settings.Save(current);
        }
        catch (SettingsValidationException exception)
        {
            StatusText.Text = exception.Message;
            return;
        }

        _cancellation = new CancellationTokenSource();
        Progress.IsVisible = true;
        UploadButton.IsEnabled = false;
        CancelButton.Content = _localizer["uploadCancel"];
        StatusText.Text = _localizer["uploadInProgress"];
        ResultPanel.IsVisible = false;

        try
        {
            _result = await _pipeline.UploadAsync(
                new UploadRequest(_data, _fileName, GuessContentType(_fileName)),
                cancellationToken: _cancellation.Token);
            UrlBox.Text = _result.Url;
            ResultPanel.IsVisible = true;
            ResultTitle.Text = _localizer["uploadSucceeded"];
            StatusText.Text = _localizer["uploadSucceeded"];
            DeleteRemoteButton.IsEnabled = _result.DeleteUrl is not null;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = _localizer["uploadCanceled"];
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            Progress.IsVisible = false;
            UploadButton.IsEnabled = true;
        }
    }

    private async Task CopyLinkAsync()
    {
        if (_result is null)
        {
            return;
        }

        var clipboard = Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(_result.Url);
        StatusText.Text = _localizer["uploadCopied"];
    }

    private void OpenLink()
    {
        if (_result is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_result.Url) { UseShellExecute = true });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task DeleteRemoteAsync()
    {
        if (_result is null)
        {
            return;
        }

        try
        {
            DeleteRemoteButton.IsEnabled = false;
            if (await _pipeline.DeleteAsync(_result, CancellationToken.None))
            {
                StatusText.Text = _localizer["uploadRemoteDeleted"];
                ResultPanel.IsVisible = false;
                _result = null;
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            DeleteRemoteButton.IsEnabled = true;
        }
    }

    private static string GuessContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            _ => "image/png"
        };
}
