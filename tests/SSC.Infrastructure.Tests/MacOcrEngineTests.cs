using System.Runtime.InteropServices;
using SkiaSharp;
using SSC.Domain.Capture;
using SSC.Platform.Mac;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class MacOcrEngineTests
{
    [Fact]
    public async Task Vision_RecognizesGeneratedText()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var engine = new MacOcrEngine();
        if (!engine.IsAvailable)
        {
            return;
        }

        var frame = CreateTextFrame("Hello World");
        var result = await engine.RecognizeAsync(
            frame,
            "en-US",
            TestContext.Current.CancellationToken);

        Assert.Contains("Hello", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Lines);
    }

    private static CaptureFrame CreateTextFrame(string text)
    {
        const int width = 560;
        const int height = 160;
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var typeface = SKTypeface.FromFamilyName("Helvetica", SKFontStyle.Bold) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 44);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawText(text, 30, 100, font, paint);
        canvas.Flush();

        var pixels = new byte[width * height * 4];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return new CaptureFrame(width, height, pixels, DateTimeOffset.UtcNow);
    }
}
