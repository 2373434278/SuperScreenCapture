using SSC.Application.Abstractions;
using SSC.Domain.Recording;

namespace SSC.Platform.Windows;

/// <summary>
/// Windows recording (WGC/DXGI + Media Foundation) is delivered in M5.
/// </summary>
public sealed class WindowsRecordingService : IRecordingService
{
    private const string Message = "Recording support for Windows arrives in M5.";

    public Task StartAsync(
        string outputPath,
        RecordingOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new PlatformNotSupportedException(Message));

    public Task<bool> PauseAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> ResumeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<bool> SetMicrophoneAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public RecordingEngineStatus GetStatus() =>
        new(RecordingEngineState.Idle, 0, 0, Message);
}
