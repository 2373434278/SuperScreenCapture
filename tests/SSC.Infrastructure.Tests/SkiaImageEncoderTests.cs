using SSC.Domain.Capture;
using SSC.Infrastructure.Imaging;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class SkiaImageEncoderTests
{
    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("webp")]
    public async Task EncodeAsync_ProducesFormatSignature(string format)
    {
        var frame = new CaptureFrame(8, 8, CreatePixels(8, 8), DateTimeOffset.UtcNow);
        var encoder = new SkiaImageEncoder();

        var bytes = await encoder.EncodeAsync(
            frame,
            format,
            90,
            TestContext.Current.CancellationToken);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 12);

        switch (format)
        {
            case "png":
                Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
                break;
            case "jpg":
                Assert.Equal(new byte[] { 0xFF, 0xD8 }, bytes[..2]);
                break;
            case "webp":
                Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
                break;
        }
    }

    private static byte[] CreatePixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 40;
            pixels[i + 1] = 120;
            pixels[i + 2] = 220;
            pixels[i + 3] = 255;
        }

        return pixels;
    }
}
