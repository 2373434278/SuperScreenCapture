using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Events;
using SSC.Application.Ocr;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.Infrastructure.Imaging;
using SSC.Infrastructure.Localization;
using SSC.Platform.Abstractions;
using SSC.Platform.Mock;
using SSC.UI.History;
using Xunit;

namespace SSC.UI.Tests;

public sealed class HistoryWindowTests
{
    [AvaloniaFact]
    public async Task HistoryWindow_RendersEntriesAndSearch()
    {
        using var temp = new TempDirectory();
        var frame = CreateFrame(320, 200);
        var encoder = new SkiaImageEncoder();
        var bytes = await encoder.EncodeAsync(
            frame,
            "png",
            90,
            TestContext.Current.CancellationToken);
        var imagePath = Path.Combine(temp.Path, "history.png");
        await File.WriteAllBytesAsync(imagePath, bytes, TestContext.Current.CancellationToken);

        var repository = new InMemoryHistoryRepository();
        repository.Add(HistoryEntry.Create(
            Guid.NewGuid(),
            CaptureKind.Screenshot,
            "Design review",
            imagePath,
            DateTimeOffset.UtcNow,
            320,
            200,
            tags: new[] { "design" }));
        var localizer = new JsonLocalizer();
        var window = CreateWindow(repository, localizer);

        try
        {
            window.Show();
            await Task.Delay(500, TestContext.Current.CancellationToken);
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-history.png")))
            {
                rendered!.Save(stream, new PngBitmapEncoderOptions());
            }

            Assert.True(rendered.PixelSize.Width >= 1000);

            // 选中首张卡片以渲染右侧详情抽屉。
            var grid = window.FindControl<ListBox>("GridResults")!;
            grid.SelectedIndex = 0;
            var drawerFrame = window.CaptureRenderedFrame();
            Assert.NotNull(drawerFrame);
            using (var drawerStream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-history-drawer.png")))
            {
                drawerFrame!.Save(drawerStream, new PngBitmapEncoderOptions());
            }

            // 打开删除确认弹层。
            window.FindControl<Button>("DetailDelete")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var modalFrame = window.CaptureRenderedFrame();
            Assert.NotNull(modalFrame);
            using (var modalStream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-history-delete.png")))
            {
                modalFrame!.Save(modalStream, new PngBitmapEncoderOptions());
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static HistoryWindow CreateWindow(IHistoryRepository repository, JsonLocalizer localizer)
    {
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var settings = new SettingsService(new InMemorySettingsRepository(), new TestPaths(), platform);
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
            localizer,
            new SkiaCaptureFrameLoader(),
            new SkiaAnnotationRenderer(),
            screenshots,
            clipboard,
            new OcrServiceChain(new IOcrEngine[] { new MockOcrEngine() }),
            new InMemoryAppEventBus());
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

    private sealed class TestPaths : IAppPaths
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "ssc-history-ui-tests",
            Guid.NewGuid().ToString("N"));

        public string AppDataDirectory => _root;

        public string LogsDirectory => Path.Combine(_root, "logs");

        public string DatabasePath => Path.Combine(_root, "ssc.db");

        public string BlobStoreDirectory => Path.Combine(_root, "blobs");

        public string ScreenshotsDirectory => _root;

        public string TempDirectory => Path.Combine(_root, "tmp");

        public void EnsureCreated() => Directory.CreateDirectory(_root);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ssc-history-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
