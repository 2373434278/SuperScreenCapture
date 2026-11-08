using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SSC.Application.Abstractions;
using SSC.Application.Recording;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using SSC.Domain.Recording;
using SSC.Platform.Abstractions;
using SSC.UI.Capture;

namespace SSC.UI.Recording;

public sealed record RecordingOutcome(
    bool Success,
    string Message,
    RecordingStopResult? StopResult);

public sealed class RecordingCoordinator
{
    private const int CountdownSeconds = 3;

    private readonly IScreenCaptureService _capture;
    private readonly IPermissionService _permissions;
    private readonly RecordingSessionManager _session;
    private readonly SettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly PlatformCapabilities _capabilities;
    private bool _running;

    public RecordingCoordinator(
        IScreenCaptureService capture,
        IPermissionService permissions,
        RecordingSessionManager session,
        SettingsService settings,
        ILocalizer localizer,
        PlatformCapabilities capabilities)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public async Task<RecordingOutcome> StartRegionRecordingAsync(
        CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            return new RecordingOutcome(false, _localizer["recordingBusy"], null);
        }

        if (!_capabilities.Recording)
        {
            return new RecordingOutcome(false, _localizer["recordingUnavailable"], null);
        }

        _running = true;
        var mainWindow = GetMainWindow();
        var wasVisible = mainWindow?.IsVisible == true;
        try
        {
            if (!await EnsurePermissionAsync(cancellationToken).ConfigureAwait(true))
            {
                ShowMainWindow(mainWindow);
                return new RecordingOutcome(false, _localizer["recordingPermissionRequired"], null);
            }

            if (wasVisible)
            {
                mainWindow!.Hide();
                await Task.Delay(180, cancellationToken).ConfigureAwait(true);
            }

            var displays = await _capture.GetDisplaysAsync(cancellationToken).ConfigureAwait(true);
            if (displays.Count == 0)
            {
                ShowMainWindow(mainWindow);
                return new RecordingOutcome(false, _localizer["recordingFailed"], null);
            }

            var selection = await SelectRegionAsync(displays, cancellationToken).ConfigureAwait(true);
            if (selection is null)
            {
                RestoreMainWindow(mainWindow, wasVisible);
                return new RecordingOutcome(false, _localizer["recordingCanceled"], null);
            }

            var display = selection.Display;
            var region = new PixelRect(
                display.Bounds.X + selection.Rect.X,
                display.Bounds.Y + selection.Rect.Y,
                selection.Rect.Width,
                selection.Rect.Height);
            var options = CreateOptions(display, region);
            return await ShowSetupAndRecordAsync(
                options,
                mainWindow,
                wasVisible,
                () => StartRegionRecordingAsync(),
                cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            RestoreMainWindow(mainWindow, wasVisible);
            return new RecordingOutcome(false, _localizer["recordingCanceled"], null);
        }
        catch (Exception exception)
        {
            ShowMainWindow(mainWindow);
            return new RecordingOutcome(false, $"{_localizer["recordingFailed"]}: {exception.Message}", null);
        }
        finally
        {
            _running = false;
        }
    }

    public async Task<RecordingOutcome> StartFullScreenRecordingAsync(
        CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            return new RecordingOutcome(false, _localizer["recordingBusy"], null);
        }

        if (!_capabilities.Recording)
        {
            return new RecordingOutcome(false, _localizer["recordingUnavailable"], null);
        }

        _running = true;
        var mainWindow = GetMainWindow();
        var wasVisible = mainWindow?.IsVisible == true;
        try
        {
            if (!await EnsurePermissionAsync(cancellationToken).ConfigureAwait(true))
            {
                ShowMainWindow(mainWindow);
                return new RecordingOutcome(false, _localizer["recordingPermissionRequired"], null);
            }

            if (wasVisible)
            {
                mainWindow!.Hide();
                await Task.Delay(180, cancellationToken).ConfigureAwait(true);
            }

            var displays = await _capture.GetDisplaysAsync(cancellationToken).ConfigureAwait(true);
            var display = displays.FirstOrDefault(candidate => candidate.IsPrimary) ?? displays.FirstOrDefault();
            if (display is null)
            {
                ShowMainWindow(mainWindow);
                return new RecordingOutcome(false, _localizer["recordingFailed"], null);
            }

            var options = CreateOptions(display, display.Bounds);
            return await ShowSetupAndRecordAsync(
                options,
                mainWindow,
                wasVisible,
                () => StartFullScreenRecordingAsync(),
                cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            RestoreMainWindow(mainWindow, wasVisible);
            return new RecordingOutcome(false, _localizer["recordingCanceled"], null);
        }
        catch (Exception exception)
        {
            ShowMainWindow(mainWindow);
            return new RecordingOutcome(false, $"{_localizer["recordingFailed"]}: {exception.Message}", null);
        }
        finally
        {
            _running = false;
        }
    }

