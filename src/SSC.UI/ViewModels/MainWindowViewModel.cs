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

    public ILocalizer L => _localizer;

    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = new();

    public string WindowTitle => L["appTitle"];

    public string AppVersion => AppInfo.Version;

    public string AppLicense => AppInfo.License;

    public string AppName => AppInfo.Name;

    public bool IsGeneralTab => _activeTab == "general";

    public bool IsHotkeysTab => _activeTab == "hotkeys";

    public bool IsOutputTab => _activeTab == "output";

    public bool IsRecordingTab => _activeTab == "recording";

    public bool IsOcrTab => _activeTab == "ocr";

    public bool IsUploadTab => _activeTab == "upload";

    public bool IsAboutTab => _activeTab == "about";

    public bool IsPlaceholderTab => IsOutputTab || IsRecordingTab || IsOcrTab || IsUploadTab;

    public bool IsChinese => L.Language == "zh";

    public bool IsEnglish => L.Language == "en";

    public bool IsMacOS => _platform.Kind is PlatformKind.MacOS or PlatformKind.Mock;

    public bool IsWindows => _platform.Kind == PlatformKind.Windows;

    public bool IsDarkTheme => _draft.Theme == "dark";

    public bool IsLightTheme => _draft.Theme == "light";

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

    public string HeaderTitle => _activeTab switch
    {
        "general" => L["generalTitle"],
        "hotkeys" => L["hotkeysTitle"],
        "output" => L["outputTitle"],
        "recording" => L["recordingTitle"],
        "ocr" => L["ocrTitle"],
        "upload" => L["uploadTitle"],
        "about" => L["aboutTitle"],
        _ => L["appTitle"]
    };

    public string HeaderSubtitle => _activeTab switch
    {
        "general" => L["generalDescription"],
        "hotkeys" => L["hotkeysDescription"],
        "about" => L["appSubtitle"],
        _ => L["placeholderDescription"]
    };

    public string PlaceholderTitle => _activeTab switch
    {
        "output" => L["outputTitle"],
        "recording" => L["recordingTitle"],
        "ocr" => L["ocrTitle"],
        "upload" => L["uploadTitle"],
        _ => L["placeholderTitle"]
    };

    public string PlaceholderDescription => L["placeholderDescription"];

    [ObservableProperty]
    private string statusMessage = string.Empty;

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
        OnPropertyChanged(nameof(PlaceholderTitle));
        OnPropertyChanged(nameof(PlaceholderDescription));
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

    private void NotifyTabProperties()
    {
        OnPropertyChanged(nameof(IsGeneralTab));
        OnPropertyChanged(nameof(IsHotkeysTab));
        OnPropertyChanged(nameof(IsOutputTab));
        OnPropertyChanged(nameof(IsRecordingTab));
        OnPropertyChanged(nameof(IsOcrTab));
        OnPropertyChanged(nameof(IsUploadTab));
        OnPropertyChanged(nameof(IsAboutTab));
        OnPropertyChanged(nameof(IsPlaceholderTab));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(PlaceholderTitle));
        OnPropertyChanged(nameof(PlaceholderDescription));
    }

    private static void ApplyTheme(string theme)
    {
        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }
}
