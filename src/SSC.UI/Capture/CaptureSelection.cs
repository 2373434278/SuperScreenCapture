using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.UI.Capture;

/// <summary>覆盖层回传的选区：显示器、冻结帧、区域或窗口目标。</summary>
public sealed record CaptureSelection(
    DisplayDescriptor Display,
    CaptureFrame Frame,
    PixelRect Rect,
    WindowDescriptor? Window = null);

