namespace SSC.Domain.Geometry;

public readonly record struct PixelPoint(int X, int Y)
{
    public static readonly PixelPoint Origin = new(0, 0);

    public PixelPoint Offset(int dx, int dy) => new(X + dx, Y + dy);

    public override string ToString() => $"{X},{Y}";
}
