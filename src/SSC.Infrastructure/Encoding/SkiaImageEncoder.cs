using System.Runtime.InteropServices;
using SkiaSharp;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.Infrastructure.Imaging;

public sealed class SkiaImageEncoder : IImageEncoder
{
    public Task<byte[]> EncodeAsync(
        CaptureFrame frame,
        string format,
        int quality,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Task.Run(() => Encode(frame, format, quality), cancellationToken);
    }

    private static byte[] Encode(CaptureFrame frame, string format, int quality)
    {
        var info = new SKImageInfo(
            frame.Width,
            frame.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        using var bitmap = new SKBitmap(info);
        Marshal.Copy(frame.Pixels, 0, bitmap.GetPixels(), frame.Pixels.Length);

        using var image = SKImage.FromBitmap(bitmap);
        var encodedFormat = format.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => SKEncodedImageFormat.Jpeg,
            "webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };

        using var data = image.Encode(encodedFormat, Math.Clamp(quality, 1, 100));
        return data.ToArray();
    }
}
