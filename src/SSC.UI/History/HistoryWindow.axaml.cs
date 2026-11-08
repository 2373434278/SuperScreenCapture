using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Application.Events;
using SSC.Application.Settings;
using SSC.Application.Upload;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.UI.Editing;
using SSC.UI.Ocr;
using SSC.UI.Upload;

namespace SSC.UI.History;

public partial class HistoryWindow : Window
{
    private readonly IHistoryRepository _repository;
    private readonly ILocalizer _localizer;
    private readonly ICaptureFrameLoader _loader;
    private readonly IAnnotationRenderer _renderer;
    private readonly ScreenshotService _screenshots;
    private readonly IClipboardService _clipboard;
    private readonly IOcrService _ocr;
    private readonly IAppEventBus _events;
    private readonly UploadPipeline? _uploads;
    private readonly SettingsService? _settings;
    private readonly IImageEncoder? _encoder;
    private readonly ObservableCollection<HistoryCardViewModel> _cards = new();
    private readonly DispatcherTimer _searchTimer;
    private IDisposable? _subscription;
    private CancellationTokenSource? _refreshCancellation;
    private HistoryCardViewModel? _selected;
    private CaptureKind? _kind;
    private DateTimeOffset? _since;
    private bool _gridView = true;
    private int _refreshVersion;
    private Guid? _pendingDeleteId;
    private DateTimeOffset _pendingDeleteAt;

    public HistoryWindow(
        IHistoryRepository repository,
        ILocalizer localizer,
        ICaptureFrameLoader loader,
        IAnnotationRenderer renderer,
        ScreenshotService screenshots,
        IClipboardService clipboard,
        IOcrService ocr,
        IAppEventBus events,
        UploadPipeline? uploads = null,
        SettingsService? settings = null,
        IImageEncoder? encoder = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _screenshots = screenshots ?? throw new ArgumentNullException(nameof(screenshots));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _uploads = uploads;
        _settings = settings;
        _encoder = encoder;

        InitializeComponent();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = RefreshAsync();
        };

