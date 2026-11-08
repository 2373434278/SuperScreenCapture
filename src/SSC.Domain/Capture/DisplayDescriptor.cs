using SSC.Domain.Geometry;

namespace SSC.Domain.Capture;

public sealed record DisplayDescriptor
{
    private DisplayDescriptor(string id, string name, PixelRect bounds, double scaleFactor, bool isPrimary)
    {
        Id = id;
        Name = name;
        Bounds = bounds;
        ScaleFactor = scaleFactor;
        IsPrimary = isPrimary;
    }

    public string Id { get; }

    public string Name { get; }

    public PixelRect Bounds { get; }

    public double ScaleFactor { get; }

    public bool IsPrimary { get; }

    public static DisplayDescriptor Create(
        string id,
        string name,
        PixelRect bounds,
        double scaleFactor,
        bool isPrimary = false)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Display id is required.", nameof(id));
        }

        if (scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor), "Scale factor must be positive.");
        }

        return new DisplayDescriptor(id, name, bounds, scaleFactor, isPrimary);
    }
}
