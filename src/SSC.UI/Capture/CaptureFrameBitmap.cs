using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SSC.Domain.Capture;

namespace SSC.UI.Capture;

/// <summary>把 BGRA 抓帧复制为可渲染的 <see cref="WriteableBitmap"/>。</summary>
public static class CaptureFrameBitmap
{
    public static WriteableBitmap Create(CaptureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var buffer = bitmap.Lock();
        for (var row = 0; row < frame.Height; row++)
        {
            Marshal.Copy(
                frame.Pixels,
                row * frame.Stride,
                IntPtr.Add(buffer.Address, row * buffer.RowBytes),
                frame.Stride);
        }

        return bitmap;
    }
}