    private async Task<RecordingOutcome> ShowSetupAndRecordAsync(
        RecordingOptions options,
        Window? mainWindow,
        bool wasVisible,
        Func<Task<RecordingOutcome>> recordAgain,
        CancellationToken cancellationToken)
    {
        var setup = new RecordingSetupWindow(options, _localizer);
        var chosen = await setup.ShowAsync().ConfigureAwait(true);
        if (chosen is null)
        {
            RestoreMainWindow(mainWindow, wasVisible);
            return new RecordingOutcome(false, _localizer["recordingCanceled"], null);
        }

        var completion = new TaskCompletionSource<RecordingStopResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bar = new RecordingControlBarWindow(_session, _localizer)
        {
            StopRequested = async () =>
            {
                if (_session.State is RecordingState.Recording or RecordingState.Paused or RecordingState.Failed)
                {
                    completion.TrySetResult(await _session.StopAsync());
                }
                else
                {
                    completion.TrySetResult(null);
                }
            },
            FailureDetected = async error =>
            {
                completion.TrySetResult(await _session.StopAsync());
            }
        };

        bar.Show();
        try
        {
            for (var seconds = CountdownSeconds; seconds >= 1; seconds--)
            {
                bar.ShowCountdown(seconds);
                await Task.WhenAny(
                    Task.Delay(1000, cancellationToken),
                    completion.Task).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                if (completion.Task.IsCompleted)
                {
                    return Canceled(mainWindow, wasVisible);
                }
            }

            if (completion.Task.IsCompleted)
            {
                return Canceled(mainWindow, wasVisible);
            }

            var start = await _session.StartAsync(chosen, cancellationToken).ConfigureAwait(true);
            if (!start.Success)
            {
                ShowMainWindow(mainWindow);
                return new RecordingOutcome(false, start.Message, null);
            }

            if (completion.Task.IsCompleted || cancellationToken.IsCancellationRequested)
            {
                await _session.StopAsync(CancellationToken.None).ConfigureAwait(true);
                return Canceled(mainWindow, wasVisible);
            }

            bar.BeginRecording();
            var result = await completion.Task.ConfigureAwait(true);
            if (result is null)
            {
                return Canceled(mainWindow, wasVisible);
            }

            RestoreMainWindow(mainWindow, wasVisible);
            if (result.Success)
            {
                new RecordingSummaryWindow(
                    result,
                    _session.CurrentOptions,
                    _localizer,
                    () => recordAgain()).Show();
                return new RecordingOutcome(true, result.Message, result);
            }

            ShowMainWindow(mainWindow);
            return new RecordingOutcome(false, result.Message, result);
        }
        catch (OperationCanceledException)
        {
            if (_session.IsActive)
            {
                await _session.StopAsync(CancellationToken.None).ConfigureAwait(true);
            }

            RestoreMainWindow(mainWindow, wasVisible);
            return Canceled(mainWindow, wasVisible);
        }
        finally
        {
            bar.CloseFromCoordinator();
        }
    }

    private async Task<CaptureSelection?> SelectRegionAsync(
        IReadOnlyList<DisplayDescriptor> displays,
        CancellationToken cancellationToken)
    {
        var captured = new List<(DisplayDescriptor Display, CaptureFrame Frame)>(displays.Count);
        foreach (var display in displays)
        {
            captured.Add((
                display,
                await _capture.CaptureDisplayAsync(display.Id, null, cancellationToken).ConfigureAwait(true)));
        }

        var completion = new TaskCompletionSource<CaptureSelection?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var overlays = new List<CaptureOverlayWindow>(captured.Count);
        try
        {
            foreach (var (display, frame) in captured)
            {
                overlays.Add(new CaptureOverlayWindow(
                    display,
                    frame,
                    _localizer,
                    selection => completion.TrySetResult(selection)));
            }

            foreach (var overlay in overlays)
            {
                overlay.Show();
            }

            (overlays.FirstOrDefault(overlay => overlay.Display.IsPrimary) ?? overlays[0]).FocusOverlay();

            using var registration = cancellationToken.Register(() =>
                Dispatcher.UIThread.Post(() => completion.TrySetResult(null)));

            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            foreach (var overlay in overlays)
            {
                overlay.Close();
            }
        }
    }

    private RecordingOptions CreateOptions(DisplayDescriptor display, PixelRect region)
    {
        var settings = _settings.Current;
        return new RecordingOptions(
            display.Id,
            region,
            display.ScaleFactor,
            settings.RecordingFps,
            settings.RecordingBitrateMbps,
            settings.RecordingMaxHeight,
            settings.RecordMicrophone);
    }

    private RecordingOutcome Canceled(Window? mainWindow, bool wasVisible)
    {
        RestoreMainWindow(mainWindow, wasVisible);
        return new RecordingOutcome(false, _localizer["recordingCanceled"], null);
    }

    private async Task<bool> EnsurePermissionAsync(CancellationToken cancellationToken)
    {
        var status = await _permissions.CheckAsync(PermissionKind.ScreenRecording, cancellationToken)
            .ConfigureAwait(true);
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        status = await _permissions.RequestAsync(PermissionKind.ScreenRecording, cancellationToken)
            .ConfigureAwait(true);
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        await _permissions.OpenSettingsAsync(PermissionKind.ScreenRecording, cancellationToken)
            .ConfigureAwait(true);
        return false;
    }

    private static void RestoreMainWindow(Window? mainWindow, bool wasVisible)
    {
        if (mainWindow is null || !wasVisible)
        {
            return;
        }

        mainWindow.Show();
        mainWindow.Activate();
    }

    private static void ShowMainWindow(Window? mainWindow)
    {
        if (mainWindow is null)
        {
            return;
        }

        mainWindow.Show();
        mainWindow.Activate();
    }

    private static Window? GetMainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
