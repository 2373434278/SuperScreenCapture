using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSC.Application;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Platform.Abstractions;
using SSC.UI.Capture;
using SSC.UI.History;
using SSC.UI.Recording;

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
    private readonly PlatformDescriptor _platform;
    private readonly CaptureCoordinator? _captureCoordinator;
    private readonly RecordingCoordinator? _recordingCoordinator;
    private readonly HistoryWindow? _historyWindow;
    private AppSettings _draft;
    private string _activeTab = "general";
    private bool _disposed;

    /// <summary>由依赖注入构造设置视图模型。</summary>
    public MainWindowViewModel(
        SettingsService settingsService,
        ILocalizer localizer,
        IAutoStartService autoStart,
        PlatformDescriptor platform,
        CaptureCoordinator? captureCoordinator = null,
        RecordingCoordinator? recordingCoordinator = null,
        HistoryWindow? historyWindow = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _autoStart = autoStart ?? throw new ArgumentNullException(nameof(autoStart));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _captureCoordinator = captureCoordinator;
        _recordingCoordinator = recordingCoordinator;
        _historyWindow = historyWindow;

        _draft = _settingsService.Current;
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

    public bool IsMacOS => _platform.Kind is PlatformKind.MacOS or PlatformKind.Mock;

    public bool IsWindows => _platform.Kind == PlatformKind.Windows;

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

    /// <summary>
    /// 自动保存目前恒为开启：截图必须先落盘进入历史记录，才能继续标注。
    /// 因此原型中的开关在真实界面里以只读状态展示。
    /// </summary>
    public bool AutoSave => true;

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
    /// 校验快捷键、持久化草稿并应用系统级副作用（开机自启）。
    /// 若检测到快捷键冲突，则中止保存并把错误写回对应行。
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

        try
        {
            _settingsService.Save(_draft);
            _autoStart.SetEnabled(_draft.LaunchAtLogin);
            StatusMessage = L["statusSaved"];
        }
        catch (SettingsValidationException exception)
        {
            MarkConflicts(exception.Conflicts);
            StatusMessage = L["statusConflicts"];
        }
    }

    /// <summary>“检查更新”入口：当前版本直接提示已是最新。</summary>
    [RelayCommand]
    private void CheckUpdates() => StatusMessage = L["aboutUpToDate"];

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
                gesture));
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
