using SSC.Domain.Geometry;

namespace SSC.Domain.Capture;

public sealed record WindowDescriptor(
    string Id,
    string Title,
    PixelRect Bounds,
    string OwnerName);
