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
}
