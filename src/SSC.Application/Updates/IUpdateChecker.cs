namespace SSC.Application.Updates;

public enum UpdateCheckState
{
    UpToDate = 0,
    UpdateAvailable = 1,
    Unreachable = 2,
    Failed = 3
}

public sealed record UpdateCheckResult(
    UpdateCheckState State,
    AppVersion Current,
    AppVersion? Latest,
    string? ReleaseUrl,
    string? Notes,
    DateTimeOffset CheckedAt,
    string? Error);

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(string channel, bool force, CancellationToken cancellationToken = default);
}
