namespace SSC.Application.Abstractions;

public enum PermissionKind
{
    ScreenRecording = 0,
    Microphone = 1,
    Camera = 2
}

public enum PermissionStatus
{
    Unknown = 0,
    Granted = 1,
    Denied = 2,
    NotDetermined = 3,
    Restricted = 4
}

public interface IPermissionService
{
    Task<PermissionStatus> CheckAsync(PermissionKind kind, CancellationToken cancellationToken = default);

    Task<PermissionStatus> RequestAsync(PermissionKind kind, CancellationToken cancellationToken = default);

    Task OpenSettingsAsync(PermissionKind kind, CancellationToken cancellationToken = default);
}
