using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.Platform.Mac;

public sealed class MacClipboardService : IClipboardService
{
    public Task CopyImageAsync(CaptureFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return Task.Run(() =>
        {
            if (MacNativeMethods.ssc_copy_image_to_clipboard(
                    frame.Pixels,
                    (uint)frame.Width,
                    (uint)frame.Height,
                    (uint)frame.Stride) != 1)
            {
                throw new InvalidOperationException(MacNativeMethods.GetLastError());
            }
        }, cancellationToken);
    }
}
