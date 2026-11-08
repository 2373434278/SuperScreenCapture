using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Infrastructure.Localization;
using SSC.Platform.Abstractions;
using SSC.Platform.Mock;
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
        Assert.Equal(6, viewModel.Hotkeys.Count);

        viewModel.Hotkeys[0].Gesture = viewModel.Hotkeys[1].Gesture;
        viewModel.SaveSettingsCommand.Execute(null);

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

    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string? Read(string key) => _values.GetValueOrDefault(key);

        public void Write(string key, string value) => _values[key] = value;
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
