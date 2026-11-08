using Avalonia;
using Avalonia.Controls;
using SSC.Application.Abstractions;

namespace SSC.UI.Capture;

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

    public void SetCount(int frames, int width, int height)
    {
        CountText.Text = string.Format(_localizer["scrollCount"], frames, width, height);
    }

    public void SetStatus(string message) => StatusText.Text = message;

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
        Editing.WindowCaptureExclusion.Apply(this);
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
