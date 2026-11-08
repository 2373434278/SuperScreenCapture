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
using SSC.Platform.Mock;
using SSC.UI.Recording;
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
