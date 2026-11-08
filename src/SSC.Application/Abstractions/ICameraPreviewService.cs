namespace SSC.Application.Abstractions;

public sealed record CameraFrame(byte[] Pixels, int Width, int Height, int Stride);

public interface ICameraPreviewService
{
    bool IsSupported { get; }

    Task StartAsync(Action<CameraFrame> onFrame, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
