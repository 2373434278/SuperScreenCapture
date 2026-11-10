using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SSC.Platform.Abstractions;
using SSC.UI.Controls;
using SSC.UI.Dialogs;
using SSC.UI.Platform;
using Xunit;

namespace SSC.UI.Tests;

public sealed class PlatformBehaviorTests
{
    [Fact]
    public void ShortcutHints_UsePlatformModifiers()
    {
        var previous = PlatformInfo.Current;
        try
        {
            PlatformInfo.Current = PlatformKind.MacOS;
            Assert.Equal("⌘C", ShortcutHints.Copy);
            Assert.Equal("⌘⇧Z", ShortcutHints.Redo);
            Assert.Equal("⌘,", ShortcutHints.Settings);

            PlatformInfo.Current = PlatformKind.Windows;
            Assert.Equal("Ctrl+C", ShortcutHints.Copy);
            Assert.Equal("Ctrl+Shift+Z", ShortcutHints.Redo);
            Assert.Equal("Ctrl+,", ShortcutHints.Settings);
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [AvaloniaFact]
    public void HotkeyRecorder_DisplayFollowsInjectedPlatform()
    {
        var previous = PlatformInfo.Current;
        try
        {
            // Construct once, then flip the platform afterwards: the display must
            // track PlatformInfo.Current at key-down time, not a cached OS value.
            var recorder = new HotkeyRecorderBox();
            recorder.RaiseEvent(new FocusChangedEventArgs(InputElement.GotFocusEvent));

            PlatformInfo.Current = PlatformKind.Windows;
            recorder.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.C,
                KeyModifiers = KeyModifiers.Control,
            });
            Assert.Equal("Ctrl+C", recorder.Text);

            PlatformInfo.Current = PlatformKind.MacOS;
            recorder.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.C,
                KeyModifiers = KeyModifiers.Meta,
            });
            Assert.Equal("⌘C", recorder.Text);
        }
        finally
        {
            PlatformInfo.Current = previous;
        }
    }

    [Fact]
    public void SheetPosition_CentersOnOwnerWidthAndScales()
    {
        var centered = DialogPresenter.ComputeSheetPosition(
            new PixelPoint(100, 200), ownerWidthDips: 1000, dialogWidthDips: 600, renderScaling: 1);
        Assert.Equal(new PixelPoint(300, 200), centered);

        var retina = DialogPresenter.ComputeSheetPosition(
            new PixelPoint(100, 200), ownerWidthDips: 1000, dialogWidthDips: 600, renderScaling: 2);
        Assert.Equal(new PixelPoint(500, 200), retina);
    }

    [Fact]
    public void SheetPosition_FallsBackWhenOwnerIsNotLaidOut()
    {
        var position = new PixelPoint(24, 48);
        Assert.Equal(position, DialogPresenter.ComputeSheetPosition(position, 0, 580, 1));
    }
}
