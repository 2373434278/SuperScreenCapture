using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Platform.Abstractions;
using Xunit;

namespace SSC.Application.Tests;

public sealed class CaptureFileNamerTests
{
    [Fact]
    public void CreatePath_UsesTemplateAndSequences()
    {
        using var temp = new TempDirectory();
        var platform = new PlatformDescriptor(PlatformKind.MacOS, "macOS", "test");
        var settings = new SettingsService(
            new InMemorySettingsRepository(),
            new TestPaths(temp.Path),
            platform).Current;
        settings.SaveDirectory = temp.Path;
        settings.FileNamePattern = "{date}-{type}-{n}";
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero));
        var namer = new CaptureFileNamer(clock);

        var first = namer.CreatePath(settings, CaptureKind.Screenshot, "png");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        File.WriteAllText(first, "x");
        var second = namer.CreatePath(settings, CaptureKind.Screenshot, "png");

        var date = clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd");
        Assert.Equal($"{date}-screenshot-001.png", Path.GetFileName(first));
        Assert.Equal($"{date}-screenshot-002.png", Path.GetFileName(second));
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
                "ssc-capture-tests",
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
