using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Events;
using SSC.Application.Ocr;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using SSC.Domain.History;
using SSC.Infrastructure.Imaging;
using SSC.Infrastructure.Localization;
using SSC.Platform.Abstractions;
using SSC.Platform.Mock;
using SSC.UI.Controls;
using SSC.UI.Editing;
using SSC.UI.History;
using SSC.UI.Ocr;
using SSC.UI.Platform;
using SSC.UI.ViewModels;
using SSC.UI.Views;
using DomainPixelRect = SSC.Domain.Geometry.PixelRect;
using Xunit;

namespace SSC.UI.Tests;

public sealed class CaptionButtonsTests
{
    [AvaloniaFact]
    public void CaptionButtons_OnWindows_AreVisibleWithThreeButtons()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.Windows;

            var buttons = new CaptionButtons();

            Assert.True(buttons.IsVisible);
            Assert.NotNull(buttons.MinimizeButton);
            Assert.NotNull(buttons.MaximizeButton);
            Assert.NotNull(buttons.CloseButton);
            Assert.Equal(46d, buttons.MinimizeButton.Width);
            Assert.Equal(46d, buttons.MaximizeButton.Width);
            Assert.Equal(46d, buttons.CloseButton.Width);
            Assert.False(buttons.Focusable);
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void CaptionButtons_OnMacOS_AreHidden()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.MacOS;

            var buttons = new CaptionButtons();

            Assert.False(buttons.IsVisible);
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void MaximizeButton_OnWindows_RegistersSnapLayoutsHitTest()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.Windows;

            var buttons = new CaptionButtons();

            Assert.Equal(
                Win32Properties.Win32HitTestValue.MaxButton,
                Win32Properties.GetNonClientHitTestResult(buttons.MaximizeButton));
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void CaptionButtons_UseLocalizedTooltipsAndAutomationNames()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.Windows;
            var localizer = new JsonLocalizer();

            var buttons = new CaptionButtons { Localizer = localizer };

            Assert.Equal(localizer["windowMinimize"], ToolTip.GetTip(buttons.MinimizeButton));
            Assert.Equal(localizer["windowMaximize"], ToolTip.GetTip(buttons.MaximizeButton));
            Assert.Equal(localizer["windowClose"], ToolTip.GetTip(buttons.CloseButton));

            localizer.SetLanguage("en");
            Assert.Equal("Minimize", ToolTip.GetTip(buttons.MinimizeButton));
            Assert.Equal("Close", ToolTip.GetTip(buttons.CloseButton));
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void NextWindowState_TogglesBetweenMaximizedAndNormal()
    {
        Assert.Equal(WindowState.Maximized, CaptionButtons.NextWindowState(WindowState.Normal));
        Assert.Equal(WindowState.Normal, CaptionButtons.NextWindowState(WindowState.Maximized));
        Assert.Equal(WindowState.Maximized, CaptionButtons.NextWindowState(WindowState.Minimized));
    }

    [AvaloniaFact]
    public void NativeWindows_IncludeCaptionButtonsOnWindows()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.Windows;

            var mainWindow = CreateMainWindow();
            var historyWindow = CreateHistoryWindow();
            var editorWindow = CreateAnnotationEditorWindow();
            var ocrWindow = CreateOcrResultWindow();

            try
            {
                AssertCaptionButtons(mainWindow);
                AssertCaptionButtons(historyWindow);
                AssertCaptionButtons(editorWindow);
                AssertCaptionButtons(ocrWindow);
            }
            finally
            {
                mainWindow.Close();
                historyWindow.Close();
                editorWindow.Close();
                ocrWindow.Close();
            }
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void MainWindow_HidesCaptionButtonsOnMacOS()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.MacOS;

            var window = CreateMainWindow();
            try
            {
                var buttons = window.FindControl<CaptionButtons>("SystemCaptionButtons");
                Assert.NotNull(buttons);
                Assert.False(buttons!.IsVisible);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void MainWindow_OnWindows_RendersCaptionButtonsInTopBar()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformStyles.Apply(Avalonia.Application.Current!, PlatformKind.Windows);
            var window = CreateMainWindow();
            try
            {
                window.Show();
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                using var stream = File.Create(
                    Path.Combine(Path.GetTempPath(), "ssc-ui-windows-captionbar.png"));
                frame!.Save(stream, new PngBitmapEncoderOptions());
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            PlatformStyles.Apply(Avalonia.Application.Current!, PlatformKind.MacOS);
            PlatformInfo.Current = previous;
        }
    }

    private static void AssertCaptionButtons(Window window)
    {
        var buttons = window.FindControl<CaptionButtons>("SystemCaptionButtons");
        Assert.NotNull(buttons);
        Assert.True(buttons!.IsVisible);
        Assert.NotNull(buttons.MinimizeButton);
        Assert.NotNull(buttons.MaximizeButton);
        Assert.NotNull(buttons.CloseButton);
    }

