using SSC.Application.Abstractions;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Storage.Streams;
using SoftwareBitmap = Windows.Graphics.Imaging.SoftwareBitmap;

namespace SSC.Platform.Windows;

public sealed class WindowsCameraPreviewService : ICameraPreviewService
{
    private readonly object _gate = new();
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private Action<CameraFrame>? _onFrame;

    public bool IsSupported => true;

    public async Task StartAsync(
        Action<CameraFrame> onFrame,
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        _onFrame = onFrame;

        var capture = new MediaCapture();
        var settings = new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Video,
            PhotoCaptureSource = PhotoCaptureSource.VideoPreview,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            SharingMode = MediaCaptureSharingMode.SharedReadOnly
        };
        await capture.InitializeAsync(settings);

        var source = capture.FrameSources.Values
            .FirstOrDefault(candidate => candidate.Info.SourceKind == MediaFrameSourceKind.Color)
            ?? throw new InvalidOperationException("No camera is available.");
        var reader = await capture.CreateFrameReaderAsync(source);
        reader.FrameArrived += OnFrameArrived;

        lock (_gate)
        {
            _capture = capture;
            _reader = reader;
        }

        await reader.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        MediaFrameReader? reader;
        MediaCapture? capture;
        lock (_gate)
        {
            reader = _reader;
            capture = _capture;
            _reader = null;
            _capture = null;
            _onFrame = null;
        }

        if (reader is not null)
        {
            reader.FrameArrived -= OnFrameArrived;
            await reader.StopAsync();
            reader.Dispose();
        }

        capture?.Dispose();
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var reference = sender.TryAcquireLatestFrame();
        var bitmap = reference?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null)
        {
            return;
        }

        SoftwareBitmap? converted = null;
        try
        {
            if (bitmap.BitmapPixelFormat != global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8)
            {
                converted = SoftwareBitmap.Convert(
                    bitmap,
                    global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
                bitmap = converted;
            }

            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            var buffer = new global::Windows.Storage.Streams.Buffer((uint)pixels.Length);
            bitmap.CopyToBuffer(buffer);
            using var dataReader = global::Windows.Storage.Streams.DataReader.FromBuffer(buffer);
            dataReader.ReadBytes(pixels);
            _onFrame?.Invoke(new CameraFrame(pixels, width, height, stride));
        }
        finally
        {
            converted?.Dispose();
        }
    }
}
