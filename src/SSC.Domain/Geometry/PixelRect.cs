namespace SSC.Domain.Geometry;

/// <summary>
/// A rectangle expressed in physical pixels. Multi-display coordinates may be negative.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static readonly PixelRect Empty = new(0, 0, 0, 0);

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public PixelPoint Location => new(X, Y);

    public PixelSize Size => new(Width, Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromEdges(int left, int top, int right, int bottom)
    {
        var x = Math.Min(left, right);
        var y = Math.Min(top, bottom);
        return new PixelRect(x, y, Math.Abs(right - left), Math.Abs(bottom - top));
    }

    public static PixelRect FromPoints(PixelPoint start, PixelPoint end) =>
        FromEdges(start.X, start.Y, end.X, end.Y);

    public bool Contains(PixelPoint point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        return right <= left || bottom <= top
            ? Empty
            : new PixelRect(left, top, right - left, bottom - top);
    }

    public PixelRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public PixelRect Inflate(int delta) =>
        new(X - delta, Y - delta, Width + (delta * 2), Height + (delta * 2));

    public PixelRect ClampTo(PixelRect bounds) => Intersect(bounds);

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}
