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
            ScreenCapture: false,
            WindowCapture: false,
            SystemAudio: false,
            Ocr: false,
            GlobalHotkeys: false));
        services.AddSingleton<IScreenCaptureService, WindowsScreenCaptureService>();
        services.AddSingleton<IPermissionService, WindowsPermissionService>();
        services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
        services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
        return services;
    }
}
