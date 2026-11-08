using System.ComponentModel;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Events;
using SSC.Application.Recording;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using SSC.Domain.History;
using SSC.Domain.Recording;
using SSC.Platform.Abstractions;
using Xunit;

namespace SSC.Application.Tests;

public sealed class RecordingSessionManagerTests
{
    [Fact]
    public async Task StartAndStop_CreatesHistoryEntry()
    {
        using var temp = new TempDirectory();
        var engine = new FakeEngine();
        var history = new InMemoryHistoryRepository();
        var manager = CreateManager(temp, engine, history, new FakePermissions());
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
        Assert.Equal(RecordingState.Recording, manager.State);
        Assert.NotNull(start.FilePath);
        Assert.True(File.Exists(start.FilePath));
        Assert.True(engine.Options!.IncludeMicrophone);
        Assert.True(manager.MicrophoneAvailable);

        engine.ElapsedSeconds = 12.3;
        var stop = await manager.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(stop.Success);
        Assert.Equal(RecordingState.Completed, manager.State);
        var entry = Assert.Single(history.List());
        Assert.Equal(CaptureKind.Recording, entry.Kind);
        Assert.Equal(640, entry.Width);
        Assert.Equal(360, entry.Height);
        Assert.InRange(entry.Duration!.Value.TotalSeconds, 12.29, 12.31);
        Assert.Contains("recording", entry.Tags);
    }

