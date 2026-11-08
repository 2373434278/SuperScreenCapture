using SSC.Application.Abstractions;

namespace SSC.Platform.Mac;

public sealed class MacCameraPreviewService : ICameraPreviewService
{
    public bool IsSupported => false;

    public Task StartAsync(
        Action<CameraFrame> onFrame,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
