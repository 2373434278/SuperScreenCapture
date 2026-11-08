using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SSC.Application.Abstractions;
using SSC.Domain.Recording;

namespace SSC.UI.Recording;

public partial class RecordingSetupWindow : Window
{
    private readonly ILocalizer _localizer;
    private readonly RecordingOptions _baseOptions;
    private int _maxHeight;
    private int _fps;
    private int _bitrate;
    private bool _microphone;
    private RecordingOptions? _result;
    private bool _closed;

    public RecordingSetupWindow(RecordingOptions options, ILocalizer localizer)
    {
        _baseOptions = options ?? throw new ArgumentNullException(nameof(options));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _maxHeight = options.MaxHeight;
        _fps = options.Fps;
        _bitrate = options.BitrateMbps;
        _microphone = options.IncludeMicrophone;

        InitializeComponent();
        ApplyLocalization();
        WireControls();
        UpdateChoices();
    }

    public Task<RecordingOptions?> ShowAsync()
    {
        var completion = new TaskCompletionSource<RecordingOptions?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => completion.TrySetResult(_result);
        Show();
        return completion.Task;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            CloseWithResult(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            StartRecording();
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _closed = true;
        base.OnClosing(e);
    }

    private void WireControls()
    {
        Header.PointerPressed += (_, e) =>
        {
            if (IsButtonSource(e.Source))
            {
                return;
            }

            if (e.GetCurrentPoint(Header).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        CloseButton.Click += (_, _) => CloseWithResult(null);
        CancelButton.Click += (_, _) => CloseWithResult(null);
        StartButton.Click += (_, _) => StartRecording();
        Resolution1080.Click += (_, _) => SetMaxHeight(1080);
        Resolution1440.Click += (_, _) => SetMaxHeight(1440);
        Resolution2160.Click += (_, _) => SetMaxHeight(2160);
        Fps30.Click += (_, _) => SetFps(30);
        Fps60.Click += (_, _) => SetFps(60);
        Bitrate8.Click += (_, _) => SetBitrate(8);
        Bitrate12.Click += (_, _) => SetBitrate(12);
        Bitrate20.Click += (_, _) => SetBitrate(20);
        MicrophoneButton.Click += (_, _) =>
        {
            _microphone = !_microphone;
            UpdateChoices();
        };
    }

    private void SetMaxHeight(int height)
    {
        _maxHeight = height;
        UpdateChoices();
    }

    private void SetFps(int fps)
    {
        _fps = fps;
        UpdateChoices();
    }

    private void SetBitrate(int bitrate)
    {
        _bitrate = bitrate;
        UpdateChoices();
    }

    private void StartRecording()
    {
        if (_closed)
        {
            return;
        }

        CloseWithResult(_baseOptions with
        {
            MaxHeight = _maxHeight,
            Fps = _fps,
            BitrateMbps = _bitrate,
            IncludeMicrophone = _microphone
        });
    }

    private void CloseWithResult(RecordingOptions? result)
    {
        if (_closed)
        {
            return;
        }

        _result = result;
        Close();
    }

    private void UpdateChoices()
    {
        SetClass(Resolution1080, "active", _maxHeight == 1080);
        SetClass(Resolution1440, "active", _maxHeight == 1440);
        SetClass(Resolution2160, "active", _maxHeight == 2160);
        SetClass(Fps30, "active", _fps == 30);
        SetClass(Fps60, "active", _fps == 60);
        SetClass(Bitrate8, "active", _bitrate == 8);
        SetClass(Bitrate12, "active", _bitrate == 12);
        SetClass(Bitrate20, "active", _bitrate == 20);
        SetClass(MicrophoneButton, "active", _microphone);
        MicrophoneButton.Content = _microphone
            ? _localizer["recordingMicOn"]
            : _localizer["recordingMicOff"];
    }

    private void ApplyLocalization()
    {
        Title = _localizer["appTitle"];
        TitleText.Text = _localizer["recordingSetupTitle"];
        DescriptionText.Text = _localizer["recordingReadyDesc"];
        RegionText.Text = string.Format(
            _localizer["recordingRegion"],
            _baseOptions.Region.Width,
            _baseOptions.Region.Height);
        ResolutionLabel.Text = _localizer["recordingResolution"];
        FpsLabel.Text = _localizer["recordingFps"];
        BitrateLabel.Text = _localizer["recordingBitrate"];
        MicrophoneLabel.Text = _localizer["recordingMicrophone"];
        SystemAudioLabel.Text = _localizer["recordingSystemAudio"];
        SystemAudioValue.Text = _localizer["recordingLater"];
        CancelButton.Content = _localizer["recordingCancel"];
        StartText.Text = _localizer["recordingStart"];
        ToolTip.SetTip(CloseButton, _localizer["recordingCancel"]);
        ToolTip.SetTip(SystemAudioValue, _localizer["recordingSystemAudioTip"]);
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

    private static bool IsButtonSource(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<Button>(includeSelf: true) is not null;
}
