using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Application.Scroll;
using SSC.Application.Settings;
using SSC.Application.Upload;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using SSC.UI.Editing;

namespace SSC.UI.Capture;

public sealed class CaptureCoordinator
{
    private readonly IScreenCaptureService _capture;
    private readonly IPermissionService _permissions;
    private readonly ScreenshotService _screenshots;
    private readonly SettingsService _settings;
    private readonly IClipboardService _clipboard;
    private readonly ILocalizer _localizer;
    private readonly IAnnotationRenderer _annotationRenderer;
    private readonly IOcrService _ocrService;
    private readonly UploadPipeline _uploads;
    private readonly IImageEncoder _encoder;
    private readonly IScrollStitcher _stitcher;
    private bool _running;

    public CaptureCoordinator(
        IScreenCaptureService capture,
        IPermissionService permissions,
        ScreenshotService screenshots,
        SettingsService settings,
        IClipboardService clipboard,
        ILocalizer localizer,
        IAnnotationRenderer annotationRenderer,
        IOcrService ocrService,
        UploadPipeline uploads,
        IImageEncoder encoder,
        IScrollStitcher stitcher)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _screenshots = screenshots ?? throw new ArgumentNullException(nameof(screenshots));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _annotationRenderer = annotationRenderer ?? throw new ArgumentNullException(nameof(annotationRenderer));
        _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _stitcher = stitcher ?? throw new ArgumentNullException(nameof(stitcher));
    }

    public Task<CaptureOutcome> CaptureRegionAsync(CancellationToken cancellationToken = default) =>
        RunOverlayCaptureAsync(CaptureOverlayMode.Region, windowResolver: null, cancellationToken);

    public Task<CaptureOutcome> CaptureWindowSelectionAsync(CancellationToken cancellationToken = default) =>
        RunOverlayCaptureAsync(
            CaptureOverlayMode.Window,
            point => _capture.GetWindowAtPointAsync(point, cancellationToken),
            cancellationToken);

    public async Task<CaptureOutcome> CaptureScrollAsync(CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            return new CaptureOutcome(false, _localizer["captureBusy"]);
        }

        _running = true;
        var mainWindow = GetMainWindow();
        var wasVisible = mainWindow?.IsVisible == true;
        ScrollCaptureBarWindow? activeBar = null;
        try
        {
            if (!await EnsurePermissionAsync(cancellationToken).ConfigureAwait(true))
            {
                return new CaptureOutcome(false, _localizer["capturePermissionRequired"]);
            }

            if (wasVisible)
            {
                mainWindow!.Hide();
                await Task.Delay(180, cancellationToken).ConfigureAwait(true);
            }

            var displays = await _capture.GetDisplaysAsync(cancellationToken).ConfigureAwait(true);
            if (displays.Count == 0)
            {
                return new CaptureOutcome(false, _localizer["captureFailed"]);
            }

            var captured = new List<(DisplayDescriptor Display, CaptureFrame Frame)>(displays.Count);
            foreach (var display in displays)
            {
                captured.Add((
                    display,
                    await _capture.CaptureDisplayAsync(display.Id, null, cancellationToken).ConfigureAwait(true)));
            }

            var selectionCompletion = new TaskCompletionSource<CaptureSelection?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var overlays = new List<CaptureOverlayWindow>(captured.Count);
            CaptureSelection? selection;
            try
            {
                foreach (var (display, frame) in captured)
                {
                    overlays.Add(new CaptureOverlayWindow(
                        display,
                        frame,
                        _localizer,
                        selected => selectionCompletion.TrySetResult(selected)));
                }

                foreach (var overlay in overlays)
                {
                    overlay.Show();
                }

                (overlays.FirstOrDefault(overlay => overlay.Display.IsPrimary) ?? overlays[0]).FocusOverlay();
                using var registration = cancellationToken.Register(() =>
                    Dispatcher.UIThread.Post(() => selectionCompletion.TrySetResult(null)));
                selection = await selectionCompletion.Task.ConfigureAwait(true);
            }
            finally
            {
                foreach (var overlay in overlays)
                {
                    overlay.Close();
                }
            }

            if (selection is null)
            {
                RestoreMainWindow(mainWindow, wasVisible);
                return new CaptureOutcome(false, _localizer["captureCanceled"]);
            }

            var displayDescriptor = selection.Display;
            var region = selection.Rect;
            var stitched = selection.Frame.Crop(region);
            var frameCount = 1;
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var bar = new ScrollCaptureBarWindow(_localizer);
            activeBar = bar;
            bar.SetCount(frameCount, stitched.Width, stitched.Height);

            bar.CaptureNextRequested = async () =>
            {
                bar.SetBusy(true);
                try
                {
                    await Task.Delay(120, cancellationToken).ConfigureAwait(true);
                    var next = await _capture.CaptureDisplayAsync(
                        displayDescriptor.Id,
                        region,
                        cancellationToken).ConfigureAwait(true);
                    stitched = _stitcher.Stitch(new[] { stitched, next });
                    frameCount++;
                    bar.SetCount(frameCount, stitched.Width, stitched.Height);
                    bar.SetStatus(string.Empty);
                }
                catch (ScrollStitchException exception)
                {
                    bar.SetStatus(exception.Message);
                }
                catch (Exception exception)
                {
                    bar.SetStatus(exception.Message);
                }
                finally
                {
                    bar.SetBusy(false);
                }
            };

            ScreenshotResult? saved = null;
            bar.FinishRequested = async () =>
            {
                if (completion.Task.IsCompleted)
                {
                    return;
                }

                bar.SetBusy(true);
                try
                {
                    saved = await _screenshots.SaveAsync(
                        stitched,
                        CaptureKind.Screenshot,
                        cancellationToken).ConfigureAwait(true);
                    completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    bar.SetStatus(exception.Message);
                    bar.SetBusy(false);
                }
            };
            bar.CancelRequested = () =>
            {
                completion.TrySetResult(false);
                return Task.CompletedTask;
            };

            bar.Show();
            using var cancelRegistration = cancellationToken.Register(() =>
                Dispatcher.UIThread.Post(() => completion.TrySetResult(false)));
            var finished = await completion.Task.ConfigureAwait(true);
            bar.CloseFromCoordinator();
            RestoreMainWindow(mainWindow, wasVisible);

            if (!finished || saved is null)
            {
                return new CaptureOutcome(false, _localizer["scrollCanceled"]);
            }

            var editor = new AnnotationEditorWindow(
                saved,
                stitched,
                _localizer,
                _annotationRenderer,
                _screenshots,
                _clipboard,
                _ocrService,
                _uploads,
                _settings,
                _encoder);
            editor.Show();
            editor.Activate();
            return new CaptureOutcome(true, _localizer["scrollSaved"], saved);
        }
        catch (OperationCanceledException)
        {
            RestoreMainWindow(mainWindow, wasVisible);
            return new CaptureOutcome(false, _localizer["scrollCanceled"]);
        }
        catch (Exception exception)
        {
            RestoreMainWindow(mainWindow, wasVisible);
            return new CaptureOutcome(false, $"{_localizer["scrollFailed"]}: {exception.Message}");
        }
        finally
        {
            activeBar?.CloseFromCoordinator();
            _running = false;
        }
    }

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
                    _ocrService,
                    _uploads,
                    settings: _settings,
                    encoder: _encoder,
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
                        _ocrService,
                        _uploads,
                        settings: _settings,
                        encoder: _encoder,
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

    private static void RestoreMainWindow(Window? mainWindow, bool wasVisible)
    {
        if (mainWindow is null || !wasVisible)
        {
            return;
        }

        mainWindow.Show();
        mainWindow.Activate();
    }

    private static Window? GetMainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
