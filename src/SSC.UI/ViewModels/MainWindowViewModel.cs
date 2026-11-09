using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSC.Application;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Application.Hotkeys;
using SSC.Application.Settings;
using SSC.Application.Storage;
using SSC.Application.Updates;
using SSC.Application.Upload;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.Platform.Abstractions;
using SSC.UI.Capture;
using SSC.UI.Editing;
using SSC.UI.History;
using SSC.UI.Ocr;
using SSC.UI.Recording;
using SSC.UI.Upload;

namespace SSC.UI.ViewModels;

/// <summary>
/// 设置主窗口（原型 P5）的视图模型。
/// 内部持有一份 <see cref="AppSettings"/> 草稿，所有编辑先写入草稿，
/// 只有点击“保存设置”时才会通过 <see cref="SaveSettingsCommand"/> 持久化；
/// 语言与主题属于可即时预览的选项，因此会立刻生效。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly ILocalizer _localizer;
    private readonly IAutoStartService _autoStart;
    private readonly HotkeyManager? _hotkeys;
    private readonly IUpdateChecker? _updates;
    private readonly IStorageMaintenance? _storage;
    private readonly PlatformDescriptor _platform;
    private readonly CaptureCoordinator? _captureCoordinator;
    private readonly RecordingCoordinator? _recordingCoordinator;
    private readonly HistoryWindow? _historyWindow;
    private readonly IHistoryRepository? _historyRepository;
    private readonly ICaptureFrameLoader? _frameLoader;
    private readonly IAnnotationRenderer? _annotationRenderer;
    private readonly ScreenshotService? _screenshots;
    private readonly IClipboardService? _clipboard;
    private readonly IOcrService? _ocr;
    private readonly UploadPipeline? _uploads;
    private readonly IImageEncoder? _imageEncoder;
    private AppSettings _draft;
    private string _activeTab = "general";
    private bool _disposed;
    private AutoStartState _autoStartState = AutoStartState.Disabled;
    private bool _isCheckingUpdates;
    private bool _hasUpdate;
    private string? _latestReleaseUrl;
    private string? _updateStatusKey;
    private string? _updateStatusArgument;
    private StorageUsage? _storageUsage;
    private bool _isCleanupArmed;
    private bool _isClearArmed;
    private DispatcherTimer? _storageArmTimer;

    /// <summary>由依赖注入构造设置视图模型。</summary>
    public MainWindowViewModel(
        SettingsService settingsService,
        ILocalizer localizer,
        IAutoStartService autoStart,
        PlatformDescriptor platform,
        CaptureCoordinator? captureCoordinator = null,
        RecordingCoordinator? recordingCoordinator = null,
        HistoryWindow? historyWindow = null,
        IHistoryRepository? historyRepository = null,
        ICaptureFrameLoader? frameLoader = null,
        IAnnotationRenderer? annotationRenderer = null,
        ScreenshotService? screenshots = null,
        IClipboardService? clipboard = null,
        IOcrService? ocr = null,
        UploadPipeline? uploads = null,
        IImageEncoder? imageEncoder = null,
        HotkeyManager? hotkeys = null,
        IUpdateChecker? updates = null,
        IStorageMaintenance? storage = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _autoStart = autoStart ?? throw new ArgumentNullException(nameof(autoStart));
        _hotkeys = hotkeys;
        _updates = updates;
        _storage = storage;
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _captureCoordinator = captureCoordinator;
        _recordingCoordinator = recordingCoordinator;
        _historyWindow = historyWindow;
        _historyRepository = historyRepository;
        _frameLoader = frameLoader;
        _annotationRenderer = annotationRenderer;
        _screenshots = screenshots;
        _clipboard = clipboard;
        _ocr = ocr;
        _uploads = uploads;
        _imageEncoder = imageEncoder;

        _draft = _settingsService.Current;
        var autoStartStatus = _autoStart.GetStatus();
        _autoStartState = autoStartStatus.State;
        _draft.LaunchAtLogin = autoStartStatus.State is AutoStartState.Enabled or AutoStartState.RequiresApproval;
        _localizer.SetLanguage(_draft.Language);
        _localizer.LanguageChanged += OnLanguageChanged;
        ApplyTheme(_draft.Theme);
        StatusMessage = L["statusReady"];
        RebuildHotkeys();
    }

    /// <summary>供 XAML 的 <c>{Binding L[key]}</c> 索引器使用的本地化器。</summary>
    public ILocalizer L => _localizer;

    /// <summary>快捷键设置页展示的可编辑行集合。</summary>
    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = new();

    public string WindowTitle => L["appTitle"];

    public string AppVersion => AppInfo.Version;

    public string AppLicense => AppInfo.License;

    public string AppName => AppInfo.Name;

    /// <summary>带 "v" 前缀的版本号，用于顶栏品牌徽标和关于页。</summary>
    public string AppVersionLabel => $"{L["versionPrefix"]}{AppVersion}";

    public bool IsGeneralTab => _activeTab == "general";

    public bool IsHotkeysTab => _activeTab == "hotkeys";

    public bool IsOutputTab => _activeTab == "output";

    public bool IsRecordingTab => _activeTab == "recording";

    public bool IsOcrTab => _activeTab == "ocr";

    public bool IsUploadTab => _activeTab == "upload";

    public bool IsAboutTab => _activeTab == "about";

    public bool IsChinese => L.Language == "zh";

    public bool IsEnglish => L.Language == "en";

    /// <summary>按运行平台自动识别的系统名称，取代原型里的 macOS/Windows 手动切换。</summary>
    public string PlatformLabel => _platform.Kind switch
    {
        PlatformKind.MacOS => "macOS",
        PlatformKind.Windows => "Windows",
        _ => _platform.DisplayName
    };

    public bool IsDarkTheme => _draft.Theme == "dark";

    public bool IsLightTheme => _draft.Theme == "light";

    // ---------------------------------------------------------------- 通用

    public bool LaunchAtLogin
    {
        get => _draft.LaunchAtLogin;
        set
        {
            if (_draft.LaunchAtLogin == value)
            {
                return;
            }

            _draft.LaunchAtLogin = value;
            OnPropertyChanged();
        }
    }

    public bool CloseToTray
    {
        get => _draft.CloseToTray;
        set
        {
            if (_draft.CloseToTray == value)
            {
                return;
            }

            _draft.CloseToTray = value;
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- 输出

    /// <summary>截图保存目录，来自最近一次保存的草稿。</summary>
    public string SaveDirectory
    {
        get => _draft.SaveDirectory;
        set
        {
            var normalized = value ?? string.Empty;
            if (_draft.SaveDirectory == normalized)
            {
                return;
            }

            _draft.SaveDirectory = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileNamePreview));
        }
    }

    /// <summary>支持 {date}、{type}、{n} 占位符的文件名模板。</summary>
    public string FileNamePattern
    {
        get => _draft.FileNamePattern;
        set
        {
            var normalized = value ?? string.Empty;
            if (_draft.FileNamePattern == normalized)
            {
                return;
            }

            _draft.FileNamePattern = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileNamePreview));
        }
    }

    /// <summary>下一张截图的文件名预览，随模板与格式实时更新。</summary>
    public string FileNamePreview
    {
        get
        {
            var pattern = string.IsNullOrWhiteSpace(_draft.FileNamePattern)
                ? "{date}-{type}-{n}"
                : _draft.FileNamePattern;
            var name = pattern
                .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                .Replace("{type}", "screenshot", StringComparison.OrdinalIgnoreCase)
                .Replace("{n}", "001", StringComparison.OrdinalIgnoreCase);
            return $"{name}.{_draft.DefaultFormat}";
        }
    }

    public bool IsPng => _draft.DefaultFormat == "png";

    public bool IsJpg => _draft.DefaultFormat == "jpg";

    public bool IsWebp => _draft.DefaultFormat == "webp";

    /// <summary>JPG 质量（40-100），与滑块双向绑定。</summary>
    public double JpegQuality
    {
        get => _draft.JpegQuality;
        set
        {
            var clamped = (int)Math.Round(Math.Clamp(value, 40, 100));
            if (_draft.JpegQuality == clamped)
            {
                return;
            }

            _draft.JpegQuality = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(JpegQualityText));
        }
    }

    public string JpegQualityText => _draft.JpegQuality.ToString(CultureInfo.InvariantCulture);

    public bool AutoCopy
    {
        get => _draft.AutoCopy;
        set
        {
            if (_draft.AutoCopy == value)
            {
                return;
            }

            _draft.AutoCopy = value;
            OnPropertyChanged();
        }
    }

    /// <summary>延时截图可选秒数，下拉框按索引绑定。</summary>
    public static int[] CaptureDelayOptions { get; } = { 1, 3, 5, 10 };

    public int CaptureDelaySeconds
    {
        get => _draft.CaptureDelaySeconds;
        set
        {
            var clamped = Math.Clamp(value, 1, 30);
            if (_draft.CaptureDelaySeconds == clamped)
            {
                return;
            }

            _draft.CaptureDelaySeconds = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CaptureDelayIndex));
        }
    }

    public int CaptureDelayIndex
    {
        get
        {
            var index = Array.IndexOf(CaptureDelayOptions, _draft.CaptureDelaySeconds);
            return index >= 0 ? index : 1;
        }
        set
        {
            if (value >= 0 && value < CaptureDelayOptions.Length)
            {
                CaptureDelaySeconds = CaptureDelayOptions[value];
            }
        }
    }

    public bool IsWindowsHost => _platform.Kind == PlatformKind.Windows;

    public bool CanLaunchAtLogin => _autoStartState != AutoStartState.Unavailable;

    public string? AutoStartStatusText => _autoStartState switch
    {
        AutoStartState.RequiresApproval => L["autoStartNeedsApproval"],
        AutoStartState.Unavailable => L["autoStartUnavailable"],
        _ => null
    };

    public bool HasAutoStartStatus => AutoStartStatusText is not null;

    public bool IsStableChannel => _draft.UpdateChannel == "stable";

    public bool IsPreviewChannel => _draft.UpdateChannel == "preview";

    public int UpdateChannelIndex
    {
        get => IsPreviewChannel ? 1 : 0;
        set
        {
            var channel = value == 1 ? "preview" : "stable";
            if (_draft.UpdateChannel == channel)
            {
                return;
            }

            _draft.UpdateChannel = channel;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStableChannel));
            OnPropertyChanged(nameof(IsPreviewChannel));
        }
    }

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        private set
        {
            if (_isCheckingUpdates == value)
            {
                return;
            }

            _isCheckingUpdates = value;
            OnPropertyChanged();
        }
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (_hasUpdate == value)
            {
                return;
            }

            _hasUpdate = value;
            OnPropertyChanged();
        }
    }

    public string? UpdateStatusText => _updateStatusKey is null
        ? null
        : _updateStatusArgument is null
            ? L[_updateStatusKey]
            : string.Format(CultureInfo.CurrentCulture, L[_updateStatusKey], _updateStatusArgument);

    // ---------------------------------------------------------------- 存储

    public static int[] RetentionOptions { get; } = { 0, 7, 30, 90 };

    public string StorageSummaryText => _storageUsage is null
        ? L["storageCalculating"]
        : string.Format(
            CultureInfo.CurrentCulture,
            L["storageSummary"],
            FormatBytes(_storageUsage.TotalBytes),
            _storageUsage.EntryCount);

    public string StorageDetailText => _storageUsage is null
        ? string.Empty
        : string.Format(
            CultureInfo.CurrentCulture,
            L["storageBreakdown"],
            FormatBytes(_storageUsage.CaptureBytes),
            FormatBytes(_storageUsage.DatabaseBytes),
            FormatBytes(_storageUsage.BlobBytes + _storageUsage.LogBytes),
            _storageUsage.FavoriteCount);

    public int RetentionIndex
    {
        get
        {
            var index = Array.IndexOf(RetentionOptions, _draft.HistoryRetentionDays);
            return index >= 0 ? index : 0;
        }
        set
        {
            if (value < 0 || value >= RetentionOptions.Length)
            {
                return;
            }

            var days = RetentionOptions[value];
            if (_draft.HistoryRetentionDays == days)
            {
                return;
            }

            _draft.HistoryRetentionDays = days;
            OnPropertyChanged();
        }
    }

    public bool KeepFavoritesOnCleanup
    {
        get => _keepFavoritesOnCleanup;
        set
        {
            if (_keepFavoritesOnCleanup == value)
            {
                return;
            }

            _keepFavoritesOnCleanup = value;
            OnPropertyChanged();
        }
    }

    public string CleanupButtonText => _isCleanupArmed ? L["storageCleanupConfirm"] : L["storageCleanup"];

    public string ClearButtonText => _isClearArmed ? L["storageClearConfirm"] : L["storageClear"];

    private bool _keepFavoritesOnCleanup = true;

    public bool OpenEditor
    {
        get => _draft.OpenEditor;
        set
        {
            if (_draft.OpenEditor == value)
            {
                return;
            }

            _draft.OpenEditor = value;
            OnPropertyChanged();
        }
    }

    public bool ShowQuickAccess
    {
        get => _draft.ShowQuickAccess;
        set
        {
            if (_draft.ShowQuickAccess == value)
            {
                return;
            }

            _draft.ShowQuickAccess = value;
            OnPropertyChanged();
        }
    }

    public static int[] GifFpsOptions { get; } = { 5, 10, 15 };

    public static int[] GifDurationOptions { get; } = { 5, 10, 20, 30, 60 };

    public static int[] GifDimensionOptions { get; } = { 480, 640, 960, 0 };

    public int GifFpsIndex
    {
        get => Math.Max(0, Array.IndexOf(GifFpsOptions, _draft.GifFps));
        set
        {
            if (value < 0 || value >= GifFpsOptions.Length || _draft.GifFps == GifFpsOptions[value])
            {
                return;
            }

            _draft.GifFps = GifFpsOptions[value];
            OnPropertyChanged();
        }
    }

    public int GifDurationIndex
    {
        get => Math.Max(0, Array.IndexOf(GifDurationOptions, _draft.GifMaxDurationSeconds));
        set
        {
            if (value < 0 || value >= GifDurationOptions.Length ||
                _draft.GifMaxDurationSeconds == GifDurationOptions[value])
            {
                return;
            }

            _draft.GifMaxDurationSeconds = GifDurationOptions[value];
            OnPropertyChanged();
        }
    }

    public int GifDimensionIndex
    {
        get => Math.Max(0, Array.IndexOf(GifDimensionOptions, _draft.GifMaxDimension));
        set
        {
            if (value < 0 || value >= GifDimensionOptions.Length ||
                _draft.GifMaxDimension == GifDimensionOptions[value])
            {
                return;
            }

            _draft.GifMaxDimension = GifDimensionOptions[value];
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- 录制

    public int RecordingFps
    {
        get => _draft.RecordingFps;
        set
        {
            if (_draft.RecordingFps == value)
            {
                return;
            }

            _draft.RecordingFps = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFps30));
            OnPropertyChanged(nameof(IsFps60));
        }
    }

    public bool IsFps30 => _draft.RecordingFps == 30;

    public bool IsFps60 => _draft.RecordingFps == 60;

    /// <summary>分辨率下拉框索引，对应 1080p / 1440p / 4K。</summary>
    public int RecordingMaxHeightIndex
    {
        get => _draft.RecordingMaxHeight switch
        {
            1440 => 1,
            2160 => 2,
            _ => 0
        };
        set
        {
            var height = value switch
            {
                1 => 1440,
                2 => 2160,
                _ => 1080
            };
            if (_draft.RecordingMaxHeight == height)
            {
                return;
            }

            _draft.RecordingMaxHeight = height;
            OnPropertyChanged();
        }
    }

    /// <summary>码率下拉框索引，对应 8 / 12 / 20 Mbps。</summary>
    public int RecordingBitrateIndex
    {
        get => _draft.RecordingBitrateMbps switch
        {
            8 => 0,
            20 => 2,
            _ => 1
        };
        set
        {
            var bitrate = value switch
            {
                0 => 8,
                2 => 20,
                _ => 12
            };
            if (_draft.RecordingBitrateMbps == bitrate)
            {
                return;
            }

            _draft.RecordingBitrateMbps = bitrate;
            OnPropertyChanged();
        }
    }

    public bool RecordMicrophone
    {
        get => _draft.RecordMicrophone;
        set
        {
            if (_draft.RecordMicrophone == value)
            {
                return;
            }

            _draft.RecordMicrophone = value;
            OnPropertyChanged();
        }
    }

    /// <summary>编码器描述，当前固定展示 H.264 硬件编码。</summary>
    public string RecordingCodec => L["recordingCodecValue"];

    // ---------------------------------------------------------------- OCR

    /// <summary>首选 OCR 引擎索引：0 = 系统原生，1 = 内置 Tesseract。</summary>
    public int OcrEngineIndex
    {
        get => _draft.OcrEngine == "tesseract" ? 1 : 0;
        set
        {
            var engine = value == 1 ? "tesseract" : "native";
            if (_draft.OcrEngine == engine)
            {
                return;
            }

            _draft.OcrEngine = engine;
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- 上传

    public bool UploadEnabled
    {
        get => _draft.UploadEnabled;
        set
        {
            if (_draft.UploadEnabled == value)
            {
                return;
            }

            _draft.UploadEnabled = value;
            NotifyUploadProviders();
        }
    }

    public bool IsUploadS3
    {
        get => IsProviderSelected("s3");
        set
        {
            if (value)
            {
                SelectUploadProvider("s3");
            }
            else if (IsProviderSelected("s3"))
            {
                UploadEnabled = false;
            }

            NotifyUploadProviders();
        }
    }

    public bool IsUploadImgur
    {
        get => IsProviderSelected("imgur");
        set
        {
            if (value)
            {
                SelectUploadProvider("imgur");
            }
            else if (IsProviderSelected("imgur"))
            {
                UploadEnabled = false;
            }

            NotifyUploadProviders();
        }
    }

    public bool IsUploadCustom
    {
        get => IsProviderSelected("custom");
        set
        {
            if (value)
            {
                SelectUploadProvider("custom");
            }
            else if (IsProviderSelected("custom"))
            {
                UploadEnabled = false;
            }

            NotifyUploadProviders();
        }
    }

    public bool IsUploadS3Active => _draft.Upload.Provider == "s3";

    public bool IsUploadImgurActive => _draft.Upload.Provider == "imgur";

    public bool IsUploadCustomActive => _draft.Upload.Provider == "custom";

    public string UploadBucket
    {
        get => _draft.Upload.Bucket;
        set
        {
            var normalized = value ?? string.Empty;
            if (_draft.Upload.Bucket == normalized)
            {
                return;
            }

            _draft.Upload.Bucket = normalized;
            OnPropertyChanged();
        }
    }

    public string UploadRegion
    {
        get => _draft.Upload.Region;
        set
        {
            var normalized = value ?? string.Empty;
            if (_draft.Upload.Region == normalized)
            {
                return;
            }

            _draft.Upload.Region = normalized;
            OnPropertyChanged();
        }
    }

    public string UploadAccessKey
    {
        get => _draft.Upload.AccessKey;
        set
        {
            var normalized = value ?? string.Empty;
            if (_draft.Upload.AccessKey == normalized)
            {
                return;
            }

            _draft.Upload.AccessKey = normalized;
            OnPropertyChanged();
        }
    }

    public string UploadSecretKey
    {
        get => _draft.Upload.SecretKey;
        set
        {
            var normalized = value ?? string.Empty;
            if (_draft.Upload.SecretKey == normalized)
            {
                return;
            }

            _draft.Upload.SecretKey = normalized;
            OnPropertyChanged();
        }
    }

    public string UploadClientId
    {
        get => _draft.Upload.ClientId;
        set
        {
            var normalized = value ?? string.Empty;
            if (_draft.Upload.ClientId == normalized)
            {
                return;
            }

            _draft.Upload.ClientId = normalized;
            OnPropertyChanged();
        }
    }

    public string UploadEndpoint
    {
        get => _draft.Upload.Endpoint;
        set
        {
            var normalized = value ?? string.Empty;
            if (_draft.Upload.Endpoint == normalized)
            {
                return;
            }

            _draft.Upload.Endpoint = normalized;
            OnPropertyChanged();
        }
    }

    public string UploadToken
    {
        get => _draft.Upload.Token;
        set
        {
            var normalized = value ?? string.Empty;
            if (_draft.Upload.Token == normalized)
            {
                return;
            }

            _draft.Upload.Token = normalized;
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- 页头

    public string HeaderTitle => _activeTab switch
    {
        "general" => L["generalTitle"],
        "hotkeys" => L["hotkeysTitle"],
        "output" => L["outputTitle"],
        "recording" => L["recordingTitle"],
        "ocr" => L["ocrTitle"],
        "upload" => L["navUpload"],
        "about" => L["aboutTitle"],
        _ => L["appTitle"]
    };

    public string HeaderSubtitle => _activeTab switch
    {
        "general" => L["generalDescription"],
        "hotkeys" => L["hotkeysDescription"],
        "output" => L["outputDescription"],
        "recording" => L["recordingDescription"],
        "ocr" => L["ocrDescription"],
        "upload" => L["uploadDescription"],
        "about" => L["aboutDescription"],
        _ => L["appSubtitle"]
    };

    [ObservableProperty]
    private string statusMessage = string.Empty;

    // ---------------------------------------------------------------- 命令

    /// <summary>切换当前设置页（general / hotkeys / output / ...）。</summary>
    [RelayCommand]
    private void SelectTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab) || _activeTab == tab)
        {
            return;
        }

        _activeTab = tab;
        NotifyTabProperties();
        if (tab == "output")
        {
            _ = RefreshStorageAsync();
        }
    }

    [RelayCommand]
    private void SetLanguage(string? language)
    {
        if (language is not ("zh" or "en"))
        {
            return;
        }

        _draft.Language = language;
        _localizer.SetLanguage(language);
    }

    [RelayCommand]
    private void SetTheme(string? theme)
    {
        if (theme is not ("dark" or "light") || _draft.Theme == theme)
        {
            return;
        }

        _draft.Theme = theme;
        ApplyTheme(theme);
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
    }

    [RelayCommand]
    private void ToggleTheme() => SetTheme(_draft.Theme == "dark" ? "light" : "dark");

    /// <summary>选择默认截图格式（png / jpg / webp）。</summary>
    [RelayCommand]
    private void SetFormat(string? format)
    {
        if (format is not ("png" or "jpg" or "webp") || _draft.DefaultFormat == format)
        {
            return;
        }

        _draft.DefaultFormat = format;
        OnPropertyChanged(nameof(IsPng));
        OnPropertyChanged(nameof(IsJpg));
        OnPropertyChanged(nameof(IsWebp));
        OnPropertyChanged(nameof(FileNamePreview));
    }

    /// <summary>选择默认录制帧率。</summary>
    [RelayCommand]
    private void SetRecordingFps(string? fps)
    {
        if (int.TryParse(fps, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            RecordingFps = value;
        }
    }

    /// <summary>激活某个上传服务卡片，并同时启用上传。</summary>
    [RelayCommand]
    private void SelectUploadProvider(string? provider)
    {
        if (provider is not ("s3" or "imgur" or "custom"))
        {
            return;
        }

        _draft.Upload.Provider = provider;
        _draft.UploadEnabled = true;
        NotifyUploadProviders();
    }

    [RelayCommand]
    private async Task CaptureRegionAsync()
    {
        if (_captureCoordinator is null)
        {
            ShowComingSoon();
            return;
        }

        var outcome = await _captureCoordinator.CaptureRegionAsync();
        StatusMessage = outcome.Message;
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        if (_recordingCoordinator is null)
        {
            ShowComingSoon();
            return;
        }

        var outcome = await _recordingCoordinator.StartRegionRecordingAsync();
        StatusMessage = outcome.Message;
    }

    [RelayCommand]
    private void OpenHistory()
    {
        if (_historyWindow is null)
        {
            ShowComingSoon();
            return;
        }

        _historyWindow.Show();
        _historyWindow.Activate();
    }

    /// <summary>用最近一张截图打开标注编辑器。</summary>
    [RelayCommand]
    private async Task AnnotateLatestAsync()
    {
        var entry = FindLatestScreenshot();
        if (entry is null)
        {
            StatusMessage = L["noRecentCapture"];
            return;
        }

        if (_frameLoader is null || _annotationRenderer is null || _screenshots is null || _clipboard is null)
        {
            StatusMessage = L["statusUnavailable"];
            return;
        }

        var frame = await _frameLoader.LoadAsync(entry.FilePath);
        if (frame is null)
        {
            StatusMessage = L["historyLoadFailed"];
            return;
        }

        var result = new ScreenshotResult(entry, Array.Empty<byte>(), entry.FilePath);
        new AnnotationEditorWindow(
            result,
            frame,
            L,
            _annotationRenderer,
            _screenshots,
            _clipboard,
            _ocr,
            _uploads,
            _settingsService,
            _imageEncoder).Show();
        StatusMessage = L["statusReady"];
    }

    /// <summary>对最近一张截图执行 OCR 并展示结果。</summary>
    [RelayCommand]
    private async Task OcrLatestAsync()
    {
        var entry = FindLatestScreenshot();
        if (entry is null)
        {
            StatusMessage = L["noRecentCapture"];
            return;
        }

        if (_frameLoader is null || _ocr is null)
        {
            StatusMessage = L["statusUnavailable"];
            return;
        }

        var frame = await _frameLoader.LoadAsync(entry.FilePath);
        if (frame is null)
        {
            StatusMessage = L["historyLoadFailed"];
            return;
        }

        try
        {
            StatusMessage = L["ocrRecognizing"];
            var result = await _ocr.RecognizeAsync(frame);
            new OcrResultWindow(frame, result, _ocr, L).Show();
            StatusMessage = L["statusReady"];
        }
        catch (Exception exception)
        {
            StatusMessage = $"{L["ocrFailed"]}: {exception.Message}";
        }
    }

    /// <summary>用最近一张截图打开上传对话框。</summary>
    [RelayCommand]
    private async Task UploadLatestAsync()
    {
        var entry = FindLatestScreenshot();
        if (entry is null)
        {
            StatusMessage = L["noRecentCapture"];
            return;
        }

        if (_frameLoader is null || _uploads is null || _imageEncoder is null)
        {
            StatusMessage = L["statusUnavailable"];
            return;
        }

        var frame = await _frameLoader.LoadAsync(entry.FilePath);
        if (frame is null)
        {
            StatusMessage = L["historyLoadFailed"];
            return;
        }

        try
        {
            var bytes = await _imageEncoder.EncodeAsync(frame, "png", 95);
            var fileName = $"{Path.GetFileNameWithoutExtension(entry.FilePath)}.png";
            new UploadDialogWindow(bytes, fileName, _uploads, _settingsService, L).Show();
            StatusMessage = L["statusReady"];
        }
        catch (Exception exception)
        {
            StatusMessage = $"{L["uploadFailed"]}: {exception.Message}";
        }
    }

    private HistoryEntry? FindLatestScreenshot() =>
        _historyRepository
            ?.Query(new HistoryQuery(Kind: CaptureKind.Screenshot, Limit: 1))
            .FirstOrDefault();

    [RelayCommand]
    private void RestoreHotkeys()
    {
        var defaults = HotkeyCatalog.CreateDefaults(_platform.Kind);
        foreach (var row in Hotkeys)
        {
            row.ErrorMessage = null;
            if (defaults.TryGetValue(row.ActionKey, out var gesture))
            {
                row.Gesture = gesture;
            }
        }

        StatusMessage = L["statusReady"];
    }

    /// <summary>
    /// 校验快捷键、注册到系统、持久化草稿并应用开机自启。
    /// 任一环节失败都会把原因写回对应行并中止保存，避免"显示已保存但实际不生效"。
    /// </summary>
    [RelayCommand]
    private void SaveSettings()
    {
        foreach (var row in Hotkeys)
        {
            row.ErrorMessage = null;
        }

        var gestures = Hotkeys.ToDictionary(
            static row => row.ActionKey,
            static row => row.Gesture,
            StringComparer.OrdinalIgnoreCase);
        _draft.Hotkeys = new Dictionary<string, string>(gestures, StringComparer.OrdinalIgnoreCase);

        var conflicts = HotkeyConflictDetector.Detect(gestures);
        if (conflicts.Count > 0)
        {
            MarkConflicts(conflicts);
            StatusMessage = L["statusConflicts"];
            return;
        }

        var apply = _hotkeys?.Apply(gestures);
        if (apply is { Success: false })
        {
            MarkHotkeyFailures(apply);
            StatusMessage = L["statusConflicts"];
            return;
        }

        try
        {
            _settingsService.Save(_draft);
        }
        catch (SettingsValidationException exception)
        {
            MarkConflicts(exception.Conflicts);
            StatusMessage = L["statusConflicts"];
            return;
        }

        var autoStartResult = _autoStart.SetEnabled(_draft.LaunchAtLogin);
        _autoStartState = autoStartResult.State;
        OnPropertyChanged(nameof(CanLaunchAtLogin));
        OnPropertyChanged(nameof(AutoStartStatusText));

        if (!autoStartResult.Success)
        {
            if (_autoStartState == AutoStartState.Unavailable)
            {
                _draft.LaunchAtLogin = false;
                OnPropertyChanged(nameof(LaunchAtLogin));
            }

            StatusMessage = string.IsNullOrWhiteSpace(autoStartResult.Error)
                ? L["autoStartFailed"]
                : $"{L["autoStartFailed"]}: {autoStartResult.Error}";
            return;
        }

        StatusMessage = L["statusSaved"];
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (_isCheckingUpdates)
        {
            return;
        }

        if (_updates is null)
        {
            SetUpdateStatus("aboutUpToDate");
            return;
        }

        IsCheckingUpdates = true;
        SetUpdateStatus("updateChecking");
        try
        {
            var result = await _updates
                .CheckAsync(_draft.UpdateChannel, force: true)
                .ConfigureAwait(true);

            HasUpdate = result.State == UpdateCheckState.UpdateAvailable;
            _latestReleaseUrl = result.ReleaseUrl;
            OnPropertyChanged(nameof(HasUpdate));

            switch (result.State)
            {
                case UpdateCheckState.UpdateAvailable:
                    SetUpdateStatus("updateAvailable", result.Latest?.Display ?? string.Empty);
                    break;
                case UpdateCheckState.UpToDate:
                    SetUpdateStatus("aboutUpToDate");
                    break;
                case UpdateCheckState.Unreachable:
                    SetUpdateStatus("updateOffline");
                    break;
                default:
                    SetUpdateStatus("updateFailed");
                    break;
            }

            StatusMessage = UpdateStatusText ?? L["statusReady"];
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        var url = string.IsNullOrWhiteSpace(_latestReleaseUrl)
            ? "https://github.com/2373434278/SuperScreenCapture/releases"
            : _latestReleaseUrl;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            StatusMessage = $"{L["updateFailed"]}: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshStorageAsync()
    {
        if (_storage is null)
        {
            StatusMessage = L["storageUnavailable"];
            return;
        }

        try
        {
            _storageUsage = await Task.Run(_storage.GetUsage).ConfigureAwait(true);
            OnPropertyChanged(nameof(StorageSummaryText));
            OnPropertyChanged(nameof(StorageDetailText));
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task CleanupStorageAsync()
    {
        if (_storage is null)
        {
            StatusMessage = L["storageUnavailable"];
            return;
        }

        if (!_isCleanupArmed)
        {
            ArmStorageAction(cleanup: true);
            return;
        }

        if (_draft.HistoryRetentionDays <= 0)
        {
            StatusMessage = L["storageNoRetention"];
            return;
        }

        ResetStorageArmed();
        var cutoff = DateTimeOffset.Now.AddDays(-_draft.HistoryRetentionDays);
        var result = await _storage
            .DeleteOlderThanAsync(cutoff, _keepFavoritesOnCleanup)
            .ConfigureAwait(true);
        StatusMessage = result.DeletedEntries == 0
            ? L["storageNothingToClean"]
            : string.Format(CultureInfo.CurrentCulture, L["storageCleaned"], result.DeletedEntries, FormatBytes(result.ReclaimedBytes));
        await RefreshStorageAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearStorageAsync()
    {
        if (_storage is null)
        {
            StatusMessage = L["storageUnavailable"];
            return;
        }

        if (!_isClearArmed)
        {
            ArmStorageAction(cleanup: false);
            return;
        }

        ResetStorageArmed();
        var result = await _storage.ClearAsync(_keepFavoritesOnCleanup).ConfigureAwait(true);
        StatusMessage = result.DeletedEntries == 0
            ? L["storageNothingToClean"]
            : string.Format(CultureInfo.CurrentCulture, L["storageCleaned"], result.DeletedEntries, FormatBytes(result.ReclaimedBytes));
        await RefreshStorageAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenStorageFolder()
    {
        if (_storage is null)
        {
            StatusMessage = L["storageUnavailable"];
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_storage.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void ArmStorageAction(bool cleanup)
    {
        _isCleanupArmed = cleanup;
        _isClearArmed = !cleanup;
        NotifyStorageButtons();

        _storageArmTimer?.Stop();
        _storageArmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _storageArmTimer.Tick += (_, _) => ResetStorageArmed();
        _storageArmTimer.Start();
    }

    private void ResetStorageArmed()
    {
        _storageArmTimer?.Stop();
        _storageArmTimer = null;
        _isCleanupArmed = false;
        _isClearArmed = false;
        NotifyStorageButtons();
    }

    private void NotifyStorageButtons()
    {
        OnPropertyChanged(nameof(CleanupButtonText));
        OnPropertyChanged(nameof(ClearButtonText));
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024d / 1024d:0.0} GB",
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B"
    };

    public void ShowStatus(string message) => StatusMessage = message;

    [RelayCommand]
    public void ShowComingSoon() => StatusMessage = L["statusComingSoon"];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _storageArmTimer?.Stop();
        _localizer.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string language)
    {
        var gestures = Hotkeys.Count > 0
            ? Hotkeys.ToDictionary(static row => row.ActionKey, static row => row.Gesture, StringComparer.OrdinalIgnoreCase)
            : _draft.Hotkeys;

        RebuildHotkeys(gestures);

        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(IsChinese));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(RecordingCodec));
        OnPropertyChanged(nameof(FileNamePreview));
        OnPropertyChanged(nameof(AutoStartStatusText));
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(StorageSummaryText));
        OnPropertyChanged(nameof(StorageDetailText));
        NotifyStorageButtons();
        StatusMessage = L["statusReady"];
    }

    private void RebuildHotkeys(IReadOnlyDictionary<string, string>? gestures = null)
    {
        gestures ??= _draft.Hotkeys;

        Hotkeys.Clear();
        foreach (var action in HotkeyCatalog.Actions)
        {
            var gesture = gestures.TryGetValue(action.Key, out var stored) && !string.IsNullOrWhiteSpace(stored)
                ? stored
                : HotkeyCatalog.GetDefaultGesture(action, _platform.Kind);

            Hotkeys.Add(new HotkeyRowViewModel(
                action.Key,
                action.LabelKey,
                L[action.LabelKey],
                gesture,
                L["hotkeyPressPrompt"]));
        }
    }

    private void MarkConflicts(IReadOnlyList<HotkeyConflict> conflicts)
    {
        foreach (var conflict in conflicts)
        {
            var firstAction = HotkeyCatalog.Actions.FirstOrDefault(action => action.Key == conflict.FirstAction);
            var secondAction = HotkeyCatalog.Actions.FirstOrDefault(action => action.Key == conflict.SecondAction);
            var firstRow = Hotkeys.FirstOrDefault(row => row.ActionKey == conflict.FirstAction);
            var secondRow = Hotkeys.FirstOrDefault(row => row.ActionKey == conflict.SecondAction);

            if (firstRow is not null && secondAction is not null)
            {
                firstRow.ErrorMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    L["conflictWith"],
                    L[secondAction.LabelKey]);
            }

            if (secondRow is not null && firstAction is not null)
            {
                secondRow.ErrorMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    L["conflictWith"],
                    L[firstAction.LabelKey]);
            }
        }
    }

    private void MarkHotkeyFailures(HotkeyApplyResult result)
    {
        foreach (var actionKey in result.InvalidActionKeys)
        {
            var row = Hotkeys.FirstOrDefault(item => item.ActionKey == actionKey);
            if (row is not null)
            {
                row.ErrorMessage = L["hotkeyInvalid"];
            }
        }

        foreach (var failure in result.Failures)
        {
            var row = Hotkeys.FirstOrDefault(item => item.ActionKey == failure.ActionKey);
            if (row is null)
            {
                continue;
            }

            row.ErrorMessage = failure.Error switch
            {
                HotkeyRegistrationError.AlreadyInUse => L["hotkeyInUse"],
                HotkeyRegistrationError.Unsupported => L["hotkeyUnavailable"],
                _ => string.IsNullOrWhiteSpace(failure.Detail) ? L["hotkeyUnavailable"] : failure.Detail
            };
        }
    }

    private void SetUpdateStatus(string? key, string? argument = null)
    {
        _updateStatusKey = key;
        _updateStatusArgument = argument;
        OnPropertyChanged(nameof(UpdateStatusText));
    }

    private bool IsProviderSelected(string provider) =>
        _draft.UploadEnabled && _draft.Upload.Provider == provider;

    private void NotifyUploadProviders()
    {
        OnPropertyChanged(nameof(UploadEnabled));
        OnPropertyChanged(nameof(IsUploadS3));
        OnPropertyChanged(nameof(IsUploadImgur));
        OnPropertyChanged(nameof(IsUploadCustom));
        OnPropertyChanged(nameof(IsUploadS3Active));
        OnPropertyChanged(nameof(IsUploadImgurActive));
        OnPropertyChanged(nameof(IsUploadCustomActive));
    }

    private void NotifyTabProperties()
    {
        OnPropertyChanged(nameof(IsGeneralTab));
        OnPropertyChanged(nameof(IsHotkeysTab));
        OnPropertyChanged(nameof(IsOutputTab));
        OnPropertyChanged(nameof(IsRecordingTab));
        OnPropertyChanged(nameof(IsOcrTab));
        OnPropertyChanged(nameof(IsUploadTab));
        OnPropertyChanged(nameof(IsAboutTab));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
    }

    private static void ApplyTheme(string theme)
    {
        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }
}
