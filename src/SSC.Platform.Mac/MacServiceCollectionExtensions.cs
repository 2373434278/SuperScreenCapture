using Microsoft.Extensions.DependencyInjection;
using SSC.Application.Abstractions;
using SSC.Platform.Abstractions;

namespace SSC.Platform.Mac;

public static class MacServiceCollectionExtensions
{
    public static IServiceCollection AddMacPlatform(this IServiceCollection services)
    {
        services.AddSingleton(new PlatformDescriptor(
            PlatformKind.MacOS,
            "macOS",
            Environment.OSVersion.VersionString));
        services.AddSingleton(new PlatformCapabilities(
            ScreenCapture: true,
            WindowCapture: true,
            Recording: true,
            Camera: false,
            SystemAudio: false,
            Ocr: false,
            GlobalHotkeys: false));
        services.AddSingleton<IScreenCaptureService, MacScreenCaptureService>();
        services.AddSingleton<IPermissionService, MacPermissionService>();
        services.AddSingleton<IHotkeyService, MacHotkeyService>();
        services.AddSingleton<IAutoStartService, MacAutoStartService>();
        services.AddSingleton<IClipboardService, MacClipboardService>();
        services.AddSingleton<IRecordingService, MacRecordingService>();
        services.AddSingleton<ICameraPreviewService, MacCameraPreviewService>();
        return services;
    }
}
