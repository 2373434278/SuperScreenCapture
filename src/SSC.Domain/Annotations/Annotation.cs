using SSC.Domain.Geometry;

namespace SSC.Domain.Annotations;

public sealed class Annotation
{
    public Annotation(
        AnnotationKind kind,
        PixelRect bounds,
        AnnotationStyle style,
        IReadOnlyList<PixelPoint>? points = null,
        string text = "",
        int step = 0)
    {
        Kind = kind;
        Bounds = bounds;
        Style = style ?? throw new ArgumentNullException(nameof(style));
        Points = points is null ? Array.Empty<PixelPoint>() : points.ToArray();
        Text = text ?? string.Empty;
        Step = step;
    }

    public AnnotationKind Kind { get; }

    public PixelRect Bounds { get; }

    public AnnotationStyle Style { get; }

    public IReadOnlyList<PixelPoint> Points { get; }

    public string Text { get; }

    public int Step { get; }

    public bool HitTest(PixelPoint point, int tolerance = 6) =>
        Bounds.Inflate(tolerance).Contains(point);

    public Annotation WithStep(int step) => new(Kind, Bounds, Style, Points, Text, step);
}
