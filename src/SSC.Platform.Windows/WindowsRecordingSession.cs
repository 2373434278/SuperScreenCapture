using System.Diagnostics;
using SSC.Application.Abstractions;
using SSC.Domain.Recording;

namespace SSC.Platform.Windows;

internal sealed class WindowsRecordingSession : IDisposable
{
    private readonly RecordingOptions _options;
    private readonly string _outputPath;
    private readonly object _stateGate = new();
    private readonly object _frameGate = new();
    private readonly Stopwatch _clock = new();
    private WindowsVideoWriter? _writer;
    private WindowsAudioCapture? _audio;
    private IWindowsFrameSource? _frameSource;
    private RecordingEngineState _state = RecordingEngineState.Preparing;
    private string? _error;
    private bool _paused;
    private TimeSpan _pauseOffset;
    private TimeSpan? _pauseStarted;
    private long? _firstAudioTimestamp;
    private long _audioStartTimestamp;
    private bool _audioResync = true;
    private bool _microphoneEnabled;
    private long _lastVideoTimestamp;
    private long _lastAudioTimestamp;
    private readonly long _frameDuration100ns;
    private long _nextVideoTimestamp;

    public WindowsRecordingSession(RecordingOptions options, string outputPath)
    {
        _options = options;
        _outputPath = outputPath;
        _frameDuration100ns = 10_000_000L / Math.Max(1, options.Fps);
    }

    public void Start()
    {
        WindowsVideoWriter.EnsureMediaFoundation();
        var audio = _options.IncludeMicrophone ? WindowsAudioCapture.TryCreate() : null;
        _microphoneEnabled = audio is not null;

        try
        {
            _writer = new WindowsVideoWriter(
                _outputPath,
                _options,
                _options.Region.Width,
                _options.Region.Height,
                audio is not null);
            _audio = audio;
            _clock.Start();

            lock (_stateGate)
            {
                _state = RecordingEngineState.Recording;
            }

            _audio?.Start(OnAudioSample);
            _frameSource = CreateFrameSource();
        }
        catch
        {
            _audio?.Dispose();
            _audio = null;
            _writer?.Dispose();
            _writer = null;
            throw;
        }
    }

    public Task StopAsync()
    {
        lock (_frameGate)
        {
            if (_state is RecordingEngineState.Completed or RecordingEngineState.Failed)
            {
                return Task.CompletedTask;
            }

            lock (_stateGate)
            {
                _state = RecordingEngineState.Finalizing;
            }
        }

        try
        {
            _frameSource?.Stop();
        }
        catch (Exception exception)
        {
            _error ??= exception.Message;
        }
        finally
        {
            _frameSource?.Dispose();
            _frameSource = null;
        }

        try
        {
            _audio?.Stop();
        }
        catch (Exception exception)
        {
            _error ??= exception.Message;
        }
        finally
        {
            _audio?.Dispose();
            _audio = null;
        }

        var failed = _error is not null;
        try
        {
            if (!failed)
            {
                _writer?.Complete();
            }
        }
        catch (Exception exception)
        {
            _error = exception.Message;
            failed = true;
        }

        _writer?.Dispose();
        _writer = null;
        _clock.Stop();

        lock (_stateGate)
        {
            _state = failed ? RecordingEngineState.Failed : RecordingEngineState.Completed;
        }

        return Task.CompletedTask;
    }

    public bool Pause()
    {
        lock (_stateGate)
        {
            if (_state != RecordingEngineState.Recording || _paused)
            {
                return false;
            }

            _paused = true;
            _pauseStarted = _clock.Elapsed;
            return true;
        }
    }

    public bool Resume()
    {
        lock (_stateGate)
        {
            if (_state != RecordingEngineState.Recording || !_paused || _pauseStarted is null)
            {
                return false;
            }

            _pauseOffset += _clock.Elapsed - _pauseStarted.Value;
            _pauseStarted = null;
            _paused = false;
            return true;
        }
    }

    public bool SetMicrophone(bool enabled)
    {
        if (_audio is null)
        {
            return false;
        }

        lock (_stateGate)
        {
            _microphoneEnabled = enabled;
            if (enabled)
            {
                _audioResync = true;
            }
        }

        return true;
    }

