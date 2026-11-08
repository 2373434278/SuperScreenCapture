using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.Application.Abstractions;

public interface IScreenCaptureService
{
    Task<IReadOnlyList<DisplayDescriptor>> GetDisplaysAsync(CancellationToken cancellationToken = default);

    Task<CaptureFrame> CaptureDisplayAsync(
        string displayId,
        PixelRect? region = null,
        CancellationToken cancellationToken = default);

    Task<CaptureFrame> CaptureWindowAsync(string windowId, CancellationToken cancellationToken = default);
}
