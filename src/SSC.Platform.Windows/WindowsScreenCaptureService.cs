using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.Platform.Windows;

/// <summary>
/// Windows.Graphics.Capture and DXGI Desktop Duplication land in M2.
/// </summary>
public sealed class WindowsScreenCaptureService : IScreenCaptureService
{
    public Task<IReadOnlyList<DisplayDescriptor>> GetDisplaysAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DisplayDescriptor>>(Array.Empty<DisplayDescriptor>());

    public Task<CaptureFrame> CaptureDisplayAsync(
        string displayId,
        PixelRect? region = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException<CaptureFrame>(
            new PlatformNotSupportedException("Windows Graphics Capture is implemented in M2."));

    public Task<CaptureFrame> CaptureWindowAsync(
        string windowId,
        CancellationToken cancellationToken = default) =>
        Task.FromException<CaptureFrame>(
            new PlatformNotSupportedException("Window capture is implemented in M2."));
}
