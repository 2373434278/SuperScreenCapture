using SSC.Application.Capture;

namespace SSC.UI.Capture;

/// <summary>截图/录制流程的统一结果，包含成功标记、提示文案与可选结果。</summary>
public sealed record CaptureOutcome(bool Success, string Message, ScreenshotResult? Result = null);
