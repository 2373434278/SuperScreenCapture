using Avalonia.Controls;
using Avalonia.Input.Platform;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.UI.Ocr;

public partial class OcrResultWindow : Window
{
    private readonly CaptureFrame _frame;
    private readonly IOcrService _ocr;
    private readonly ILocalizer _localizer;
    private readonly OcrOverlayControl _canvas;
    private OcrResult _result;
    private bool _busy;

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
        CanvasHost.Children.Add(_canvas);
        _canvas.LineSelected += (_, index) => LineList.SelectedIndex = index;
        LineList.SelectionChanged += (_, _) => _canvas.SelectedIndex = LineList.SelectedIndex;

        var languages = ocr.Languages.Count > 0
            ? ocr.Languages
            : new[] { "zh-Hans", "en-US" };
        LanguageBox.ItemsSource = languages;
        LanguageBox.SelectedItem = languages.Contains(result.Language) ? result.Language : languages[0];

        ApplyLocalization();
        WireControls();
        ApplyResult(result);
    }

    private void WireControls()
    {
        CloseButton.Click += (_, _) => Close();
        RecognizeButton.Click += (_, _) => _ = RecognizeAsync();
        CopyAllButton.Click += (_, _) => _ = CopyTextAsync(_result.Text);
        CopySelectedButton.Click += (_, _) =>
        {
            if (LineList.SelectedItem is OcrLine line)
            {
                _ = CopyTextAsync(line.Text);
            }
        };
    }

    private void ApplyLocalization()
    {
        Title = _localizer["ocrResultTitle"];
        TitleText.Text = _localizer["ocrResultTitle"];
        LanguageLabel.Text = _localizer["ocrLanguage"];
        RecognizeButton.Content = _localizer["ocrRecognize"];
        CopyAllButton.Content = _localizer["ocrCopyAll"];
        CopySelectedButton.Content = _localizer["ocrCopySelected"];
        CloseButton.Content = _localizer["ocrClose"];
        LoadingText.Text = _localizer["ocrRecognizing"];
        EmptyText.Text = _localizer["ocrNoText"];
        LinesTitle.Text = _localizer["ocrLine"];
        StatusText.Text = string.Empty;
    }

    private void ApplyResult(OcrResult result)
    {
        _result = result;
        _canvas.Lines = result.Lines;
        _canvas.SelectedIndex = -1;
        LineList.ItemsSource = result.Lines;
        EmptyText.IsVisible = result.Lines.Count == 0;
        StatusText.Text = string.Empty;
    }

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
            var language = LanguageBox.SelectedItem as string;
            var result = await _ocr.RecognizeAsync(_frame, language);
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
}
