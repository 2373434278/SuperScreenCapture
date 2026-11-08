using SSC.Domain.Capture;

namespace SSC.Application.Abstractions;

public interface ICaptureFrameLoader
{
    Task<CaptureFrame?> LoadAsync(string filePath, CancellationToken cancellationToken = default);
}
