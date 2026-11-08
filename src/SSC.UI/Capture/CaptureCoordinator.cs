using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using SSC.UI.Editing;

namespace SSC.UI.Capture;

public sealed class CaptureCoordinator
{
    private readonly IScreenCaptureService _capture;
    private readonly IPermissionService _permissions;
    private readonly ScreenshotService _screenshots;
    private readonly IClipboardService _clipboard;
    private readonly ILocalizer _localizer;
    private readonly IAnnotationRenderer _annotationRenderer;
    private bool _running;

    public CaptureCoordinator(
        IScreenCaptureService capture,
        IPermissionService permissions,
        ScreenshotService screenshots,
        IClipboardService clipboard,
        ILocalizer localizer,
        IAnnotationRenderer annotationRenderer)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _screenshots = screenshots ?? throw new ArgumentNullException(nameof(screenshots));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _annotationRenderer = annotationRenderer ?? throw new ArgumentNullException(nameof(annotationRenderer));
    }

    public Task<CaptureOutcome> CaptureRegionAsync(CancellationToken cancellationToken = default) =>
        RunOverlayCaptureAsync(CaptureOverlayMode.Region, windowResolver: null, cancellationToken);

    public Task<CaptureOutcome> CaptureWindowSelectionAsync(CancellationToken cancellationToken = default) =>
        RunOverlayCaptureAsync(
            CaptureOverlayMode.Window,
            point => _capture.GetWindowAtPointAsync(point, cancellationToken),
            cancellationToken);

    public async Task<CaptureOutcome> CaptureFullScreenAsync(CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            return new CaptureOutcome(false, _localizer["captureBusy"]);
        }

        _running = true;
        try
        {
            if (!await EnsurePermissionAsync(cancellationToken).ConfigureAwait(true))
            {
                return new CaptureOutcome(false, _localizer["capturePermissionRequired"]);
            }

            var mainWindow = GetMainWindow();
            var wasVisible = mainWindow?.IsVisible == true;
            if (wasVisible)
            {
                mainWindow!.Hide();
                await Task.Delay(180, cancellationToken).ConfigureAwait(true);
            }

            AnnotationEditorWindow? editor = null;
            try
            {
                var displays = await _capture.GetDisplaysAsync(cancellationToken).ConfigureAwait(true);
                var display = displays.FirstOrDefault(candidate => candidate.IsPrimary) ?? displays.FirstOrDefault()
                    ?? throw new InvalidOperationException("No display is available.");

                var frame = await _capture.CaptureDisplayAsync(display.Id, null, cancellationToken).ConfigureAwait(true);
                var result = await _screenshots.SaveAsync(frame, CaptureKind.Screenshot, cancellationToken).ConfigureAwait(true);
                editor = new AnnotationEditorWindow(
                    result,
                    frame,
                    _localizer,
                    _annotationRenderer,
                    _screenshots,
                    _clipboard,
                    retake: () => CaptureFullScreenAsync());
                editor.Show();
                return new CaptureOutcome(true, _localizer["captureSaved"], result);
            }
            finally
            {
                if (wasVisible)
                {
                    mainWindow!.Show();
                    mainWindow.Activate();
                }

                editor?.Activate();
            }
        }
        catch (OperationCanceledException)
        {
            return new CaptureOutcome(false, _localizer["captureCanceled"]);
        }
        catch (Exception exception)
        {
            return new CaptureOutcome(false, $"{_localizer["captureFailed"]}: {exception.Message}");
        }
        finally
        {
            _running = false;
        }
    }

    public async Task<CaptureOutcome> CaptureDelayedAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken).ConfigureAwait(true);
        return await CaptureFullScreenAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task<CaptureOutcome> RunOverlayCaptureAsync(
        CaptureOverlayMode mode,
        Func<PixelPoint, Task<WindowDescriptor?>>? windowResolver,
        CancellationToken cancellationToken)
    {
        if (_running)
        {
            return new CaptureOutcome(false, _localizer["captureBusy"]);
        }

        _running = true;
        try
        {
            if (!await EnsurePermissionAsync(cancellationToken).ConfigureAwait(true))
            {
                return new CaptureOutcome(false, _localizer["capturePermissionRequired"]);
            }

            var mainWindow = GetMainWindow();
            var wasVisible = mainWindow?.IsVisible == true;
            if (wasVisible)
            {
                mainWindow!.Hide();
                await Task.Delay(180, cancellationToken).ConfigureAwait(true);
            }

            AnnotationEditorWindow? editor = null;
            try
            {
                var displays = await _capture.GetDisplaysAsync(cancellationToken).ConfigureAwait(true);
                if (displays.Count == 0)
                {
                    return new CaptureOutcome(false, _localizer["captureFailed"]);
                }

                var captured = new List<(DisplayDescriptor Display, CaptureFrame Frame)>(displays.Count);
                foreach (var display in displays)
                {
                    captured.Add((display, await _capture.CaptureDisplayAsync(display.Id, null, cancellationToken).ConfigureAwait(true)));
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
                            selection => completion.TrySetResult(selection),
                            mode,
                            windowResolver));
                    }

                    foreach (var overlay in overlays)
                    {
                        overlay.Show();
                    }

                    (overlays.FirstOrDefault(overlay => overlay.Display.IsPrimary) ?? overlays[0]).FocusOverlay();

                    using var registration = cancellationToken.Register(() =>
                        Dispatcher.UIThread.Post(() => completion.TrySetResult(null)));

                    var selection = await completion.Task.ConfigureAwait(true);
                    if (selection is null)
                    {
                        return new CaptureOutcome(false, _localizer["captureCanceled"]);
                    }

                    CaptureFrame capturedFrame;
                    if (selection.Window is { } window)
                    {
                        capturedFrame = await _capture.CaptureWindowAsync(window.Id, cancellationToken).ConfigureAwait(true);
                    }
                    else
                    {
                        capturedFrame = selection.Frame.Crop(selection.Rect);
                    }

                    var result = await _screenshots.SaveAsync(capturedFrame, CaptureKind.Screenshot, cancellationToken)
                        .ConfigureAwait(true);
                    Func<Task> retake = mode == CaptureOverlayMode.Window
                        ? () => CaptureWindowSelectionAsync()
                        : () => CaptureRegionAsync();
                    editor = new AnnotationEditorWindow(
                        result,
                        capturedFrame,
                        _localizer,
                        _annotationRenderer,
                        _screenshots,
                        _clipboard,
                        retake);
                    editor.Show();
                    return new CaptureOutcome(true, _localizer["captureSaved"], result);
                }
                finally
                {
                    foreach (var overlay in overlays)
                    {
                        overlay.Close();
                    }
                }
            }
            finally
            {
                if (wasVisible)
                {
                    mainWindow!.Show();
                    mainWindow.Activate();
                }

                editor?.Activate();
            }
        }
        catch (OperationCanceledException)
        {
            return new CaptureOutcome(false, _localizer["captureCanceled"]);
        }
        catch (Exception exception)
        {
            return new CaptureOutcome(false, $"{_localizer["captureFailed"]}: {exception.Message}");
        }
        finally
        {
            _running = false;
        }
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

    private static Window? GetMainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
