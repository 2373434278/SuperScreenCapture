using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SSC.Application.Abstractions;
using SSC.Application.Color;
using SSC.Domain.Capture;

namespace SSC.UI.ColorPicker;

/// <summary>
/// 取色覆盖层：绘制冻结画面、放大镜、HEX/RGB/HSL 读数与最近颜色。
/// 遮罩刻意保持深色，与截图覆盖层一致。
/// </summary>
public sealed class ColorPickerOverlayControl : Control
{
    private const int SampleRadius = 6;
    private const int CellSize = 9;
    private const double PanelWidth = 250;
    private const double PanelHeight = 92;

    private static readonly Dictionary<string, IBrush> BrushCache = new();

    private readonly DisplayDescriptor _display;
    private readonly CaptureFrame _frame;
    private readonly WriteableBitmap _bitmap;
    private readonly IReadOnlyList<string> _recentColors;
    private readonly ILocalizer _localizer;
    private readonly Action<string> _picked;
    private readonly Action _canceled;
    private readonly List<(Rect Bounds, string Hex)> _swatches = new();
    private PixelPoint _cursor;
    private bool _hover;

    public ColorPickerOverlayControl(
        DisplayDescriptor display,
        CaptureFrame frame,
        IReadOnlyList<string> recentColors,
        ILocalizer localizer,
        Action<string> picked,
        Action canceled)
    {
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _recentColors = recentColors ?? Array.Empty<string>();
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _picked = picked ?? throw new ArgumentNullException(nameof(picked));
        _canceled = canceled ?? throw new ArgumentNullException(nameof(canceled));

        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);

