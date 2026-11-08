using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SSC.Application.Editing;
using SSC.Domain.Annotations;
using SSC.Domain.Capture;
using SSC.UI.Capture;
using DomainPixelPoint = SSC.Domain.Geometry.PixelPoint;
using DomainPixelRect = SSC.Domain.Geometry.PixelRect;

namespace SSC.UI.Editing;

/// <summary>文本工具请求：文档坐标与当前显示坐标。</summary>
public sealed record TextInputRequest(DomainPixelPoint DocumentPoint, Point DisplayPoint);

/// <summary>
/// Displays the rendered document and hosts the pointer gestures. Committed annotations are
/// rendered by the Skia renderer; the in-progress draft is drawn with Avalonia primitives.
/// </summary>
/// <summary>
/// 标注画布：负责渲染截图与标注、命中测试、拖拽创建/移动/缩放，
/// 并把文本工具的位置请求交给窗口弹出输入框。
/// </summary>
public sealed class AnnotationEditorCanvas : Control
{
    private readonly CaptureFrame _source;
    private readonly AnnotationEditorSession _session;
    private readonly IAnnotationRenderer _renderer;
    private readonly List<DomainPixelPoint> _path = new();
    private WriteableBitmap? _bitmap;
    private Annotation? _draft;
    private DomainPixelRect? _cropPreview;
    private DomainPixelPoint? _dragStart;
    private bool _dragging;
    private double _scale = 1;
    private Rect _imageRect;
    private int _selectedIndex = -1;

    public AnnotationEditorCanvas(
        CaptureFrame source,
        AnnotationEditorSession session,
        IAnnotationRenderer renderer)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    public event EventHandler<TextInputRequest>? TextInputRequested;

    public int SelectedIndex => _selectedIndex;

    public void Refresh()
    {
        var frame = _renderer.Render(_source, _session.Document);
        var bitmap = CaptureFrameBitmap.Create(frame);
        var previous = _bitmap;
        _bitmap = bitmap;
        previous?.Dispose();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_bitmap is null)
        {
            return;
        }

        var crop = _session.Document.Crop;
        var availableWidth = Math.Max(1, Bounds.Width - 36);
        var availableHeight = Math.Max(1, Bounds.Height - 36);
        _scale = Math.Min(availableWidth / crop.Width, availableHeight / crop.Height);
        var width = crop.Width * _scale;
        var height = crop.Height * _scale;
        _imageRect = new Rect(
            (Bounds.Width - width) / 2,
            (Bounds.Height - height) / 2,
            width,
            height);

