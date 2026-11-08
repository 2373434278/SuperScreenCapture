using System.Diagnostics;
using System.Runtime.InteropServices;
using SSC.Application.Abstractions;

namespace SSC.Platform.Mac;

public sealed class MacPermissionService : IPermissionService
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public Task<PermissionStatus> CheckAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult(PermissionStatus.Unknown);
        }

        return Task.FromResult(kind switch
        {
            PermissionKind.ScreenRecording => CGPreflightScreenCaptureAccess()
                ? PermissionStatus.Granted
                : PermissionStatus.Denied,
            PermissionKind.Microphone => MacNativeMethods.ssc_microphone_permission_status() switch
            {
                1 => PermissionStatus.Granted,
                2 => PermissionStatus.Denied,
                3 => PermissionStatus.Restricted,
                _ => PermissionStatus.NotDetermined
            },
            _ => PermissionStatus.NotDetermined
        });
    }

    public Task<PermissionStatus> RequestAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult(PermissionStatus.Unknown);
        }

        if (kind == PermissionKind.ScreenRecording)
        {
            return Task.FromResult(CGRequestScreenCaptureAccess()
                ? PermissionStatus.Granted
                : PermissionStatus.Denied);
        }

        if (kind == PermissionKind.Microphone)
        {
            return Task.Run(() => MacNativeMethods.ssc_request_microphone_permission() switch
            {
                1 => PermissionStatus.Granted,
                2 => PermissionStatus.Denied,
                _ => PermissionStatus.NotDetermined
            }, cancellationToken);
        }

        return Task.FromResult(PermissionStatus.NotDetermined);
    }

    public Task OpenSettingsAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Task.CompletedTask;
        }

        var anchor = kind switch
        {
            PermissionKind.Microphone => "Privacy_Microphone",
            PermissionKind.Camera => "Privacy_Camera",
            _ => "Privacy_ScreenCapture"
        };

        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"x-apple.systempreferences:com.apple.preference.security?{anchor}",
            UseShellExecute = false
        });

        return Task.CompletedTask;
    }

    [DllImport(CoreGraphics, EntryPoint = "CGPreflightScreenCaptureAccess")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightScreenCaptureAccess();

    [DllImport(CoreGraphics, EntryPoint = "CGRequestScreenCaptureAccess")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestScreenCaptureAccess();
}