        _bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var buffer = _bitmap.Lock();
        for (var row = 0; row < frame.Height; row++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                frame.Pixels,
                row * frame.Stride,
                IntPtr.Add(buffer.Address, row * buffer.RowBytes),
                frame.Width * 4);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        var pixel = ToPixel(point);
        if (_cursor != pixel)
        {
            _cursor = pixel;
            _hover = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        foreach (var (bounds, hex) in _swatches)
        {
            if (bounds.Contains(point))
            {
                _picked(hex);
                e.Handled = true;
                return;
            }
        }

        var (r, g, b) = Sample(ToPixel(point));
        _picked(ColorFormats.ToHex(r, g, b));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            _canceled();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _hover)
        {
            var (r, g, b) = Sample(_cursor);
            _picked(ColorFormats.ToHex(r, g, b));
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawImage(_bitmap, new Rect(0, 0, _frame.Width, _frame.Height), bounds);

        if (!_hover)
        {
            return;
        }

        var (red, green, blue) = Sample(_cursor);
        var hex = ColorFormats.ToHex(red, green, blue);
        var accent = new SolidColorBrush(Color.FromRgb(red, green, blue));

        var cursorPoint = ToPoint(_cursor);
        var crosshair = new Pen(new SolidColorBrush(Color.Parse("#99FFFFFF")), 1);
        context.DrawLine(crosshair, new Point(0, cursorPoint.Y), new Point(bounds.Width, cursorPoint.Y));
        context.DrawLine(crosshair, new Point(cursorPoint.X, 0), new Point(cursorPoint.X, bounds.Height));

        var magnifierSize = SampleRadius * 2 * CellSize;
        var magnifier = new Rect(
            Math.Clamp(cursorPoint.X + 22, 8, Math.Max(8, bounds.Width - magnifierSize - 8)),
            Math.Clamp(cursorPoint.Y + 22, 8, Math.Max(8, bounds.Height - magnifierSize - PanelHeight - 16)),
            magnifierSize,
            magnifierSize);

        context.FillRectangle(new SolidColorBrush(Color.Parse("#E60B0F16")), magnifier);
        for (var offsetY = -SampleRadius; offsetY < SampleRadius; offsetY++)
        {
            for (var offsetX = -SampleRadius; offsetX < SampleRadius; offsetX++)
            {
                var (r, g, b) = Sample(new PixelPoint(_cursor.X + offsetX, _cursor.Y + offsetY));
                var cell = new Rect(
                    magnifier.X + (offsetX + SampleRadius) * CellSize,
                    magnifier.Y + (offsetY + SampleRadius) * CellSize,
                    CellSize,
                    CellSize);
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), cell);
            }
        }

        context.DrawRectangle(
            new Pen(new SolidColorBrush(Color.Parse("#7B7FFF")), 1.5),
            magnifier);
        var center = new Rect(
            magnifier.X + SampleRadius * CellSize,
            magnifier.Y + SampleRadius * CellSize,
            CellSize,
            CellSize);
        context.DrawRectangle(new Pen(Brushes.White, 1.5), center);

        var panel = new Rect(
            Math.Clamp(magnifier.X, 8, Math.Max(8, bounds.Width - PanelWidth - 8)),
            Math.Min(magnifier.Bottom + 8, Math.Max(8, bounds.Height - PanelHeight - 8)),
            PanelWidth,
            PanelHeight);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#F20B0F16")), panel, 10);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#33FFFFFF")), 1), panel, 10);

        context.FillRectangle(accent, new Rect(panel.X + 12, panel.Y + 12, 42, 42), 8);
        context.DrawText(
            Format(hex, 15, Brushes.White),
            new Point(panel.X + 62, panel.Y + 11));
        context.DrawText(
            Format(ColorFormats.ToRgb(red, green, blue), 10.5, new SolidColorBrush(Color.Parse("#A7B1C2"))),
            new Point(panel.X + 62, panel.Y + 33));
        context.DrawText(
            Format(ColorFormats.ToHsl(red, green, blue), 10.5, new SolidColorBrush(Color.Parse("#A7B1C2"))),
            new Point(panel.X + 62, panel.Y + 48));

        _swatches.Clear();
        var swatchX = panel.X + 12;
        var swatchY = panel.Bottom - 24;
        foreach (var color in _recentColors.Take(8))
        {
            if (!ColorFormats.TryParseHex(color, out var parsed))
            {
                continue;
            }

            var swatch = new Rect(swatchX, swatchY, 16, 16);
            context.FillRectangle(
                new SolidColorBrush(Color.FromRgb(parsed.R, parsed.G, parsed.B)),
                swatch,
                4);
            _swatches.Add((swatch, color));
            swatchX += 20;
        }

        context.DrawText(
            Format(_localizer["colorPickerHint"], 9.5, new SolidColorBrush(Color.Parse("#68748A"))),
            new Point(panel.Right - 96, panel.Bottom - 22));
    }

    private (byte R, byte G, byte B) Sample(PixelPoint point)
    {
        var x = Math.Clamp(point.X, 0, _frame.Width - 1);
        var y = Math.Clamp(point.Y, 0, _frame.Height - 1);
        var index = (y * _frame.Width + x) * 4;
        return (_frame.Pixels[index + 2], _frame.Pixels[index + 1], _frame.Pixels[index]);
    }

    private PixelPoint ToPixel(Point point)
    {
        var width = Math.Max(1, Bounds.Width);
        var height = Math.Max(1, Bounds.Height);
        return new PixelPoint(
            (int)Math.Round(point.X / width * _frame.Width),
            (int)Math.Round(point.Y / height * _frame.Height));
    }

    private Point ToPoint(PixelPoint point)
    {
        var width = Math.Max(1, Bounds.Width);
        var height = Math.Max(1, Bounds.Height);
        return new Point(
            point.X / (double)_frame.Width * width,
            point.Y / (double)_frame.Height * height);
    }

    private static FormattedText Format(string text, double size, IBrush brush) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("SF Mono, Menlo, Consolas"),
            size,
            brush);

    private static IBrush Brush(string hex)
    {
        if (!BrushCache.TryGetValue(hex, out var brush))
        {
            brush = new SolidColorBrush(Color.Parse(hex));
            BrushCache[hex] = brush;
        }

        return brush;
    }
}
