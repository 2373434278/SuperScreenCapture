using SSC.Application.Scroll;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using Xunit;

namespace SSC.Application.Tests;

public sealed class ScrollStitcherTests
{
    [Fact]
    public void Stitch_FiveScreens_RestoresLongImage()
    {
        var tall = CreateTallFrame(400, 620);
        var frames = new List<CaptureFrame>();
        for (var offset = 0; offset <= 320; offset += 80)
        {
            frames.Add(tall.Crop(new PixelRect(0, offset, 400, 300)));
        }

        var result = new ScrollStitcher().Stitch(frames);

        Assert.Equal(400, result.Width);
        Assert.Equal(620, result.Height);
        Assert.Equal(tall.Pixels, result.Pixels);
        Assert.True(Similarity(tall, result) >= 0.98);
    }

    [Fact]
    public void Stitch_RejectsIdenticalFrames()
    {
        var frame = CreateTallFrame(320, 240);

        Assert.Throws<ScrollStitchException>(() =>
            new ScrollStitcher().Stitch(new[] { frame, frame }));
    }

    [Fact]
    public void Stitch_RejectsFramesWithoutReliableOverlap()
    {
        var first = CreateTallFrame(320, 240);
        var second = CreateTallFrame(320, 240, seed: 99);

        Assert.Throws<ScrollStitchException>(() =>
            new ScrollStitcher().Stitch(new[] { first, second }));
    }

    private static double Similarity(CaptureFrame expected, CaptureFrame actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return 0;
        }

        var matches = 0;
        var total = expected.Width * expected.Height;
        for (var offset = 0; offset < expected.Pixels.Length; offset += 4)
        {
            if (expected.Pixels[offset] == actual.Pixels[offset] &&
                expected.Pixels[offset + 1] == actual.Pixels[offset + 1] &&
                expected.Pixels[offset + 2] == actual.Pixels[offset + 2])
            {
                matches++;
            }
        }

        return matches / (double)total;
    }

    private static CaptureFrame CreateTallFrame(int width, int height, int seed = 7)
    {
        var pixels = new byte[width * height * 4];
        var random = new Random(seed);
        random.NextBytes(pixels);
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }

        return new CaptureFrame(width, height, pixels, DateTimeOffset.UtcNow);
    }
}
