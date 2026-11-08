using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.UI.Capture;

namespace SSC.UI.Editing;

public sealed class PinWindow : Window
{
    private const double PinMaxWidth = 520;
    private const double PinMaxHeight = 420;
    private const double HeaderHeight = 34;

    public PinWindow(CaptureFrame frame, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(localizer);

        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        var scale = Math.Min(1.0, Math.Min(PinMaxWidth / frame.Width, PinMaxHeight / frame.Height));
        var imageWidth = Math.Max(120, frame.Width * scale);
        var imageHeight = Math.Max(80, frame.Height * scale);
        Width = imageWidth;
        Height = imageHeight + HeaderHeight;

        var header = new Border
        {
            Height = HeaderHeight,
            Background = FindBrush("SscPanelAltBg", Color.Parse("#181E2B")),
            Padding = new Thickness(10, 0),
            Cursor = new Cursor(StandardCursorType.SizeAll)
        };

        var title = new TextBlock
        {
            Text = localizer["editorPinnedTitle"],
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = FindBrush("SscTextMuted", Color.Parse("#A7B1C2")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var pinIcon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M9 4h6l-1 6 4 3v2H6v-2l4-3-1-6ZM12 15v5"),
            Stroke = FindBrush("SscTextMuted", Color.Parse("#A7B1C2")),
            StrokeThickness = 1.7,
            Width = 12,
            Height = 12,
            Stretch = Stretch.Uniform,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var close = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = FindBrush("SscTextMuted", Color.Parse("#A7B1C2")),
            FontSize = 16,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        close.Click += (_, _) => Close();

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        headerGrid.Children.Add(pinIcon);
        Grid.SetColumn(title, 1);
        title.Margin = new Thickness(6, 0, 0, 0);
        headerGrid.Children.Add(title);
        Grid.SetColumn(close, 3);
        headerGrid.Children.Add(close);
        header.Child = headerGrid;
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        var image = new Image
        {
            Source = CaptureFrameBitmap.Create(frame),
            Stretch = Stretch.Uniform
        };

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(header);
        Grid.SetRow(image, 1);
        layout.Children.Add(image);

        Content = new Border
        {
            Background = FindBrush("SscWindowBackground", Color.Parse("#0B0E15")),
            BorderBrush = FindBrush("SscBorder", Color.Parse("#22FFFFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = layout
        };

        Opened += (_, _) =>
        {
            PositionNearTopRight();
            WindowCaptureExclusion.Apply(this);
        };
    }

    private void PositionNearTopRight()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        Position = new PixelPoint(
            area.X + area.Width - (int)Math.Round(Width) - 24,
            area.Y + 72);
    }

    private static IBrush FindBrush(string key, Color fallback)
    {
        if (Avalonia.Application.Current?.TryFindResource(key, out var resource) == true &&
            resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}

internal static class WindowCaptureExclusion
{
    private const uint WdaExcludeFromCapture = 0x11;

    public static void Apply(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null)
            {
                return;
            }

            if (OperatingSystem.IsWindows() &&
                string.Equals(handle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
            {
                SetWindowDisplayAffinity(handle.Handle, WdaExcludeFromCapture);
            }
            else if (OperatingSystem.IsMacOS() &&
                     string.Equals(handle.HandleDescriptor, "NSWindow", StringComparison.OrdinalIgnoreCase))
            {
                var selector = sel_registerName("setSharingType:");
                objc_msgSend_IntPtr(handle.Handle, selector, 0);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("/usr/lib/libobjc.A.dylib", CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, nint value);
}
