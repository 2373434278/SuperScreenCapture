using Avalonia.Controls;
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

/// <summary>
/// 仅在 SSC_UPDATE_SCREENSHOTS=1 时运行，把设置页渲染图写回 assets/screenshots。
/// 其余窗口的截图由 build/update-screenshots.sh 从 UI 测试输出复制。
/// </summary>
public sealed class ScreenshotTour
{
    [AvaloniaFact]
    public async Task RenderSettingsScreens()
    {
        if (Environment.GetEnvironmentVariable("SSC_UPDATE_SCREENSHOTS") != "1")
        {
            return;
        }

        var output = ResolveScreenshotDirectory();
        Directory.CreateDirectory(output);

        var platform = new PlatformDescriptor(PlatformKind.MacOS, "macOS", "15.0");
        var settings = new SettingsService(new MemorySettingsRepository(), new TempPaths(), platform);
        var localizer = new JsonLocalizer();
        var viewModel = new MainWindowViewModel(settings, localizer, new MockAutoStartService(), platform);
        var window = new MainWindow(viewModel);
        window.Show();
        await Task.Delay(600, TestContext.Current.CancellationToken);

        // 主题默认已改为「跟随系统」，截图必须显式固定深浅色，避免随构建机外观漂移。
        viewModel.SetThemeCommand.Execute("dark");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        window.Opacity = 1;
        Save(window, Path.Combine(output, "settings-dark.png"));

        viewModel.SetThemeCommand.Execute("light");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        window.Opacity = 1;
        Save(window, Path.Combine(output, "settings-light.png"));

        viewModel.SetThemeCommand.Execute("dark");
        window.Close();
    }

    private static void Save(Window window, string path)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = File.Create(path);
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static string ResolveScreenshotDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SuperScreenCapture.slnx")))
            {
                return Path.Combine(directory.FullName, "assets", "screenshots");
            }

            directory = directory.Parent;
        }

        return Path.Combine(Path.GetTempPath(), "ssc-screenshots");
    }

    private sealed class MemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new();

        public string? Read(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public void Write(string key, string value) => _values[key] = value;
    }

    private sealed class TempPaths : IAppPaths
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ssc-screenshot-paths");

        public string AppDataDirectory => _root;

        public string LogsDirectory => Path.Combine(_root, "logs");

        public string DatabasePath => Path.Combine(_root, "ssc.db");

        public string BlobStoreDirectory => Path.Combine(_root, "blobs");

        public string ScreenshotsDirectory => Path.Combine(_root, "shots");

        public string TempDirectory => Path.Combine(_root, "tmp");

        public void EnsureCreated()
        {
            Directory.CreateDirectory(_root);
        }
    }
}
