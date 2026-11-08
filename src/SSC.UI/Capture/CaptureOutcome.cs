using SSC.Application.Capture;

namespace SSC.UI.Capture;

public sealed record CaptureOutcome(bool Success, string Message, ScreenshotResult? Result = null);
