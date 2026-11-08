using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Platform.Abstractions;
using Xunit;

namespace SSC.Application.Tests;

public sealed class SettingsServiceTests
{
    private static SettingsService CreateService(FakeSettingsRepository repository) =>
        new(repository, new FakeAppPaths(), new PlatformDescriptor(PlatformKind.MacOS, "macOS", "test"));

    [Fact]
    public void Constructor_LoadsDefaults()
    {
        var service = CreateService(new FakeSettingsRepository());

        Assert.Equal("zh", service.Current.Language);
        Assert.Equal("dark", service.Current.Theme);
        Assert.Equal("⌘⇧2", service.Current.Hotkeys["captureRegion"]);
        Assert.Equal("/tmp/ssc/screenshots", service.Current.SaveDirectory);
    }

    [Fact]
    public void Save_PersistsAndReloadsCustomSettings()
    {
        var repository = new FakeSettingsRepository();
        var service = CreateService(repository);
        var settings = service.Current;
        settings.Language = "en";
        settings.Theme = "light";
        settings.Hotkeys["captureRegion"] = "⌘⇧9";

        service.Save(settings);

        var reloaded = CreateService(repository);
        Assert.Equal("en", reloaded.Current.Language);
        Assert.Equal("light", reloaded.Current.Theme);
        Assert.Equal("⌘⇧9", reloaded.Current.Hotkeys["captureRegion"]);
    }

    [Fact]
    public void Save_NormalizesOutOfRangeValues()
    {
        var service = CreateService(new FakeSettingsRepository());
        var settings = service.Current;
        settings.JpegQuality = 5;
        settings.RecordingFps = 24;
        settings.RecordingBitrateMbps = 500;
        settings.DefaultFormat = "JPG";
        settings.FileNamePattern = "  ";
        settings.Hotkeys = new Dictionary<string, string> { ["captureRegion"] = "  ⌘⇧2  ", ["unknown"] = "X" };

        service.Save(settings);

        var saved = service.Current;
        Assert.Equal(40, saved.JpegQuality);
        Assert.Equal(30, saved.RecordingFps);
        Assert.Equal(100, saved.RecordingBitrateMbps);
        Assert.Equal("jpg", saved.DefaultFormat);
        Assert.Equal("{date}-{type}-{n}", saved.FileNamePattern);
        Assert.Equal("⌘⇧2", saved.Hotkeys["captureRegion"]);
        Assert.False(saved.Hotkeys.ContainsKey("unknown"));
        Assert.Equal(HotkeyCatalog.Actions.Count, saved.Hotkeys.Count);
    }

    [Fact]
    public void Save_RejectsConflictingHotkeys()
    {
        var service = CreateService(new FakeSettingsRepository());
        var settings = service.Current;
        settings.Hotkeys["captureFull"] = settings.Hotkeys["captureRegion"];

        var exception = Assert.Throws<SettingsValidationException>(() => service.Save(settings));

        Assert.Single(exception.Conflicts);
    }

    [Fact]
    public void Save_RaisesChangedEvent()
    {
        var service = CreateService(new FakeSettingsRepository());
        AppSettings? observed = null;
        service.Changed += (_, settings) => observed = settings;
        var updated = service.Current;
        updated.Language = "en";

        service.Save(updated);

        Assert.NotNull(observed);
        Assert.Equal("en", observed!.Language);
    }

    [Fact]
    public void Current_ReturnsIndependentClone()
    {
        var service = CreateService(new FakeSettingsRepository());
        var first = service.Current;
        first.Language = "en";
        first.Hotkeys["captureRegion"] = "changed";

        Assert.Equal("zh", service.Current.Language);
        Assert.Equal("⌘⇧2", service.Current.Hotkeys["captureRegion"]);
    }

    private sealed class FakeSettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string? Read(string key) => _values.GetValueOrDefault(key);

        public void Write(string key, string value) => _values[key] = value;
    }

    private sealed class FakeAppPaths : IAppPaths
    {
        public string AppDataDirectory => "/tmp/ssc";

        public string LogsDirectory => "/tmp/ssc/logs";

        public string DatabasePath => "/tmp/ssc/ssc.db";

        public string BlobStoreDirectory => "/tmp/ssc/blobs";

        public string ScreenshotsDirectory => "/tmp/ssc/screenshots";

        public string TempDirectory => "/tmp/ssc/tmp";

        public void EnsureCreated()
        {
        }
    }
}
