using Microsoft.Extensions.DependencyInjection;
using SSC.Application.Abstractions;
using SSC.Platform.Abstractions;

namespace SSC.Platform.Windows;

public static class WindowsServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton(new PlatformDescriptor(
            PlatformKind.Windows,
            "Windows",
            Environment.OSVersion.VersionString));
        services.AddSingleton(new PlatformCapabilities(
            ScreenCapture: true,
            WindowCapture: true,
            Recording: true,
            Camera: true,
            SystemAudio: false,
            Ocr: false,
            GlobalHotkeys: true));
        services.AddSingleton<IScreenCaptureService, WindowsScreenCaptureService>();
        services.AddSingleton<IPermissionService, WindowsPermissionService>();
        services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
        services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
        services.AddSingleton<IClipboardService, WindowsClipboardService>();
        services.AddSingleton<IRecordingService, WindowsRecordingService>();
        services.AddSingleton<ICameraPreviewService, WindowsCameraPreviewService>();
        services.AddSingleton<IOcrEngine, WindowsOcrEngine>();
        return services;
    }
}