        var shadow = new Rect(_imageRect.X, _imageRect.Y + 8, _imageRect.Width, _imageRect.Height);
        context.DrawRectangle(
            new SolidColorBrush(Colors.Black) { Opacity = 0.35 },
            null,
            shadow);
        context.DrawImage(
            _bitmap,
            new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height),
            _imageRect);

        if (_selectedIndex >= 0 && _selectedIndex < _session.Document.Annotations.Count)
        {
            DrawSelection(context, _session.Document.Annotations[_selectedIndex]);
        }

        if (_draft is not null)
        {
            DrawDraft(context, _draft);
        }

        if (_cropPreview is { } cropPreview)
        {
            DrawCropPreview(context, cropPreview);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || !_imageRect.Contains(point.Position))
        {
            return;
        }

        Focus();
        var documentPoint = ToDocumentPoint(point.Position);
        _selectedIndex = -1;
        _draft = null;
        _cropPreview = null;

        switch (_session.ActiveTool)
        {
            case AnnotationToolKind.Select:
                _selectedIndex = _session.Document.FindLastAt(documentPoint);
                InvalidateVisual();
                e.Handled = true;
                return;
            case AnnotationToolKind.Text:
                TextInputRequested?.Invoke(this, new TextInputRequest(documentPoint, point.Position));
                e.Handled = true;
                return;
            case AnnotationToolKind.Step:
                _session.Commit(new AnnotationToolInput(documentPoint, documentPoint));
                e.Handled = true;
                return;
        }

        _dragStart = documentPoint;
        _dragging = true;
        _path.Clear();
        _path.Add(documentPoint);
        UpdateDraft(documentPoint);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _dragStart is null)
        {
            return;
        }

        var documentPoint = ToDocumentPoint(e.GetPosition(this));
        if (_session.IsPathTool)
        {
            var last = _path[^1];
            if (last != documentPoint)
            {
                _path.Add(documentPoint);
            }
        }

        UpdateDraft(documentPoint);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging || _dragStart is null)
        {
            return;
        }

        var documentPoint = ToDocumentPoint(e.GetPosition(this));
        var input = new AnnotationToolInput(
            _dragStart.Value,
            documentPoint,
            _session.IsPathTool ? _path.ToArray() : null);

        _dragging = false;
        _dragStart = null;
        _draft = null;
        _cropPreview = null;
        _path.Clear();
        e.Pointer.Capture(null);

        _session.Commit(input);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        _dragStart = null;
        _draft = null;
        _cropPreview = null;
        _path.Clear();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Delete or Key.Back))
        {
            return;
        }

        if (_selectedIndex < 0)
        {
            return;
        }

        _session.RemoveAt(_selectedIndex);
        _selectedIndex = -1;
        e.Handled = true;
    }

    private void UpdateDraft(DomainPixelPoint end)
    {
        if (_dragStart is null)
        {
            return;
        }

        var input = new AnnotationToolInput(
            _dragStart.Value,
            end,
            _session.IsPathTool ? _path.ToArray() : null);

        if (_session.ActiveTool == AnnotationToolKind.Crop)
        {
            _cropPreview = _session.BuildCrop(input);
            _draft = null;
            return;
        }

        _draft = _session.BuildAnnotation(input);
    }

    private DomainPixelPoint ToDocumentPoint(Point point)
    {
        var crop = _session.Document.Crop;
        var x = crop.X + ((point.X - _imageRect.X) / _scale);
        var y = crop.Y + ((point.Y - _imageRect.Y) / _scale);
        return new DomainPixelPoint(
            Math.Clamp((int)Math.Round(x), crop.X, Math.Max(crop.X, crop.Right - 1)),
            Math.Clamp((int)Math.Round(y), crop.Y, Math.Max(crop.Y, crop.Bottom - 1)));
    }

    private Point ToDisplayPoint(DomainPixelPoint point)
    {
        var crop = _session.Document.Crop;
        return new Point(
            _imageRect.X + ((point.X - crop.X) * _scale),
            _imageRect.Y + ((point.Y - crop.Y) * _scale));
    }

    private Rect ToDisplayRect(DomainPixelRect rect) =>
        new(
            _imageRect.X + ((rect.X - _session.Document.Crop.X) * _scale),
            _imageRect.Y + ((rect.Y - _session.Document.Crop.Y) * _scale),
            rect.Width * _scale,
            rect.Height * _scale);

    private void DrawSelection(DrawingContext context, Annotation annotation)
    {
        var bounds = annotation.Bounds;
        var inflated = new DomainPixelRect(
            bounds.X - 2,
            bounds.Y - 2,
            bounds.Width + 4,
            bounds.Height + 4);
        var pen = new Pen(
            new SolidColorBrush(Colors.White) { Opacity = 0.9 },
            1,
            new DashStyle(new double[] { 5, 4 }, 0));
        context.DrawRectangle(null, pen, ToDisplayRect(inflated));
    }

    private void DrawDraft(DrawingContext context, Annotation annotation)
    {
        var style = annotation.Style;
        var color = ParseColor(style.Color);
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, Math.Max(1, style.StrokeWidth * _scale));
        var bounds = ToDisplayRect(annotation.Bounds);

        switch (annotation.Kind)
        {
            case AnnotationKind.Rectangle:
                context.DrawRectangle(new SolidColorBrush(color) { Opacity = 0.1 }, pen, bounds);
                break;
            case AnnotationKind.Ellipse:
                context.DrawEllipse(
                    new SolidColorBrush(color) { Opacity = 0.1 },
                    pen,
                    bounds.Center,
                    bounds.Width / 2,
                    bounds.Height / 2);
                break;
            case AnnotationKind.Arrow:
                DrawDraftArrow(context, annotation, brush, pen);
                break;
            case AnnotationKind.Pen:
                DrawDraftPath(context, annotation, pen);
                break;
            case AnnotationKind.Highlighter:
                DrawDraftHighlighter(context, annotation, color);
                break;
            case AnnotationKind.Text:
                DrawDraftText(context, annotation, brush);
                break;
            case AnnotationKind.Step:
                DrawDraftStep(context, annotation, brush);
                break;
            case AnnotationKind.Mosaic:
            case AnnotationKind.Blur:
                DrawEffectPreview(context, bounds, brush);
                break;
        }
    }

    private void DrawDraftArrow(DrawingContext context, Annotation annotation, IBrush brush, Pen pen)
    {
        if (annotation.Points.Count < 2)
        {
            return;
        }

        var start = ToDisplayPoint(annotation.Points[0]);
        var end = ToDisplayPoint(annotation.Points[1]);
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var head = Math.Max(12 * _scale, annotation.Style.StrokeWidth * _scale * 3.2);
        var lineEnd = new Point(
            end.X - (Math.Cos(angle) * head * 0.6),
            end.Y - (Math.Sin(angle) * head * 0.6));

        context.DrawLine(pen, start, lineEnd);
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(end, isFilled: true);
            stream.LineTo(new Point(
                end.X - (Math.Cos(angle - 0.42) * head),
                end.Y - (Math.Sin(angle - 0.42) * head)));
            stream.LineTo(new Point(
                end.X - (Math.Cos(angle + 0.42) * head),
                end.Y - (Math.Sin(angle + 0.42) * head)));
            stream.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    private void DrawDraftPath(DrawingContext context, Annotation annotation, Pen pen)
    {
        if (annotation.Points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(ToDisplayPoint(annotation.Points[0]), isFilled: false);
            for (var i = 1; i < annotation.Points.Count; i++)
            {
                stream.LineTo(ToDisplayPoint(annotation.Points[i]));
            }

            stream.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawDraftHighlighter(DrawingContext context, Annotation annotation, Color color)
    {
        if (annotation.Points.Count < 2)
        {
            return;
        }

        var brush = new SolidColorBrush(color) { Opacity = 0.35 };
        var pen = new Pen(
            brush,
            Math.Max(10 * _scale, annotation.Style.StrokeWidth * _scale * 4),
            lineCap: PenLineCap.Round);
        context.DrawLine(
            pen,
            ToDisplayPoint(annotation.Points[0]),
            ToDisplayPoint(annotation.Points[1]));
    }

    private void DrawDraftText(DrawingContext context, Annotation annotation, IBrush brush)
    {
        if (string.IsNullOrWhiteSpace(annotation.Text))
        {
            return;
        }

        var text = new FormattedText(
            annotation.Text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            Math.Max(8, annotation.Style.FontSize * _scale),
            brush);
        context.DrawText(text, ToDisplayPoint(new DomainPixelPoint(annotation.Bounds.X, annotation.Bounds.Y)));
    }

    private void DrawDraftStep(DrawingContext context, Annotation annotation, IBrush brush)
    {
        var center = new Point(
            _imageRect.X + (((annotation.Bounds.X + (annotation.Bounds.Width / 2)) - _session.Document.Crop.X) * _scale),
            _imageRect.Y + (((annotation.Bounds.Y + (annotation.Bounds.Height / 2)) - _session.Document.Crop.Y) * _scale));
        var radius = Math.Max(12, annotation.Style.FontSize * 0.65) * _scale;
        context.DrawEllipse(
            brush,
            new Pen(new SolidColorBrush(Colors.White), 2 * _scale),
            center,
            radius,
            radius);

        var label = new FormattedText(
            annotation.Step.ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            radius * 0.95,
            new SolidColorBrush(Colors.White));
        context.DrawText(
            label,
            new Point(center.X - (label.Width / 2), center.Y - (label.Height / 2)));
    }

    private static void DrawEffectPreview(DrawingContext context, Rect bounds, IBrush brush)
    {
        var pen = new Pen(brush, 1, new DashStyle(new double[] { 5, 4 }, 0));
        context.DrawRectangle(
            new SolidColorBrush(Colors.Black) { Opacity = 0.18 },
            pen,
            bounds);
    }

    private void DrawCropPreview(DrawingContext context, DomainPixelRect crop)
    {
        var target = ToDisplayRect(crop);
        var dim = new SolidColorBrush(Colors.Black) { Opacity = 0.35 };
        context.DrawRectangle(dim, null, new Rect(
            _imageRect.X,
            _imageRect.Y,
            _imageRect.Width,
            Math.Max(0, target.Y - _imageRect.Y)));
        context.DrawRectangle(dim, null, new Rect(
            _imageRect.X,
            target.Bottom,
            _imageRect.Width,
            Math.Max(0, _imageRect.Bottom - target.Bottom)));
        context.DrawRectangle(dim, null, new Rect(
            _imageRect.X,
            target.Y,
            Math.Max(0, target.X - _imageRect.X),
            target.Height));
        context.DrawRectangle(dim, null, new Rect(
            target.Right,
            target.Y,
            Math.Max(0, _imageRect.Right - target.Right),
            target.Height));

        var pen = new Pen(
            new SolidColorBrush(Colors.White),
            1,
            new DashStyle(new double[] { 6, 4 }, 0));
        context.DrawRectangle(null, pen, target);
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_selectedIndex >= _session.Document.Annotations.Count)
        {
            _selectedIndex = -1;
        }

        Refresh();
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return Color.Parse(value);
        }
        catch (FormatException)
        {
            return Colors.Red;
        }
    }
}
