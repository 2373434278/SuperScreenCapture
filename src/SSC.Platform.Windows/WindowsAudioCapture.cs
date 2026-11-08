using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace SSC.Platform.Windows;

internal sealed class WindowsAudioCapture : IDisposable
{
    private readonly IMFMediaSource _source;
    private readonly IMFSourceReader _reader;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private Action<IMFSample, long>? _onSample;
    private double _level;

    private WindowsAudioCapture(IMFMediaSource source, IMFSourceReader reader)
    {
        _source = source;
        _reader = reader;
    }

    public double Level => Volatile.Read(ref _level);

    public static WindowsAudioCapture? TryCreate()
    {
        try
        {
            using var devices = MediaFactory.MFEnumAudioDeviceSources();
            var activate = devices.FirstOrDefault();
            if (activate is null)
            {
                return null;
            }

            var source = activate.ActivateObject<IMFMediaSource>();
            var reader = MediaFactory.MFCreateSourceReaderFromMediaSource(source, null!);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);

            using var mediaType = MediaFactory.MFCreateMediaType();
            Set(mediaType, MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            Set(mediaType, MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            Set(mediaType, MediaTypeAttributeKeys.AudioSamplesPerSecond, 48_000u);
            Set(mediaType, MediaTypeAttributeKeys.AudioNumChannels, 2u);
            Set(mediaType, MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            Set(mediaType, MediaTypeAttributeKeys.AudioBlockAlignment, 4u);
            Set(mediaType, MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 192_000u);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, mediaType);
            return new WindowsAudioCapture(source, reader);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Start(Action<IMFSample, long> onSample)
    {
        if (_loop is not null)
        {
            return;
        }

        _onSample = onSample;
        _cancellation = new CancellationTokenSource();
        _loop = Task.Run(() => CaptureLoop(_cancellation.Token));
    }

    public void Stop()
    {
        if (_loop is null)
        {
            return;
        }

        _cancellation?.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _loop = null;
        _cancellation?.Dispose();
        _cancellation = null;
        Volatile.Write(ref _level, 0);
    }

    public void Dispose()
    {
        Stop();
        _reader.Dispose();
        _source.Dispose();
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var sample = _reader.ReadSample(
                    SourceReaderIndex.FirstAudioStream,
                    SourceReaderControlFlag.None,
                    out _,
                    out var flags,
                    out var timestamp);
                if (sample is null)
                {
                    if ((flags & SourceReaderFlag.EndOfStream) != 0)
                    {
                        break;
                    }

                    continue;
                }

                using (sample)
                {
                    Volatile.Write(ref _level, CalculateLevel(sample));
                    _onSample?.Invoke(sample, timestamp);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static double CalculateLevel(IMFSample sample)
    {
        try
        {
            using var buffer = sample.ConvertToContiguousBuffer();
            if (buffer is null)
            {
                return 0;
            }

            buffer.Lock(out var pointer, out _, out var length);
            try
            {
                var samples = length / sizeof(short);
                if (samples <= 0)
                {
                    return 0;
                }

                var sum = 0.0;
                unsafe
                {
                    var data = (short*)pointer;
                    for (var index = 0; index < samples; index++)
                    {
                        var value = data[index] / 32768.0;
                        sum += value * value;
                    }
                }

                return Math.Clamp(Math.Sqrt(sum / samples) * 6.0, 0, 1);
            }
            finally
            {
                buffer.Unlock();
            }
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static void Set(IMFMediaType type, Guid key, Guid value) => type.Set(key, value);

    private static void Set(IMFMediaType type, Guid key, uint value) => type.Set(key, value);
}
