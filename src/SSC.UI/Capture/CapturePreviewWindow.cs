using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Domain.Capture;

namespace SSC.UI.Capture;

public sealed class CapturePreviewWindow : Window
{
    public CapturePreviewWindow(
        ScreenshotResult result,
        CaptureFrame frame,
        ILocalizer localizer,
        IClipboardService clipboard)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(clipboard);

        Title = localizer["capturePreviewTitle"];
        Width = 820;
        Height = 620;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = FindBrush("SscWindowBackground", Color.Parse("#0B0E15"));
        Foreground = FindBrush("SscText", Colors.White);

        var status = new TextBlock
        {
            Text = result.FilePath,
            FontSize = 11,
            Foreground = FindBrush("SscTextMuted", Color.Parse("#A7B1C2")),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var copyButton = new Button { Content = localizer["capturePreviewCopy"], Padding = new Thickness(14, 6) };
        copyButton.Click += async (_, _) =>
        {
            await clipboard.CopyImageAsync(frame);
            status.Text = localizer["capturePreviewCopied"];
        };

        var openButton = new Button { Content = localizer["capturePreviewOpenFolder"], Padding = new Thickness(14, 6) };
        openButton.Click += (_, _) => OpenFolder(result.FilePath);

        var closeButton = new Button { Content = localizer["capturePreviewClose"], Padding = new Thickness(14, 6) };
        closeButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(copyButton);
        buttons.Children.Add(openButton);
        buttons.Children.Add(closeButton);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(16, 10, 16, 14)
        };
        footer.Children.Add(status);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        var image = new Image
        {
            Source = CaptureFrameBitmap.Create(frame),
            Stretch = Stretch.Uniform,
            Margin = new Thickness(16)
        };

        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        layout.Children.Add(image);
        Grid.SetRow(footer, 1);
        layout.Children.Add(footer);

        Content = layout;
    }

    private static IBrush FindBrush(string key, Color fallback)
    {
        if (Avalonia.Application.Current?.TryFindResource(key, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }

    private static void OpenFolder(string filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }
                : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", $"\"{folder}\"")
                {
                    UseShellExecute = false
                };

            Process.Start(startInfo);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
