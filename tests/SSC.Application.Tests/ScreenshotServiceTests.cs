using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Events;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.Platform.Abstractions;
using Xunit;

namespace SSC.Application.Tests;

public sealed class ScreenshotServiceTests
{
    [Fact]
    public async Task SaveAsync_WritesFileCopiesAndAddsHistory()
    {
        using var temp = new TempDirectory();
        var platform = new PlatformDescriptor(PlatformKind.MacOS, "macOS", "test");
        var settingsService = new SettingsService(
            new InMemorySettingsRepository(),
            new TestPaths(temp.Path),
            platform);
        var history = new InMemoryHistoryRepository();
        var clipboard = new RecordingClipboardService();
        var encoder = new StubEncoder();
        var bus = new InMemoryAppEventBus();
        Guid? published = null;
        using var subscription = bus.Subscribe<HistoryChanged>(message => published = message.EntryId);
        var service = new ScreenshotService(
            encoder,
            clipboard,
            history,
            settingsService,
            new CaptureFileNamer(new FixedClock(DateTimeOffset.UtcNow)),
            new FixedClock(DateTimeOffset.UtcNow),
            bus);
        var frame = new CaptureFrame(2, 2, new byte[2 * 2 * 4], DateTimeOffset.UtcNow);

        var result = await service.SaveAsync(frame, CaptureKind.Screenshot, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(
            result.FilePath,
            TestContext.Current.CancellationToken));
        Assert.Same(frame, clipboard.LastFrame);
        Assert.Equal(1, history.Count());
        Assert.Equal(result.Entry.Id, published);
    }

    [Fact]
    public async Task SaveAsync_UsesExplicitFormat()
    {
        using var temp = new TempDirectory();
        var platform = new PlatformDescriptor(PlatformKind.MacOS, "macOS", "test");
        var settingsService = new SettingsService(
            new InMemorySettingsRepository(),
            new TestPaths(temp.Path),
            platform);
        var encoder = new StubEncoder();
        var service = new ScreenshotService(
            encoder,
            new RecordingClipboardService(),
            new InMemoryHistoryRepository(),
            settingsService,
            new CaptureFileNamer(new FixedClock(DateTimeOffset.UtcNow)),
            new FixedClock(DateTimeOffset.UtcNow),
            new InMemoryAppEventBus());
        var frame = new CaptureFrame(2, 2, new byte[2 * 2 * 4], DateTimeOffset.UtcNow);

        var result = await service.SaveAsync(
            frame,
            CaptureKind.Screenshot,
            "webp",
            TestContext.Current.CancellationToken);

        Assert.Equal("webp", encoder.LastFormat);
        Assert.EndsWith(".webp", result.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubEncoder : IImageEncoder
    {
        public string? LastFormat { get; private set; }

        public Task<byte[]> EncodeAsync(
            CaptureFrame frame,
            string format,
            int quality,
            CancellationToken cancellationToken = default)
        {
            LastFormat = format;
            return Task.FromResult(new byte[] { 1, 2, 3 });
        }
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public CaptureFrame? LastFrame { get; private set; }

        public Task CopyImageAsync(CaptureFrame frame, CancellationToken cancellationToken = default)
        {
            LastFrame = frame;
            return Task.CompletedTask;
        }
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
        public TestPaths(string root)
        {
            AppDataDirectory = root;
            LogsDirectory = Path.Combine(root, "logs");
            DatabasePath = Path.Combine(root, "ssc.db");
            BlobStoreDirectory = Path.Combine(root, "blobs");
            ScreenshotsDirectory = root;
            TempDirectory = Path.Combine(root, "tmp");
        }

        public string AppDataDirectory { get; }

        public string LogsDirectory { get; }

        public string DatabasePath { get; }

        public string BlobStoreDirectory { get; }

        public string ScreenshotsDirectory { get; }

        public string TempDirectory { get; }

        public void EnsureCreated()
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ssc-screenshot-tests",
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
