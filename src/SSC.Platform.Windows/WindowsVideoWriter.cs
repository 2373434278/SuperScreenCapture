using SharpGen.Runtime;
using Vortice.MediaFoundation;
using SSC.Domain.Recording;

namespace SSC.Platform.Windows;

internal sealed class WindowsVideoWriter : IDisposable
{
    private static readonly object StartupGate = new();
    private static bool _started;

    private readonly IMFSinkWriter _writer;
    private readonly int _videoStream;
    private readonly int? _audioStream;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly long _frameDuration;
    private readonly object _writeGate = new();
    private bool _completed;

    public WindowsVideoWriter(
        string outputPath,
        RecordingOptions options,
        int inputWidth,
        int inputHeight,
        bool includeAudio)
    {
        EnsureMediaFoundation();

        _inputWidth = inputWidth;
        _inputHeight = inputHeight;
        var output = options.OutputSize;
        _frameDuration = 10_000_000L / Math.Max(1, options.Fps);

        using var attributes = MediaFactory.MFCreateAttributes(2);
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
        attributes.Set(SinkWriterAttributeKeys.LowLatency, 1u);
        _writer = MediaFactory.MFCreateSinkWriterFromURL(outputPath, null, attributes);

        using var outputVideo = MediaFactory.MFCreateMediaType();
        Set(outputVideo, MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        Set(outputVideo, MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
        Set(outputVideo, MediaTypeAttributeKeys.AvgBitrate, (uint)Math.Max(1, options.BitrateMbps) * 1_000_000u);
        Set(outputVideo, MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        Set(outputVideo, MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)output.Width, (uint)output.Height));
        Set(outputVideo, MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(options.Fps, 1));
        Set(outputVideo, MediaTypeAttributeKeys.PixelAspectRatio, MediaFactory.PackRatio(1, 1));
        _videoStream = _writer.AddStream(outputVideo);

        using var inputVideo = MediaFactory.MFCreateMediaType();
        Set(inputVideo, MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        Set(inputVideo, MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
        Set(inputVideo, MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        Set(inputVideo, MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)inputWidth, (uint)inputHeight));
        Set(inputVideo, MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(options.Fps, 1));
        Set(inputVideo, MediaTypeAttributeKeys.PixelAspectRatio, MediaFactory.PackRatio(1, 1));
        Set(inputVideo, MediaTypeAttributeKeys.DefaultStride, (uint)(inputWidth * 4));
        _writer.SetInputMediaType(_videoStream, inputVideo, null);

        if (includeAudio)
        {
            using var outputAudio = MediaFactory.MFCreateMediaType();
            Set(outputAudio, MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            Set(outputAudio, MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
            Set(outputAudio, MediaTypeAttributeKeys.AudioSamplesPerSecond, 48_000u);
            Set(outputAudio, MediaTypeAttributeKeys.AudioNumChannels, 2u);
            Set(outputAudio, MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            Set(outputAudio, MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 192_000u);
            _audioStream = _writer.AddStream(outputAudio);

            using var inputAudio = MediaFactory.MFCreateMediaType();
            Set(inputAudio, MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            Set(inputAudio, MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            Set(inputAudio, MediaTypeAttributeKeys.AudioSamplesPerSecond, 48_000u);
            Set(inputAudio, MediaTypeAttributeKeys.AudioNumChannels, 2u);
            Set(inputAudio, MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            Set(inputAudio, MediaTypeAttributeKeys.AudioBlockAlignment, 4u);
            Set(inputAudio, MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 192_000u);
            _writer.SetInputMediaType(_audioStream.Value, inputAudio, null);
        }

        _writer.BeginWriting();
    }

    public void WriteVideoFrame(IntPtr source, int sourceStride, long timestamp100ns)
    {
        var widthBytes = _inputWidth * 4;
        using var buffer = MediaFactory.MFCreateMemoryBuffer(widthBytes * _inputHeight);
        buffer.Lock(out var destination, out _, out _);
        try
        {
            unsafe
            {
                for (var row = 0; row < _inputHeight; row++)
                {
                    Buffer.MemoryCopy(
                        (byte*)source + (row * sourceStride),
                        (byte*)destination + (row * widthBytes),
                        widthBytes,
                        widthBytes);
                }
            }
        }
        finally
        {
            buffer.Unlock();
        }

        buffer.CurrentLength = widthBytes * _inputHeight;
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = timestamp100ns;
        sample.SampleDuration = _frameDuration;
        lock (_writeGate)
        {
            _writer.WriteSample(_videoStream, sample);
        }
    }

    public void WriteAudioSample(IMFSample sample, long timestamp100ns)
    {
        if (_audioStream is null)
        {
            return;
        }

        sample.SampleTime = timestamp100ns;
        lock (_writeGate)
        {
            _writer.WriteSample(_audioStream.Value, sample);
        }
    }

    public void Complete()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        lock (_writeGate)
        {
            _writer.Finalize();
        }
    }

    public void Dispose()
    {
        lock (_writeGate)
        {
            _writer.Dispose();
        }
    }

    public static void EnsureMediaFoundation()
    {
        lock (StartupGate)
        {
            if (_started)
            {
                return;
            }

            MediaFactory.MFStartup().CheckError();
            _started = true;
        }
    }

    private static void Set(IMFMediaType type, Guid key, Guid value) =>
        type.Set(key, value);

    private static void Set(IMFMediaType type, Guid key, uint value) =>
        type.Set(key, value);

    private static void Set(IMFMediaType type, Guid key, ulong value) =>
        type.Set(key, value);
}
