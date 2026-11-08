using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using SSC.Application;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.UI.Ocr;

/// <summary>
/// 文字识别结果窗口（原型 P6）。左侧展示识别框，右侧展示文本行；
/// 点击任一侧都会联动高亮另一侧，语言切换与重新识别位于顶栏。
/// </summary>
public partial class OcrResultWindow : Window
{
    private readonly CaptureFrame _frame;
    private readonly IOcrService _ocr;
    private readonly ILocalizer _localizer;
    private readonly OcrOverlayControl _canvas;
    private readonly Dictionary<string, Button> _languageButtons = new(StringComparer.Ordinal);
    private OcrResult _result;
    private string? _selectedLanguage;
    private bool _busy;

    /// <summary>由调用方传入截图与首次识别结果。</summary>
    public OcrResultWindow(
        CaptureFrame frame,
        OcrResult result,
        IOcrService ocr,
        ILocalizer localizer)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

        InitializeComponent();
        _canvas = new OcrOverlayControl(frame);
        CanvasHost.Width = Math.Max(1, frame.Width);
        CanvasHost.Height = Math.Max(1, frame.Height);
        CanvasHost.Children.Add(_canvas);
        _canvas.LineSelected += (_, index) => LineList.SelectedIndex = index;
        LineList.SelectionChanged += (_, _) => _canvas.SelectedIndex = SelectedRowIndex;

        BuildLanguageSegments();
        ApplyLocalization();
        WireControls();
        ApplyResult(result);
    }

    /// <summary>构建语言分段控件：中文、EN、混合（自动）。</summary>
    private void BuildLanguageSegments()
    {
        var languages = _ocr.Languages.Count > 0
            ? _ocr.Languages
            : new[] { "zh-Hans", "en-US" };
        foreach (var language in languages.Take(2))
        {
            AddLanguageButton(language, LanguageLabel(language));
        }

        AddLanguageButton(null, _localizer["ocrMixed"]);
        _selectedLanguage = languages.Contains(_result.Language) ? _result.Language : languages[0];
        UpdateLanguageButtons();
    }

    private void AddLanguageButton(string? language, string label)
    {
        var button = new Button { Content = label };
        button.Classes.Add("seg");
        button.Click += (_, _) =>
        {
            _selectedLanguage = language;
            UpdateLanguageButtons();
        };
        LanguageSeg.Children.Add(button);
        _languageButtons[language ?? string.Empty] = button;
    }

    private void UpdateLanguageButtons()
    {
        foreach (var (language, button) in _languageButtons)
        {
            SetClass(button, "active", string.Equals(language, _selectedLanguage ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }
    }

    private string LanguageLabel(string language) => language switch
    {
        "zh-Hans" or "zh-CN" or "zh" => _localizer["languageZh"],
        "en-US" or "en" => "EN",
        _ => language
    };

    private void WireControls()
    {
        CloseButton.Click += (_, _) => Close();
        BackButton.Click += (_, _) => Close();
        RecognizeButton.Click += (_, _) => _ = RecognizeAsync();
        CopyAllButton.Click += (_, _) => _ = CopyTextAsync(_result.Text);
        CopySelectedButton.Click += (_, _) =>
        {
            if (LineList.SelectedItem is OcrLineRow row)
            {
                _ = CopyTextAsync(row.Text);
            }
        };
    }

    private void ApplyLocalization()
    {
        Title = _localizer["ocrResultTitle"];
        TitleText.Text = _localizer["ocrTitle"];
        BackText.Text = _localizer["ocrBack"];
        EngineText.Text = OperatingSystem.IsWindows()
            ? _localizer["ocrEngineWindows"]
            : _localizer["ocrEngineVision"];
        RecognizeText.Text = _localizer["ocrRecognize"];
        CopyAllText.Text = _localizer["ocrCopyAll"];
        CopySelectedText.Text = _localizer["ocrCopySelected"];
        LoadingText.Text = _localizer["ocrRecognizing"];
        EmptyTitle.Text = _localizer["ocrNoText"];
        EmptyText.Text = _localizer["ocrEmptyDesc"];
        LocalText.Text = _localizer["ocrLocal"];
        VersionText.Text = $"{_localizer["versionPrefix"]}{AppInfo.Version}";
        StatusText.Text = string.Empty;
    }

    private void ApplyResult(OcrResult result)
    {
        _result = result;
        _canvas.Lines = result.Lines;
        _canvas.SelectedIndex = -1;
        LineList.ItemsSource = result.Lines
            .Select((line, index) => new OcrLineRow(index, line))
            .ToArray();
        EmptyState.IsVisible = result.Lines.Count == 0;
        StatsText.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            _localizer["ocrStats"],
            result.Lines.Count,
            result.Text.Length);
        StatusText.Text = string.Empty;
    }

    private int SelectedRowIndex => LineList.SelectedItem is OcrLineRow row ? row.Index : -1;

    private async Task RecognizeAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        LoadingOverlay.IsVisible = true;
        RecognizeButton.IsEnabled = false;
        try
        {
            var result = await _ocr.RecognizeAsync(_frame, _selectedLanguage);
            if (result.Language is not null)
            {
                _selectedLanguage = result.Language;
                UpdateLanguageButtons();
            }

            ApplyResult(result);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"{_localizer["ocrFailed"]}: {exception.Message}";
        }
        finally
        {
            _busy = false;
            LoadingOverlay.IsVisible = false;
            RecognizeButton.IsEnabled = true;
        }
    }

    private async void OnCopyLine(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: OcrLineRow row })
        {
            await CopyTextAsync(row.Text);
        }
    }

    private async Task CopyTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clipboard = Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
        StatusText.Text = _localizer["ocrCopied"];
    }

    protected override void OnClosed(EventArgs e)
    {
        _canvas.Dispose();
        base.OnClosed(e);
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
}
