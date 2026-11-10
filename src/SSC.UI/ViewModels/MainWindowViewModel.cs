using System.Collections.ObjectModel;
using System.ComponentModel;
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
using SSC.UI.Dialogs;

namespace SSC.UI.ViewModels;

/// <summary>
/// 设置主窗口（原型 P5）的视图模型。
/// 内部持有一份 <see cref="AppSettings"/> 草稿，所有编辑都会立即写入草稿并调用
/// <see cref="SettingsService.Save"/> 持久化；校验失败时回滚对应项并在行内展示错误。
/// 语言/主题等可预览选项仍然先应用到运行时，再随其余设置一起落盘。
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
    private readonly Dictionary<string, string> _hotkeyBaselines = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressHotkeyCommit;
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

    public bool IsAutoTheme => _draft.Theme == "auto";

    public bool IsDarkTheme => _draft.Theme == "dark";

    public bool IsLightTheme => _draft.Theme == "light";

    // ---------------------------------------------------------------- 通用

    public bool LaunchAtLogin
    {
        get => _draft.LaunchAtLogin;
        set
        {
            var previous = _draft.LaunchAtLogin;
            if (previous == value)
            {
                return;
            }

            var previousState = _autoStartState;
            var result = _autoStart.SetEnabled(value);
            _autoStartState = result.State;
            NotifyAutoStart();

            if (!result.Success || _autoStartState == AutoStartState.Unavailable)
            {
                _draft.LaunchAtLogin = previous;
                NotifyAutoStart();
                StatusMessage = string.IsNullOrWhiteSpace(result.Error)
                    ? L["autoStartFailed"]
                    : $"{L["autoStartFailed"]}: {result.Error}";
                return;
            }

            _draft.LaunchAtLogin = value;
            NotifyAutoStart();
            if (TryPersist())
            {
                return;
            }

            // 持久化失败时把系统登录项也回滚，避免"界面关掉但系统仍自启"。
            _autoStart.SetEnabled(previous);
            _autoStartState = previousState;
            _draft.LaunchAtLogin = previous;
            NotifyAutoStart();
        }
    }

    public bool CloseToTray
    {
        get => _draft.CloseToTray;
        set
        {
            var previous = _draft.CloseToTray;
            if (previous == value)
            {
                return;
            }

            CommitSetting(
                () => _draft.CloseToTray = value,
                () => _draft.CloseToTray = previous,
                nameof(CloseToTray));
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
            var previous = _draft.SaveDirectory;
            if (previous == normalized)
            {
                return;
            }

            CommitSetting(
                () => _draft.SaveDirectory = normalized,
                () => _draft.SaveDirectory = previous,
                nameof(SaveDirectory),
                nameof(FileNamePreview));
        }
    }

    /// <summary>支持 {date}、{type}、{n} 占位符的文件名模板。</summary>
    public string FileNamePattern
    {
        get => _draft.FileNamePattern;
        set
        {
            var normalized = value ?? string.Empty;
            var previous = _draft.FileNamePattern;
            if (previous == normalized)
            {
                return;
            }

            CommitSetting(
                () => _draft.FileNamePattern = normalized,
                () => _draft.FileNamePattern = previous,
                nameof(FileNamePattern),
                nameof(FileNamePreview));
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
            var previous = _draft.JpegQuality;
            if (previous == clamped)
            {
                return;
            }

            CommitSetting(
                () => _draft.JpegQuality = clamped,
                () => _draft.JpegQuality = previous,
                nameof(JpegQuality),
                nameof(JpegQualityText));
        }
    }

    public string JpegQualityText => _draft.JpegQuality.ToString(CultureInfo.InvariantCulture);

    public bool AutoCopy
    {
        get => _draft.AutoCopy;
        set
        {
            var previous = _draft.AutoCopy;
            if (previous == value)
            {
                return;
            }

            CommitSetting(
                () => _draft.AutoCopy = value,
                () => _draft.AutoCopy = previous,
                nameof(AutoCopy));
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
            var previous = _draft.CaptureDelaySeconds;
            if (previous == clamped)
            {
                return;
            }

            CommitSetting(
                () => _draft.CaptureDelaySeconds = clamped,
                () => _draft.CaptureDelaySeconds = previous,
                nameof(CaptureDelaySeconds),
                nameof(CaptureDelayIndex));
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
            var previous = _draft.UpdateChannel;
            if (previous == channel)
            {
                return;
            }

            CommitSetting(
                () => _draft.UpdateChannel = channel,
                () => _draft.UpdateChannel = previous,
                nameof(UpdateChannelIndex),
                nameof(IsStableChannel),
                nameof(IsPreviewChannel));
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
            var previous = _draft.HistoryRetentionDays;
            if (previous == days)
            {
                return;
            }

            CommitSetting(
                () => _draft.HistoryRetentionDays = days,
                () => _draft.HistoryRetentionDays = previous,
                nameof(RetentionIndex));
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
            var previous = _draft.OpenEditor;
            if (previous == value)
            {
                return;
            }

            CommitSetting(
                () => _draft.OpenEditor = value,
                () => _draft.OpenEditor = previous,
                nameof(OpenEditor));
        }
    }

    public bool ShowQuickAccess
    {
        get => _draft.ShowQuickAccess;
        set
        {
            var previous = _draft.ShowQuickAccess;
            if (previous == value)
            {
                return;
            }

            CommitSetting(
                () => _draft.ShowQuickAccess = value,
                () => _draft.ShowQuickAccess = previous,
                nameof(ShowQuickAccess));
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
            if (value < 0 || value >= GifFpsOptions.Length)
            {
                return;
            }

            var fps = GifFpsOptions[value];
            var previous = _draft.GifFps;
            if (previous == fps)
            {
                return;
            }

            CommitSetting(
                () => _draft.GifFps = fps,
                () => _draft.GifFps = previous,
                nameof(GifFpsIndex));
        }
    }

    public int GifDurationIndex
    {
        get => Math.Max(0, Array.IndexOf(GifDurationOptions, _draft.GifMaxDurationSeconds));
        set
        {
            if (value < 0 || value >= GifDurationOptions.Length)
            {
                return;
            }

            var seconds = GifDurationOptions[value];
            var previous = _draft.GifMaxDurationSeconds;
            if (previous == seconds)
            {
                return;
            }

            CommitSetting(
                () => _draft.GifMaxDurationSeconds = seconds,
                () => _draft.GifMaxDurationSeconds = previous,
                nameof(GifDurationIndex));
        }
    }

    public int GifDimensionIndex
    {
        get => Math.Max(0, Array.IndexOf(GifDimensionOptions, _draft.GifMaxDimension));
        set
        {
            if (value < 0 || value >= GifDimensionOptions.Length)
            {
                return;
            }

            var dimension = GifDimensionOptions[value];
            var previous = _draft.GifMaxDimension;
            if (previous == dimension)
            {
                return;
            }

            CommitSetting(
                () => _draft.GifMaxDimension = dimension,
                () => _draft.GifMaxDimension = previous,
                nameof(GifDimensionIndex));
        }
    }

    // ---------------------------------------------------------------- 录制

    public int RecordingFps
    {
        get => _draft.RecordingFps;
        set
        {
            var previous = _draft.RecordingFps;
            if (previous == value)
            {
                return;
            }

            CommitSetting(
                () => _draft.RecordingFps = value,
                () => _draft.RecordingFps = previous,
                nameof(RecordingFps),
                nameof(IsFps30),
                nameof(IsFps60));
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
            var previous = _draft.RecordingMaxHeight;
            if (previous == height)
            {
                return;
            }

            CommitSetting(
                () => _draft.RecordingMaxHeight = height,
                () => _draft.RecordingMaxHeight = previous,
                nameof(RecordingMaxHeightIndex));
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
            var previous = _draft.RecordingBitrateMbps;
            if (previous == bitrate)
            {
                return;
            }

            CommitSetting(
                () => _draft.RecordingBitrateMbps = bitrate,
                () => _draft.RecordingBitrateMbps = previous,
                nameof(RecordingBitrateIndex));
        }
    }

    public bool RecordMicrophone
    {
        get => _draft.RecordMicrophone;
        set
        {
            var previous = _draft.RecordMicrophone;
            if (previous == value)
            {
                return;
            }

            CommitSetting(
                () => _draft.RecordMicrophone = value,
                () => _draft.RecordMicrophone = previous,
                nameof(RecordMicrophone));
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
            var previous = _draft.OcrEngine;
            if (previous == engine)
            {
                return;
            }

            CommitSetting(
                () => _draft.OcrEngine = engine,
                () => _draft.OcrEngine = previous,
                nameof(OcrEngineIndex));
        }
    }

    // ---------------------------------------------------------------- 上传

    public bool UploadEnabled
    {
        get => _draft.UploadEnabled;
        set
        {
            var previous = _draft.UploadEnabled;
            if (previous == value)
            {
                return;
            }

            CommitSetting(
                () => _draft.UploadEnabled = value,
                () => _draft.UploadEnabled = previous,
                nameof(UploadEnabled),
                nameof(IsUploadS3),
                nameof(IsUploadImgur),
                nameof(IsUploadCustom),
                nameof(IsUploadS3Active),
                nameof(IsUploadImgurActive),
                nameof(IsUploadCustomActive));
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
            var previous = _draft.Upload.Bucket;
            if (previous == normalized)
            {
                return;
            }

            CommitSetting(
                () => _draft.Upload.Bucket = normalized,
                () => _draft.Upload.Bucket = previous,
                nameof(UploadBucket));
        }
    }

    public string UploadRegion
    {
        get => _draft.Upload.Region;
        set
        {
            var normalized = value ?? string.Empty;
            var previous = _draft.Upload.Region;
            if (previous == normalized)
            {
                return;
            }

            CommitSetting(
                () => _draft.Upload.Region = normalized,
                () => _draft.Upload.Region = previous,
                nameof(UploadRegion));
        }
    }

    public string UploadAccessKey
    {
        get => _draft.Upload.AccessKey;
        set
        {
            var normalized = value ?? string.Empty;
            var previous = _draft.Upload.AccessKey;
            if (previous == normalized)
            {
                return;
            }

            CommitSetting(
                () => _draft.Upload.AccessKey = normalized,
                () => _draft.Upload.AccessKey = previous,
                nameof(UploadAccessKey));
        }
    }

    public string UploadSecretKey
    {
        get => _draft.Upload.SecretKey;
        set
        {
            var normalized = value ?? string.Empty;
            var previous = _draft.Upload.SecretKey;
            if (previous == normalized)
            {
                return;
            }

            CommitSetting(
                () => _draft.Upload.SecretKey = normalized,
                () => _draft.Upload.SecretKey = previous,
                nameof(UploadSecretKey));
        }
    }

    public string UploadClientId
    {
        get => _draft.Upload.ClientId;
        set
        {
            var normalized = value ?? string.Empty;
            var previous = _draft.Upload.ClientId;
            if (previous == normalized)
            {
                return;
            }

            CommitSetting(
                () => _draft.Upload.ClientId = normalized,
                () => _draft.Upload.ClientId = previous,
                nameof(UploadClientId));
        }
    }

    public string UploadEndpoint
    {
        get => _draft.Upload.Endpoint;
        set
        {
            var normalized = value ?? string.Empty;
            var previous = _draft.Upload.Endpoint;
            if (previous == normalized)
            {
                return;
            }

            CommitSetting(
                () => _draft.Upload.Endpoint = normalized,
                () => _draft.Upload.Endpoint = previous,
                nameof(UploadEndpoint));
        }
    }

    public string UploadToken
    {
        get => _draft.Upload.Token;
        set
        {
            var normalized = value ?? string.Empty;
            var previous = _draft.Upload.Token;
            if (previous == normalized)
            {
                return;
            }

            CommitSetting(
                () => _draft.Upload.Token = normalized,
                () => _draft.Upload.Token = previous,
                nameof(UploadToken));
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

        var previous = _draft.Language;
        if (previous == language)
        {
            return;
        }

        _draft.Language = language;
        _localizer.SetLanguage(language);
        if (TryPersist())
        {
            return;
        }

        _draft.Language = previous;
        _localizer.SetLanguage(previous);
    }

    [RelayCommand]
    private void SetTheme(string? theme)
    {
        if (theme is not ("auto" or "dark" or "light"))
        {
            return;
        }

        var previous = _draft.Theme;
        if (previous == theme)
        {
            return;
        }

        _draft.Theme = theme;
        ApplyTheme(theme);
        NotifyTheme();
        if (TryPersist())
        {
            NotifyTheme();
            return;
        }

        _draft.Theme = previous;
        ApplyTheme(previous);
        NotifyTheme();
    }

    /// <summary>三态循环：跟随系统 → 深色 → 浅色 → 跟随系统。</summary>
    [RelayCommand]
    private void ToggleTheme() => SetTheme(_draft.Theme switch
    {
        "auto" => "dark",
        "dark" => "light",
        _ => "auto"
    });

    /// <summary>选择默认截图格式（png / jpg / webp）。</summary>
    [RelayCommand]
    private void SetFormat(string? format)
    {
        if (format is not ("png" or "jpg" or "webp"))
        {
            return;
        }

        var previous = _draft.DefaultFormat;
        if (previous == format)
        {
            return;
        }

        _draft.DefaultFormat = format;
        NotifyFormat();
        if (TryPersist())
        {
            NotifyFormat();
            return;
        }

        _draft.DefaultFormat = previous;
        NotifyFormat();
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

        var previousProvider = _draft.Upload.Provider;
        var previousEnabled = _draft.UploadEnabled;
        if (previousProvider == provider && previousEnabled)
        {
            NotifyUploadProviders();
            return;
        }

        _draft.Upload.Provider = provider;
        _draft.UploadEnabled = true;
        NotifyUploadProviders();
        if (TryPersist())
        {
            NotifyUploadProviders();
            return;
        }

        _draft.Upload.Provider = previousProvider;
        _draft.UploadEnabled = previousEnabled;
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
            await DialogPresenter.ShowAsync(new UploadDialogWindow(bytes, fileName, _uploads, _settingsService, L));
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
        var snapshot = CollectGestures();

        _suppressHotkeyCommit = true;
        try
        {
            foreach (var row in Hotkeys)
            {
                row.ErrorMessage = null;
                if (defaults.TryGetValue(row.ActionKey, out var gesture))
                {
                    row.Gesture = gesture;
                }
            }
        }
        finally
        {
            _suppressHotkeyCommit = false;
        }

        var gestures = CollectGestures();
        var apply = _hotkeys?.Apply(gestures);
        if (apply is { Success: false })
        {
            MarkHotkeyFailures(apply);
            RestoreGestureSnapshot(snapshot);
            StatusMessage = L["statusConflicts"];
            return;
        }

        if (!TryPersist())
        {
            RestoreGestureSnapshot(snapshot);
            _hotkeys?.Apply(snapshot);
            return;
        }

        SyncHotkeyRowsFromDraft();
        UpdateHotkeyBaselines();
        StatusMessage = L["statusSaved"];
    }

    /// <summary>
    /// 恢复全部设置默认值（由确认对话框调用）：热键先应用到系统（失败也继续），
    /// 再整体保存；随后同步语言、主题与开机自启，并全量刷新绑定。
    /// </summary>
    [RelayCommand]
    private void ResetAllSettings()
    {
        var defaults = _settingsService.CreateDefaults();

        _suppressHotkeyCommit = true;
        try
        {
            _draft = defaults;
            foreach (var row in Hotkeys)
            {
                row.ErrorMessage = null;
                if (defaults.Hotkeys.TryGetValue(row.ActionKey, out var gesture))
                {
                    row.Gesture = gesture;
                }
            }
        }
        finally
        {
            _suppressHotkeyCommit = false;
        }

        var apply = _hotkeys?.Apply(defaults.Hotkeys);
        if (apply is { Success: false })
        {
            MarkHotkeyFailures(apply);
        }

        if (!TryPersist())
        {
            return;
        }

        SyncHotkeyRowsFromDraft();
        UpdateHotkeyBaselines();
        _localizer.SetLanguage(_draft.Language);
        ApplyTheme(_draft.Theme);
        var autoStartResult = _autoStart.SetEnabled(false);
        _autoStartState = autoStartResult.State;
        OnPropertyChanged(string.Empty);
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

        foreach (var row in Hotkeys)
        {
            row.PropertyChanged -= OnHotkeyRowPropertyChanged;
        }

        Hotkeys.Clear();
        _hotkeyBaselines.Clear();
        foreach (var action in HotkeyCatalog.Actions)
        {
            var gesture = gestures.TryGetValue(action.Key, out var stored) && !string.IsNullOrWhiteSpace(stored)
                ? stored
                : HotkeyCatalog.GetDefaultGesture(action, _platform.Kind);

            var row = new HotkeyRowViewModel(
                action.Key,
                action.LabelKey,
                L[action.LabelKey],
                gesture,
                L["hotkeyPressPrompt"]);
            row.PropertyChanged += OnHotkeyRowPropertyChanged;
            Hotkeys.Add(row);
            _hotkeyBaselines[action.Key] = gesture;
        }
    }

    /// <summary>行编辑只有 <c>Gesture</c> 需要提交；批量恢复期间通过抑制位跳过。</summary>
    private void OnHotkeyRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HotkeyRowViewModel.Gesture) || _suppressHotkeyCommit)
        {
            return;
        }

        if (sender is HotkeyRowViewModel row)
        {
            CommitHotkeyRow(row);
        }
    }

    /// <summary>
    /// 单行热键的行级提交流程：全量冲突检测 → 回滚该行（冲突时）→
    /// <see cref="HotkeyManager.Apply"/>（事务式，SameAsCurrent 短路）→ 持久化。
    /// 任一步失败都把该行还原到编辑前的值，不写盘。
    /// </summary>
    private void CommitHotkeyRow(HotkeyRowViewModel row)
    {
        var previous = _hotkeyBaselines.TryGetValue(row.ActionKey, out var baseline)
            ? baseline
            : string.Empty;
        var current = row.Gesture ?? string.Empty;
        if (string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var item in Hotkeys)
        {
            item.ErrorMessage = null;
        }

        var gestures = CollectGestures();
        var conflicts = HotkeyConflictDetector.Detect(gestures);
        if (conflicts.Count > 0)
        {
            RevertRowGesture(row, previous);
            MarkConflicts(conflicts);
            StatusMessage = L["statusConflicts"];
            return;
        }

        var apply = _hotkeys?.Apply(gestures);
        if (apply is { Success: false })
        {
            RevertRowGesture(row, previous);
            MarkHotkeyFailures(apply);
            StatusMessage = L["statusConflicts"];
            return;
        }

        if (!TryPersist())
        {
            RevertRowGesture(row, previous);
            _hotkeys?.Apply(CollectGestures());
            return;
        }

        // Normalize 可能把空值补回默认组合键，回读后同步到行上。
        SyncHotkeyRowsFromDraft();
        UpdateHotkeyBaselines();
    }

    private void RevertRowGesture(HotkeyRowViewModel row, string gesture)
    {
        _suppressHotkeyCommit = true;
        try
        {
            row.Gesture = gesture;
        }
        finally
        {
            _suppressHotkeyCommit = false;
        }
    }

    private void RestoreGestureSnapshot(IReadOnlyDictionary<string, string> snapshot)
    {
        _suppressHotkeyCommit = true;
        try
        {
            foreach (var row in Hotkeys)
            {
                if (snapshot.TryGetValue(row.ActionKey, out var gesture))
                {
                    row.Gesture = gesture;
                }
            }
        }
        finally
        {
            _suppressHotkeyCommit = false;
        }
    }

    /// <summary>把回读草稿里的规范快捷键同步到行上（抑制行级提交）。</summary>
    private void SyncHotkeyRowsFromDraft()
    {
        _suppressHotkeyCommit = true;
        try
        {
            foreach (var row in Hotkeys)
            {
                if (_draft.Hotkeys.TryGetValue(row.ActionKey, out var gesture) &&
                    !string.Equals(row.Gesture, gesture, StringComparison.Ordinal))
                {
                    row.Gesture = gesture;
                }
            }
        }
        finally
        {
            _suppressHotkeyCommit = false;
        }
    }

    private void UpdateHotkeyBaselines()
    {
        foreach (var row in Hotkeys)
        {
            _hotkeyBaselines[row.ActionKey] = row.Gesture;
        }
    }

    /// <summary>
    /// 把草稿交给 <see cref="SettingsService.Save"/>（Normalize + 冲突校验 + 写盘 + Changed）。
    /// 成功后用服务回读的规范值替换草稿，让 UI 反映被强制修正的设置；
    /// 失败时不写盘并返回 false，由调用方回滚刚修改的字段。
    /// </summary>
    private bool TryPersist()
    {
        _draft.Hotkeys = CollectGestures();
        try
        {
            _settingsService.Save(_draft);
        }
        catch (SettingsValidationException exception)
        {
            MarkConflicts(exception.Conflicts);
            StatusMessage = L["statusConflicts"];
            return false;
        }

        _draft = _settingsService.Current;
        StatusMessage = L["statusSaved"];
        return true;
    }

    /// <summary>写入单个设置并立即持久化；保存失败时还原为操作前的值。</summary>
    private void CommitSetting(
        Action apply,
        Action rollback,
        string propertyName,
        params string[] dependentProperties)
    {
        apply();
        NotifySetting(propertyName, dependentProperties);
        if (TryPersist())
        {
            // 回读 Normalize 强制后的值，刷新绑定显示真实生效的内容。
            NotifySetting(propertyName, dependentProperties);
            return;
        }

        rollback();
        NotifySetting(propertyName, dependentProperties);
    }

    private void NotifySetting(string propertyName, params string[] dependentProperties)
    {
        OnPropertyChanged(propertyName);
        foreach (var dependent in dependentProperties)
        {
            OnPropertyChanged(dependent);
        }
    }

    private Dictionary<string, string> CollectGestures() =>
        Hotkeys.ToDictionary(
            static row => row.ActionKey,
            static row => row.Gesture,
            StringComparer.OrdinalIgnoreCase);

    private void NotifyAutoStart()
    {
        OnPropertyChanged(nameof(LaunchAtLogin));
        OnPropertyChanged(nameof(CanLaunchAtLogin));
        OnPropertyChanged(nameof(AutoStartStatusText));
        OnPropertyChanged(nameof(HasAutoStartStatus));
    }

    /// <summary>
    /// 系统外观变化（auto 下由 <c>Application.ActualThemeVariantChanged</c> 触发）时刷新主题绑定。
    /// 只通知、不写盘，避免与设置保存互相触发形成反馈环。
    /// </summary>
    public void RefreshThemeState() => NotifyTheme();

    private void NotifyTheme()
    {
        OnPropertyChanged(nameof(IsAutoTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
    }

    private void NotifyFormat()
    {
        OnPropertyChanged(nameof(IsPng));
        OnPropertyChanged(nameof(IsJpg));
        OnPropertyChanged(nameof(IsWebp));
        OnPropertyChanged(nameof(FileNamePreview));
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
            application.RequestedThemeVariant = theme switch
            {
                "light" => ThemeVariant.Light,
                "dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }
}
