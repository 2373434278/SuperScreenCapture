using Microsoft.Extensions.DependencyInjection;
using SSC.Application.Abstractions;
using SSC.Platform.Abstractions;

namespace SSC.Platform.Mock;

public static class MockServiceCollectionExtensions
{
    public static IServiceCollection AddMockPlatform(this IServiceCollection services)
    {
        services.AddSingleton(new PlatformDescriptor(
            PlatformKind.Mock,
            "Mock platform",
            "Mock"));
        services.AddSingleton(new PlatformCapabilities(
            ScreenCapture: true,
            WindowCapture: true,
            Recording: true,
            Camera: true,
            SystemAudio: false,
            Ocr: true,
            GlobalHotkeys: true));
        services.AddSingleton<IScreenCaptureService, MockScreenCaptureService>();
        services.AddSingleton<IPermissionService, MockPermissionService>();
        services.AddSingleton<IHotkeyService, MockHotkeyService>();
        services.AddSingleton<IAutoStartService, MockAutoStartService>();
        services.AddSingleton<IClipboardService, MockClipboardService>();
        services.AddSingleton<IRecordingService, MockRecordingService>();
        services.AddSingleton<ICameraPreviewService, MockCameraPreviewService>();
        return services;
    }
}
