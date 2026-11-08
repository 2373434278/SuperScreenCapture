using SSC.Domain.Geometry;

namespace SSC.Domain.Annotations;

/// <summary>
/// Immutable annotation snapshot. Coordinates are physical pixels relative to the captured frame;
/// <see cref="Crop"/> selects the exported region.
/// </summary>
public sealed class AnnotationDocument
{
    public AnnotationDocument(
        int width,
        int height,
        PixelRect crop,
        IEnumerable<Annotation> annotations)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        Width = width;
        Height = height;

        var frame = new PixelRect(0, 0, width, height);
        var clipped = crop.Intersect(frame);
        Crop = clipped.IsEmpty ? frame : clipped;
        Annotations = annotations?.ToArray() ?? Array.Empty<Annotation>();
    }

    public int Width { get; }

    public int Height { get; }

    public PixelRect Crop { get; }

    public IReadOnlyList<Annotation> Annotations { get; }

    public static AnnotationDocument Create(int width, int height) =>
        new(width, height, new PixelRect(0, 0, width, height), Array.Empty<Annotation>());

    public AnnotationDocument WithAnnotation(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        return new AnnotationDocument(Width, Height, Crop, Annotations.Append(annotation));
    }

    public AnnotationDocument WithCrop(PixelRect crop)
    {
        var clipped = crop.Intersect(Crop);
        return clipped.IsEmpty || clipped == Crop
            ? this
            : new AnnotationDocument(Width, Height, clipped, Annotations);
    }

    public AnnotationDocument RemoveAt(int index)
    {
        if (index < 0 || index >= Annotations.Count)
        {
            return this;
        }

        var remaining = Annotations.Where((_, i) => i != index);
        return new AnnotationDocument(Width, Height, Crop, remaining);
    }

    public AnnotationDocument RenumberSteps()
    {
        var number = 1;
        var renumbered = new List<Annotation>(Annotations.Count);
        foreach (var annotation in Annotations)
        {
            renumbered.Add(annotation.Kind == AnnotationKind.Step
                ? annotation.WithStep(number++)
                : annotation);
        }

        return new AnnotationDocument(Width, Height, Crop, renumbered);
    }

    public int NextStepNumber() =>
        Annotations
            .Where(static annotation => annotation.Kind == AnnotationKind.Step)
            .Select(static annotation => annotation.Step)
            .DefaultIfEmpty(0)
            .Max() + 1;

    public int FindLastAt(PixelPoint point, int tolerance = 6)
    {
        for (var i = Annotations.Count - 1; i >= 0; i--)
        {
            if (Annotations[i].HitTest(point, tolerance))
            {
                return i;
            }
        }

        return -1;
    }
}
