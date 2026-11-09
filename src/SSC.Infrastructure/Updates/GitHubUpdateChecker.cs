using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SSC.Application;
using SSC.Application.Abstractions;
using SSC.Application.Updates;

namespace SSC.Infrastructure.Updates;

public sealed class GitHubUpdateChecker : IUpdateChecker
{
    public const string CacheKey = "app.update.cache";
    public const string DefaultRepository = "2373434278/SuperScreenCapture";

    private static readonly TimeSpan SuccessTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ISettingsRepository _settings;
    private readonly IClock _clock;
    private readonly string _repository;
    private readonly AppVersion _current;

    public GitHubUpdateChecker(ISettingsRepository settings, IClock clock)
        : this(settings, clock, new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(10)
        }, DefaultRepository, null)
    {
    }

    public GitHubUpdateChecker(
        ISettingsRepository settings,
        IClock clock,
        HttpMessageHandler handler,
        string? repository = null,
        AppVersion? current = null)
        : this(settings, clock, new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) }, repository, current)
    {
    }

    private GitHubUpdateChecker(
        ISettingsRepository settings,
        IClock clock,
        HttpClient http,
        string? repository,
        AppVersion? current)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _repository = string.IsNullOrWhiteSpace(repository) ? DefaultRepository : repository;
        _current = current ?? AppVersion.Parse(AppInfo.Version);
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string channel,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var normalizedChannel = string.Equals(channel, "preview", StringComparison.OrdinalIgnoreCase)
            ? "preview"
            : "stable";
        var now = _clock.UtcNow;
        var cache = ReadCache();

        if (!force && cache is not null)
        {
            if (cache.SucceededAt is { } succeededAt && now - succeededAt < SuccessTtl && cache.LatestTag is not null)
            {
                return FromCache(cache, now);
            }

            if (cache.SucceededAt is null && now - cache.AttemptedAt < FailureTtl)
            {
                return new UpdateCheckResult(
                    ParseState(cache.Status),
                    _current,
                    null,
                    null,
                    null,
                    now,
                    cache.Error);
            }
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{_repository}/releases?per_page=30");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SuperScreenCapture", _current.Display));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));

            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode == (HttpStatusCode)403
                    ? "GitHub API rate limit reached."
                    : $"GitHub API returned {(int)response.StatusCode}.";
                WriteFailure(now, message);
                return new UpdateCheckResult(UpdateCheckState.Failed, _current, null, null, null, now, message);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync<List<ReleaseDto>>(stream, JsonOptions, timeout.Token)
                .ConfigureAwait(false) ?? new List<ReleaseDto>();

            var candidates = releases
                .Where(static release => !release.Draft && !string.IsNullOrWhiteSpace(release.TagName))
                .Where(release => normalizedChannel == "preview" || !release.Prerelease)
                .Select(release => (Release: release, Version: ParseVersion(release.TagName)))
                .Where(static pair => pair.Version is not null)
                .Select(static pair => (pair.Release, Version: pair.Version!))
                .OrderByDescending(static pair => pair.Version)
                .ToList();

            if (candidates.Count == 0)
            {
                const string message = "No published releases were found.";
                WriteFailure(now, message);
                return new UpdateCheckResult(UpdateCheckState.Failed, _current, null, null, null, now, message);
            }

            var latest = candidates[0];
            var state = latest.Version > _current ? UpdateCheckState.UpdateAvailable : UpdateCheckState.UpToDate;
            var result = new UpdateCheckResult(
                state,
                _current,
                latest.Version,
                latest.Release.HtmlUrl,
                latest.Release.Body,
                now,
                null);

            WriteCache(new CacheEntry(
                latest.Version.Display,
                latest.Release.HtmlUrl,
                latest.Release.Body,
                now,
                now,
                state.ToString(),
                null));

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            const string message = "The update check timed out.";
            WriteFailure(now, message);
            return new UpdateCheckResult(UpdateCheckState.Unreachable, _current, null, null, null, now, message);
        }
        catch (HttpRequestException exception)
        {
            WriteFailure(now, exception.Message);
            return new UpdateCheckResult(UpdateCheckState.Unreachable, _current, null, null, null, now, exception.Message);
        }
        catch (JsonException exception)
        {
            WriteFailure(now, exception.Message);
            return new UpdateCheckResult(UpdateCheckState.Failed, _current, null, null, null, now, exception.Message);
        }
    }

    private UpdateCheckResult FromCache(CacheEntry cache, DateTimeOffset now)
    {
        if (!AppVersion.TryParse(cache.LatestTag, out var latest) || latest is null)
        {
            return new UpdateCheckResult(UpdateCheckState.Failed, _current, null, null, null, now, cache.Error);
        }

        var state = latest > _current ? UpdateCheckState.UpdateAvailable : UpdateCheckState.UpToDate;
        return new UpdateCheckResult(state, _current, latest, cache.ReleaseUrl, cache.Notes, now, null);
    }

    private CacheEntry? ReadCache()
    {
        try
        {
            var raw = _settings.Read(CacheKey);
            return string.IsNullOrWhiteSpace(raw)
                ? null
                : JsonSerializer.Deserialize<CacheEntry>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void WriteCache(CacheEntry entry) =>
        _settings.Write(CacheKey, JsonSerializer.Serialize(entry, JsonOptions));

    private void WriteFailure(DateTimeOffset now, string error) =>
        WriteCache(new CacheEntry(null, null, null, now, null, UpdateCheckState.Unreachable.ToString(), error));

    private static UpdateCheckState ParseState(string? status) =>
        Enum.TryParse<UpdateCheckState>(status, ignoreCase: true, out var state)
            ? state
            : UpdateCheckState.Failed;

    private static AppVersion? ParseVersion(string? tag) =>
        AppVersion.TryParse(tag, out var version) ? version : null;

    private sealed record ReleaseDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string? TagName,
        [property: System.Text.Json.Serialization.JsonPropertyName("html_url")] string? HtmlUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("body")] string? Body,
        [property: System.Text.Json.Serialization.JsonPropertyName("draft")] bool Draft,
        [property: System.Text.Json.Serialization.JsonPropertyName("prerelease")] bool Prerelease);

    private sealed record CacheEntry(
        string? LatestTag,
        string? ReleaseUrl,
        string? Notes,
        DateTimeOffset AttemptedAt,
        DateTimeOffset? SucceededAt,
        string? Status,
        string? Error);
}
