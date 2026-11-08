using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Events;
using SSC.Application.Recording;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using SSC.Domain.History;
using SSC.Domain.Recording;
using SSC.Infrastructure.Localization;
using SSC.Platform.Abstractions;
using SSC.Application.Ocr;
using SSC.Application.Upload;
using SSC.Platform.Mock;
using SSC.UI.Capture;
using SSC.UI.Ocr;
using SSC.UI.Recording;
using SSC.UI.Upload;
using DomainPixelRect = SSC.Domain.Geometry.PixelRect;
using Xunit;

namespace SSC.UI.Tests;

public sealed class RecordingWindowTests
{
    [AvaloniaFact]
    public async Task SetupWindow_RendersAndReturnsAdjustedOptions()
    {
        var localizer = new JsonLocalizer();
        var options = new RecordingOptions(
            "1",
            new PixelRect(0, 0, 1280, 720),
            2,
            30,
            12,
            1080,
            true);
        var window = new RecordingSetupWindow(options, localizer);
        var completion = window.ShowAsync();

        var rendered = window.CaptureRenderedFrame();
        Assert.NotNull(rendered);
        using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-recording-setup.png")))
        {
            rendered!.Save(stream, new PngBitmapEncoderOptions());
        }

        window.FindControl<Button>("Resolution1440")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.FindControl<Button>("Fps60")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.FindControl<Button>("StartButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var result = await completion;
        Assert.NotNull(result);
        Assert.Equal(1440, result!.MaxHeight);
        Assert.Equal(60, result.Fps);
        Assert.True(result.IncludeMicrophone);
    }

    [AvaloniaFact]
    public async Task ControlBar_RendersRecordingState()
    {
        var localizer = new JsonLocalizer();
        var manager = CreateManager(localizer);
        var options = new RecordingOptions(
            "1",
            new PixelRect(0, 0, 640, 360),
            2,
            30,
            12,
            1080,
            true);
        var start = await manager.StartAsync(options, TestContext.Current.CancellationToken);
        Assert.True(start.Success);

        var window = new RecordingControlBarWindow(manager, localizer);
        try
        {
            window.Show();
            window.BeginRecording();
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-recording-bar.png")))
            {
                rendered!.Save(stream, new PngBitmapEncoderOptions());
            }

            Assert.True(rendered.PixelSize.Width >= 450);
        }
        finally
        {
            window.Close();
            await manager.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public void SummaryWindow_RendersSavedRecording()
    {
        var localizer = new JsonLocalizer();
        var entry = HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Recording,
            "recording",
            "/tmp/recording.mp4",
            DateTimeOffset.UtcNow,
            1920,
            1080,
            TimeSpan.FromSeconds(34),
            tags: new[] { "recording" });
        var result = new RecordingStopResult(true, "saved", entry, TimeSpan.FromSeconds(34));
        var options = new RecordingOptions(
            "1",
            new PixelRect(0, 0, 1920, 1080),
            2,
            30,
            12,
            1080,
            true);
        var window = new RecordingSummaryWindow(result, options, localizer);

        try
        {
            window.Show();
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-recording-summary.png")))
            {
                rendered!.Save(stream, new PngBitmapEncoderOptions());
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CameraPreviewWindow_RendersMockFrames()
    {
        var localizer = new JsonLocalizer();
        var window = new CameraPreviewWindow(new MockCameraPreviewService(), localizer);

        try
        {
            window.Show();
            await Task.Delay(400, TestContext.Current.CancellationToken);
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-camera-pip.png")))
            {
                rendered!.Save(stream, new PngBitmapEncoderOptions());
            }

            Assert.True(rendered.PixelSize.Width >= 130);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OcrResultWindow_RendersLinesAndImage()
    {
        var localizer = new JsonLocalizer();
        var frame = CreateFrame(420, 280);
        var result = new OcrResult(
            "SuperScreenCapture\nM6",
            new[]
            {
                new OcrLine("SuperScreenCapture", new DomainPixelRect(24, 24, 220, 24), 0.98),
                new OcrLine("M6", new DomainPixelRect(24, 62, 50, 24), 0.96)
            },
            "en-US");
        var window = new OcrResultWindow(
            frame,
            result,
            new OcrServiceChain(new IOcrEngine[] { new MockOcrEngine() }),
            localizer);

        try
        {
            window.Show();
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-ocr-result.png")))
            {
                rendered!.Save(stream, new PngBitmapEncoderOptions());
            }

            Assert.True(rendered.PixelSize.Width >= 900);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ScrollBar_Renders()
    {
        var window = new ScrollCaptureBarWindow(new JsonLocalizer());

        try
        {
            window.Show();
            window.SetCount(3, 800, 1200);
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-scroll-bar.png")))
            {
                rendered!.Save(stream, new PngBitmapEncoderOptions());
            }

            Assert.True(rendered.PixelSize.Width >= 450);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task UploadDialog_RendersProviderFields()
    {
        var localizer = new JsonLocalizer();
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var settings = new SettingsService(new InMemorySettingsRepository(), new TestPaths(), platform);
        var pipeline = new UploadPipeline(
            new IUploadProvider[] { new FakeUploadProvider() },
            settings,
            localizer);
        var window = new UploadDialogWindow(
            new byte[] { 1, 2, 3 },
            "screenshot.png",
            pipeline,
            settings,
            localizer);

        try
        {
            window.Show();
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-upload.png")))
            {
                rendered!.Save(stream, new PngBitmapEncoderOptions());
            }

            // 启用上传并触发一次成功的模拟上传，验证成功状态布局。
            window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("EnableSwitch")!.IsChecked = true;
            window.FindControl<Button>("UploadButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(600, TestContext.Current.CancellationToken);
            var successFrame = window.CaptureRenderedFrame();
            Assert.NotNull(successFrame);
            using (var successStream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-upload-success.png")))
            {
                successFrame!.Save(successStream, new PngBitmapEncoderOptions());
            }

            Assert.True(rendered.PixelSize.Width >= 500);
        }
        finally
        {
            window.Close();
        }
    }

    private static CaptureFrame CreateFrame(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 40;
            pixels[i + 1] = 80;
            pixels[i + 2] = 140;
            pixels[i + 3] = 255;
        }

        return new CaptureFrame(width, height, pixels, DateTimeOffset.UtcNow);
    }

    private static RecordingSessionManager CreateManager(JsonLocalizer localizer)
    {
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var settings = new SettingsService(
            new InMemorySettingsRepository(),
            new TestPaths(),
            platform);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        return new RecordingSessionManager(
            new MockRecordingService(),
            new MockPermissionService(),
            new InMemoryHistoryRepository(),
            settings,
            new CaptureFileNamer(clock),
            clock,
            new InMemoryAppEventBus(),
            localizer);
    }

    private sealed class FakeUploadProvider : IUploadProvider
    {
        public UploadProviderInfo Info { get; } = new("imgur", "uploadProviderImgur");

        public bool IsConfigured(UploadSettings settings) => true;

        public Task<UploadResult> UploadAsync(
            UploadRequest request,
            UploadSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new UploadResult(Info.Id, "https://example.test/image.png"));

        public Task<bool> DeleteAsync(
            UploadResult result,
            UploadSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class InMemoryHistoryRepository : IHistoryRepository
    {
        private readonly List<HistoryEntry> _entries = new();

        public IReadOnlyList<HistoryEntry> List(int limit = 100, int offset = 0) =>
            _entries.Skip(offset).Take(limit).ToArray();

        public HistoryEntry? Get(Guid id) => _entries.FirstOrDefault(entry => entry.Id == id);

        public void Add(HistoryEntry entry) => _entries.Add(entry);

        public void Update(HistoryEntry entry)
        {
        }

        public void Delete(Guid id) => _entries.RemoveAll(entry => entry.Id == id);

        public int Count() => _entries.Count;
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

    private sealed class TestPaths : IAppPaths
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "ssc-recording-ui-tests",
            Guid.NewGuid().ToString("N"));

        public string AppDataDirectory => _root;

        public string LogsDirectory => Path.Combine(_root, "logs");

        public string DatabasePath => Path.Combine(_root, "ssc.db");

        public string BlobStoreDirectory => Path.Combine(_root, "blobs");

        public string ScreenshotsDirectory => _root;

        public string TempDirectory => Path.Combine(_root, "tmp");

        public void EnsureCreated() => Directory.CreateDirectory(_root);
    }
}
