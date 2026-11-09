using SkiaSharp;
using SSC.Application.Animated;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class GifEncoderTests
{
    [Fact]
    public void Encode_ProducesDecodableAnimatedGif()
    {
        using var stream = new MemoryStream();
        var encoder = new GifEncoder(stream, 4, 4);
        var black = new byte[16];
        var white = new byte[16];
        Array.Fill(white, (byte)215);

        encoder.WriteFrame(black, 10);
        encoder.WriteFrame(white, 10);
        encoder.Finish();

        var bytes = stream.ToArray();
        Assert.Equal("GIF89a"u8.ToArray(), bytes[..6]);
        Assert.Equal(0x3B, bytes[^1]);
        Assert.Contains("NETSCAPE2.0", System.Text.Encoding.ASCII.GetString(bytes));

        using var codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.NotNull(codec);
        Assert.Equal(2, codec!.FrameCount);

        var info = new SKImageInfo(4, 4, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var first = new SKBitmap(info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, first.GetPixels()));
        Assert.Equal(SKColors.Black, first.GetPixel(0, 0));

        using var second = new SKBitmap(info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, second.GetPixels(), new SKCodecOptions(1)));
        Assert.Equal(SKColors.White, second.GetPixel(0, 0));
    }

    [Fact]
    public void FitSize_PreservesAspectAndCapsLongestEdge()
    {
        var (width, height) = GifEncoder.FitSize(1920, 1080, 960);

        Assert.Equal(960, width);
        Assert.Equal(540, height);
    }

    [Fact]
    public void Quantize_MapsCornerColorsToPaletteIndices()
    {
        var pixels = new byte[2 * 2 * 4];
        // top-left red, top-right green, bottom-left blue, bottom-right white (BGRA)
        pixels[0] = 0; pixels[1] = 0; pixels[2] = 255;
        pixels[4] = 0; pixels[5] = 255; pixels[6] = 0;
        pixels[8] = 255; pixels[9] = 0; pixels[10] = 0;
        pixels[12] = 255; pixels[13] = 255; pixels[14] = 255;

        var frame = new SSC.Domain.Capture.CaptureFrame(2, 2, pixels, DateTimeOffset.Now);
        var indices = GifEncoder.Quantize(frame, 2, 2);

        Assert.Equal(180, indices[0]);
        Assert.Equal(30, indices[1]);
        Assert.Equal(5, indices[2]);
        Assert.Equal(215, indices[3]);
    }
}
