using SSC.Domain.Recording;

namespace SSC.Application.Abstractions;

public enum RecordingEngineState
{
    Idle = 0,
    Preparing = 1,
    Recording = 2,
    Paused = 3,
    Finalizing = 4,
    Completed = 5,
    Failed = 6
}

public sealed record RecordingEngineStatus(
    RecordingEngineState State,
    double ElapsedSeconds,
    double MicrophoneLevel,
    string? Error);

public interface IRecordingService
{
    Task StartAsync(
        string outputPath,
        RecordingOptions options,
        CancellationToken cancellationToken = default);

    Task<bool> PauseAsync(CancellationToken cancellationToken = default);

    Task<bool> ResumeAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<bool> SetMicrophoneAsync(bool enabled, CancellationToken cancellationToken = default);

    RecordingEngineStatus GetStatus();
}
