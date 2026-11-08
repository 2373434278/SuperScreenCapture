using SSC.Application.Abstractions;

namespace SSC.Platform.Mock;

public sealed class MockPermissionService : IPermissionService
{
    public Task<PermissionStatus> CheckAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PermissionStatus.Granted);

    public Task<PermissionStatus> RequestAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PermissionStatus.Granted);

    public Task OpenSettingsAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