    private static MainWindow CreateMainWindow()
    {
        var platform = new PlatformDescriptor(PlatformKind.Windows, "Windows", "10.0.19041");
        var settings = new SettingsService(
            new InMemorySettingsRepository(),
            new FakeAppPaths(Path.Combine(Path.GetTempPath(), "ssc-caption-tests")),
            platform);
        var viewModel = new MainWindowViewModel(
            settings,
            new JsonLocalizer(),
            new MockAutoStartService(),
            platform);
        return new MainWindow(viewModel);
    }

    private static HistoryWindow CreateHistoryWindow()
    {
        var repository = new InMemoryHistoryRepository();
        var platform = new PlatformDescriptor(PlatformKind.Windows, "Windows", "10.0.19041");
        var settings = new SettingsService(
            new InMemorySettingsRepository(),
            new FakeAppPaths(Path.Combine(Path.GetTempPath(), "ssc-caption-tests")),
            platform);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var clipboard = new StubClipboardService();
        var screenshots = new ScreenshotService(
            new SkiaImageEncoder(),
            clipboard,
            repository,
            settings,
            new CaptureFileNamer(clock),
            clock,
            new InMemoryAppEventBus());
        return new HistoryWindow(
            repository,
            new JsonLocalizer(),
            new SkiaCaptureFrameLoader(),
            new SkiaAnnotationRenderer(),
            screenshots,
            clipboard,
            new OcrServiceChain(new IOcrEngine[] { new MockOcrEngine() }),
            new InMemoryAppEventBus());
    }

    private static AnnotationEditorWindow CreateAnnotationEditorWindow()
    {
        var frame = CreateFrame(320, 200);
        var platform = new PlatformDescriptor(PlatformKind.Windows, "Windows", "10.0.19041");
        var settings = new SettingsService(
            new InMemorySettingsRepository(),
            new FakeAppPaths(Path.Combine(Path.GetTempPath(), "ssc-caption-tests")),
            platform);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var clipboard = new StubClipboardService();
        var screenshots = new ScreenshotService(
            new SkiaImageEncoder(),
            clipboard,
            new InMemoryHistoryRepository(),
            settings,
            new CaptureFileNamer(clock),
            clock,
            new InMemoryAppEventBus());
        var result = new ScreenshotResult(
            HistoryEntry.Create(
                Guid.NewGuid(),
                CaptureKind.Screenshot,
                "shot",
                "/tmp/ssc-caption-shot.png",
                DateTimeOffset.UtcNow,
                frame.Width,
                frame.Height,
                tags: new[] { "capture" }),
            Array.Empty<byte>(),
            "/tmp/ssc-caption-shot.png");
        return new AnnotationEditorWindow(
            result,
            frame,
            new JsonLocalizer(),
            new SkiaAnnotationRenderer(),
            screenshots,
            clipboard);
    }

    private static OcrResultWindow CreateOcrResultWindow()
    {
        var frame = CreateFrame(320, 200);
        var result = new OcrResult(
            "SuperScreenCapture",
            new[] { new OcrLine("SuperScreenCapture", new DomainPixelRect(24, 24, 220, 24), 0.98) },
            "en-US");
        return new OcrResultWindow(
            frame,
            result,
            new OcrServiceChain(new IOcrEngine[] { new MockOcrEngine() }),
            new JsonLocalizer());
    }

    private static CaptureFrame CreateFrame(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 30;
            pixels[i + 1] = 80;
            pixels[i + 2] = 160;
            pixels[i + 3] = 255;
        }

        return new CaptureFrame(width, height, pixels, DateTimeOffset.UtcNow);
    }

    private sealed class InMemoryHistoryRepository : IHistoryRepository
    {
        private readonly List<HistoryEntry> _entries = new();

        public IReadOnlyList<HistoryEntry> List(int limit = 100, int offset = 0) =>
            _entries.OrderByDescending(static entry => entry.CreatedAt).Skip(offset).Take(limit).ToArray();

        public HistoryEntry? Get(Guid id) => _entries.FirstOrDefault(entry => entry.Id == id);

        public void Add(HistoryEntry entry) => _entries.Add(entry);

        public void Update(HistoryEntry entry)
        {
            var index = _entries.FindIndex(candidate => candidate.Id == entry.Id);
            if (index >= 0)
            {
                _entries[index] = entry;
            }
        }

        public void Delete(Guid id) => _entries.RemoveAll(entry => entry.Id == id);

        public int Count() => _entries.Count;
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public Task CopyImageAsync(CaptureFrame frame, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string? Read(string key) => _values.GetValueOrDefault(key);

        public void Write(string key, string value) => _values[key] = value;
    }

    private sealed class FakeAppPaths : IAppPaths
    {
        private readonly string _root;

        public FakeAppPaths(string root) => _root = root;

        public string AppDataDirectory => _root;

        public string LogsDirectory => Path.Combine(_root, "logs");

        public string DatabasePath => Path.Combine(_root, "ssc.db");

        public string BlobStoreDirectory => Path.Combine(_root, "blobs");

        public string ScreenshotsDirectory => Path.Combine(_root, "screenshots");

        public string TempDirectory => Path.Combine(_root, "tmp");

        public void EnsureCreated() => Directory.CreateDirectory(_root);
    }
}
