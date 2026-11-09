using Avalonia;
using Avalonia.Controls;

namespace SSC.UI.Platform;

public enum WindowChromeMode
{
    Native,
    Overlay
}

/// <summary>
/// Applies the hybrid chrome policy: macOS extends the client area into a 54px
/// title bar so traffic lights sit inside the toolbar; Windows keeps the native
/// title bar and caption buttons (Snap Layouts).
/// </summary>
public static class WindowChrome
{
    public static readonly AttachedProperty<WindowChromeMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<Window, WindowChromeMode>(
            "Mode", typeof(WindowChrome), WindowChromeMode.Overlay);

    public static readonly AttachedProperty<double> TitleBarHeightProperty =
        AvaloniaProperty.RegisterAttached<Window, double>(
            "TitleBarHeight", typeof(WindowChrome), 54d);

    static WindowChrome()
    {
        ModeProperty.Changed.AddClassHandler<Window>((window, _) => Apply(window));
        TitleBarHeightProperty.Changed.AddClassHandler<Window>((window, _) => Apply(window));
    }

    public static void SetMode(Window window, WindowChromeMode value) =>
        window.SetValue(ModeProperty, value);

    public static WindowChromeMode GetMode(Window window) =>
        window.GetValue(ModeProperty);

    public static void SetTitleBarHeight(Window window, double value) =>
        window.SetValue(TitleBarHeightProperty, value);

    public static double GetTitleBarHeight(Window window) =>
        window.GetValue(TitleBarHeightProperty);

    private static void Apply(Window window)
    {
        if (GetMode(window) != WindowChromeMode.Native || !PlatformInfo.IsMacOS)
        {
            return;
        }

        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = GetTitleBarHeight(window);
    }
}
