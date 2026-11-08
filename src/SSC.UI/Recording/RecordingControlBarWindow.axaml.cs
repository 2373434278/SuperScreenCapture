using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SSC.Application.Abstractions;
using SSC.Application.Recording;
using SSC.Domain.Recording;

namespace SSC.UI.Recording;

public partial class RecordingControlBarWindow : Window
{
    private readonly RecordingSessionManager _session;
    private readonly ILocalizer _localizer;
    private readonly DispatcherTimer _timer;
    private readonly Border[] _levels;
    private ControlPhase _phase = ControlPhase.Countdown;
    private bool _microphoneEnabled;
    private bool _failureReported;
    private bool _allowClose;

    public RecordingControlBarWindow(RecordingSessionManager session, ILocalizer localizer)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _microphoneEnabled = session.CurrentOptions?.IncludeMicrophone ?? false;

        InitializeComponent();
        _levels = new[] { Level1, Level2, Level3, Level4, Level5 };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTimerTick;

        WireControls();
        ApplyLocalization();
        BuildSettingsFlyout();
        UpdateMicrophoneButton();
        ShowCountdown(3);
    }

    public Func<Task>? StopRequested { get; set; }

    public Func<string?, Task>? FailureDetected { get; set; }

    public void ShowCountdown(int seconds)
    {
        _phase = ControlPhase.Countdown;
        TimeText.Text = seconds.ToString();
        PhaseText.Text = _localizer["recordingSetupTitle"];
        PauseButton.IsEnabled = false;
        GearButton.IsEnabled = false;
        MicButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        SetLevel(0);
    }

    public void BeginRecording()
    {
        _phase = ControlPhase.Recording;
        PauseButton.IsEnabled = true;
        GearButton.IsEnabled = true;
        UpdateMicrophoneButton();
        UpdatePauseButton();
        _timer.Start();
        UpdatePhase(RecordingState.Recording, TimeSpan.Zero, 0);
    }

    public void CloseFromCoordinator()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PositionTopCenter();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose && StopRequested is not null)
        {
            e.Cancel = true;
            _ = StopAsync();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        base.OnClosed(e);
    }

    private void WireControls()
    {
        DragHandle.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(DragHandle).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        StopButton.Click += (_, _) => _ = StopAsync();
        PauseButton.Click += (_, _) => _ = TogglePauseAsync();
        MicButton.Click += (_, _) => _ = ToggleMicrophoneAsync();
    }

    private async Task StopAsync()
    {
        if (_phase == ControlPhase.Stopping)
        {
            return;
        }

        _phase = ControlPhase.Stopping;
        _timer.Stop();
        StopButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        MicButton.IsEnabled = false;
        GearButton.IsEnabled = false;

        if (StopRequested is not null)
        {
            await StopRequested();
        }
    }

    private async Task TogglePauseAsync()
    {
        if (_session.State == RecordingState.Recording)
        {
            if (await _session.PauseAsync())
            {
                UpdatePauseButton();
            }
        }
        else if (_session.State == RecordingState.Paused)
        {
            if (await _session.ResumeAsync())
            {
                UpdatePauseButton();
            }
        }
    }

    private async Task ToggleMicrophoneAsync()
    {
        if (!_session.MicrophoneAvailable)
        {
            return;
        }

        _microphoneEnabled = !_microphoneEnabled;
        if (await _session.SetMicrophoneAsync(_microphoneEnabled))
        {
            UpdateMicrophoneButton();
            if (!_microphoneEnabled)
            {
                SetLevel(0);
            }
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_phase is ControlPhase.Countdown or ControlPhase.Stopping)
        {
            return;
        }

        var status = _session.GetStatus();
        if (status.State == RecordingState.Failed && !_failureReported)
        {
            _failureReported = true;
            _phase = ControlPhase.Stopping;
            _timer.Stop();
            UpdatePhase(status.State, status.Elapsed, 0);
            if (FailureDetected is not null)
            {
                await FailureDetected(status.Error);
            }
            return;
        }

        UpdatePhase(status.State, status.Elapsed, status.MicrophoneLevel);
        UpdatePauseButton();
    }

    private void UpdatePhase(RecordingState state, TimeSpan elapsed, double level)
    {
        TimeText.Text = FormatTime(elapsed);
        PhaseText.Text = state switch
        {
            RecordingState.Paused => _localizer["recordingPaused"],
            RecordingState.Finalizing => _localizer["recordingSaved"],
            RecordingState.Failed => _localizer["recordingFailureTitle"],
            _ => _localizer["recordingRecording"]
        };

        RecordDot.Background = state switch
        {
            RecordingState.Paused => FindBrush("SscTextMuted", Color.Parse("#A7B1C2")),
            RecordingState.Failed => FindBrush("SscDanger", Color.Parse("#FF6B78")),
            RecordingState.Finalizing => FindBrush("SscAccent", Color.Parse("#7B7FFF")),
            _ => FindBrush("SscDanger", Color.Parse("#FF6B78"))
        };

        SetLevel(level);
    }

    private void UpdatePauseButton()
    {
        var paused = _session.State == RecordingState.Paused;
        PauseIcon.Data = Geometry.Parse(paused ? "m8 5 11 7-11 7z" : "M9 5v14M15 5v14");
        ToolTip.SetTip(PauseButton, _localizer[paused ? "recordingResume" : "recordingPause"]);
    }

    private void UpdateMicrophoneButton()
    {
        var available = _session.MicrophoneAvailable;
        MicButton.IsEnabled = available && _phase != ControlPhase.Countdown;
        SetClass(MicButton, "active", available && _microphoneEnabled);
        _microphoneEnabled = available && _microphoneEnabled;
        ToolTip.SetTip(
            MicButton,
            _localizer[_microphoneEnabled ? "recordingMicOn" : "recordingMicOff"]);
    }

    private void SetLevel(double level)
    {
        var normalized = Math.Clamp(level, 0, 1);
        for (var index = 0; index < _levels.Length; index++)
        {
            _levels[index].Opacity = normalized >= (index + 1) * 0.16 ? 1 : 0.18;
        }
    }

    private void BuildSettingsFlyout()
    {
        var options = _session.CurrentOptions;
        var panel = new StackPanel { Spacing = 7, Margin = new Thickness(12) };
        AddRow(panel, _localizer["recordingResolution"], $"{options?.OutputSize.Height ?? 0}p");
        AddRow(panel, _localizer["recordingFps"], $"{options?.Fps ?? 0}");
        AddRow(panel, _localizer["recordingBitrate"], $"{options?.BitrateMbps ?? 0}M");
        AddRow(panel, _localizer["recordingSystemAudio"], _localizer["recordingLater"]);
        panel.Children.Add(new TextBlock
        {
            Text = _localizer["recordingSettingsLocked"],
            FontSize = 10.5,
            Foreground = FindBrush("SscTextMuted", Color.Parse("#A7B1C2")),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 190,
            Margin = new Thickness(0, 4, 0, 0)
        });

        GearButton.Flyout = new Flyout
        {
            Content = new Border
            {
                Background = FindBrush("SscPanelBg", Color.Parse("#121722")),
                BorderBrush = FindBrush("SscBorder", Color.Parse("#22FFFFFF")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = panel
            }
        };
    }

    private void AddRow(Panel panel, string label, string value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            Foreground = FindBrush("SscTextMuted", Color.Parse("#A7B1C2"))
        });
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 11.5,
            Foreground = FindBrush("SscText", Colors.White)
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        panel.Children.Add(row);
    }

    private void ApplyLocalization()
    {
        Title = _localizer["appTitle"];
        ToolTip.SetTip(StopButton, _localizer["recordingStop"]);
        ToolTip.SetTip(SystemAudioButton, _localizer["recordingSystemAudioTip"]);
        ToolTip.SetTip(CameraButton, _localizer["recordingCamera"]);
        ToolTip.SetTip(GearButton, _localizer["recordingSettings"]);
        ToolTip.SetTip(PauseButton, _localizer["recordingPause"]);
        ToolTip.SetTip(MicButton, _localizer["recordingMicOff"]);
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

    private static string FormatTime(TimeSpan elapsed)
    {
        var total = Math.Max(0, (int)elapsed.TotalSeconds);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }

    private static void SetClass(Control control, string name, bool enabled)
    {
        if (enabled)
        {
            if (!control.Classes.Contains(name))
            {
                control.Classes.Add(name);
            }
        }
        else
        {
            control.Classes.Remove(name);
        }
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

    private enum ControlPhase
    {
        Countdown,
        Recording,
        Stopping
    }
}
