using System.Runtime.InteropServices;
using SkiaSharp;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.Infrastructure.Imaging;

public sealed class SkiaCaptureFrameLoader : ICaptureFrameLoader
{
    public Task<CaptureFrame?> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            using var stream = File.OpenRead(filePath);
            using var decoded = SKBitmap.Decode(stream);
            if (decoded is null)
            {
                return null;
            }

            using var bitmap = decoded.ColorType == SKColorType.Bgra8888 &&
                               decoded.AlphaType == SKAlphaType.Premul
                ? decoded.Copy()
                : decoded.Copy(SKColorType.Bgra8888);
            if (bitmap is null)
            {
                return null;
            }

            var width = bitmap.Width;
            var height = bitmap.Height;
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            return new CaptureFrame(width, height, pixels, DateTimeOffset.UtcNow);
        }, cancellationToken);
}
