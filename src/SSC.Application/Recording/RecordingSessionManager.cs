using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Events;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.Domain.Recording;

namespace SSC.Application.Recording;

/// <summary>
/// Facade that owns the recording lifecycle: permissions, output naming, engine calls
/// and the history entry created after a successful stop.
/// </summary>
public sealed class RecordingSessionManager
{
    private readonly IRecordingService _engine;
    private readonly IPermissionService _permissions;
    private readonly IHistoryRepository _history;
    private readonly SettingsService _settings;
    private readonly CaptureFileNamer _fileNamer;
    private readonly IClock _clock;
    private readonly IAppEventBus _events;
    private readonly ILocalizer _localizer;
    private RecordingOptions? _options;
    private string? _outputPath;
    private string? _error;
    private bool _microphoneAvailable;

    public RecordingSessionManager(
        IRecordingService engine,
        IPermissionService permissions,
        IHistoryRepository history,
        SettingsService settings,
        CaptureFileNamer fileNamer,
        IClock clock,
        IAppEventBus events,
        ILocalizer localizer)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _fileNamer = fileNamer ?? throw new ArgumentNullException(nameof(fileNamer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public event EventHandler<RecordingState>? StateChanged;

    public RecordingState State { get; private set; } = RecordingState.Idle;

    public RecordingOptions? CurrentOptions => _options;

    public string? OutputPath => _outputPath;

    public bool MicrophoneAvailable => _microphoneAvailable;

    public bool IsActive => State is RecordingState.Preparing
        or RecordingState.Countdown
        or RecordingState.Recording
        or RecordingState.Paused
        or RecordingState.Finalizing;

    public async Task<RecordingStartResult> StartAsync(
        RecordingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (IsActive)
        {
            return new RecordingStartResult(false, _localizer["recordingBusy"], null, null);
        }

        Transition(RecordingState.Preparing);
        _error = null;
        _microphoneAvailable = false;

        try
        {
            if (!await EnsureScreenPermissionAsync(cancellationToken).ConfigureAwait(false))
            {
                return FailStart(_localizer["recordingPermissionRequired"]);
            }

            var normalized = options.Normalize();
            if (string.IsNullOrWhiteSpace(normalized.DisplayId) || normalized.Region.IsEmpty)
            {
                return FailStart(_localizer["recordingFailed"]);
            }

            var includeMicrophone = normalized.IncludeMicrophone &&
                                    await EnsureMicrophonePermissionAsync(cancellationToken).ConfigureAwait(false);
            normalized = normalized with { IncludeMicrophone = includeMicrophone };
            _microphoneAvailable = includeMicrophone;

            var settings = _settings.Current;
            var path = _fileNamer.CreatePath(settings, CaptureKind.Recording, "mp4");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _options = normalized;
            _outputPath = path;

            await _engine.StartAsync(path, normalized, cancellationToken).ConfigureAwait(false);
            Transition(RecordingState.Recording);

            var message = includeMicrophone
                ? _localizer["recordingStarted"]
                : _localizer["recordingStartedNoMicrophone"];
            return new RecordingStartResult(true, message, normalized, path);
        }
        catch (Exception exception)
        {
            return FailStart($"{_localizer["recordingFailed"]}: {exception.Message}");
        }
    }

    public async Task<bool> PauseAsync(CancellationToken cancellationToken = default)
    {
        if (State != RecordingState.Recording)
        {
            return false;
        }

        if (!await _engine.PauseAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        Transition(RecordingState.Paused);
        return true;
    }

    public async Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (State != RecordingState.Paused)
        {
            return false;
        }

        if (!await _engine.ResumeAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        Transition(RecordingState.Recording);
        return true;
    }

    public Task<bool> SetMicrophoneAsync(bool enabled, CancellationToken cancellationToken = default) =>
        _microphoneAvailable
            ? _engine.SetMicrophoneAsync(enabled, cancellationToken)
            : Task.FromResult(false);

    public async Task<RecordingStopResult> StopAsync(CancellationToken cancellationToken = default)
    {
        if (State == RecordingState.Failed)
        {
            try
            {
                await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            var failedStatus = _engine.GetStatus();
            _outputPath = null;
            return new RecordingStopResult(
                false,
                _error ?? _localizer["recordingFailed"],
                null,
                TimeSpan.FromSeconds(Math.Max(0, failedStatus.ElapsedSeconds)));
        }

        if (State is not (RecordingState.Recording or RecordingState.Paused))
        {
            return new RecordingStopResult(false, _localizer["recordingNotActive"], null, TimeSpan.Zero);
        }

        Transition(RecordingState.Finalizing);
        try
        {
            await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
            var status = _engine.GetStatus();
            var duration = TimeSpan.FromSeconds(Math.Max(0, status.ElapsedSeconds));
            var path = _outputPath;

            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path) ||
                new FileInfo(path).Length == 0)
            {
                return FailStop(_localizer["recordingFailed"]);
            }

            var options = _options ?? new RecordingOptions(
                string.Empty,
                new SSC.Domain.Geometry.PixelRect(0, 0, 2, 2),
                1,
                30,
                12,
                1080,
                false);
            var size = options.OutputSize;

            var entry = HistoryEntry.Create(
                Guid.NewGuid(),
                CaptureKind.Recording,
                Path.GetFileNameWithoutExtension(path),
                path,
                _clock.UtcNow,
                size.Width,
                size.Height,
                duration,
                tags: new[] { "recording" });

            _history.Add(entry);
            _events.Publish(new HistoryChanged(entry.Id));
            Transition(RecordingState.Completed);
            return new RecordingStopResult(true, _localizer["recordingSaved"], entry, duration);
        }
        catch (Exception exception)
        {
            return FailStop($"{_localizer["recordingFailed"]}: {exception.Message}");
        }
    }

    public RecordingStatus GetStatus()
    {
        var engineStatus = _engine.GetStatus();
        if ((State is RecordingState.Recording or RecordingState.Paused) &&
            engineStatus.State == RecordingEngineState.Failed)
        {
            _error = engineStatus.Error ?? _localizer["recordingFailed"];
            Transition(RecordingState.Failed);
        }

        return new RecordingStatus(
            State,
            TimeSpan.FromSeconds(Math.Max(0, engineStatus.ElapsedSeconds)),
            Math.Clamp(engineStatus.MicrophoneLevel, 0, 1),
            _microphoneAvailable,
            _options,
            _error ?? engineStatus.Error);
    }

    public void Reset()
    {
        if (IsActive)
        {
            return;
        }

        _options = null;
        _outputPath = null;
        _error = null;
        _microphoneAvailable = false;
        Transition(RecordingState.Idle);
    }

    private async Task<bool> EnsureScreenPermissionAsync(CancellationToken cancellationToken)
    {
        var status = await _permissions.CheckAsync(PermissionKind.ScreenRecording, cancellationToken)
            .ConfigureAwait(false);
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        status = await _permissions.RequestAsync(PermissionKind.ScreenRecording, cancellationToken)
            .ConfigureAwait(false);
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        await _permissions.OpenSettingsAsync(PermissionKind.ScreenRecording, cancellationToken)
            .ConfigureAwait(false);
        return false;
    }

    private async Task<bool> EnsureMicrophonePermissionAsync(CancellationToken cancellationToken)
    {
        var status = await _permissions.CheckAsync(PermissionKind.Microphone, cancellationToken)
            .ConfigureAwait(false);
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        status = await _permissions.RequestAsync(PermissionKind.Microphone, cancellationToken)
            .ConfigureAwait(false);
        return status == PermissionStatus.Granted;
    }

    private RecordingStartResult FailStart(string message)
    {
        _error = message;
        _outputPath = null;
        Transition(RecordingState.Failed);
        return new RecordingStartResult(false, message, null, null);
    }

    private RecordingStopResult FailStop(string message)
    {
        _error = message;
        Transition(RecordingState.Failed);
        return new RecordingStopResult(false, message, null, TimeSpan.Zero);
    }

    private void Transition(RecordingState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, state);
    }
}