        ApplyLocalization();
        WireControls();
        SetView(grid: true);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _subscription ??= _events.Subscribe<HistoryChanged>(message =>
        {
            _ = message;
            Dispatcher.UIThread.Post(() => { _ = RefreshAsync(); });
        });
        _ = RefreshAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _searchTimer.Stop();
        _refreshCancellation?.Cancel();
        _subscription?.Dispose();
        _subscription = null;
        DisposeCards();
        base.OnClosed(e);
    }

    private void WireControls()
    {
        CloseButton.Click += (_, _) => Close();
        SearchBox.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        AllFilter.Click += (_, _) => SetKind(null);
        ScreenshotFilter.Click += (_, _) => SetKind(CaptureKind.Screenshot);
        RecordingFilter.Click += (_, _) => SetKind(CaptureKind.Recording);
        GifFilter.Click += (_, _) => SetKind(CaptureKind.AnimatedImage);
        TimeFilter.SelectionChanged += (_, _) => UpdateSince();
        GridViewButton.Click += (_, _) => SetView(grid: true);
        ListViewButton.Click += (_, _) => SetView(grid: false);
        ListResults.SelectionChanged += (_, _) => SelectCard(ListResults.SelectedItem as HistoryCardViewModel);
        GridResults.SelectionChanged += (_, _) => SelectCard(GridResults.SelectedItem as HistoryCardViewModel);

        DetailOpen.Click += (_, _) => _ = RunActionAsync("Open", _selected);
        DetailCopy.Click += (_, _) => _ = RunActionAsync("Copy", _selected);
        DetailEdit.Click += (_, _) => _ = RunActionAsync("Edit", _selected);
        DetailPin.Click += (_, _) => _ = RunActionAsync("Pin", _selected);
        DetailOcr.Click += (_, _) => _ = RunActionAsync("Ocr", _selected);
        DetailUpload.Click += (_, _) => _ = RunActionAsync("Upload", _selected);
        DetailDelete.Click += (_, _) => _ = RunActionAsync("Delete", _selected);
    }

    private void ApplyLocalization()
    {
        Title = _localizer["historyTitle"];
        TitleText.Text = _localizer["historyTitle"];
        AllFilter.Content = _localizer["historyAll"];
        ScreenshotFilter.Content = _localizer["historyScreenshots"];
        RecordingFilter.Content = _localizer["historyRecordings"];
        GifFilter.Content = _localizer["historyGifs"];
        SearchBox.PlaceholderText = _localizer["historySearchPlaceholder"];
        DetailOpen.Content = _localizer["historyOpen"];
        DetailCopy.Content = _localizer["historyCopy"];
        DetailEdit.Content = _localizer["historyEdit"];
        DetailPin.Content = _localizer["historyPin"];
        DetailOcr.Content = _localizer["historyOcr"];
        DetailUpload.Content = _localizer["historyUpload"];
        DetailDelete.Content = _localizer["historyDelete"];
        EmptyText.Text = _localizer["historyEmpty"];
        TimeFilter.ItemsSource = new[]
        {
            _localizer["historyToday"],
            _localizer["historyWeek"],
            _localizer["historyAllTime"]
        };
        TimeFilter.SelectedIndex = 2;
    }

    private void SetKind(CaptureKind? kind)
    {
        _kind = kind;
        SetClass(AllFilter, "active", kind is null);
        SetClass(ScreenshotFilter, "active", kind == CaptureKind.Screenshot);
        SetClass(RecordingFilter, "active", kind == CaptureKind.Recording);
        SetClass(GifFilter, "active", kind == CaptureKind.AnimatedImage);
        _ = RefreshAsync();
    }

    private void UpdateSince()
    {
        _since = TimeFilter.SelectedIndex switch
        {
            0 => DateTimeOffset.Now.Date,
            1 => DateTimeOffset.Now.AddDays(-7),
            _ => null
        };
        _ = RefreshAsync();
    }

    private void SetView(bool grid)
    {
        _gridView = grid;
        GridResults.IsVisible = grid;
        ListResults.IsVisible = !grid;
        SetClass(GridViewButton, "active", grid);
        SetClass(ListViewButton, "active", !grid);
    }

    private async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        var cancellationToken = _refreshCancellation.Token;

        try
        {
            var query = new HistoryQuery(SearchBox.Text, _kind, _since, 200, 0);
            var entries = await Task.Run(() => _repository.Query(query), cancellationToken);
            var count = await Task.Run(() => _repository.Count(query), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (version != _refreshVersion)
            {
                return;
            }

            ClearResults();
            foreach (var entry in entries)
            {
                var card = new HistoryCardViewModel(entry, _localizer);
                if (card.IsImage)
                {
                    card.Thumbnail = await LoadThumbnailAsync(entry.FilePath, cancellationToken);
                }

                if (version != _refreshVersion)
                {
                    card.Dispose();
                    return;
                }

                _cards.Add(card);
            }

            ListResults.ItemsSource = _cards;
            GridResults.ItemsSource = _cards;
            CountText.Text = count.ToString();
            EmptyState.IsVisible = _cards.Count == 0;
            EmptyText.Text = string.IsNullOrWhiteSpace(SearchBox.Text)
                ? _localizer["historyEmpty"]
                : _localizer["historyNoResults"];
            StatusText.Text = $"{_cards.Count} / {count}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private async Task<Bitmap?> LoadThumbnailAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var stream = File.OpenRead(filePath);
                return Bitmap.DecodeToWidth(stream, 280, BitmapInterpolationMode.MediumQuality);
            }, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void ClearResults()
    {
        ListResults.ItemsSource = null;
        GridResults.ItemsSource = null;
        DetailBorder.IsVisible = false;
        DetailImage.Source = null;
        _selected = null;
        DisposeCards();
        _cards.Clear();
    }

    private void DisposeCards()
    {
        foreach (var card in _cards)
        {
            card.Dispose();
        }
    }

    private void SelectCard(HistoryCardViewModel? card)
    {
        _selected = card;
        DetailBorder.IsVisible = card is not null;
        if (card is null)
        {
            DetailImage.Source = null;
            return;
        }

        DetailTitle.Text = card.Title;
        DetailMeta.Text = card.DetailMeta;
        DetailImage.Source = card.Thumbnail;
        DetailCopy.IsEnabled = card.IsImage;
        DetailEdit.IsEnabled = card.IsImage;
        DetailPin.IsEnabled = card.IsImage;
        DetailOcr.IsEnabled = card.IsImage;
        DetailUpload.IsEnabled = card.IsImage;
    }

    private async void OnCardAction(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryCardViewModel card, CommandParameter: string action })
        {
            return;
        }

        await RunActionAsync(action, card);
    }

    private async Task RunActionAsync(string action, HistoryCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var entry = card.Entry;
        switch (action)
        {
            case "Open":
                OpenPath(entry.FilePath);
                break;
            case "Copy":
                await CopyImageAsync(entry);
                break;
            case "Edit":
                await EditAsync(entry);
                break;
            case "Pin":
                await PinAsync(entry);
                break;
            case "Ocr":
                await RecognizeAsync(entry);
                break;
            case "Upload":
                await UploadAsync(entry);
                break;
            case "Delete":
                await DeleteWithConfirmationAsync(card);
                break;
        }
    }

    private async Task CopyImageAsync(HistoryEntry entry)
    {
        var frame = await _loader.LoadAsync(entry.FilePath);
        if (frame is null)
        {
            StatusText.Text = _localizer["historyLoadFailed"];
            return;
        }

        await _clipboard.CopyImageAsync(frame);
        StatusText.Text = _localizer["historyCopied"];
    }

    private async Task EditAsync(HistoryEntry entry)
    {
        var frame = await _loader.LoadAsync(entry.FilePath);
        if (frame is null)
        {
            StatusText.Text = _localizer["historyLoadFailed"];
            return;
        }

        var result = new ScreenshotResult(entry, Array.Empty<byte>(), entry.FilePath);
        new AnnotationEditorWindow(
            result,
            frame,
            _localizer,
            _renderer,
            _screenshots,
            _clipboard,
            _ocr,
            _uploads,
            _settings,
            _encoder).Show();
    }

    private async Task PinAsync(HistoryEntry entry)
    {
        var frame = await _loader.LoadAsync(entry.FilePath);
        if (frame is null)
        {
            StatusText.Text = _localizer["historyLoadFailed"];
            return;
        }

        new PinWindow(frame, _localizer).Show();
    }

    private async Task RecognizeAsync(HistoryEntry entry)
    {
        var frame = await _loader.LoadAsync(entry.FilePath);
        if (frame is null)
        {
            StatusText.Text = _localizer["historyLoadFailed"];
            return;
        }

        try
        {
            StatusText.Text = _localizer["historyRecognizing"];
            var result = await _ocr.RecognizeAsync(frame);
            new OcrResultWindow(frame, result, _ocr, _localizer).Show();
            StatusText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"{_localizer["ocrFailed"]}: {exception.Message}";
        }
    }

    private async Task UploadAsync(HistoryEntry entry)
    {
        if (_uploads is null || _settings is null || _encoder is null)
        {
            StatusText.Text = _localizer["historyUploadSoon"];
            return;
        }

        var frame = await _loader.LoadAsync(entry.FilePath);
        if (frame is null)
        {
            StatusText.Text = _localizer["historyLoadFailed"];
            return;
        }

        try
        {
            var bytes = await _encoder.EncodeAsync(frame, "png", 95);
            var fileName = $"{Path.GetFileNameWithoutExtension(entry.FilePath)}.png";
            new UploadDialogWindow(bytes, fileName, _uploads, _settings, _localizer).Show();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"{_localizer["uploadFailed"]}: {exception.Message}";
        }
    }

    private async Task DeleteWithConfirmationAsync(HistoryCardViewModel card)
    {
        var id = card.Entry.Id;
        if (_pendingDeleteId != id || DateTimeOffset.UtcNow - _pendingDeleteAt > TimeSpan.FromSeconds(5))
        {
            _pendingDeleteId = id;
            _pendingDeleteAt = DateTimeOffset.UtcNow;
            StatusText.Text = _localizer["historyConfirmDelete"];
            return;
        }

        _pendingDeleteId = null;
        _repository.Delete(id);
        try
        {
            if (File.Exists(card.Entry.FilePath))
            {
                File.Delete(card.Entry.FilePath);
            }
        }
        catch (IOException)
        {
        }

        StatusText.Text = _localizer["historyDeleted"];
        await RefreshAsync();
    }

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void SetClass(Control control, string name, bool enabled)
    {
        if (enabled)
        {
            if (!control.Classes.Contains(name))
            {
                control.Classes.Add(name);
            }
        }
        else
        {
            control.Classes.Remove(name);
        }
    }
}
