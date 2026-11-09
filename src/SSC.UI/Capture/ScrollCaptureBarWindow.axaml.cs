using Avalonia;
using Avalonia.Controls;
using SSC.Application.Abstractions;

namespace SSC.UI.Capture;

/// <summary>
/// 滚动长截图悬浮控制条：显示已拼接屏数，并提供“捕获下一屏 / 完成 / 取消”操作。
/// </summary>
public partial class ScrollCaptureBarWindow : Window
{
    private readonly ILocalizer _localizer;
    private bool _allowClose;

    public ScrollCaptureBarWindow(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        InitializeComponent();
        ApplyLocalization();
        NextButton.Click += (_, _) => _ = (CaptureNextRequested?.Invoke() ?? Task.CompletedTask);
        FinishButton.Click += (_, _) => _ = (FinishRequested?.Invoke() ?? Task.CompletedTask);
        CancelButton.Click += (_, _) => _ = (CancelRequested?.Invoke() ?? Task.CompletedTask);
    }

    public Func<Task>? CaptureNextRequested { get; set; }

    public Func<Task>? FinishRequested { get; set; }

    public Func<Task>? CancelRequested { get; set; }

    /// <summary>更新屏数与拼接后的画布尺寸。</summary>
    public void SetCount(int frames, int width, int height)
    {
        CountText.Text = string.Format(_localizer["scrollCount"], frames, width, height);
    }

    /// <summary>显示拼接过程中的提示或错误。</summary>
    public void SetStatus(string message) => StatusText.Text = message;

    /// <summary>拼接进行中时禁用三个操作按钮，避免并发。</summary>
    public void SetBusy(bool busy)
    {
        NextButton.IsEnabled = !busy;
        FinishButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
    }

    public void CloseFromCoordinator()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose && CancelRequested is not null)
        {
            e.Cancel = true;
            _ = CancelRequested();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PositionTopCenter();
        Services.WindowCaptureExclusion.Apply(this);
    }

    private void ApplyLocalization()
    {
        Title = _localizer["scrollTitle"];
        TitleText.Text = _localizer["scrollTitle"];
        HintText.Text = _localizer["scrollHint"];
        NextButton.Content = _localizer["scrollNext"];
        FinishButton.Content = _localizer["scrollFinish"];
        CancelButton.Content = _localizer["scrollCancel"];
    }

    private void PositionTopCenter()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        Position = new PixelPoint(
            area.X + ((area.Width - (int)Math.Round(Width)) / 2),
            area.Y + 24);
    }
}
