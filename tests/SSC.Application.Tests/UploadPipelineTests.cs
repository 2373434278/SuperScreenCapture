using System.ComponentModel;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Application.Upload;
using SSC.Platform.Abstractions;
using Xunit;

namespace SSC.Application.Tests;

public sealed class UploadPipelineTests
{
    [Fact]
    public async Task Upload_RetriesTransientFailure()
    {
        var settings = new SettingsService(
            new InMemorySettingsRepository(),
            new TestPaths(),
            new PlatformDescriptor(PlatformKind.Mock, "Mock", "test"));
        var current = settings.Current;
        current.UploadEnabled = true;
        current.Upload.Provider = "fake";
        settings.Save(current);

        var provider = new FakeProvider(failures: 1);
        var pipeline = new UploadPipeline(new[] { provider }, settings, new TestLocalizer());

        var result = await pipeline.UploadAsync(
            new UploadRequest(new byte[] { 1, 2, 3 }, "shot.png", "image/png"),
            maxAttempts: 2,
            TestContext.Current.CancellationToken);

        Assert.Equal("https://example.test/shot.png", result.Url);
        Assert.Equal(2, provider.Attempts);
    }

    [Fact]
    public async Task Upload_RejectsDisabledSetting()
    {
        var settings = new SettingsService(
            new InMemorySettingsRepository(),
            new TestPaths(),
            new PlatformDescriptor(PlatformKind.Mock, "Mock", "test"));
        var pipeline = new UploadPipeline(new[] { new FakeProvider(0) }, settings, new TestLocalizer());

        await Assert.ThrowsAsync<UploadException>(() =>
            pipeline.UploadAsync(
                new UploadRequest(new byte[] { 1 }, "shot.png", "image/png"),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    private sealed class FakeProvider : IUploadProvider
    {
        private readonly int _failures;

        public FakeProvider(int failures)
        {
            _failures = failures;
        }

        public UploadProviderInfo Info { get; } = new("fake", "uploadProviderImgur");

        public int Attempts { get; private set; }

        public bool IsConfigured(UploadSettings settings) => true;

        public Task<UploadResult> UploadAsync(
            UploadRequest request,
            UploadSettings settings,
            CancellationToken cancellationToken = default)
        {
            _ = settings;
            Attempts++;
            if (Attempts <= _failures)
            {
                throw new HttpRequestException("transient");
            }

            return Task.FromResult(new UploadResult(Info.Id, $"https://example.test/{request.FileName}"));
        }

        public Task<bool> DeleteAsync(
            UploadResult result,
            UploadSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class TestLocalizer : ILocalizer
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal)
        {
            ["uploadDisabled"] = "disabled",
            ["uploadNotConfigured"] = "not configured",
            ["uploadEmpty"] = "empty",
            ["uploadFailed"] = "failed"
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

        public IReadOnlyList<string> SupportedLanguages => new[] { "en" };

        public string this[string key] => _values.TryGetValue(key, out var value) ? value : key;

        public bool SetLanguage(string language) => true;
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
            "ssc-upload-tests",
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
