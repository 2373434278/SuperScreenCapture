using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.UI.Capture;

public sealed class CaptureOverlayWindow : Window
{
    private readonly CaptureOverlayControl _overlay;
    private readonly Action<CaptureSelection?> _complete;
    private bool _finished;

    public CaptureOverlayWindow(
        DisplayDescriptor display,
        CaptureFrame frame,
        ILocalizer localizer,
        Action<CaptureSelection?> complete,
        CaptureOverlayMode mode = CaptureOverlayMode.Region,
        Func<SSC.Domain.Geometry.PixelPoint, Task<WindowDescriptor?>>? windowResolver = null)
    {
        Display = display ?? throw new ArgumentNullException(nameof(display));
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Position = new PixelPoint(display.Bounds.X, display.Bounds.Y);
        Width = Math.Max(1, display.Bounds.Width / display.ScaleFactor);
        Height = Math.Max(1, display.Bounds.Height / display.ScaleFactor);

        _overlay = new CaptureOverlayControl(display, frame, localizer, HandleComplete, mode, windowResolver);
        Content = _overlay;
        Opened += (_, _) => _overlay.FocusOverlay();
    }

    public DisplayDescriptor Display { get; }

    public void FocusOverlay() => _overlay.FocusOverlay();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_finished)
        {
            _finished = true;
            _complete(null);
        }

        base.OnClosing(e);
    }

    private void HandleComplete(CaptureSelection? selection)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _complete(selection);
        Close();
    }
}
