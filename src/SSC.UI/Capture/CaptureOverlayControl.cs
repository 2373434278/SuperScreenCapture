using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using DomainPixelPoint = SSC.Domain.Geometry.PixelPoint;
using DomainPixelRect = SSC.Domain.Geometry.PixelRect;

namespace SSC.UI.Capture;

/// <summary>
/// 截图覆盖层绘制与交互控件（原型 P1）：冻结画面、选区句柄、HUD、放大镜、
/// 键盘提示、显示器切换与取消/确认按钮都在这里完成。
/// </summary>
public sealed class CaptureOverlayControl : Control
{
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(148, 3, 6, 14));
    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.FromArgb(230, 8, 12, 20));
    private static readonly IPen WhitePen = new Pen(Brushes.White, 1.2);
    private static readonly IPen AccentPen = new Pen(new SolidColorBrush(Color.Parse("#7B7FFF")), 1.4);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
    private static readonly IPen CrosshairPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#17C964"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#7B7FFF"));
    private static readonly IPen PanelBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)), 1);

    private readonly WriteableBitmap _bitmap;
    private readonly Action<CaptureSelection?> _complete;
    private readonly ILocalizer _localizer;
    private readonly CaptureOverlayMode _mode;
    private readonly Func<DomainPixelPoint, Task<WindowDescriptor?>>? _windowResolver;
    private readonly IReadOnlyList<DisplayDescriptor>? _displays;
    private readonly Action<DisplayDescriptor>? _activateDisplay;
    private readonly List<(DisplayDescriptor Display, Rect Rect)> _displayTabRects = new();
    private Rect _confirmRect;
    private Rect _cancelRect;

    private WindowDescriptor? _hoveredWindow;
    private bool _hoverLookupRunning;
    private DateTime _lastHoverLookup = DateTime.MinValue;
    private DomainPixelRect? _selection;
    private DomainPixelPoint _pointer;
    private bool _pointerInside;
    private bool _spaceDown;
    private bool _completed;
    private DragMode _dragMode;
    private string? _handle;
    private DomainPixelPoint _dragStart;
    private DomainPixelRect _dragOriginal;

    public CaptureOverlayControl(
        DisplayDescriptor display,
        CaptureFrame frame,
        ILocalizer localizer,
        Action<CaptureSelection?> complete,
        CaptureOverlayMode mode = CaptureOverlayMode.Region,
        Func<DomainPixelPoint, Task<WindowDescriptor?>>? windowResolver = null,
        IReadOnlyList<DisplayDescriptor>? displays = null,
        Action<DisplayDescriptor>? activateDisplay = null)
    {
        Display = display ?? throw new ArgumentNullException(nameof(display));
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
        _mode = mode;
        _windowResolver = windowResolver;
        _displays = displays;
        _activateDisplay = activateDisplay;
        _bitmap = CaptureFrameBitmap.Create(frame);

        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public DisplayDescriptor Display { get; }

    public CaptureFrame Frame { get; }

    private double ScaleX => Bounds.Width > 0 ? Frame.Width / Bounds.Width : 1;

    private double ScaleY => Bounds.Height > 0 ? Frame.Height / Bounds.Height : 1;

    /// <summary>覆盖层显示后接管键盘焦点。</summary>
    public void FocusOverlay() => Focus();

    /// <summary>整帧绘制：截图 → 选区/提示 → HUD → 顶栏 → 放大镜。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bounds.Width < 1 || Bounds.Height < 1)
        {
            return;
        }

        context.DrawImage(_bitmap, new Rect(Bounds.Size));
        _confirmRect = default;
        _cancelRect = default;

        if (_mode == CaptureOverlayMode.Window)
        {
            DrawWindowSelection(context);
            DrawKeysHint(context, windowMode: true);
            DrawFrozenChip(context);
            DrawDisplayTabs(context);
            return;
        }

        if (_selection is { } selection)
        {
            DrawSelection(context, selection);
        }
        else
        {
            DrawHint(context, _localizer["captureOverlayHint"]);
            DrawCrosshair(context);
        }

        DrawKeysHint(context);
        DrawHud(context);
        DrawFrozenChip(context);
        DrawDisplayTabs(context);

        if (_selection is { } currentSelection)
        {
            DrawSelectionActions(context, currentSelection);
        }

        if (!_selection.HasValue && _pointerInside)
        {
            DrawMagnifier(context);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            Cancel();
            return;
        }

        Focus();
        var position = e.GetPosition(this);

        // 顶栏与选区操作按钮优先命中，避免误开始新的框选。
        if (_confirmRect.Contains(position))
        {
            Complete();
            e.Handled = true;
            return;
        }

        if (_cancelRect.Contains(position))
        {
            Cancel();
            e.Handled = true;
            return;
        }

        foreach (var (display, rect) in _displayTabRects)
        {
            if (rect.Contains(position))
            {
                _activateDisplay?.Invoke(display);
                e.Handled = true;
                return;
            }
        }

        var framePoint = ToFrame(position);

        if (_mode == CaptureOverlayMode.Window)
        {
            _pointer = framePoint;
            _pointerInside = true;
            TryCompleteWindow();
            e.Handled = true;
            return;
        }

        if (_selection is { } selection && HitHandle(position) is { } handle)
        {
            _dragMode = DragMode.Resize;
            _handle = handle;
            _dragStart = framePoint;
            _dragOriginal = selection;
        }
        else if (_selection is { } current && (ToDip(current).Contains(position) || _spaceDown))
        {
            _dragMode = DragMode.Move;
            _handle = null;
            _dragStart = framePoint;
            _dragOriginal = current;
        }
        else
        {
            _dragMode = DragMode.New;
            _handle = null;
            _dragStart = framePoint;
            _selection = new DomainPixelRect(framePoint.X, framePoint.Y, 0, 0);
        }

        e.Pointer.Capture(this);
        _pointer = framePoint;
        _pointerInside = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var position = e.GetPosition(this);
        _pointer = ToFrame(position);
        _pointerInside = true;

        if (_mode == CaptureOverlayMode.Window)
        {
            RequestWindowHover();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_dragMode != DragMode.None)
        {
            UpdateDrag(_pointer);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_mode == CaptureOverlayMode.Window)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_dragMode == DragMode.New && _selection is { } selection && (selection.Width < 8 || selection.Height < 8))
        {
            _selection = null;
        }

        _dragMode = DragMode.None;
        _handle = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerInside = false;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Enter:
                Complete();
                e.Handled = true;
                return;
            case Key.Escape:
                Cancel();
                e.Handled = true;
                return;
            case Key.Space:
                _spaceDown = true;
                e.Handled = true;
                return;
        }

        if (_selection is not { } selection)
        {
            return;
        }

        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        var x = selection.X;
        var y = selection.Y;

        switch (e.Key)
        {
            case Key.Left:
                x -= step;
                break;
            case Key.Right:
                x += step;
                break;
            case Key.Up:
                y -= step;
                break;
            case Key.Down:
                y += step;
                break;
            default:
                return;
        }

        x = Math.Clamp(x, 0, Math.Max(0, Frame.Width - selection.Width));
        y = Math.Clamp(y, 0, Math.Max(0, Frame.Height - selection.Height));
        _selection = selection with { X = x, Y = y };
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
        }
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);
        if (_selection.HasValue)
        {
            Complete();
        }
    }

    /// <summary>根据当前拖拽模式更新选区；越界坐标统一夹取到显示器内。</summary>
    private void UpdateDrag(DomainPixelPoint framePoint)
    {
        switch (_dragMode)
        {
            case DragMode.New:
                _selection = DomainPixelRect.FromPoints(_dragStart, framePoint).ClampTo(new DomainPixelRect(0, 0, Frame.Width, Frame.Height));
                break;
            case DragMode.Move:
                var offsetX = framePoint.X - _dragStart.X;
                var offsetY = framePoint.Y - _dragStart.Y;
                var movedX = Math.Clamp(_dragOriginal.X + offsetX, 0, Math.Max(0, Frame.Width - _dragOriginal.Width));
                var movedY = Math.Clamp(_dragOriginal.Y + offsetY, 0, Math.Max(0, Frame.Height - _dragOriginal.Height));
                _selection = _dragOriginal with { X = movedX, Y = movedY };
                break;
            case DragMode.Resize:
                _selection = Resize(_dragOriginal, _handle, framePoint);
                break;
        }
    }

    private DomainPixelRect Resize(DomainPixelRect original, string? handle, DomainPixelPoint point)
    {
        if (string.IsNullOrEmpty(handle))
        {
            return original;
        }

        var left = original.X;
        var top = original.Y;
        var right = original.X + original.Width;
        var bottom = original.Y + original.Height;

        if (handle.Contains('w', StringComparison.Ordinal))
        {
            left = Math.Clamp(point.X, 0, right - 8);
        }

        if (handle.Contains('e', StringComparison.Ordinal))
        {
            right = Math.Clamp(point.X, left + 8, Frame.Width);
        }

        if (handle.Contains('n', StringComparison.Ordinal))
        {
            top = Math.Clamp(point.Y, 0, bottom - 8);
        }

        if (handle.Contains('s', StringComparison.Ordinal))
        {
            bottom = Math.Clamp(point.Y, top + 8, Frame.Height);
        }

        return new DomainPixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>结束框选并回传选区；窗口模式回传命中的窗口。</summary>
    private void Complete()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        if (_mode == CaptureOverlayMode.Window)
        {
            if (_hoveredWindow is { } window)
            {
                _complete(new CaptureSelection(Display, Frame, DomainPixelRect.Empty, window));
            }
            else
            {
                _complete(null);
            }

            return;
        }

        if (_selection is { } selection && selection.Width > 0 && selection.Height > 0)
        {
            _complete(new CaptureSelection(Display, Frame, selection));
        }
        else
        {
            _complete(null);
        }
    }

    private void Cancel()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _complete(null);
    }

    private DomainPixelPoint ToFrame(Point point) => new(
        Math.Clamp((int)Math.Round(point.X * ScaleX), 0, Frame.Width),
        Math.Clamp((int)Math.Round(point.Y * ScaleY), 0, Frame.Height));

    private Rect ToDip(DomainPixelRect rect) => new(
        rect.X / ScaleX,
        rect.Y / ScaleY,
        rect.Width / ScaleX,
        rect.Height / ScaleY);

    private string? HitHandle(Point point)
    {
        if (_selection is not { } selection)
        {
            return null;
        }

        var rect = ToDip(selection);
        var handles = new (string Name, Point Position)[]
        {
            ("nw", new Point(rect.Left, rect.Top)),
            ("n", new Point(rect.Center.X, rect.Top)),
            ("ne", new Point(rect.Right, rect.Top)),
            ("e", new Point(rect.Right, rect.Center.Y)),
            ("se", new Point(rect.Right, rect.Bottom)),
            ("s", new Point(rect.Center.X, rect.Bottom)),
            ("sw", new Point(rect.Left, rect.Bottom)),
            ("w", new Point(rect.Left, rect.Center.Y))
        };

        foreach (var handle in handles)
        {
            if (Math.Abs(point.X - handle.Position.X) <= 9 && Math.Abs(point.Y - handle.Position.Y) <= 9)
            {
                return handle.Name;
            }
        }

        return null;
    }

    private void DrawSelection(DrawingContext context, DomainPixelRect selection)
    {
        var rect = ToDip(selection);
        DrawDimOutside(context, rect);

        context.DrawRectangle(null, WhitePen, rect);

        foreach (var handle in HandlePoints(rect))
        {
            context.DrawRectangle(Brushes.White, AccentPen, new Rect(handle.X - 4.5, handle.Y - 4.5, 9, 9));
        }
    }

    private void DrawDimOutside(DrawingContext context, Rect rect)
    {
        var full = new Rect(Bounds.Size);

        context.DrawRectangle(DimBrush, null, new Rect(0, 0, full.Width, rect.Top));
        context.DrawRectangle(DimBrush, null, new Rect(0, rect.Bottom, full.Width, Math.Max(0, full.Height - rect.Bottom)));
        context.DrawRectangle(DimBrush, null, new Rect(0, rect.Top, rect.Left, rect.Height));
        context.DrawRectangle(DimBrush, null, new Rect(rect.Right, rect.Top, Math.Max(0, full.Width - rect.Right), rect.Height));
    }

    private void DrawWindowSelection(DrawingContext context)
    {
        if (_hoveredWindow is { } window)
        {
            var local = window.Bounds.Offset(-Display.Bounds.X, -Display.Bounds.Y);
            var clipped = local.ClampTo(new DomainPixelRect(0, 0, Frame.Width, Frame.Height));
            if (!clipped.IsEmpty)
            {
                var rect = ToDip(clipped);
                DrawDimOutside(context, rect);

                var accentBrush = new SolidColorBrush(Color.FromArgb(28, 123, 127, 255));
                var accentPen = new Pen(new SolidColorBrush(Color.Parse("#7B7FFF")), 2);
                context.DrawRectangle(accentBrush, accentPen, rect);

                var label = string.IsNullOrWhiteSpace(window.Title) ? $"Window {window.Id}" : window.Title;
                var title = Format(label, 12, Brushes.White);
                var x = Math.Clamp(rect.X, 8, Math.Max(8, Bounds.Width - title.Width - 24));
                var y = Math.Max(8, rect.Y - title.Height - 18);
                var panel = new Rect(x, y, title.Width + 18, title.Height + 10);
                context.DrawRectangle(
                    PanelBrush,
                    new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1),
                    panel,
                    7,
                    7);
                context.DrawText(title, new Point(x + 9, y + 5));
                return;
            }
        }

        DrawHint(context, _localizer["captureOverlayWindowHint"]);
        DrawCrosshair(context);
    }

    private void DrawHint(DrawingContext context, string hint)
    {
        var text = Format(hint, 13, Brushes.White);
        var rect = new Rect(
            (Bounds.Width - text.Width) / 2 - 14,
            (Bounds.Height / 2) - 20,
            text.Width + 28,
            text.Height + 18);

        context.DrawRectangle(PanelBrush, new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1), rect, 10, 10);
        context.DrawText(text, new Point(rect.X + 14, rect.Y + 9));
    }

    /// <summary>绘制底部键盘提示：每个键位使用独立 kbd 胶囊，对应原型 capture-hint。</summary>
    private void DrawKeysHint(DrawingContext context, bool windowMode = false)
    {
        var items = windowMode
            ? new (string Key, string Label)[]
            {
                ("Click", _localizer["captureHintClick"]),
                ("Esc", _localizer["captureHintCancel"])
            }
            : new (string Key, string Label)[]
            {
                ("Enter", _localizer["captureHintConfirm"]),
                ("Esc", _localizer["captureHintCancel"]),
                ("Space", _localizer["captureHintMove"]),
                ("↑↓←→", _localizer["captureHintNudge"])
            };

        var measured = new List<(FormattedText Key, FormattedText Label)>(items.Length);
        var contentWidth = 0d;
        var maxHeight = 0d;
        foreach (var (key, label) in items)
        {
            var keyText = Format(key, 9.5, Brushes.White, monospace: true);
            var labelText = Format(label, 11, MutedBrush);
            measured.Add((keyText, labelText));
            contentWidth += keyText.Width + 8 + 4 + labelText.Width + 10;
            maxHeight = Math.Max(maxHeight, Math.Max(keyText.Height + 4, labelText.Height));
        }

        var panelWidth = contentWidth + 24;
        var panelHeight = maxHeight + 14;
        var panelX = (Bounds.Width - panelWidth) / 2;
        var panelY = Bounds.Height - 18 - panelHeight;
        var panel = new Rect(panelX, panelY, panelWidth, panelHeight);
        context.DrawRectangle(PanelBrush, PanelBorderPen, panel, 9, 9);

        var x = panelX + 12;
        var centerY = panelY + panelHeight / 2;
        foreach (var (keyText, labelText) in measured)
        {
            var keyRect = new Rect(x, centerY - keyText.Height / 2 - 2, keyText.Width + 8, keyText.Height + 4);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), null, keyRect, 4, 4);
            context.DrawText(keyText, new Point(x + 4, centerY - keyText.Height / 2));
            x = keyRect.Right + 4;
            context.DrawText(labelText, new Point(x, centerY - labelText.Height / 2));
            x += labelText.Width + 10;
        }
    }

    /// <summary>左上角“画面已冻结”提示。</summary>
    private void DrawFrozenChip(DrawingContext context)
    {
        var text = Format(_localizer["captureFrozen"], 11, Brushes.White);
        var rect = new Rect(14, 46, text.Width + 32, 30);
        context.DrawRectangle(PanelBrush, PanelBorderPen, rect, 9, 9);

        var iconCenter = new Point(rect.X + 16, rect.Y + 15);
        context.DrawEllipse(null, new Pen(SuccessBrush, 1.6), iconCenter, 6, 6);
        context.DrawLine(new Pen(SuccessBrush, 1.6), new Point(iconCenter.X - 2.5, iconCenter.Y), new Point(iconCenter.X - 0.5, iconCenter.Y + 2));
        context.DrawLine(new Pen(SuccessBrush, 1.6), new Point(iconCenter.X - 0.5, iconCenter.Y + 2), new Point(iconCenter.X + 3, iconCenter.Y - 2.5));

        context.DrawText(text, new Point(rect.X + 26, rect.Y + (rect.Height - text.Height) / 2));
    }

    /// <summary>右上角显示器切换标签；点击标签可聚焦另一块显示器的覆盖层。</summary>
    private void DrawDisplayTabs(DrawingContext context)
    {
        _displayTabRects.Clear();
        if (_displays is null || _displays.Count == 0)
        {
            return;
        }

        var labels = new List<(DisplayDescriptor Display, FormattedText Text, bool Active)>(_displays.Count);
        var contentWidth = 0d;
        for (var index = 0; index < _displays.Count; index++)
        {
            var display = _displays[index];
            var text = Format(string.Format(_localizer["captureDisplay"], index + 1), 11, Brushes.White, bold: true);
            var active = display.Id == Display.Id;
            labels.Add((display, text, active));
            contentWidth += text.Width + 30;
        }

        var panelWidth = contentWidth + 8 + (labels.Count - 1) * 4;
        var panelX = Bounds.Width - 14 - panelWidth;
        const double panelY = 14;
        var panel = new Rect(panelX, panelY, panelWidth, 34);
        context.DrawRectangle(PanelBrush, PanelBorderPen, panel, 10, 10);

        var x = panelX + 4;
        foreach (var (display, text, active) in labels)
        {
            var tab = new Rect(x, panelY + 4, text.Width + 30, 26);
            if (active)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(41, 255, 255, 255)), null, tab, 7, 7);
            }

            context.DrawEllipse(active ? SuccessBrush : new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)), null,
                new Point(tab.X + 10, tab.Y + 13), 2.5, 2.5);
            context.DrawText(text, new Point(tab.X + 17, tab.Y + (tab.Height - text.Height) / 2));
            _displayTabRects.Add((display, tab));
            x = tab.Right + 4;
        }
    }

    /// <summary>选区右上角的取消 / 确认按钮。</summary>
    private void DrawSelectionActions(DrawingContext context, DomainPixelRect selection)
    {
        var rect = ToDip(selection);
        var cancelText = Format(_localizer["captureCancel"], 11.5, Brushes.White, bold: true);
        var confirmText = Format(_localizer["captureConfirm"], 11.5, Brushes.White, bold: true);
        var cancelWidth = cancelText.Width + 20;
        var confirmWidth = confirmText.Width + 24;
        var totalWidth = cancelWidth + confirmWidth + 8 + 5;
        const double height = 34;

        var x = Math.Clamp(rect.Right - totalWidth - 7, 8, Math.Max(8, Bounds.Width - totalWidth - 8));
        var y = Math.Clamp(rect.Top + 7, 8, Math.Max(8, Bounds.Height - height - 8));

        var container = new Rect(x, y, totalWidth, height);
        context.DrawRectangle(PanelBrush, PanelBorderPen, container, 9, 9);

        var cancelRect = new Rect(x + 4, y + 4, cancelWidth, height - 8);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), null, cancelRect, 7, 7);
        context.DrawText(cancelText, new Point(cancelRect.X + 10, cancelRect.Y + (cancelRect.Height - cancelText.Height) / 2));

        var confirmRect = new Rect(cancelRect.Right + 5, y + 4, confirmWidth, height - 8);
        context.DrawRectangle(AccentBrush, null, confirmRect, 7, 7);
        context.DrawText(confirmText, new Point(confirmRect.X + 12, confirmRect.Y + (confirmRect.Height - confirmText.Height) / 2));

        _cancelRect = cancelRect;
        _confirmRect = confirmRect;
    }

    private void DrawHud(DrawingContext context)
    {
        var local = _selection is { } selection
            ? new DomainPixelPoint(selection.X, selection.Y)
            : _pointer;
        var width = _selection?.Width ?? 0;
        var height = _selection?.Height ?? 0;
        var label = $"X {Display.Bounds.X + local.X}  Y {Display.Bounds.Y + local.Y}   {width} × {height}   {Display.ScaleFactor:0.#}x";
        var text = Format(label, 11, Brushes.White);

        double x;
        double y;

        if (_selection is { } current)
        {
            var dip = ToDip(current);
            x = dip.X;
            y = dip.Y - text.Height - 16;
        }
        else
        {
            var dip = ToDip(new DomainPixelRect(_pointer.X, _pointer.Y, 1, 1));
            x = dip.X + 18;
            y = dip.Y + 18;
        }

        x = Math.Clamp(x, 8, Math.Max(8, Bounds.Width - text.Width - 26));
        y = Math.Clamp(y, 8, Math.Max(8, Bounds.Height - text.Height - 22));

        var rect = new Rect(x, y, text.Width + 16, text.Height + 10);
        context.DrawRectangle(PanelBrush, new Pen(new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), 1), rect, 7, 7);
        context.DrawText(text, new Point(x + 8, y + 5));
    }

    private void DrawCrosshair(DrawingContext context)
    {
        if (!_pointerInside)
        {
            return;
        }

        var dip = ToDip(new DomainPixelRect(_pointer.X, _pointer.Y, 1, 1));
        context.DrawLine(CrosshairPen, new Point(0, dip.Y), new Point(Bounds.Width, dip.Y));
        context.DrawLine(CrosshairPen, new Point(dip.X, 0), new Point(dip.X, Bounds.Height));
    }

    private void DrawMagnifier(DrawingContext context)
    {
        const double sourceSize = 17;
        const double destSize = 136;
        const double panelSize = destSize + 14;

        var pointerDip = ToDip(new DomainPixelRect(_pointer.X, _pointer.Y, 1, 1));
        var panelX = pointerDip.X + 28;
        var panelY = pointerDip.Y + 28;

        if (panelX + panelSize > Bounds.Width - 8)
        {
            panelX = pointerDip.X - panelSize - 28;
        }

        if (panelY + panelSize + 28 > Bounds.Height - 8)
        {
            panelY = pointerDip.Y - panelSize - 28;
        }

        panelX = Math.Clamp(panelX, 8, Math.Max(8, Bounds.Width - panelSize - 8));
        panelY = Math.Clamp(panelY, 8, Math.Max(8, Bounds.Height - panelSize - 8));

        var panel = new Rect(panelX, panelY, panelSize, panelSize + 26);
        context.DrawRectangle(PanelBrush, new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1), panel, 10, 10);

        var sourceX = Math.Clamp(_pointer.X - 8, 0, Math.Max(0, Frame.Width - (int)sourceSize));
        var sourceY = Math.Clamp(_pointer.Y - 8, 0, Math.Max(0, Frame.Height - (int)sourceSize));
        var source = new Rect(sourceX, sourceY, sourceSize, sourceSize);
        var destination = new Rect(panelX + 7, panelY + 7, destSize, destSize);

        using (context.PushClip(new RoundedRect(destination, 7)))
        {
            context.DrawImage(_bitmap, source, destination);

            var cell = destSize / sourceSize;
            for (var index = 0; index <= (int)sourceSize; index++)
            {
                var offset = index * cell;
                context.DrawLine(GridPen, new Point(destination.X + offset, destination.Y), new Point(destination.X + offset, destination.Bottom));
                context.DrawLine(GridPen, new Point(destination.X, destination.Y + offset), new Point(destination.Right, destination.Y + offset));
            }

            context.DrawRectangle(null, WhitePen, new Rect(destination.X + (8 * cell), destination.Y + (8 * cell), cell, cell));
        }

        var color = GetPixelColor(_pointer.X, _pointer.Y);
        context.DrawRectangle(new SolidColorBrush(color), WhitePen, new Rect(panelX + 8, panelY + panelSize + 8, 12, 12));
        context.DrawText(
            Format($"#{color.R:X2}{color.G:X2}{color.B:X2}", 10.5, Brushes.White),
            new Point(panelX + 26, panelY + panelSize + 6));
    }

    private void RequestWindowHover()
    {
        if (_mode != CaptureOverlayMode.Window || _windowResolver is null || _hoverLookupRunning)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastHoverLookup).TotalMilliseconds < 70)
        {
            return;
        }

        _lastHoverLookup = DateTime.UtcNow;
        _ = ResolveWindowHoverAsync();
    }

    private async Task ResolveWindowHoverAsync()
    {
        if (_windowResolver is null)
        {
            return;
        }

        _hoverLookupRunning = true;
        try
        {
            var global = new DomainPixelPoint(
                Display.Bounds.X + _pointer.X,
                Display.Bounds.Y + _pointer.Y);
            var window = await _windowResolver(global).ConfigureAwait(true);
            if (!Equals(window, _hoveredWindow))
            {
                _hoveredWindow = window;
                Dispatcher.UIThread.Post(InvalidateVisual);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _hoverLookupRunning = false;
        }
    }

    private void TryCompleteWindow()
    {
        if (_completed || _mode != CaptureOverlayMode.Window || _hoveredWindow is not { } window)
        {
            return;
        }

        _completed = true;
        _complete(new CaptureSelection(Display, Frame, DomainPixelRect.Empty, window));
    }

    private Color GetPixelColor(int x, int y)
    {
        x = Math.Clamp(x, 0, Frame.Width - 1);
        y = Math.Clamp(y, 0, Frame.Height - 1);
        var offset = (y * Frame.Stride) + (x * 4);
        return Color.FromRgb(Frame.Pixels[offset + 2], Frame.Pixels[offset + 1], Frame.Pixels[offset]);
    }

    private static IEnumerable<Point> HandlePoints(Rect rect)
    {
        yield return new Point(rect.Left, rect.Top);
        yield return new Point(rect.Center.X, rect.Top);
        yield return new Point(rect.Right, rect.Top);
        yield return new Point(rect.Right, rect.Center.Y);
        yield return new Point(rect.Right, rect.Bottom);
        yield return new Point(rect.Center.X, rect.Bottom);
        yield return new Point(rect.Left, rect.Bottom);
        yield return new Point(rect.Left, rect.Center.Y);
    }

    private static FormattedText Format(
        string text,
        double size,
        IBrush brush,
        bool monospace = false,
        bool bold = false) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                monospace ? "SF Mono, Menlo, Consolas" : "SF Pro Text, PingFang SC, Segoe UI, Microsoft YaHei",
                bold ? FontStyle.Normal : FontStyle.Normal,
                bold ? FontWeight.SemiBold : FontWeight.Normal),
            size,
            brush);

    private enum DragMode
    {
        None,
        New,
        Move,
        Resize
    }
}
