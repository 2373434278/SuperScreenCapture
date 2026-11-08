using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.Platform.Mock;

public sealed class MockScreenCaptureService : IScreenCaptureService
{
    public Task<IReadOnlyList<DisplayDescriptor>> GetDisplaysAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DisplayDescriptor> displays = new[]
        {
            DisplayDescriptor.Create(
                "mock-primary",
                "Mock Display 1",
                new PixelRect(0, 0, 2560, 1600),
                scaleFactor: 2,
                isPrimary: true),
            DisplayDescriptor.Create(
                "mock-secondary",
                "Mock Display 2",
                new PixelRect(-1920, 0, 1920, 1080),
                scaleFactor: 1)
        };

        return Task.FromResult(displays);
    }

    public Task<CaptureFrame> CaptureDisplayAsync(
        string displayId,
        PixelRect? region = null,
        CancellationToken cancellationToken = default)
    {
        var width = region?.Width ?? 640;
        var height = region?.Height ?? 400;
        var pixels = new byte[checked(width * height * 4)];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)((x * 255) / Math.Max(1, width - 1));
                pixels[offset + 1] = (byte)((y * 255) / Math.Max(1, height - 1));
                pixels[offset + 2] = 220;
                pixels[offset + 3] = 255;
            }
        }

        return Task.FromResult(new CaptureFrame(width, height, pixels, DateTimeOffset.UtcNow));
    }

    public Task<CaptureFrame> CaptureWindowAsync(string windowId, CancellationToken cancellationToken = default) =>
        CaptureDisplayAsync(windowId, region: null, cancellationToken);

    public Task<WindowDescriptor?> GetWindowAtPointAsync(
        PixelPoint point,
        CancellationToken cancellationToken = default)
    {
        WindowDescriptor? window = new WindowDescriptor(
            "mock-window",
            "Mock Window",
            new PixelRect(point.X, point.Y, 640, 400),
            "Mock");

        return Task.FromResult<WindowDescriptor?>(window);
    }
}
