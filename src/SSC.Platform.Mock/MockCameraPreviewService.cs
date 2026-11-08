using SSC.Application.Abstractions;

namespace SSC.Platform.Mock;

public sealed class MockCameraPreviewService : ICameraPreviewService
{
    private CancellationTokenSource? _cancellation;

    public bool IsSupported => true;

    public Task StartAsync(
        Action<CameraFrame> onFrame,
        CancellationToken cancellationToken = default)
    {
        StopAsync().GetAwaiter().GetResult();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => FrameLoop(onFrame, _cancellation.Token), _cancellation.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        return Task.CompletedTask;
    }

    private static async Task FrameLoop(Action<CameraFrame> onFrame, CancellationToken cancellationToken)
    {
        const int size = 320;
        var frameIndex = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var pixels = new byte[size * size * 4];
            var centerX = size / 2 + (int)(Math.Sin(frameIndex * 0.08) * 60);
            var centerY = size / 2 + (int)(Math.Cos(frameIndex * 0.06) * 40);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var offset = ((y * size) + x) * 4;
                    var distance = Math.Sqrt(((x - centerX) * (x - centerX)) + ((y - centerY) * (y - centerY)));
                    var face = distance < 90;
                    pixels[offset] = (byte)(face ? 180 : (x * 255 / size));
                    pixels[offset + 1] = (byte)(face ? 150 : (y * 255 / size));
                    pixels[offset + 2] = (byte)(face ? 130 : 60);
                    pixels[offset + 3] = 255;
                }
            }

            onFrame(new CameraFrame(pixels, size, size, size * 4));
            frameIndex++;
            try
            {
                await Task.Delay(66, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
