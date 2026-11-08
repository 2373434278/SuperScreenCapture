using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.UI.Capture;

namespace SSC.UI.Ocr;

public sealed class OcrOverlayControl : Control
{
    private readonly CaptureFrame _frame;
    private readonly WriteableBitmap _bitmap;
    private IReadOnlyList<OcrLine> _lines = Array.Empty<OcrLine>();
    private Rect _imageRect;
    private double _scale = 1;
    private int _selectedIndex = -1;

    public OcrOverlayControl(CaptureFrame frame)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _bitmap = CaptureFrameBitmap.Create(frame);
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    public event EventHandler<int>? LineSelected;

    public void Dispose() => _bitmap.Dispose();

    public IReadOnlyList<OcrLine> Lines
    {
        get => _lines;
        set
        {
            _lines = value ?? Array.Empty<OcrLine>();
            InvalidateVisual();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
            {
                return;
            }

            _selectedIndex = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var availableWidth = Math.Max(1, Bounds.Width - 36);
        var availableHeight = Math.Max(1, Bounds.Height - 36);
        _scale = Math.Min(availableWidth / _frame.Width, availableHeight / _frame.Height);
        var width = _frame.Width * _scale;
        var height = _frame.Height * _scale;
        _imageRect = new Rect(
            (Bounds.Width - width) / 2,
            (Bounds.Height - height) / 2,
            width,
            height);

        context.DrawImage(
            _bitmap,
            new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height),
            _imageRect);

        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            var rect = new Rect(
                _imageRect.X + (line.Bounds.X * _scale),
                _imageRect.Y + (line.Bounds.Y * _scale),
                line.Bounds.Width * _scale,
                line.Bounds.Height * _scale);
            var selected = index == _selectedIndex;
            var fill = new SolidColorBrush(selected ? Color.Parse("#335B5FF0") : Color.Parse("#2200D4FF"));
            var pen = new Pen(
                new SolidColorBrush(selected ? Color.Parse("#7B7FFF") : Color.Parse("#22D3EE")),
                selected ? 2 : 1);
            context.DrawRectangle(fill, pen, rect);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!_imageRect.Contains(point))
        {
            return;
        }

        var x = (point.X - _imageRect.X) / _scale;
        var y = (point.Y - _imageRect.Y) / _scale;
        for (var index = _lines.Count - 1; index >= 0; index--)
        {
            var line = _lines[index];
            if (x >= line.Bounds.X &&
                x < line.Bounds.Right &&
                y >= line.Bounds.Y &&
                y < line.Bounds.Bottom)
            {
                SelectedIndex = index;
                LineSelected?.Invoke(this, index);
                e.Handled = true;
                return;
            }
        }
    }
}
