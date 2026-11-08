namespace SSC.Platform.Abstractions;

public sealed record PlatformDescriptor(PlatformKind Kind, string DisplayName, string OperatingSystem);

public sealed record PlatformCapabilities(
    bool ScreenCapture,
    bool WindowCapture,
    bool Recording,
    bool Camera,
    bool SystemAudio,
    bool Ocr,
    bool GlobalHotkeys);
