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
    public void DisplayDescriptor_RejectsInvalidScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DisplayDescriptor.Create("d1", "Display", new Domain.Geometry.PixelRect(0, 0, 10, 10), 0));
    }
}
