using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using SSC.Application.Abstractions;
using SSC.Application.Animated;
using SSC.Application.Capture;
using SSC.Application.Events;
using SSC.Application.Settings;
using SSC.Domain.Capture;
using SSC.Domain.History;
using SSC.UI.Capture;
using SSC.UI.Dialogs;

namespace SSC.UI.Recording;

public sealed record GifRecordingOutcome(bool Success, string Message, string? FilePath = null);

/// <summary>
/// GIF 区域录制：复用截图覆盖层选区，按设置帧率抓帧并流式写入 GIF。
/// </summary>
public sealed class GifRecordingCoordinator
{
    private readonly IScreenCaptureService _capture;
    private readonly IPermissionService _permissions;
    private readonly ILocalizer _localizer;
    private readonly SettingsService _settings;
    private readonly IHistoryRepository _history;
    private readonly IAppEventBus _events;
    private readonly CaptureFileNamer _namer;
    private bool _running;

    public GifRecordingCoordinator(
        IScreenCaptureService capture,
        IPermissionService permissions,
        ILocalizer localizer,
        SettingsService settings,
        IHistoryRepository history,
        IAppEventBus events,
        CaptureFileNamer namer)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _namer = namer ?? throw new ArgumentNullException(nameof(namer));
    }

    public async Task<GifRecordingOutcome> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            return new GifRecordingOutcome(false, _localizer["gifBusy"]);
        }

        _running = true;
        var mainWindow = GetMainWindow();
        var wasVisible = mainWindow?.IsVisible == true;
        GifControlBarWindow? bar = null;
        string? tempPath = null;

        try
        {
            var status = await _permissions.CheckAsync(PermissionKind.ScreenRecording, cancellationToken)
                .ConfigureAwait(true);
            if (status is not PermissionStatus.Granted)
            {
                status = await _permissions.RequestAsync(PermissionKind.ScreenRecording, cancellationToken)
                    .ConfigureAwait(true);
            }

            if (status is not PermissionStatus.Granted)
            {
                await PermissionGuide.ShowAsync(_permissions, _localizer).ConfigureAwait(true);
                return new GifRecordingOutcome(false, _localizer["capturePermissionRequired"]);
            }

            if (wasVisible)
            {
                mainWindow!.Hide();
                await Task.Delay(180, cancellationToken).ConfigureAwait(true);
            }

            var displays = await _capture.GetDisplaysAsync(cancellationToken).ConfigureAwait(true);
            if (displays.Count == 0)
            {
                return new GifRecordingOutcome(false, _localizer["gifFailed"]);
            }

            var selection = await SelectRegionAsync(displays, cancellationToken).ConfigureAwait(true);
            if (selection is null)
            {
                return new GifRecordingOutcome(false, _localizer["captureCanceled"]);
            }

            var settings = _settings.Current;
            var fps = Math.Clamp(settings.GifFps, 5, 15);
            var region = selection.Rect;
            var first = selection.Frame.Crop(region);
            var (width, height) = GifEncoder.FitSize(first.Width, first.Height, settings.GifMaxDimension);
            var path = _namer.CreatePath(settings, CaptureKind.AnimatedImage, "gif");
            tempPath = path + ".part";

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var encoder = new GifEncoder(stream, width, height);
            encoder.WriteFrame(GifEncoder.Quantize(first, width, height), Math.Max(1, (int)Math.Round(100d / fps)));

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bar = new GifControlBarWindow(_localizer, fps, () => completion.TrySetResult(true), () => completion.TrySetResult(false));
            bar.Show();
            PositionBar(bar);

            var startedAt = DateTimeOffset.Now;
            var frames = 1;
            var failures = 0;
            var stopwatch = Stopwatch.StartNew();

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000d / fps));
            using var loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            while (!completion.Task.IsCompleted && !loopCancellation.IsCancellationRequested)
            {
                var tick = timer.WaitForNextTickAsync(loopCancellation.Token);
                var finished = await Task.WhenAny(tick.AsTask(), completion.Task).ConfigureAwait(true);
                if (finished == completion.Task || completion.Task.IsCompleted)
                {
                    break;
                }

                if (!await tick.ConfigureAwait(true))
                {
                    break;
                }

                if ((DateTimeOffset.Now - startedAt).TotalSeconds >= settings.GifMaxDurationSeconds)
                {
                    break;
                }

                try
                {
                    var frame = await _capture
                        .CaptureDisplayAsync(selection.Display.Id, region, loopCancellation.Token)
                        .ConfigureAwait(true);
                    encoder.WriteFrame(
                        GifEncoder.Quantize(frame, width, height),
                        Math.Max(1, (int)Math.Round(100d / fps)));
                    frames++;
                    failures = 0;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures++;
                    if (failures >= 3)
                    {
                        break;
                    }
                }

                bar.Update(stopwatch.Elapsed, frames, fps);
            }

            encoder.Finish();
            stream.Flush();
            stream.Dispose();

            var stopped = !completion.Task.IsCompletedSuccessfully || completion.Task.Result;
            var elapsed = DateTimeOffset.Now - startedAt;
            if (!stopped)
            {
                TryDelete(tempPath);
                return new GifRecordingOutcome(false, _localizer["captureCanceled"]);
            }

            if (frames <= 1 && elapsed.TotalSeconds < 0.3)
            {
                TryDelete(tempPath);
                return new GifRecordingOutcome(false, _localizer["gifTooShort"]);
            }

            File.Move(tempPath, path, overwrite: true);
            var entry = HistoryEntry.Create(
                Guid.NewGuid(),
                CaptureKind.AnimatedImage,
                Path.GetFileName(path),
                path,
                DateTimeOffset.Now,
                width,
                height,
                elapsed,
                tags: new[] { "gif" });
            _history.Add(entry);
            _events.Publish(new HistoryChanged(entry.Id));

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (InvalidOperationException)
            {
            }

            return new GifRecordingOutcome(
                true,
                string.Format(_localizer["gifSaved"], Path.GetFileName(path)),
                path);
        }
        catch (OperationCanceledException)
        {
            return new GifRecordingOutcome(false, _localizer["captureCanceled"]);
        }
        catch (Exception exception)
        {
            if (tempPath is not null)
            {
                TryDelete(tempPath);
            }

            return new GifRecordingOutcome(false, $"{_localizer["gifFailed"]}: {exception.Message}");
        }
        finally
        {
            bar?.CloseFromCoordinator();
            if (wasVisible && mainWindow is not null)
            {
                mainWindow.Show();
                mainWindow.Activate();
            }

            _running = false;
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

    private static void PositionBar(Window bar)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
        {
            return;
        }

        var screen = bar.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        bar.Position = new PixelPoint(
            area.X + (area.Width - (int)bar.Width) / 2,
            area.Y + area.Height - (int)bar.Height - 28);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static Window? GetMainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}

