using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Application.Events;
using SSC.Application.Hotkeys;
using SSC.Application.Settings;
using SSC.Application.Upload;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.UI.Editing;
using SSC.UI.Motion;
using SSC.UI.Ocr;
using SSC.UI.Platform;
using SSC.UI.Upload;
using SSC.UI.Dialogs;

namespace SSC.UI.History;

/// <summary>
/// 历史记录窗口（原型 P4）。左侧展示网格或列表，右侧以抽屉形式展示详情，
/// 删除操作使用原型中的确认弹层，避免误删。
/// </summary>
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
    private HistoryCardViewModel? _pendingDelete;
    private CaptureKind? _kind;
    private DateTimeOffset? _since;
    private bool _gridView = true;
    private bool _favoritesOnly;
    private int _refreshVersion;

    /// <summary>由依赖注入构造历史窗口。</summary>
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
        SystemCaptionButtons.Localizer = _localizer;
        if (PlatformInfo.IsMacOS)
        {
            DeleteCard.Classes.Add("sheet");
            DeleteScrim.Classes.Add("sheet");
        }

        DialogButtonOrder.Apply(DeleteActions, DeleteConfirm);
        WindowingMotion.FadeIn(this);
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
        DetailClose.Click += (_, _) => CloseDrawer();
        SearchBox.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        AllFilter.Click += (_, _) => SetKind(null);
        ScreenshotFilter.Click += (_, _) => SetKind(CaptureKind.Screenshot);
        RecordingFilter.Click += (_, _) => SetKind(CaptureKind.Recording);
        GifFilter.Click += (_, _) => SetKind(CaptureKind.AnimatedImage);
        FavoriteFilter.Click += (_, _) => ToggleFavoritesOnly();
        DetailTagInput.KeyDown += OnTagInputKeyDown;
        TimeFilter.SelectionChanged += (_, _) => UpdateSince();
        GridViewButton.Click += (_, _) => SetView(grid: true);
        ListViewButton.Click += (_, _) => SetView(grid: false);
        ListResults.SelectionChanged += (_, _) => SelectCard(ListResults.SelectedItem as HistoryCardViewModel);
        GridResults.SelectionChanged += (_, _) => SelectCard(GridResults.SelectedItem as HistoryCardViewModel);

        DetailCopy.Click += (_, _) => _ = RunActionAsync("Copy", _selected);
        DetailEdit.Click += (_, _) => _ = RunActionAsync("Edit", _selected);
        DetailPin.Click += (_, _) => _ = RunActionAsync("Pin", _selected);
        DetailUpload.Click += (_, _) => _ = RunActionAsync("Upload", _selected);
        DetailDelete.Click += (_, _) => ShowDeleteModal(_selected);

        DeleteCancel.Click += (_, _) => HideDeleteModal();
        DeleteConfirm.Click += (_, _) => ConfirmDelete();

        GridResults.ContextRequested += OnResultsContextRequested;
        ListResults.ContextRequested += OnResultsContextRequested;
        GridResults.PointerPressed += OnResultsPointerPressed;
        ListResults.PointerPressed += OnResultsPointerPressed;
        GridResults.DoubleTapped += OnResultsDoubleTapped;
        ListResults.DoubleTapped += OnResultsDoubleTapped;
        KeyDown += OnWindowKeyDown;
    }

    private static HotkeyGesture CommandGesture(HotkeyKey key, HotkeyModifiers extra = HotkeyModifiers.None) =>
        new((PlatformInfo.IsMacOS ? HotkeyModifiers.Meta : HotkeyModifiers.Control) | extra, key);

    private static void OnResultsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list || !e.GetCurrentPoint(list).Properties.IsRightButtonPressed)
        {
            return;
        }

        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is { } item)
        {
            item.IsSelected = true;
        }
    }

    private void OnResultsContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not HistoryCardViewModel card)
        {
            return;
        }

        SelectCard(card);
        var menu = ContextMenuFactory.Create(
            new ContextMenuEntry(_localizer["contextOpen"], () => SelectCard(card), IsDefault: true),
            new ContextMenuEntry(_localizer["contextCopy"], () => _ = RunActionAsync("Copy", card), CommandGesture(HotkeyKey.C)),
            new ContextMenuEntry(_localizer["contextEdit"], () => _ = RunActionAsync("Edit", card)),
            new ContextMenuEntry(_localizer["contextPin"], () => _ = RunActionAsync("Pin", card)),
            new ContextMenuEntry(_localizer["contextUpload"], () => _ = RunActionAsync("Upload", card)),
            new ContextMenuEntry(_localizer["contextFavorite"], () => _ = RunActionAsync("Favorite", card)),
            ContextMenuEntry.Separator(),
            new ContextMenuEntry(
                _localizer["contextDelete"],
                () => ShowDeleteModal(card),
                CommandGesture(HotkeyKey.Backspace),
                IsDanger: true));
        menu.Open(list);
        e.Handled = true;
    }

    private void OnResultsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: HistoryCardViewModel card })
        {
            _ = RunActionAsync("Edit", card);
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (DeleteModal.IsVisible)
        {
            HideDeleteModal();
        }
        else if (DetailDrawer.IsVisible)
        {
            CloseDrawer();
        }
        else
        {
            Close();
        }

        e.Handled = true;
    }

    private void ApplyLocalization()
    {
        Title = _localizer["historyTitle"];
        TitleText.Text = _localizer["historyTitle"];
        SearchBox.PlaceholderText = _localizer["historySearchPlaceholder"];
        AllFilter.Content = _localizer["historyAll"];
        ScreenshotFilter.Content = _localizer["historyScreenshots"];
        RecordingFilter.Content = _localizer["historyRecordings"];
        GifFilter.Content = _localizer["historyGifs"];
        FavoriteFilter.Content = _localizer["historyFavorites"];
        DetailTagInput.PlaceholderText = _localizer["historyTagPlaceholder"];
        ToolTip.SetTip(GridViewButton, _localizer["historyGrid"]);
        AutomationProperties.SetName(GridViewButton, _localizer["historyGrid"]);
        ToolTip.SetTip(ListViewButton, _localizer["historyList"]);
        AutomationProperties.SetName(ListViewButton, _localizer["historyList"]);
        AutomationProperties.SetName(CloseButton, _localizer["historyCancel"]);
        AutomationProperties.SetName(DetailClose, _localizer["historyCancel"]);

        DetailSectionTitle.Text = _localizer["historyDetail"];
        DetailTypeLabel.Text = _localizer["historyMetaType"];
        DetailSizeLabel.Text = _localizer["historyMetaSize"];
        DetailResolutionLabel.Text = _localizer["historyMetaResolution"];
        DetailDurationLabel.Text = _localizer["historyMetaDuration"];
        DetailCreatedLabel.Text = _localizer["historyMetaCreated"];
        DetailTagsLabel.Text = _localizer["historyMetaTags"];
        DetailCopyText.Text = _localizer["historyCopy"];
        DetailEditText.Text = _localizer["historyEdit"];
        DetailPinText.Text = _localizer["historyPin"];
        DetailUploadText.Text = _localizer["historyUpload"];
        DetailDeleteText.Text = _localizer["historyDelete"];

        DeleteTitle.Text = _localizer["historyDeleteTitle"];
        DeleteDescription.Text = _localizer["historyDeleteDesc"];
        DeleteCancel.Content = _localizer["historyCancel"];
        DeleteConfirm.Content = _localizer["historyDelete"];

        EmptyTitle.Text = _localizer["historyEmpty"];
        EmptyText.Text = _localizer["historyEmptyDesc"];
        TimeFilter.ItemsSource = new[]
        {
            _localizer["historyToday"],
            _localizer["historyWeek"],
            _localizer["historyAllTime"]
        };
        TimeFilter.SelectedIndex = 2;
        VersionText.Text = $"{_localizer["versionPrefix"]}{SSC.Application.AppInfo.Version}";
        StatusText.Text = _localizer["statusReady"];
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

    private void ToggleFavoritesOnly()
    {
        _favoritesOnly = !_favoritesOnly;
        SetClass(FavoriteFilter, "active", _favoritesOnly);
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

    /// <summary>按当前搜索词和筛选条件重新查询历史记录。</summary>
    private async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();
        var cancellationToken = _refreshCancellation.Token;

        try
        {
            var query = new HistoryQuery(SearchBox.Text, _kind, _since, 200, 0, _favoritesOnly ? true : null);
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
            CountText.Text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _localizer["historyCount"],
                count);
            var hasQuery = !string.IsNullOrWhiteSpace(SearchBox.Text);
            EmptyState.IsVisible = _cards.Count == 0;
            EmptyTitle.Text = hasQuery ? _localizer["historyNoResults"] : _localizer["historyEmpty"];
            EmptyText.Text = hasQuery ? _localizer["historyNoResultDesc"] : _localizer["historyEmptyDesc"];
            StatusText.Text = _cards.Count == count
                ? string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    _localizer["historyCount"],
                    count)
                : $"{_cards.Count} / {count}";
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
        CloseDrawer();
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

    /// <summary>选中卡片后填充详情抽屉。</summary>
    private void SelectCard(HistoryCardViewModel? card)
    {
        _selected = card;
        DetailDrawer.IsVisible = card is not null;
        if (card is null)
        {
            DetailImage.Source = null;
            return;
        }

        DetailTitle.Text = card.Title;
        DetailType.Text = card.KindLabel;
        DetailSize.Text = card.SizeText;
        DetailResolution.Text = card.ResolutionText;
        DetailDuration.Text = card.HasDuration ? card.DurationText : "—";
        DetailCreated.Text = card.CreatedText;
        DetailImage.Source = card.Thumbnail;
        DetailTagInput.Text = string.Empty;
        RefreshTagChips();
        DetailCopy.IsEnabled = card.CanEditFrame;
        DetailEdit.IsEnabled = card.CanEditFrame;
        DetailPin.IsEnabled = card.IsImage;
        DetailUpload.IsEnabled = card.CanEditFrame;
    }

    private void RefreshTagChips()
    {
        DetailTagChips.Children.Clear();
        if (_selected is null)
        {
            return;
        }

        foreach (var tag in _selected.Entry.Tags)
        {
            var label = new TextBlock
            {
                Text = tag,
                VerticalAlignment = VerticalAlignment.Center
            };
            var remove = new Button
            {
                Content = "×",
                Tag = tag,
                VerticalAlignment = VerticalAlignment.Center
            };
            remove.Classes.Add("tag-remove");
            remove.Click += OnRemoveTag;

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4
            };
            panel.Children.Add(label);
            panel.Children.Add(remove);

            var chip = new Border
            {
                Child = panel,
                Margin = new Thickness(0, 0, 4, 4)
            };
            chip.Classes.Add("chip");
            DetailTagChips.Children.Add(chip);
        }
    }

    private void OnTagInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.OemComma)
        {
            e.Handled = true;
            CommitTag(DetailTagInput.Text);
            return;
        }

        if (e.Key == Key.Back && string.IsNullOrEmpty(DetailTagInput.Text) && _selected is not null)
        {
            var remaining = _selected.Entry.Tags.Take(Math.Max(0, _selected.Entry.Tags.Count - 1));
            ApplyTags(remaining);
            e.Handled = true;
        }
    }

    private void CommitTag(string? text)
    {
        var tag = text?.Trim();
        if (string.IsNullOrWhiteSpace(tag) || _selected is null)
        {
            return;
        }

        ApplyTags(_selected.Entry.Tags.Concat(new[] { tag }));
        DetailTagInput.Text = string.Empty;
    }

    private void OnRemoveTag(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && _selected is not null)
        {
            ApplyTags(_selected.Entry.Tags.Where(item =>
                !string.Equals(item, tag, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void ApplyTags(IEnumerable<string> tags)
    {
        if (_selected is null)
        {
            return;
        }

        var normalized = HistoryEntry.NormalizeTags(tags);
        _repository.SetTags(_selected.Entry.Id, normalized);
        _selected.ApplyTags(normalized);
        RefreshTagChips();
        StatusText.Text = _localizer["historyTagsSaved"];
    }

    private void CloseDrawer()
    {
        DetailDrawer.IsVisible = false;
        DetailImage.Source = null;
        DetailTagChips.Children.Clear();
        DetailTagInput.Text = string.Empty;
        _selected = null;
    }

    private void ShowDeleteModal(HistoryCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        _pendingDelete = card;
        DeleteModal.IsVisible = true;
        // Windows 下重排后首个按钮是删除，初始焦点仍必须落在取消上（两平台一致）。
        DeleteCancel.Focus();
    }

    private void HideDeleteModal()
    {
        _pendingDelete = null;
        DeleteModal.IsVisible = false;
    }

    private async void ConfirmDelete()
    {
        var card = _pendingDelete;
        HideDeleteModal();
        if (card is null)
        {
            return;
        }

        _repository.Delete(card.Entry.Id);
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
        CloseDrawer();
        await RefreshAsync();
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
            case "Favorite":
                _repository.SetFavorite(entry.Id, !entry.IsFavorite);
                card.ApplyFavorite(!entry.IsFavorite);
                StatusText.Text = _localizer["historyFavoriteSaved"];
                break;
            case "Delete":
                ShowDeleteModal(card);
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
            await DialogPresenter.ShowAsync(new UploadDialogWindow(bytes, fileName, _uploads, _settings, _localizer), this);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"{_localizer["uploadFailed"]}: {exception.Message}";
        }
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
