using System.Diagnostics;
using SSC.Application.Abstractions;

namespace SSC.Platform.Windows;

public sealed class WindowsPermissionService : IPermissionService
{
    public Task<PermissionStatus> CheckAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(kind is PermissionKind.ScreenRecording
            or PermissionKind.Microphone
            or PermissionKind.Camera
                ? PermissionStatus.Granted
                : PermissionStatus.NotDetermined);

    public Task<PermissionStatus> RequestAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default) =>
        CheckAsync(kind, cancellationToken);

    public Task OpenSettingsAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        var uri = kind switch
        {
            PermissionKind.Microphone => "ms-settings:privacy-microphone",
            PermissionKind.Camera => "ms-settings:privacy-webcam",
            _ => "ms-settings:privacy-screenshots"
        };

        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}
