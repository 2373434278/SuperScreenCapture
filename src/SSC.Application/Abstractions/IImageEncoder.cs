using SSC.Domain.Capture;

namespace SSC.Application.Abstractions;

public interface IImageEncoder
{
    Task<byte[]> EncodeAsync(
        CaptureFrame frame,
        string format,
        int quality,
        CancellationToken cancellationToken = default);
}
