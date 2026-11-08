using SSC.Domain.Capture;
using Xunit;

namespace SSC.Domain.Tests;

public sealed class CaptureFrameTests
{
    [Fact]
    public void Constructor_RejectsShortBuffers()
    {
        Assert.Throws<ArgumentException>(() =>
            new CaptureFrame(2, 2, new byte[15], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_CopiesPixelData()
    {
        var source = new byte[2 * 2 * 4];
        source[0] = 42;
        var frame = new CaptureFrame(2, 2, source, DateTimeOffset.UtcNow);
        source[0] = 0;

        Assert.Equal(42, frame.Pixels[0]);
        Assert.Equal(8, frame.Stride);
    }

    [Fact]
    public void Crop_ReturnsSelectedPixels()
    {
        var pixels = new byte[3 * 2 * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)i;
        }

        var frame = new CaptureFrame(3, 2, pixels, DateTimeOffset.UtcNow);
        var cropped = frame.Crop(new Domain.Geometry.PixelRect(1, 1, 2, 1));

        Assert.Equal(2, cropped.Width);
        Assert.Equal(1, cropped.Height);
        Assert.Equal(16, cropped.Pixels[0]);
        Assert.Equal(19, cropped.Pixels[3]);
    }

    [Fact]
    public void Crop_ClampsToFrameBounds()
    {
        var frame = new CaptureFrame(2, 2, new byte[2 * 2 * 4], DateTimeOffset.UtcNow);

        var cropped = frame.Crop(new Domain.Geometry.PixelRect(1, 1, 10, 10));

        Assert.Equal(1, cropped.Width);
        Assert.Equal(1, cropped.Height);
    }

    [Fact]
    public void Crop_RejectsRectOutsideFrame()
    {
        var frame = new CaptureFrame(2, 2, new byte[2 * 2 * 4], DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            frame.Crop(new Domain.Geometry.PixelRect(5, 5, 2, 2)));
    }

    [Fact]
    public void DisplayDescriptor_RejectsInvalidScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DisplayDescriptor.Create("d1", "Display", new Domain.Geometry.PixelRect(0, 0, 10, 10), 0));
    }
}
