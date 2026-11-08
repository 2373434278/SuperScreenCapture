using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.Platform.Mac;

/// <summary>
/// ScreenCaptureKit implementation lands in M2. The permission probe is real.
/// </summary>
public sealed class MacScreenCaptureService : IScreenCaptureService
{
    public Task<IReadOnlyList<DisplayDescriptor>> GetDisplaysAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DisplayDescriptor>>(Array.Empty<DisplayDescriptor>());

    public Task<CaptureFrame> CaptureDisplayAsync(
        string displayId,
        PixelRect? region = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException<CaptureFrame>(
            new PlatformNotSupportedException("ScreenCaptureKit capture is implemented in M2."));

    public Task<CaptureFrame> CaptureWindowAsync(
        string windowId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<CaptureFrame>(
            new PlatformNotSupportedException("Window capture is implemented in M2."));
}
