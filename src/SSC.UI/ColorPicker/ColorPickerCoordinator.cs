using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Domain.Capture;

namespace SSC.UI.ColorPicker;

public sealed class ColorPickerCoordinator
{
    private readonly IScreenCaptureService _capture;
    private readonly IPermissionService _permissions;
    private readonly ILocalizer _localizer;
    private readonly SettingsService _settings;
    private bool _running;

    public ColorPickerCoordinator(
        IScreenCaptureService capture,
        IPermissionService permissions,
        ILocalizer localizer,
        SettingsService settings)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<string?> PickAsync(CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            return null;
        }

        _running = true;
        var mainWindow = GetMainWindow();
        var wasVisible = mainWindow?.IsVisible == true;

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
                return null;
            }

            if (wasVisible)
            {
                mainWindow!.Hide();
                await Task.Delay(180, cancellationToken).ConfigureAwait(true);
            }

            var displays = await _capture.GetDisplaysAsync(cancellationToken).ConfigureAwait(true);
            if (displays.Count == 0)
            {
                return null;
            }

            var frames = new List<(DisplayDescriptor Display, CaptureFrame Frame)>(displays.Count);
            foreach (var display in displays)
            {
                frames.Add((display, await _capture
                    .CaptureDisplayAsync(display.Id, null, cancellationToken)
                    .ConfigureAwait(true)));
            }

            var completion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var recent = _settings.Current.RecentColors;
            var windows = new List<ColorPickerOverlayWindow>(frames.Count);

            foreach (var (display, frame) in frames)
            {
                var window = new ColorPickerOverlayWindow(
                    display,
                    frame,
                    recent,
                    _localizer,
                    hex => completion.TrySetResult(hex));
                windows.Add(window);
            }

            foreach (var window in windows)
            {
                window.Show();
            }

            var picked = await completion.Task.ConfigureAwait(true);
            foreach (var window in windows)
            {
                window.Close();
            }

            if (!string.IsNullOrWhiteSpace(picked))
            {
                RememberColor(picked);
            }

            return picked;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (wasVisible && mainWindow is { IsVisible: false })
            {
                mainWindow.Show();
                mainWindow.Activate();
            }

            _running = false;
        }
    }

    private void RememberColor(string hex)
    {
        var settings = _settings.Current;
        var colors = new List<string> { hex.ToUpperInvariant() };
        colors.AddRange(settings.RecentColors.Where(color =>
            !string.Equals(color, hex, StringComparison.OrdinalIgnoreCase)));
        settings.RecentColors = colors.Take(10).ToList();
        _settings.Save(settings);
    }

    private static Window? GetMainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
