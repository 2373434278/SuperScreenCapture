using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Application.Hotkeys;
using SSC.Application.Settings;
using SSC.Domain.History;
using SSC.Infrastructure.Localization;
using SSC.Platform.Abstractions;
using SSC.Platform.Mock;
using SSC.UI.Dialogs;
using SSC.UI.ViewModels;
using SSC.UI.Views;
using Xunit;

namespace SSC.UI.Tests;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindow_Renders_AndSwitchesLanguageAndTheme()
    {
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var settings = new SettingsService(new InMemorySettingsRepository(), new FakeAppPaths(), platform);
        var localizer = new JsonLocalizer();
        var viewModel = new MainWindowViewModel(
            settings,
            localizer,
            new MockAutoStartService(),
            platform);

        var window = new MainWindow(viewModel);
        window.Show();

        var darkFrame = window.CaptureRenderedFrame();
        Assert.NotNull(darkFrame);
        using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-zh-dark.png")))
        {
            darkFrame!.Save(stream, new PngBitmapEncoderOptions());
        }

        Assert.Equal("SuperScreenCapture", window.Title);
        Assert.Equal("zh", localizer.Language);

        viewModel.SetLanguageCommand.Execute("en");
        Assert.Equal("en", localizer.Language);
        Assert.Equal("General", localizer["navGeneral"]);
        Assert.Equal("General", viewModel.HeaderTitle);

        viewModel.SetThemeCommand.Execute("light");
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, Avalonia.Application.Current!.RequestedThemeVariant);

        var lightFrame = window.CaptureRenderedFrame();
        Assert.NotNull(lightFrame);
        using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-en-light.png")))
        {
            lightFrame!.Save(stream, new PngBitmapEncoderOptions());
        }

        Assert.True(lightFrame.PixelSize.Width >= 900);
    }

    [AvaloniaFact]
    public void SetTheme_AutoRequestsDefaultThemeVariant()
    {
        var harness = CreateHarness();

        // 新默认即「跟随系统」，初始化后不再固定深色。
        Assert.True(harness.ViewModel.IsAutoTheme);
        Assert.False(harness.ViewModel.IsDarkTheme);
        Assert.False(harness.ViewModel.IsLightTheme);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Default, Avalonia.Application.Current!.RequestedThemeVariant);

        harness.ViewModel.SetThemeCommand.Execute("dark");
        Assert.True(harness.ViewModel.IsDarkTheme);
        Assert.False(harness.ViewModel.IsAutoTheme);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);

        harness.ViewModel.SetThemeCommand.Execute("auto");
        Assert.True(harness.ViewModel.IsAutoTheme);
        Assert.False(harness.ViewModel.IsDarkTheme);
        Assert.False(harness.ViewModel.IsLightTheme);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Default, Avalonia.Application.Current!.RequestedThemeVariant);
        Assert.Equal("auto", harness.Settings.Current.Theme);
    }

    [AvaloniaFact]
    public void ToggleTheme_CyclesAutoDarkLightAuto()
    {
        var harness = CreateHarness();
        Assert.True(harness.ViewModel.IsAutoTheme);

        harness.ViewModel.ToggleThemeCommand.Execute(null);
        Assert.True(harness.ViewModel.IsDarkTheme);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);
        Assert.Equal("dark", harness.Settings.Current.Theme);

        harness.ViewModel.ToggleThemeCommand.Execute(null);
        Assert.True(harness.ViewModel.IsLightTheme);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, Avalonia.Application.Current!.RequestedThemeVariant);
        Assert.Equal("light", harness.Settings.Current.Theme);

        harness.ViewModel.ToggleThemeCommand.Execute(null);
        Assert.True(harness.ViewModel.IsAutoTheme);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Default, Avalonia.Application.Current!.RequestedThemeVariant);
        Assert.Equal("auto", harness.Settings.Current.Theme);
    }

    [AvaloniaFact]
    public void RefreshThemeState_NotifiesWithoutPersisting()
    {
        var harness = CreateHarness();
        var writes = harness.Repository.WriteCount;
        var raised = new List<string>();
        harness.ViewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        // 系统外观变化（ActualThemeVariantChanged）只刷新绑定，绝不能回写设置造成反馈环。
        harness.ViewModel.RefreshThemeState();

        Assert.Contains(nameof(MainWindowViewModel.IsAutoTheme), raised);
        Assert.Contains(nameof(MainWindowViewModel.IsDarkTheme), raised);
        Assert.Contains(nameof(MainWindowViewModel.IsLightTheme), raised);
        Assert.Equal(writes, harness.Repository.WriteCount);
    }

    [AvaloniaFact]
    public void MainWindow_RendersEverySettingsTab()
    {
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var settings = new SettingsService(new InMemorySettingsRepository(), new FakeAppPaths(), platform);
        var localizer = new JsonLocalizer();
        var viewModel = new MainWindowViewModel(settings, localizer, new MockAutoStartService(), platform);
        var window = new MainWindow(viewModel);
        window.Show();

        try
        {
            foreach (var tab in new[] { "output", "recording", "ocr", "upload", "about" })
            {
                viewModel.SelectTabCommand.Execute(tab);
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                using var stream = File.Create(
                    Path.Combine(Path.GetTempPath(), $"ssc-ui-settings-{tab}.png"));
                frame!.Save(stream, new PngBitmapEncoderOptions());
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HotkeysPage_FlagsConflictsAndRenders()
    {
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var settings = new SettingsService(new InMemorySettingsRepository(), new FakeAppPaths(), platform);
        var localizer = new JsonLocalizer();
        var viewModel = new MainWindowViewModel(settings, localizer, new MockAutoStartService(), platform);
        var window = new MainWindow(viewModel);
        window.Show();

        viewModel.SelectTabCommand.Execute("hotkeys");
        Assert.True(viewModel.IsHotkeysTab);
        Assert.Equal(HotkeyCatalog.Actions.Count, viewModel.Hotkeys.Count);

        var edited = viewModel.Hotkeys[0];
        var previousGesture = edited.Gesture;
        edited.Gesture = viewModel.Hotkeys[1].Gesture;

        // 行级提交：无需点击保存，冲突立即标记并回滚该行。
        Assert.Equal(previousGesture, edited.Gesture);
        Assert.True(viewModel.Hotkeys[0].HasError);
        Assert.True(viewModel.Hotkeys[1].HasError);
        Assert.Contains(localizer["hotkeyCaptureFull"], viewModel.Hotkeys[0].ErrorMessage);
        Assert.Contains(localizer["hotkeyCaptureRegion"], viewModel.Hotkeys[1].ErrorMessage);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-hotkeys-conflict.png")))
        {
            frame!.Save(stream, new PngBitmapEncoderOptions());
        }
    }

    [AvaloniaFact]
    public void TogglingSetting_PersistsImmediately()
    {
        var harness = CreateHarness();
        Assert.True(harness.Settings.Current.AutoCopy);
        Assert.Equal(0, harness.Repository.WriteCount);

        harness.ViewModel.AutoCopy = false;

        Assert.False(harness.ViewModel.AutoCopy);
        Assert.False(harness.Settings.Current.AutoCopy);
        Assert.Equal(1, harness.Repository.WriteCount);
        Assert.Contains("\"autoCopy\":false", harness.Repository.LastWrittenValue);
    }

    [AvaloniaFact]
    public void FileNamePattern_ReadsBackNormalizedValue()
    {
        var harness = CreateHarness();

        harness.ViewModel.FileNamePattern = "   ";

        // Normalize 会把空白模板强制回默认值，VM 通过回读显示真实生效的内容。
        Assert.Equal("{date}-{type}-{n}", harness.ViewModel.FileNamePattern);
        Assert.Equal("{date}-{type}-{n}", harness.Settings.Current.FileNamePattern);
    }

    [AvaloniaFact]
    public void HotkeyRowEdit_PersistsAndAppliesImmediately()
    {
        var harness = CreateHarness(withHotkeys: true);
        var row = harness.ViewModel.Hotkeys[0];

        row.Gesture = "⌥⌘F9";

        Assert.False(row.HasError);
        Assert.Equal("⌥⌘F9", harness.Settings.Current.Hotkeys[row.ActionKey]);
        Assert.Equal(HotkeyCatalog.Actions.Count, harness.HotkeyService.RegisterCount);
        Assert.Equal(1, harness.Repository.WriteCount);
    }

    [AvaloniaFact]
    public void HotkeyRowConflict_RollsBackRowAndDoesNotPersist()
    {
        var harness = CreateHarness(withHotkeys: true);
        var first = harness.ViewModel.Hotkeys[0];
        var second = harness.ViewModel.Hotkeys[1];
        var originalGesture = first.Gesture;
        var originalPersisted = harness.Settings.Current.Hotkeys[first.ActionKey];

        first.Gesture = second.Gesture;

        Assert.Equal(originalGesture, first.Gesture);
        Assert.True(first.HasError);
        Assert.True(second.HasError);
        Assert.Equal(originalPersisted, harness.Settings.Current.Hotkeys[first.ActionKey]);
        Assert.Equal(0, harness.HotkeyService.RegisterCount);
        Assert.Equal(0, harness.Repository.WriteCount);
    }

    [AvaloniaFact]
    public void RestoreHotkeys_AppliesOnceAndPersistsBatch()
    {
        var harness = CreateHarness(withHotkeys: true);
        var row = harness.ViewModel.Hotkeys[0];
        row.Gesture = "⌥⌘F9";
        Assert.Equal(1, harness.Repository.WriteCount);
        Assert.Equal(HotkeyCatalog.Actions.Count, harness.HotkeyService.RegisterCount);

        harness.ViewModel.RestoreHotkeysCommand.Execute(null);

        var defaults = HotkeyCatalog.CreateDefaults(PlatformKind.Mock);
        foreach (var item in harness.ViewModel.Hotkeys)
        {
            Assert.Equal(defaults[item.ActionKey], item.Gesture);
            Assert.False(item.HasError);
            Assert.Equal(defaults[item.ActionKey], harness.Settings.Current.Hotkeys[item.ActionKey]);
        }

        // 抑制逐行提交后整批只 Apply 一次，避免 N 次重注册。
        Assert.Equal(2, harness.HotkeyService.UnregisterAllCount);
        Assert.Equal(2 * HotkeyCatalog.Actions.Count, harness.HotkeyService.RegisterCount);
        Assert.Equal(2, harness.Repository.WriteCount);
    }

    [AvaloniaFact]
    public void ResetAllSettings_RestoresDefaultsAndSyncsViewModel()
    {
        var harness = CreateHarness(withHotkeys: true);
        harness.ViewModel.AutoCopy = false;
        harness.ViewModel.SetFormatCommand.Execute("jpg");
        harness.ViewModel.SetThemeCommand.Execute("light");
        harness.ViewModel.SetLanguageCommand.Execute("en");
        harness.ViewModel.Hotkeys[0].Gesture = "⌥⌘F9";
        Assert.Equal("en", harness.Localizer.Language);

        harness.ViewModel.ResetAllSettingsCommand.Execute(null);

        var current = harness.Settings.Current;
        var defaults = HotkeyCatalog.CreateDefaults(PlatformKind.Mock);
        Assert.Equal("zh", current.Language);
        // 恢复默认后主题回到「跟随系统」而不是深色。
        Assert.Equal("auto", current.Theme);
        Assert.True(current.AutoCopy);
        Assert.Equal("png", current.DefaultFormat);
        Assert.Equal("/tmp/ssc-ui-tests/screenshots", current.SaveDirectory);
        foreach (var (action, gesture) in defaults)
        {
            Assert.Equal(gesture, current.Hotkeys[action]);
        }

        Assert.Equal("zh", harness.Localizer.Language);
        Assert.True(harness.ViewModel.AutoCopy);
        Assert.True(harness.ViewModel.IsAutoTheme);
        Assert.False(harness.ViewModel.IsDarkTheme);
        Assert.Equal(defaults["captureRegion"], harness.ViewModel.Hotkeys[0].Gesture);
    }

    [AvaloniaFact]
    public void LaunchAtLogin_PersistsAndRollsBackOnAutoStartFailure()
    {
        var harness = CreateHarness();
        var autoStart = harness.AutoStart;

        harness.ViewModel.LaunchAtLogin = true;
        Assert.True(harness.Settings.Current.LaunchAtLogin);

        autoStart.FailNextChange = true;
        harness.ViewModel.LaunchAtLogin = false;

        Assert.True(harness.ViewModel.LaunchAtLogin);
        Assert.True(harness.Settings.Current.LaunchAtLogin);
        Assert.Contains(harness.Localizer["autoStartFailed"], harness.ViewModel.StatusMessage);
    }

    [AvaloniaFact]
    public void FileNamePattern_CommitsOnLostFocusOnly()
    {
        var harness = CreateHarness();
        var window = new MainWindow(harness.ViewModel);
        window.Show();
        try
        {
            harness.ViewModel.SelectTabCommand.Execute("output");
            var box = window.FindControl<TextBox>("FileNamePatternBox");
            var other = window.FindControl<TextBox>("SaveDirectoryBox");
            Assert.NotNull(box);
            Assert.NotNull(other);
            Assert.True(box!.Focus());
            box.Text = "custom-{n}";

            // UpdateSourceTrigger=LostFocus：输入过程中不落盘。
            Assert.Equal("{date}-{type}-{n}", harness.Settings.Current.FileNamePattern);

            Assert.True(other!.Focus());
            Assert.Equal("custom-{n}", harness.Settings.Current.FileNamePattern);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ConfirmDialog_ReportsCancelOnCloseAndConfirmOnClick()
    {
        var localizer = new JsonLocalizer();

        var cancelDialog = new ConfirmDialogWindow(localizer, "Title", "Description");
        cancelDialog.Show();
        cancelDialog.Close();
        Assert.False(cancelDialog.Result.GetAwaiter().GetResult());

        var confirmDialog = new ConfirmDialogWindow(localizer, "Title", "Description");
        confirmDialog.Show();
        var confirm = confirmDialog.FindControl<Button>("ConfirmButton");
        Assert.NotNull(confirm);
        confirm!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(confirmDialog.Result.GetAwaiter().GetResult());
    }

    [AvaloniaFact]
    public async Task AnnotateWithoutHistory_ShowsNoRecentCaptureHint()
    {
        var platform = new PlatformDescriptor(PlatformKind.MacOS, "macOS", "15.0");
        var settings = new SettingsService(new InMemorySettingsRepository(), new FakeAppPaths(), platform);
        var localizer = new JsonLocalizer();
        var viewModel = new MainWindowViewModel(
            settings,
            localizer,
            new MockAutoStartService(),
            platform,
            historyRepository: new EmptyHistoryRepository());

        Assert.Equal("macOS", viewModel.PlatformLabel);

        await viewModel.AnnotateLatestCommand.ExecuteAsync(null);

        Assert.Equal(localizer["noRecentCapture"], viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public void PlatformLabel_FollowsRunningOperatingSystem()
    {
        var platform = new PlatformDescriptor(PlatformKind.Windows, "Windows", "10.0.19041");
        var settings = new SettingsService(new InMemorySettingsRepository(), new FakeAppPaths(), platform);
        var viewModel = new MainWindowViewModel(
            settings,
            new JsonLocalizer(),
            new MockAutoStartService(),
            platform);

        Assert.Equal("Windows", viewModel.PlatformLabel);
    }

    [AvaloniaFact]
    public void CloseToTrayRow_IsVisibleOnWindows()
    {
        var window = CreateWindow(new PlatformDescriptor(PlatformKind.Windows, "Windows", "10.0.19041"));
        window.Show();
        try
        {
            var row = window.FindControl<Border>("CloseToTrayRow");
            Assert.NotNull(row);
            Assert.True(row!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CloseToTrayRow_IsHiddenOnMacOS()
    {
        var window = CreateWindow(new PlatformDescriptor(PlatformKind.MacOS, "macOS", "15.0"));
        window.Show();
        try
        {
            var row = window.FindControl<Border>("CloseToTrayRow");
            Assert.NotNull(row);
            Assert.False(row!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindow CreateWindow(PlatformDescriptor platform)
    {
        var settings = new SettingsService(new InMemorySettingsRepository(), new FakeAppPaths(), platform);
        var viewModel = new MainWindowViewModel(
            settings,
            new JsonLocalizer(),
            new MockAutoStartService(),
            platform);
        return new MainWindow(viewModel);
    }

    private static Harness CreateHarness(bool withHotkeys = false)
    {
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var repository = new InMemorySettingsRepository();
        var settings = new SettingsService(repository, new FakeAppPaths(), platform);
        var localizer = new JsonLocalizer();
        var autoStart = new MockAutoStartService();
        var hotkeyService = new CountingHotkeyService();
        HotkeyManager? hotkeys = withHotkeys
            ? new HotkeyManager(hotkeyService, platform.Kind, new HotkeyRouter())
            : null;
        var viewModel = new MainWindowViewModel(
            settings,
            localizer,
            autoStart,
            platform,
            hotkeys: hotkeys);
        return new Harness(viewModel, settings, repository, localizer, autoStart, hotkeyService);
    }

    private sealed record Harness(
        MainWindowViewModel ViewModel,
        SettingsService Settings,
        InMemorySettingsRepository Repository,
        JsonLocalizer Localizer,
        MockAutoStartService AutoStart,
        CountingHotkeyService HotkeyService);

    private sealed class CountingHotkeyService : IHotkeyService
    {
        public int RegisterCount { get; private set; }

        public int UnregisterAllCount { get; private set; }

        public HotkeyRegistrationResult Register(string id, HotkeyGesture gesture, Action callback)
        {
            RegisterCount++;
            return HotkeyRegistrationResult.Ok;
        }

        public bool Unregister(string id) => true;

        public void UnregisterAll() => UnregisterAllCount++;

        public void Dispose()
        {
        }
    }

    private sealed class EmptyHistoryRepository : IHistoryRepository
    {
        public IReadOnlyList<HistoryEntry> List(int limit = 100, int offset = 0) => Array.Empty<HistoryEntry>();

        public HistoryEntry? Get(Guid id) => null;

        public void Add(HistoryEntry entry)
        {
        }

        public void Update(HistoryEntry entry)
        {
        }

        public void Delete(Guid id)
        {
        }

        public int Count() => 0;
    }

    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public int WriteCount { get; private set; }

        public string LastWrittenValue => _values.GetValueOrDefault(SettingsService.StorageKey) ?? string.Empty;

        public string? Read(string key) => _values.GetValueOrDefault(key);

        public void Write(string key, string value)
        {
            _values[key] = value;
            WriteCount++;
        }
    }

    private sealed class FakeAppPaths : IAppPaths
    {
        public string AppDataDirectory => "/tmp/ssc-ui-tests";

        public string LogsDirectory => "/tmp/ssc-ui-tests/logs";

        public string DatabasePath => "/tmp/ssc-ui-tests/ssc.db";

        public string BlobStoreDirectory => "/tmp/ssc-ui-tests/blobs";

        public string ScreenshotsDirectory => "/tmp/ssc-ui-tests/screenshots";

        public string TempDirectory => "/tmp/ssc-ui-tests/tmp";

        public void EnsureCreated()
        {
        }
    }
}
