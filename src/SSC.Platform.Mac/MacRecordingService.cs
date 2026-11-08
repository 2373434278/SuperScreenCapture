using System.Globalization;
using SSC.Application.Abstractions;
using SSC.Domain.Recording;

namespace SSC.Platform.Mac;

public sealed class MacRecordingService : IRecordingService
{
    public Task StartAsync(
        string outputPath,
        RecordingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);

        return Task.Run(() =>
        {
            if (!uint.TryParse(options.DisplayId, NumberStyles.None, CultureInfo.InvariantCulture, out var displayId))
            {
                throw new ArgumentException($"Invalid display id '{options.DisplayId}'.", nameof(options));
            }

            var normalized = options.Normalize();
            var size = normalized.OutputSize;
            var config = new MacNativeMethods.SscRecordingConfig
            {
                DisplayId = displayId,
                X = normalized.Region.X,
                Y = normalized.Region.Y,
                Width = (uint)Math.Max(1, normalized.Region.Width),
                Height = (uint)Math.Max(1, normalized.Region.Height),
                DisplayScale = normalized.DisplayScale,
                Fps = (uint)normalized.Fps,
                BitrateMbps = (uint)normalized.BitrateMbps,
                OutputWidth = (uint)size.Width,
                OutputHeight = (uint)size.Height,
                IncludeMicrophone = normalized.IncludeMicrophone ? 1 : 0
            };

            if (MacNativeMethods.ssc_recording_start(outputPath, ref config) != 1)
            {
                throw new InvalidOperationException(MacNativeMethods.GetLastError());
            }
        }, cancellationToken);
    }

    public Task<bool> PauseAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => MacNativeMethods.ssc_recording_pause() == 1, cancellationToken);

    public Task<bool> ResumeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => MacNativeMethods.ssc_recording_resume() == 1, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (MacNativeMethods.ssc_recording_stop() != 1)
            {
                throw new InvalidOperationException(MacNativeMethods.GetLastError());
            }
        }, cancellationToken);

    public Task<bool> SetMicrophoneAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.Run(() => MacNativeMethods.ssc_recording_set_microphone(enabled ? 1 : 0) == 1, cancellationToken);

    public RecordingEngineStatus GetStatus()
    {
        var state = MacNativeMethods.ssc_recording_state() switch
        {
            1 => RecordingEngineState.Recording,
            2 => RecordingEngineState.Paused,
            3 => RecordingEngineState.Finalizing,
            4 => RecordingEngineState.Completed,
            5 => RecordingEngineState.Failed,
            _ => RecordingEngineState.Idle
        };

        return new RecordingEngineStatus(
            state,
            MacNativeMethods.ssc_recording_elapsed_seconds(),
            MacNativeMethods.ssc_recording_mic_level(),
            state == RecordingEngineState.Failed ? MacNativeMethods.GetLastError() : null);
    }
}
