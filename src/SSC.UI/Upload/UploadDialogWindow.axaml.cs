using System.Diagnostics;
using SSC.UI.Motion;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Application.Upload;

namespace SSC.UI.Upload;

/// <summary>
/// 上传分享对话框（原型 P7）。包含隐私提示、文件卡片、服务商切换、动态字段、
/// 进度、成功/失败状态与链接操作，所有上传都由用户显式触发。
/// </summary>
public partial class UploadDialogWindow : Window
{
    private readonly byte[] _data;
    private readonly string _fileName;
    private readonly UploadPipeline _pipeline;
    private readonly SettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly IReadOnlyList<UploadProviderInfo> _providers;
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private readonly Bitmap? _preview;
    private UploadSettings _upload;
    private UploadResult? _result;
    private CancellationTokenSource? _cancellation;

    /// <summary>可上传内容的字节与文件名由调用方提供。</summary>
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
        WindowingMotion.FadeIn(this);
        _preview = TryCreatePreview(data);
        PreviewImage.Source = _preview;
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
        RetryButton.Click += (_, _) => _ = UploadAsync();
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
        SubtitleText.Text = _localizer["uploadDialogDesc"];
        NoteText.Text = _localizer["uploadPrivacy"];
        ProviderLabel.Text = _localizer["uploadProvider"];
        EnableLabel.Text = _localizer["uploadEnable"];
        UploadText.Text = _localizer["uploadStart"];
        CancelButton.Content = _localizer["uploadCancel"];
        CopyLinkButton.Content = _localizer["uploadCopyLink"];
        OpenLinkButton.Content = _localizer["uploadOpenLink"];
        DeleteRemoteButton.Content = _localizer["uploadDeleteRemote"];
        RetryButton.Content = _localizer["uploadRetry"];
        ResultDescription.Text = _localizer["uploadSuccessDesc"];
        ProgressText.Text = _localizer["uploadInProgress"];
        FooterHint.Text = _localizer["uploadNote"];
        EnableSwitch.IsChecked = _settings.Current.UploadEnabled;
        ToolTip.SetTip(CloseButton, _localizer["uploadClose"]);

        // 文件卡片：扩展名、体积与像素尺寸。
        var extension = Path.GetExtension(_fileName).TrimStart('.').ToUpperInvariant();
        var size = _data.Length >= 1024 * 1024
            ? $"{_data.Length / 1024d / 1024d:0.0} MB"
            : $"{_data.Length / 1024d:0} KB";
        var dimensions = _preview is null ? string.Empty : $" · {_preview.PixelSize.Width}×{_preview.PixelSize.Height}";
        FileNameText.Text = _fileName;
        FileMetaText.Text = $"{extension} · {size}{dimensions}";
    }

    private static Bitmap? TryCreatePreview(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RebuildFields()
    {
        _fields.Clear();
        FieldsPanel.Children.Clear();
        switch (_upload.Provider)
        {
            case "s3":
                AddField("uploadBucket", _upload.Bucket, value => _upload.Bucket = value, watermark: "my-bucket");
                AddField("uploadRegion", _upload.Region, value => _upload.Region = value, watermark: "ap-southeast-1");
                AddField("uploadAccessKey", _upload.AccessKey, value => _upload.AccessKey = value, watermark: "AKIA…");
                AddField("uploadSecretKey", _upload.SecretKey, value => _upload.SecretKey = value, secret: true);
                AddField("uploadPrefix", _upload.Prefix, value => _upload.Prefix = value, watermark: "screenshots");
                AddField("uploadEndpoint", _upload.Endpoint, value => _upload.Endpoint = value, watermark: "https://…");
                break;
            case "custom":
                AddField("uploadEndpoint", _upload.Endpoint, value => _upload.Endpoint = value, watermark: "https://example.com/upload");
                AddField("uploadToken", _upload.Token, value => _upload.Token = value, secret: true);
                AddField("uploadFieldName", _upload.FieldName, value => _upload.FieldName = value, watermark: "file");
                break;
            default:
                AddField("uploadClientId", _upload.ClientId, value => _upload.ClientId = value, watermark: "e4f1c…");
                break;
        }
    }

    private void AddField(string labelKey, string value, Action<string> assign, bool secret = false, string? watermark = null)
    {
        var box = new TextBox
        {
            Text = value,
            Classes = { "field" },
            PasswordChar = secret ? '•' : '\0',
            PlaceholderText = watermark
        };
        var block = new StackPanel
        {
            Width = 250,
            Margin = new Thickness(0, 0, 10, 10),
            Spacing = 0
        };
        block.Children.Add(new TextBlock
        {
            Text = _localizer[labelKey],
            Classes = { "field-label" }
        });
        block.Children.Add(box);
        FieldsPanel.Children.Add(block);
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

    /// <summary>
    /// 保存配置并执行上传。进度条为不确定状态，成功与失败分别切换到独立的
    /// 结果视图；失败视图提供重试入口。
    /// </summary>
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
        FormPanel.IsVisible = true;
        ProgressPanel.IsVisible = true;
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
            ShowSuccess();
            StatusText.Text = _localizer["uploadSucceeded"];
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = _localizer["uploadCanceled"];
        }
        catch (Exception exception)
        {
            ShowFailure(exception.Message);
            StatusText.Text = _localizer["uploadFailed"];
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            ProgressPanel.IsVisible = false;
            UploadButton.IsEnabled = true;
        }
    }

    private void ShowSuccess()
    {
        FormPanel.IsVisible = false;
        ResultPanel.IsVisible = true;
        SuccessRing.IsVisible = true;
        FailureRing.IsVisible = false;
        LinkBox.IsVisible = true;
        LinkActions.IsVisible = true;
        RetryButton.IsVisible = false;
        ResultTitle.Text = _localizer["uploadSucceeded"];
        ResultDescription.Text = _localizer["uploadSuccessDesc"];
        DeleteRemoteButton.IsEnabled = _result?.DeleteUrl is not null;
        UploadButton.IsVisible = false;
        CancelButton.Content = _localizer["uploadClose"];
    }

    private void ShowFailure(string message)
    {
        FormPanel.IsVisible = false;
        ResultPanel.IsVisible = true;
        SuccessRing.IsVisible = false;
        FailureRing.IsVisible = true;
        LinkBox.IsVisible = false;
        LinkActions.IsVisible = false;
        RetryButton.IsVisible = true;
        ResultTitle.Text = _localizer["uploadFailed"];
        ResultDescription.Text = string.IsNullOrWhiteSpace(message)
            ? _localizer["uploadFailDesc"]
            : $"{_localizer["uploadFailDesc"]}\n{message}";
        UploadButton.IsVisible = false;
        CancelButton.Content = _localizer["uploadClose"];
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
                FormPanel.IsVisible = true;
                _result = null;
                UploadButton.IsVisible = true;
                CancelButton.Content = _localizer["uploadCancel"];
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            DeleteRemoteButton.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _preview?.Dispose();
        base.OnClosed(e);
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