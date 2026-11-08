using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.Platform.Mock;

public sealed class MockClipboardService : IClipboardService
{
    public CaptureFrame? LastCopiedFrame { get; private set; }

    public Task CopyImageAsync(CaptureFrame frame, CancellationToken cancellationToken = default)
    {
        LastCopiedFrame = frame;
        return Task.CompletedTask;
    }
}
