using System.Net;
using System.Text;
using SSC.Application.Abstractions;
using SSC.Application.Updates;
using SSC.Infrastructure.Updates;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class GitHubUpdateCheckerTests
{
    private const string ReleasesJson = """
        [
          {
            "tag_name": "v1.2.0",
            "html_url": "https://example.test/releases/1.2.0",
            "body": "stable notes",
            "draft": false,
            "prerelease": false
          },
          {
            "tag_name": "v1.3.0-preview.1",
            "html_url": "https://example.test/releases/1.3.0",
            "body": "preview notes",
            "draft": false,
            "prerelease": true
          }
        ]
        """;

    [Fact]
    public async Task StableChannel_IgnoresPrerelease()
    {
        var handler = new StubHandler(ReleasesJson);
        var checker = Create(handler);

        var result = await checker.CheckAsync("stable", force: false);

        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal("1.2.0", result.Latest?.Display);
        Assert.Equal("https://example.test/releases/1.2.0", result.ReleaseUrl);
        Assert.Contains("SuperScreenCapture", handler.LastUserAgent);
    }

    [Fact]
    public async Task PreviewChannel_SelectsHighestIncludingPrerelease()
    {
        var checker = Create(new StubHandler(ReleasesJson));

        var result = await checker.CheckAsync("preview", force: false);

        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal("1.3.0-preview.1", result.Latest?.Display);
    }

    [Fact]
    public async Task SameVersion_ReportsUpToDate()
    {
        var checker = Create(new StubHandler(ReleasesJson), current: AppVersion.Parse("1.2.0"));

        var result = await checker.CheckAsync("stable", force: false);

        Assert.Equal(UpdateCheckState.UpToDate, result.State);
    }

    [Fact]
    public async Task CachedResult_IsReusedWithinTtl()
    {
        var settings = new InMemorySettings();
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var handler = new StubHandler(ReleasesJson);
        var checker = Create(handler, settings: settings, clock: clock);

        var first = await checker.CheckAsync("stable", force: false);
        Assert.Equal(UpdateCheckState.UpdateAvailable, first.State);
        Assert.Equal(1, handler.Requests);

        clock.UtcNow = clock.UtcNow.AddHours(2);
        var second = await checker.CheckAsync("stable", force: false);

        Assert.Equal(UpdateCheckState.UpdateAvailable, second.State);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Force_BypassesCache()
    {
        var settings = new InMemorySettings();
        var clock = new TestClock();
        var handler = new StubHandler(ReleasesJson);
        var checker = Create(handler, settings: settings, clock: clock);

        await checker.CheckAsync("stable", force: false);
        await checker.CheckAsync("stable", force: true);

        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task HttpFailure_ReturnsUnreachableAndCachesFailure()
    {
        var settings = new InMemorySettings();
        var clock = new TestClock();
        var handler = new StubHandler(ReleasesJson) { ThrowOnRequest = true };
        var checker = Create(handler, settings: settings, clock: clock);

        var result = await checker.CheckAsync("stable", force: false);

        Assert.Equal(UpdateCheckState.Unreachable, result.State);
        Assert.NotNull(result.Error);

        handler.ThrowOnRequest = false;
        clock.UtcNow = clock.UtcNow.AddMinutes(30);
        var cached = await checker.CheckAsync("stable", force: false);

        Assert.Equal(UpdateCheckState.Unreachable, cached.State);
        Assert.Equal(1, handler.Requests);

        clock.UtcNow = clock.UtcNow.AddHours(2);
        var retried = await checker.CheckAsync("stable", force: false);

        Assert.Equal(UpdateCheckState.UpdateAvailable, retried.State);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task EmptyReleaseList_ReturnsFailed()
    {
        var checker = Create(new StubHandler("[]"));

        var result = await checker.CheckAsync("stable", force: false);

        Assert.Equal(UpdateCheckState.Failed, result.State);
    }

    private static GitHubUpdateChecker Create(
        StubHandler handler,
        AppVersion? current = null,
        InMemorySettings? settings = null,
        TestClock? clock = null) =>
        new(
            settings ?? new InMemorySettings(),
            clock ?? new TestClock(),
            handler,
            "owner/repo",
            current ?? AppVersion.Parse("1.0.0"));

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class InMemorySettings : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new();

        public string? Read(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public void Write(string key, string value) => _values[key] = value;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHandler(string json) => _json = json;

        public int Requests { get; private set; }

        public bool ThrowOnRequest { get; set; }

        public string LastUserAgent { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            LastUserAgent = request.Headers.UserAgent.ToString();

            if (ThrowOnRequest)
            {
                throw new HttpRequestException("offline");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }
}
