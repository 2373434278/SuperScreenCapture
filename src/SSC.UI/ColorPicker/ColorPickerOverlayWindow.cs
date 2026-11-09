using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.UI.ColorPicker;

public sealed class ColorPickerOverlayWindow : Window
{
    private bool _finished;
    private readonly Action<string?> _complete;

    public ColorPickerOverlayWindow(
        DisplayDescriptor display,
        CaptureFrame frame,
        IReadOnlyList<string> recentColors,
        ILocalizer localizer,
        Action<string?> complete)
    {
        Display = display ?? throw new ArgumentNullException(nameof(display));
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));

        WindowDecorations = WindowDecorations.None;
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

        var control = new ColorPickerOverlayControl(
            display,
            frame,
            recentColors,
            localizer,
            hex => Finish(hex),
            () => Finish(null));
        Content = control;
        Opened += (_, _) =>
        {
            Activate();
            control.Focus();
        };
    }

    public DisplayDescriptor Display { get; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_finished)
        {
            _finished = true;
            _complete(null);
        }

        base.OnClosing(e);
    }

    private void Finish(string? hex)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _complete(hex);
        Close();
    }
}
