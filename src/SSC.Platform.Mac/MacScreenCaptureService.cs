using System.Globalization;
using System.Runtime.InteropServices;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.Platform.Mac;

public sealed class MacScreenCaptureService : IScreenCaptureService
{
    public Task<IReadOnlyList<DisplayDescriptor>> GetDisplaysAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DisplayDescriptor>>(() =>
        {
            var count = MacNativeMethods.ssc_display_count();
            if (count <= 0)
            {
                throw new InvalidOperationException(MacNativeMethods.GetLastError());
            }

            var displays = new List<DisplayDescriptor>(count);
            for (var index = 0; index < count; index++)
            {
                if (MacNativeMethods.ssc_display_get(index, out var display) != 1)
                {
                    throw new InvalidOperationException(MacNativeMethods.GetLastError());
                }

                displays.Add(DisplayDescriptor.Create(
                    display.Id.ToString(CultureInfo.InvariantCulture),
                    $"Display {index + 1}",
                    new PixelRect(display.X, display.Y, (int)display.Width, (int)display.Height),
                    display.Scale,
                    display.Primary != 0));
            }

            return displays;
        }, cancellationToken);

    public Task<CaptureFrame> CaptureDisplayAsync(
        string displayId,
        PixelRect? region = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (!uint.TryParse(displayId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new ArgumentException($"Invalid display id '{displayId}'.", nameof(displayId));
            }

            if (MacNativeMethods.ssc_capture_display(id, out var nativeImage) != 1)
            {
                throw new InvalidOperationException(MacNativeMethods.GetLastError());
            }

            try
            {
                var frame = CopyFrame(nativeImage);
                return region is { IsEmpty: false } rect ? frame.Crop(rect) : frame;
            }
            finally
            {
                MacNativeMethods.ssc_free_image(ref nativeImage);
            }
        }, cancellationToken);

    public Task<CaptureFrame> CaptureWindowAsync(
        string windowId,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (!uint.TryParse(windowId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new ArgumentException($"Invalid window id '{windowId}'.", nameof(windowId));
            }

            if (MacNativeMethods.ssc_capture_window(id, out var nativeImage) != 1)
            {
                throw new InvalidOperationException(MacNativeMethods.GetLastError());
            }

            try
            {
                return CopyFrame(nativeImage);
            }
            finally
            {
                MacNativeMethods.ssc_free_image(ref nativeImage);
            }
        }, cancellationToken);

    public async Task<WindowDescriptor?> GetWindowAtPointAsync(
        PixelPoint point,
        CancellationToken cancellationToken = default)
    {
        var displays = await GetDisplaysAsync(cancellationToken).ConfigureAwait(false);
        var display = displays.FirstOrDefault(candidate => candidate.Bounds.Contains(point)) ?? displays.FirstOrDefault();
        if (display is null)
        {
            return null;
        }

        var scale = display.ScaleFactor > 0 ? display.ScaleFactor : 1;
        var nativePoint = new PixelPoint(
            (int)Math.Round(point.X / scale),
            (int)Math.Round(point.Y / scale));

        return await Task.Run(() =>
        {
            if (MacNativeMethods.ssc_window_at_point(nativePoint.X, nativePoint.Y, out var window) != 1)
            {
                return null;
            }

            return new WindowDescriptor(
                window.Id.ToString(CultureInfo.InvariantCulture),
                $"Window {window.Id}",
                new PixelRect(
                    (int)Math.Round(window.X * scale),
                    (int)Math.Round(window.Y * scale),
                    Math.Max(1, (int)Math.Round(window.Width * scale)),
                    Math.Max(1, (int)Math.Round(window.Height * scale))),
                string.Empty);
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static CaptureFrame CopyFrame(MacNativeMethods.SscImage image)
    {
        var length = checked((int)image.Stride * (int)image.Height);
        var pixels = new byte[length];
        Marshal.Copy(image.Pixels, pixels, 0, length);
        return new CaptureFrame((int)image.Width, (int)image.Height, pixels, DateTimeOffset.UtcNow);
    }
}
