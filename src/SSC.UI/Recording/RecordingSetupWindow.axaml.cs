using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SSC.Application.Abstractions;
using SSC.Domain.Recording;

namespace SSC.UI.Recording;

/// <summary>
/// 录制准备卡片（原型 P3 的 rec-card）。分辨率、帧率、码率和麦克风均以胶囊形式展示，
/// 点击胶囊循环切换，保持与原型一致的极简视觉。
/// </summary>
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
        CloseButton.Click += (_, _) => CloseWithResult(null);
        StartButton.Click += (_, _) => StartRecording();
        Resolution1440.Click += (_, _) => CycleResolution();
        Fps60.Click += (_, _) => CycleFps();
        Bitrate12.Click += (_, _) => CycleBitrate();
        MicrophoneButton.Click += (_, _) =>
        {
            _microphone = !_microphone;
            UpdateChoices();
        };
    }

    /// <summary>1080p → 1440p → 4K → 1080p。</summary>
    private void CycleResolution()
    {
        _maxHeight = _maxHeight switch
        {
            1080 => 1440,
            1440 => 2160,
            _ => 1080
        };
        UpdateChoices();
    }

    /// <summary>30 与 60 帧之间切换。</summary>
    private void CycleFps()
    {
        _fps = _fps == 30 ? 60 : 30;
        UpdateChoices();
    }

    /// <summary>8 / 12 / 20 Mbps 之间循环。</summary>
    private void CycleBitrate()
    {
        _bitrate = _bitrate switch
        {
            8 => 12,
            12 => 20,
            _ => 8
        };
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
        Resolution1440.Content = _maxHeight == 2160 ? "4K" : $"{_maxHeight}p";
        Fps60.Content = $"{_fps} fps";
        Bitrate12.Content = $"{_bitrate} Mbps";
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
        StartText.Text = _localizer["recordingStart"];
        ToolTip.SetTip(CloseButton, _localizer["recordingCancel"]);
        ToolTip.SetTip(Resolution1440, _localizer["recordingResolution"]);
        ToolTip.SetTip(Fps60, _localizer["recordingFps"]);
        ToolTip.SetTip(Bitrate12, _localizer["recordingBitrate"]);
        ToolTip.SetTip(MicrophoneButton, _localizer["recordingMicrophone"]);
    }
}
