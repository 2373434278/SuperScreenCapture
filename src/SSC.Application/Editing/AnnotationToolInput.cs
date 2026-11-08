using SSC.Domain.Geometry;

namespace SSC.Application.Editing;

public sealed record AnnotationToolInput(
    PixelPoint Start,
    PixelPoint End,
    IReadOnlyList<PixelPoint>? Points = null,
    string Text = "");
