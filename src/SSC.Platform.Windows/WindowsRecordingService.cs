using SSC.Application.Abstractions;
using SSC.Domain.Recording;

namespace SSC.Platform.Windows;

public sealed class WindowsRecordingService : IRecordingService
{
    private readonly object _gate = new();
    private WindowsRecordingSession? _session;
    private RecordingEngineStatus _last = new(RecordingEngineState.Idle, 0, 0, null);

    public Task StartAsync(
        string outputPath,
        RecordingOptions options,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            lock (_gate)
            {
                if (_session is not null)
                {
                    throw new InvalidOperationException("A recording is already in progress.");
                }
            }

            var session = new WindowsRecordingSession(options, outputPath);
            try
            {
                session.Start();
                lock (_gate)
                {
                    _session = session;
                    _last = session.GetStatus();
                }
            }
            catch (Exception exception)
            {
                session.Dispose();
                lock (_gate)
                {
                    _last = new RecordingEngineStatus(
                        RecordingEngineState.Failed,
                        0,
                        0,
                        exception.Message);
                }

                throw;
            }
        }, cancellationToken);

    public Task<bool> PauseAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            lock (_gate)
            {
                return _session?.Pause() ?? false;
            }
        }, cancellationToken);

    public Task<bool> ResumeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            lock (_gate)
            {
                return _session?.Resume() ?? false;
            }
        }, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            WindowsRecordingSession? session;
            lock (_gate)
            {
                session = _session;
            }

            if (session is null)
            {
                throw new InvalidOperationException("No recording is active.");
            }

            try
            {
                await session.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                var status = session.GetStatus();
                session.Dispose();
                lock (_gate)
                {
                    if (ReferenceEquals(_session, session))
                    {
                        _session = null;
                    }

                    _last = status;
                }
            }
        }, cancellationToken);

    public Task<bool> SetMicrophoneAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            lock (_gate)
            {
                return _session?.SetMicrophone(enabled) ?? false;
            }
        }, cancellationToken);

    public RecordingEngineStatus GetStatus()
    {
        lock (_gate)
        {
            return _session?.GetStatus() ?? _last;
        }
    }
}
