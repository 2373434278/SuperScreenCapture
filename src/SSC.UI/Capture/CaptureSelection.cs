using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.UI.Capture;

public sealed record CaptureSelection(
    DisplayDescriptor Display,
    CaptureFrame Frame,
    PixelRect Rect,
    WindowDescriptor? Window = null);

