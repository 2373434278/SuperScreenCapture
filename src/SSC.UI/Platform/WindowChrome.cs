using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using SSC.UI.Controls;

namespace SSC.UI.Platform;

public enum WindowChromeMode
{
    Native,
    Overlay
}

/// <summary>
/// Applies the hybrid chrome policy: macOS and Windows both extend the client area
/// into a 54px title bar so the app owns the whole top bar. macOS keeps the system
/// decorations (traffic lights), Windows drops the native caption (custom
/// <c>CaptionButtons</c>) while keeping the resize border.
/// </summary>
public static class WindowChrome
{
    public static readonly AttachedProperty<WindowChromeMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<Window, WindowChromeMode>(
            "Mode", typeof(WindowChrome), WindowChromeMode.Overlay);

    public static readonly AttachedProperty<double> TitleBarHeightProperty =
        AvaloniaProperty.RegisterAttached<Window, double>(
            "TitleBarHeight", typeof(WindowChrome), 54d);

    /// <summary>
    /// Marks a caption bar as the window drag surface on Windows: left press moves the
    /// window, double click toggles maximize. Interactive children opt out automatically.
    /// </summary>
    public static readonly AttachedProperty<bool> IsDragAreaProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>(
            "IsDragArea", typeof(WindowChrome));

    static WindowChrome()
    {
        ModeProperty.Changed.AddClassHandler<Window>((window, _) => Apply(window));
        TitleBarHeightProperty.Changed.AddClassHandler<Window>((window, _) => Apply(window));
        IsDragAreaProperty.Changed.AddClassHandler<Border>((border, change) =>
            UpdateDragArea(border, change.GetNewValue<bool>()));
    }

    public static void SetMode(Window window, WindowChromeMode value) =>
        window.SetValue(ModeProperty, value);

    public static WindowChromeMode GetMode(Window window) =>
        window.GetValue(ModeProperty);

    public static void SetTitleBarHeight(Window window, double value) =>
        window.SetValue(TitleBarHeightProperty, value);

    public static double GetTitleBarHeight(Window window) =>
        window.GetValue(TitleBarHeightProperty);

    public static void SetIsDragArea(Border border, bool value) =>
        border.SetValue(IsDragAreaProperty, value);

    public static bool GetIsDragArea(Border border) =>
        border.GetValue(IsDragAreaProperty);

    private static void Apply(Window window)
    {
        if (GetMode(window) != WindowChromeMode.Native)
        {
            return;
        }

        if (PlatformInfo.IsWindows)
        {
            // Avalonia 12 replaced ExtendClientAreaChromeHints with WindowDecorations:
            // BorderOnly removes the native caption/title bar but keeps the resize border
            // and rounded corners, leaving the 54px toolbar as the only top bar.
            window.WindowDecorations = WindowDecorations.BorderOnly;
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaTitleBarHeightHint = GetTitleBarHeight(window);
            return;
        }

        if (PlatformInfo.IsMacOS)
        {
            // Keep system decorations: the traffic lights stay embedded in the 54px bar.
            window.ExtendClientAreaToDecorationsHint = true;
            window.ExtendClientAreaTitleBarHeightHint = GetTitleBarHeight(window);
        }
    }

    private static void UpdateDragArea(Border border, bool enabled)
    {
        border.PointerPressed -= OnDragAreaPointerPressed;
        if (enabled)
        {
            border.PointerPressed += OnDragAreaPointerPressed;
        }
    }

    private static void OnDragAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!PlatformInfo.IsWindows || sender is not Border border)
        {
            return;
        }

        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (ShouldSkipDrag(e.Source, border))
        {
            return;
        }

        if (TopLevel.GetTopLevel(border) is not Window window)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            window.WindowState = CaptionButtons.NextWindowState(window.WindowState);
            return;
        }

        e.Handled = true;
        window.BeginMoveDrag(e);
    }

    /// <summary>
    /// True when the press landed on an interactive control inside the drag area
    /// (buttons, text input, combo boxes, sliders, scroll views); such clicks must not
    /// start a window move.
    /// </summary>
    public static bool ShouldSkipDrag(object? source, Visual? dragArea)
    {
        var visual = source as Visual;
        while (visual is not null && !ReferenceEquals(visual, dragArea))
        {
            if (visual is Button or ToggleButton or TextBox or ComboBox or Slider or ScrollBar or ScrollViewer)
            {
                return true;
            }

            visual = visual.GetVisualParent();
        }

        return false;
    }
}
