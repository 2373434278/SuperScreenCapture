using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Domain.Capture;
using SSC.UI.Motion;
using SSC.UI.Services;
using Path = Avalonia.Controls.Shapes.Path;

namespace SSC.UI.QuickAccess;

public sealed record QuickAccessActions(
    Func<Task> Copy,
    Action Edit,
    Action Pin,
    Action Upload,
    Action Discard);

/// <summary>
/// 截图完成后的快速访问浮窗（参考 CleanShot 的浮动缩略图）：
/// 默认停在当前屏幕右下角，超时自动消失，悬停时暂停计时。
/// </summary>
public sealed class QuickAccessWindow : Window
{
    private readonly ILocalizer _localizer;
    private readonly QuickAccessActions _actions;
    private readonly DispatcherTimer _timer;
    private readonly ProgressBar _progress;
    private readonly TextBlock _title;
    private double _remaining;
    private bool _paused;
    private Bitmap? _preview;

    public QuickAccessWindow(
        ScreenshotResult result,
        CaptureFrame frame,
        ILocalizer localizer,
        int seconds,
        QuickAccessActions actions)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(actions);

        _localizer = localizer;
        _actions = actions;
        _remaining = Math.Clamp(seconds, 2, 15);

        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Width = 320;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;

        var preview = BuildPreview(result, frame);
        _title = new TextBlock
        {
            Text = System.IO.Path.GetFileName(result.FilePath),
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = FindBrush("SscText", Color.Parse("#F2F5FA")),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var meta = new TextBlock
        {
            Text = $"{frame.Width} × {frame.Height}",
            FontSize = 10,
            Foreground = FindBrush("SscTextSubtle", Color.Parse("#68748A"))
        };

        var info = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(12, 9, 12, 8)
        };
        info.Children.Add(_title);
        info.Children.Add(meta);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(10, 0, 10, 10)
        };
        buttons.Children.Add(CreateActionButton("IcoCopy", "quickAccessCopy", async () =>
        {
            await _actions.Copy();
            MarkCompleted("quickAccessCopied");
        }));
        buttons.Children.Add(CreateActionButton("IcoEdit", "quickAccessEdit", () =>
        {
            Close();
            _actions.Edit();
        }));
        buttons.Children.Add(CreateActionButton("IcoPin", "quickAccessPin", () =>
        {
            Close();
            _actions.Pin();
        }));
        buttons.Children.Add(CreateActionButton("IcoUpload", "quickAccessUpload", () =>
        {
            Close();
            _actions.Upload();
        }));
        buttons.Children.Add(CreateActionButton("IcoDelete", "quickAccessDiscard", () =>
        {
            Close();
            _actions.Discard();
        }));

        _progress = new ProgressBar
        {
            Height = 3,
            Minimum = 0,
            Maximum = _remaining,
            Value = _remaining,
            Background = Brushes.Transparent,
            Foreground = FindBrush("SscAccent", Color.Parse("#7B7FFF")),
            Margin = new Thickness(0)
        };

        var close = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = FindBrush("SscTextSubtle", Color.Parse("#68748A")),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0)
        };
        close.Click += (_, _) => Close();

        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = FindBrush("SscPanelBg", Color.Parse("#121722")),
            BorderBrush = FindBrush("SscBorderStrong", Color.Parse("#33FFFFFF")),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto,Auto"),
                Children =
                {
                    preview,
                    info,
                    buttons
                }
            }
        };
        Grid.SetRow(info, 1);
        Grid.SetRow(buttons, 2);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Children = { card, _progress }
        };
        Grid.SetRow(_progress, 1);
        root.Children.Add(close);
        Content = root;

        WindowingMotion.FadeIn(this);
        Opened += (_, _) =>
        {
            WindowCaptureExclusion.Apply(this);
            PositionBottomRight();
        };

        PointerEntered += (_, _) => _paused = true;
        PointerExited += (_, _) => _paused = false;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            if (_paused)
            {
                return;
            }

            _remaining -= 0.1;
            _progress.Value = Math.Max(0, _remaining);
            if (_remaining <= 0)
            {
                Close();
            }
        };
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _preview?.Dispose();
        _preview = null;
        base.OnClosed(e);
    }

    private Control BuildPreview(ScreenshotResult result, CaptureFrame frame)
    {
        try
        {
            var bitmap = new Bitmap(result.FilePath);
            _preview = bitmap;
            return new Border
            {
                Height = 140,
                Background = new SolidColorBrush(Color.Parse("#0B0F16")),
                ClipToBounds = true,
                Child = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
        catch (Exception)
        {
            return new Border
            {
                Height = 90,
                Background = new SolidColorBrush(Color.Parse("#0B0F16")),
                Child = new TextBlock
                {
                    Text = $"{frame.Width} × {frame.Height}",
                    Foreground = new SolidColorBrush(Color.Parse("#68748A")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
    }

    private Button CreateActionButton(string resourceKey, string tooltipKey, Action action)
    {
        var button = new Button
        {
            Width = 46,
            Height = 30,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(8)
        };

        var icon = new Path
        {
            Data = (Geometry?)Avalonia.Application.Current?.FindResource(resourceKey),
            Stroke = FindBrush("SscTextMuted", Color.Parse("#A7B1C2")),
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (icon.Data is null)
        {
            button.Content = tooltipKey switch
            {
                "quickAccessCopy" => "⧉",
                "quickAccessEdit" => "✎",
                "quickAccessPin" => "📌",
                "quickAccessUpload" => "⇧",
                _ => "🗑"
            };
        }
        else
        {
            button.Content = icon;
        }

        ToolTip.SetTip(button, _localizer[tooltipKey]);
        AutomationProperties.SetName(button, _localizer[tooltipKey]);
        button.Click += (_, _) => action();
        return button;
    }

    private void MarkCompleted(string statusKey)
    {
        _title.Text = _localizer[statusKey];
        _timer.Stop();
        var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            Close();
        };
        closeTimer.Start();
    }

    private void PositionBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        Position = new PixelPoint(
            area.X + area.Width - (int)Math.Round(Width) - 20,
            area.Y + area.Height - (int)Math.Round(Height) - 20);
    }

    private static IBrush FindBrush(string key, Color fallback)
    {
        if (Avalonia.Application.Current?.TryFindResource(key, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