    public RecordingEngineStatus GetStatus()
    {
        lock (_stateGate)
        {
            var elapsedTicks = Math.Max(_lastVideoTimestamp, _lastAudioTimestamp);
            if (_state == RecordingEngineState.Recording && !_paused)
            {
                elapsedTicks = Math.Max(elapsedTicks, CurrentTimestamp100ns());
            }

            return new RecordingEngineStatus(
                _state,
                TimeSpan.FromTicks(Math.Max(0, elapsedTicks)).TotalSeconds,
                _microphoneEnabled ? _audio?.Level ?? 0 : 0,
                _error);
        }
    }

    public void Dispose()
    {
        if (_state is RecordingEngineState.Recording or RecordingEngineState.Paused)
        {
            StopAsync().GetAwaiter().GetResult();
        }

        _audio?.Dispose();
        _audio = null;
        _writer?.Dispose();
        _writer = null;
        _frameSource?.Dispose();
        _frameSource = null;
    }

    private IWindowsFrameSource CreateFrameSource()
    {
        if (!long.TryParse(_options.DisplayId, out var handle))
        {
            throw new ArgumentException($"Invalid display id '{_options.DisplayId}'.");
        }

        WindowsWgcFrameSource? wgc = null;
        try
        {
            wgc = new WindowsWgcFrameSource(new IntPtr(handle), _options.Region);
            wgc.Start(OnVideoFrame, OnSourceError);
            return wgc;
        }
        catch (Exception)
        {
            wgc?.Dispose();
        }

        var dxgi = new WindowsDxgiFrameSource(new IntPtr(handle), _options.Region);
        try
        {
            dxgi.Start(OnVideoFrame, OnSourceError);
            return dxgi;
        }
        catch
        {
            dxgi.Dispose();
            throw;
        }
    }

    private void OnVideoFrame(IntPtr pointer, int stride)
    {
        lock (_frameGate)
        {
            RecordingEngineState state;
            long timestamp;
            lock (_stateGate)
            {
                state = _state;
                if (state != RecordingEngineState.Recording || _paused)
                {
                    return;
                }

                timestamp = CurrentTimestamp100ns();
                if (timestamp < _nextVideoTimestamp)
                {
                    return;
                }
            }

            try
            {
                _writer?.WriteVideoFrame(pointer, stride, timestamp);
                lock (_stateGate)
                {
                    _lastVideoTimestamp = Math.Max(_lastVideoTimestamp, timestamp);
                    _nextVideoTimestamp = timestamp + _frameDuration100ns;
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }
    }

    private void OnAudioSample(Vortice.MediaFoundation.IMFSample sample, long sourceTimestamp)
    {
        long adjusted;
        lock (_stateGate)
        {
            if (_state != RecordingEngineState.Recording || _paused || !_microphoneEnabled)
            {
                return;
            }

            if (_audioResync || _firstAudioTimestamp is null)
            {
                _firstAudioTimestamp = sourceTimestamp;
                _audioStartTimestamp = CurrentTimestamp100ns();
                _audioResync = false;
            }

            adjusted = _audioStartTimestamp +
                       (sourceTimestamp - _firstAudioTimestamp.Value) -
                       _pauseOffset.Ticks;
            if (adjusted < 0)
            {
                return;
            }
        }

        try
        {
            _writer?.WriteAudioSample(sample, adjusted);
            lock (_stateGate)
            {
                _lastAudioTimestamp = Math.Max(_lastAudioTimestamp, adjusted);
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void OnSourceError(Exception exception) => Fail(exception);

    private void Fail(Exception exception)
    {
        lock (_stateGate)
        {
            if (_state is RecordingEngineState.Failed or RecordingEngineState.Completed)
            {
                return;
            }

            _error = exception.Message;
            _state = RecordingEngineState.Failed;
        }
    }

    private long CurrentTimestamp100ns() =>
        Math.Max(0, (_clock.Elapsed - _pauseOffset).Ticks);
}
