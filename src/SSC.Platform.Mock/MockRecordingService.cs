using SSC.Application.Abstractions;
using SSC.Domain.Recording;

namespace SSC.Platform.Mock;

public sealed class MockRecordingService : IRecordingService
{
    private readonly object _gate = new();
    private RecordingEngineState _state = RecordingEngineState.Idle;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _pausedAt;
    private TimeSpan _pausedOffset;
    private TimeSpan _finalElapsed;
    private bool _microphone = true;

    public Task StartAsync(
        string outputPath,
        RecordingOptions options,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _state = RecordingEngineState.Recording;
            _startedAt = DateTimeOffset.UtcNow;
            _pausedAt = null;
            _pausedOffset = TimeSpan.Zero;
            _finalElapsed = TimeSpan.Zero;
            _microphone = options.IncludeMicrophone;
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return File.WriteAllBytesAsync(
            outputPath,
            new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34, 0x32 },
            cancellationToken);
    }

    public Task<bool> PauseAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state != RecordingEngineState.Recording)
            {
                return Task.FromResult(false);
            }

            _pausedAt = DateTimeOffset.UtcNow;
            _state = RecordingEngineState.Paused;
            return Task.FromResult(true);
        }
    }

    public Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state != RecordingEngineState.Paused || _pausedAt is null)
            {
                return Task.FromResult(false);
            }

            _pausedOffset += DateTimeOffset.UtcNow - _pausedAt.Value;
            _pausedAt = null;
            _state = RecordingEngineState.Recording;
            return Task.FromResult(true);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _finalElapsed = CalculateElapsed();
            _state = RecordingEngineState.Completed;
            _pausedAt = null;
        }

        return Task.CompletedTask;
    }

    public Task<bool> SetMicrophoneAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state == RecordingEngineState.Idle)
            {
                return Task.FromResult(false);
            }

            _microphone = enabled;
            return Task.FromResult(true);
        }
    }

    public RecordingEngineStatus GetStatus()
    {
        lock (_gate)
        {
            var elapsed = _state == RecordingEngineState.Completed
                ? _finalElapsed
                : CalculateElapsed();
            var level = _microphone && _state == RecordingEngineState.Recording
                ? 0.25 + (Math.Sin(elapsed.TotalSeconds * 6) * 0.25)
                : 0;
            return new RecordingEngineStatus(_state, elapsed.TotalSeconds, Math.Clamp(level, 0, 1), null);
        }
    }

    private TimeSpan CalculateElapsed()
    {
        if (_startedAt is null)
        {
            return TimeSpan.Zero;
        }

        var end = _pausedAt ?? DateTimeOffset.UtcNow;
        var elapsed = end - _startedAt.Value - _pausedOffset;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}
