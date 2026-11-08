namespace SSC.Domain.Capture;

/// <summary>
/// A single captured frame in BGRA32 format, top-down.
/// </summary>
public sealed class CaptureFrame
{
    public CaptureFrame(int width, int height, ReadOnlySpan<byte> pixels, DateTimeOffset capturedAt)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        var required = checked(width * height * 4);
        if (pixels.Length < required)
        {
            throw new ArgumentException($"Expected at least {required} bytes of BGRA data.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels[..required].ToArray();
        CapturedAt = capturedAt;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * 4;

    public byte[] Pixels { get; }

    public DateTimeOffset CapturedAt { get; }

    public CaptureFrame Crop(Geometry.PixelRect rect)
    {
        var clipped = rect.Intersect(new Geometry.PixelRect(0, 0, Width, Height));
        if (clipped.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "Crop rectangle is outside the frame.");
        }

        var cropped = new byte[checked(clipped.Width * clipped.Height * 4)];
        for (var row = 0; row < clipped.Height; row++)
        {
            var sourceOffset = ((clipped.Y + row) * Stride) + (clipped.X * 4);
            var targetOffset = row * clipped.Width * 4;
            Buffer.BlockCopy(Pixels, sourceOffset, cropped, targetOffset, clipped.Width * 4);
        }

        return new CaptureFrame(clipped.Width, clipped.Height, cropped, CapturedAt);
    }
}
