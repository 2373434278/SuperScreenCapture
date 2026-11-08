using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SSC.Application.Abstractions;
using SSC.Application.Recording;
using SSC.Domain.Recording;

namespace SSC.UI.Recording;

public partial class RecordingSummaryWindow : Window
{
    private readonly RecordingStopResult _result;
    private readonly ILocalizer _localizer;
    private readonly Func<Task>? _recordAgain;

    public RecordingSummaryWindow(
        RecordingStopResult result,
        RecordingOptions? options,
        ILocalizer localizer,
        Func<Task>? recordAgain = null)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _recordAgain = recordAgain;

        InitializeComponent();
        ApplyLocalization(options);
        WireControls();
    }

    private void WireControls()
    {
        Header.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual visual &&
                visual.FindAncestorOfType<Button>(includeSelf: true) is not null)
            {
                return;
            }

            if (e.GetCurrentPoint(Header).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        CloseButton.Click += (_, _) => Close();
        DoneButton.Click += (_, _) => Close();
        PlayButton.Click += (_, _) => OpenPath(_result.Entry?.FilePath, reveal: false);
        OpenFolderButton.Click += (_, _) => OpenPath(_result.Entry?.FilePath, reveal: true);
        AgainButton.Click += (_, _) =>
        {
            Close();
            if (_recordAgain is not null)
            {
                _ = _recordAgain();
            }
        };
    }

    private void ApplyLocalization(RecordingOptions? options)
    {
        Title = _localizer["appTitle"];
        HeaderTitle.Text = _localizer["recordingSavedTitle"];
        SavedTitle.Text = _localizer["recordingSavedTitle"];
        var duration = _result.Duration;
        SummaryText.Text = string.Format(
            _localizer["recordingSavedSummary"],
            FormatTime(duration),
            _result.Entry is null ? "-" : $"{_result.Entry.Width}×{_result.Entry.Height}",
            options?.Fps ?? 0);
        FilePathText.Text = _result.Entry?.FilePath ?? string.Empty;
        PlayButton.Content = _localizer["recordingPlay"];
        OpenFolderButton.Content = _localizer["recordingOpenFolder"];
        AgainButton.Content = _localizer["recordingAgain"];
        DoneButton.Content = _localizer["recordingClose"];
        ToolTip.SetTip(CloseButton, _localizer["recordingClose"]);
    }

    private static string FormatTime(TimeSpan elapsed)
    {
        var total = Math.Max(0, (int)elapsed.TotalSeconds);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }

    private static void OpenPath(string? path, bool reveal)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            if (reveal)
            {
                var folder = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(folder))
                {
                    return;
                }

                var startInfo = OperatingSystem.IsWindows()
                    ? new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }
                    : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", $"\"{folder}\"")
                    {
                        UseShellExecute = false
                    };
                Process.Start(startInfo);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (InvalidOperationException)
        {
        }
    }
}