    [Fact]
    public async Task PauseResume_TransitionsState()
    {
        using var temp = new TempDirectory();
        var manager = CreateManager(temp, new FakeEngine(), new InMemoryHistoryRepository(), new FakePermissions());
        await manager.StartAsync(
            CreateOptions(),
            TestContext.Current.CancellationToken);

        Assert.True(await manager.PauseAsync(TestContext.Current.CancellationToken));
        Assert.Equal(RecordingState.Paused, manager.State);
        Assert.True(await manager.ResumeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(RecordingState.Recording, manager.State);
    }

    [Fact]
    public async Task ScreenPermissionDenied_DoesNotStartEngine()
    {
        using var temp = new TempDirectory();
        var engine = new FakeEngine();
        var permissions = new FakePermissions { Screen = PermissionStatus.Denied };
        var manager = CreateManager(temp, engine, new InMemoryHistoryRepository(), permissions);

        var result = await manager.StartAsync(CreateOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(0, engine.StartCount);
        Assert.Equal(RecordingState.Failed, manager.State);
        Assert.True(permissions.OpenedSettings);
    }

    [Fact]
    public async Task MicrophoneDenied_RecordsWithoutAudio()
    {
        using var temp = new TempDirectory();
        var engine = new FakeEngine();
        var permissions = new FakePermissions { Microphone = PermissionStatus.Denied };
        var manager = CreateManager(temp, engine, new InMemoryHistoryRepository(), permissions);

        var result = await manager.StartAsync(CreateOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.False(engine.Options!.IncludeMicrophone);
        Assert.False(manager.MicrophoneAvailable);
        Assert.Equal("started-no-mic", result.Message);
    }

    [Fact]
    public async Task StopFailure_MarksSessionFailed()
    {
        using var temp = new TempDirectory();
        var engine = new FakeEngine { ThrowOnStop = true };
        var history = new InMemoryHistoryRepository();
        var manager = CreateManager(temp, engine, history, new FakePermissions());
        await manager.StartAsync(CreateOptions(), TestContext.Current.CancellationToken);

        var result = await manager.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(RecordingState.Failed, manager.State);
        Assert.Equal(0, history.Count());
    }

    [Fact]
    public async Task GetStatus_PropagatesEngineFailure()
    {
        using var temp = new TempDirectory();
        var engine = new FakeEngine();
        var manager = CreateManager(temp, engine, new InMemoryHistoryRepository(), new FakePermissions());
        await manager.StartAsync(CreateOptions(), TestContext.Current.CancellationToken);

        engine.Fail("permission revoked");
        var status = manager.GetStatus();

        Assert.Equal(RecordingState.Failed, manager.State);
        Assert.Equal("permission revoked", status.Error);

        engine.ThrowOnStop = true;
        var stop = await manager.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(stop.Success);
        Assert.Equal(1, engine.StopCount);
    }

    private static RecordingSessionManager CreateManager(
        TempDirectory temp,
        FakeEngine engine,
        InMemoryHistoryRepository history,
        FakePermissions permissions)
    {
        var platform = new PlatformDescriptor(PlatformKind.Mock, "Mock", "test");
        var settings = new SettingsService(
            new InMemorySettingsRepository(),
            new TestPaths(temp.Path),
            platform);
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        return new RecordingSessionManager(
            engine,
            permissions,
            history,
            settings,
            new CaptureFileNamer(clock),
            clock,
            new InMemoryAppEventBus(),
            new TestLocalizer());
    }

    private static RecordingOptions CreateOptions() => new(
        "1",
        new PixelRect(0, 0, 640, 360),
        2,
        30,
        12,
        1080,
        true);

    private sealed class FakeEngine : IRecordingService
    {
        private RecordingEngineState _state = RecordingEngineState.Idle;

        public int StartCount { get; private set; }

        public RecordingOptions? Options { get; private set; }

        public double ElapsedSeconds { get; set; }

        public bool ThrowOnStop { get; set; }

        public int StopCount { get; private set; }

        public string? Error { get; private set; }

        public void Fail(string error)
        {
            Error = error;
            _state = RecordingEngineState.Failed;
        }

        public Task StartAsync(
            string outputPath,
            RecordingOptions options,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            Options = options;
            _state = RecordingEngineState.Recording;
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return File.WriteAllBytesAsync(outputPath, new byte[] { 1, 2, 3 }, cancellationToken);
        }

        public Task<bool> PauseAsync(CancellationToken cancellationToken = default)
        {
            if (_state != RecordingEngineState.Recording)
            {
                return Task.FromResult(false);
            }

            _state = RecordingEngineState.Paused;
            return Task.FromResult(true);
        }

        public Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
        {
            if (_state != RecordingEngineState.Paused)
            {
                return Task.FromResult(false);
            }

            _state = RecordingEngineState.Recording;
            return Task.FromResult(true);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            if (ThrowOnStop)
            {
                throw new InvalidOperationException("stop failed");
            }

            _state = RecordingEngineState.Completed;
            return Task.CompletedTask;
        }

        public Task<bool> SetMicrophoneAsync(bool enabled, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public RecordingEngineStatus GetStatus() =>
            new(_state, ElapsedSeconds, 0.5, Error);
    }

    private sealed class FakePermissions : IPermissionService
    {
        public PermissionStatus Screen { get; set; } = PermissionStatus.Granted;

        public PermissionStatus Microphone { get; set; } = PermissionStatus.Granted;

        public bool OpenedSettings { get; private set; }

        public Task<PermissionStatus> CheckAsync(
            PermissionKind kind,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(kind == PermissionKind.ScreenRecording ? Screen : Microphone);

        public Task<PermissionStatus> RequestAsync(
            PermissionKind kind,
            CancellationToken cancellationToken = default) =>
            CheckAsync(kind, cancellationToken);

        public Task OpenSettingsAsync(
            PermissionKind kind,
            CancellationToken cancellationToken = default)
        {
            OpenedSettings = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestLocalizer : ILocalizer
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal)
        {
            ["recordingBusy"] = "busy",
            ["recordingPermissionRequired"] = "permission",
            ["recordingFailed"] = "failed",
            ["recordingStarted"] = "started",
            ["recordingStartedNoMicrophone"] = "started-no-mic",
            ["recordingSaved"] = "saved",
            ["recordingNotActive"] = "inactive"
        };

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<string>? LanguageChanged
        {
            add { }
            remove { }
        }

        public string Language => "en";

        public IReadOnlyList<string> SupportedLanguages => new[] { "zh", "en" };

        public string this[string key] => _values.TryGetValue(key, out var value) ? value : key;

        public bool SetLanguage(string language) => true;
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
                "ssc-recording-tests",
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
