using SSC.Domain.Capture;

namespace SSC.Application.Abstractions;

public interface IClipboardService
{
    Task CopyImageAsync(CaptureFrame frame, CancellationToken cancellationToken = default);
}