/// <summary>GIF 录制控制条：计时、帧数、停止与取消。</summary>
internal sealed class GifControlBarWindow : Window
{
    private readonly TextBlock _timer;
    private readonly TextBlock _frames;
    private bool _closed;

    public GifControlBarWindow(ILocalizer localizer, int fps, Action stop, Action cancel)
    {
        Width = 330;
        Height = 54;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        var dot = new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush(Color.Parse("#FF4757")),
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = "GIF",
            FontSize = 9.5,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FFD5D9")),
            VerticalAlignment = VerticalAlignment.Center
        };
        _timer = new TextBlock
        {
            Text = "00:00.0",
            FontFamily = new FontFamily("SF Mono, Menlo, Consolas"),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        _frames = new TextBlock
        {
            Text = $"0 · {fps}fps",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#A7B1C2")),
            VerticalAlignment = VerticalAlignment.Center
        };

        var stopButton = new Button
        {
            Content = localizer["gifStop"],
            Height = 28,
            Padding = new Thickness(12, 0),
            FontSize = 11,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#FF4757")),
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        stopButton.Click += (_, _) => stop();

        var cancelButton = new Button
        {
            Content = "×",
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#A7B1C2")),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        cancelButton.Click += (_, _) => cancel();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0)
        };
        row.Children.Add(dot);
        row.Children.Add(label);
        row.Children.Add(_timer);
        row.Children.Add(_frames);
        row.Children.Add(stopButton);
        row.Children.Add(cancelButton);

        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.Parse("#F00A0E16")),
            BorderBrush = new SolidColorBrush(Color.Parse("#29FFFFFF")),
            BorderThickness = new Thickness(1),
            Child = row
        };

        Opened += (_, _) => Services.WindowCaptureExclusion.Apply(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                cancel();
            }
        };
    }

    public void Update(TimeSpan elapsed, int frames, int fps)
    {
        var total = Math.Max(0, (int)elapsed.TotalSeconds);
        _timer.Text = $"{total / 60:00}:{total % 60:00}.{elapsed.Milliseconds / 100}";
        _frames.Text = $"{frames} · {fps}fps";
    }

    public void CloseFromCoordinator()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Close();
    }
}
