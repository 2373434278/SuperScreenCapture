namespace SSC.Domain.Geometry;

public readonly record struct PixelSize(int Width, int Height)
{
    public static readonly PixelSize Empty = new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"{Width}x{Height}";
}
