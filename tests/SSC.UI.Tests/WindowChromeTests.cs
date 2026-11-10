using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using SSC.Platform.Abstractions;
using SSC.UI.Platform;
using Xunit;

namespace SSC.UI.Tests;

public sealed class WindowChromeTests
{
    [AvaloniaFact]
    public void NativeMode_OnWindows_DropsNativeTitleBarAndExtendsClientArea()
    {
        var previous = PlatformInfo.Current;
        var window = new Window();
        try
        {
            PlatformInfo.Current = PlatformKind.Windows;

            WindowChrome.SetMode(window, WindowChromeMode.Native);
            WindowChrome.SetTitleBarHeight(window, 54);

            Assert.True(window.ExtendClientAreaToDecorationsHint);
            Assert.Equal(54d, window.ExtendClientAreaTitleBarHeightHint);

            // Avalonia 12 replaced ExtendClientAreaChromeHints with WindowDecorations:
            // BorderOnly is the NoChrome equivalent - no caption/title bar, resize border kept.
            Assert.Equal(WindowDecorations.BorderOnly, window.WindowDecorations);
        }
        finally
        {
            PlatformInfo.Current = previous;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NativeMode_OnMacOS_KeepsSystemChromeForTrafficLights()
    {
        var previous = PlatformInfo.Current;
        var window = new Window();
        try
        {
            PlatformInfo.Current = PlatformKind.MacOS;

            WindowChrome.SetMode(window, WindowChromeMode.Native);
            WindowChrome.SetTitleBarHeight(window, 54);

            Assert.True(window.ExtendClientAreaToDecorationsHint);
            Assert.Equal(54d, window.ExtendClientAreaTitleBarHeightHint);
            Assert.NotEqual(WindowDecorations.BorderOnly, window.WindowDecorations);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
        }
        finally
        {
            PlatformInfo.Current = previous;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OverlayMode_DoesNotExtendClientArea()
    {
        var previous = PlatformInfo.Current;
        var window = new Window();
        try
        {
            PlatformInfo.Current = PlatformKind.Windows;

            // Changing the title bar height re-runs Apply while Mode stays Overlay.
            WindowChrome.SetTitleBarHeight(window, 64);

            Assert.False(window.ExtendClientAreaToDecorationsHint);
        }
        finally
        {
            PlatformInfo.Current = previous;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NonMacNonWindowsPlatform_DoesNotExtendClientArea()
    {
        var previous = PlatformInfo.Current;
        var window = new Window();
        try
        {
            PlatformInfo.Current = PlatformKind.Mock;

            WindowChrome.SetMode(window, WindowChromeMode.Native);

            Assert.False(window.ExtendClientAreaToDecorationsHint);
        }
        finally
        {
            PlatformInfo.Current = previous;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IsDragArea_AttachesAndDetachesPointerHandler()
    {
        var border = new Border();
        Assert.False(WindowChrome.GetIsDragArea(border));

        WindowChrome.SetIsDragArea(border, true);
        Assert.True(WindowChrome.GetIsDragArea(border));

        WindowChrome.SetIsDragArea(border, false);
        Assert.False(WindowChrome.GetIsDragArea(border));
    }

    [AvaloniaFact]
    public void ShouldSkipDrag_IgnoresClicksOnInteractiveControls()
    {
        var dragArea = new Border();
        var inner = new Border();
        var button = new Button();
        inner.Child = button;
        dragArea.Child = inner;

        Assert.True(WindowChrome.ShouldSkipDrag(button, dragArea));
        Assert.False(WindowChrome.ShouldSkipDrag(dragArea, dragArea));

        var text = new TextBlock { Text = "caption" };
        var plainHost = new Border { Child = text };
        Assert.False(WindowChrome.ShouldSkipDrag(text, plainHost));

        var toggle = new ToggleButton();
        var toggleHost = new Border { Child = toggle };
        Assert.True(WindowChrome.ShouldSkipDrag(toggle, toggleHost));
    }

    [AvaloniaFact]
    public void ShouldSkipDrag_CoversEveryInteractiveControlType()
    {
        // Every control type named in the drag-area brief must opt out of BeginMoveDrag.
        AssertSkipDrag(new TextBox());
        AssertSkipDrag(new ComboBox());
        AssertSkipDrag(new Slider());
        AssertSkipDrag(new ScrollBar());
        AssertSkipDrag(new ScrollViewer());
    }

    [AvaloniaFact]
    public void ShouldSkipDrag_ResolvesNestedChildToItsInteractiveAncestor()
    {
        var text = new TextBlock { Text = "caption button" };
        var dragArea = new Border { Child = new Button { Content = text } };
        var window = new Window { Content = dragArea };
        window.Show();
        try
        {
            // Press lands on the button's label, not the Button itself.
            Assert.True(WindowChrome.ShouldSkipDrag(text, dragArea));
            Assert.False(WindowChrome.ShouldSkipDrag(dragArea, dragArea));
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertSkipDrag(Control interactive)
    {
        var host = new Border { Child = interactive };
        Assert.True(WindowChrome.ShouldSkipDrag(interactive, host));
    }
}
