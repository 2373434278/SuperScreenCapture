using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Domain.Annotations;
using SSC.Domain.Capture;
using SSC.UI.Capture;
using DomainPixelPoint = SSC.Domain.Geometry.PixelPoint;

namespace SSC.UI.Editing;

public partial class AnnotationEditorWindow : Window
{
    private static readonly (AnnotationToolKind Kind, string LabelKey, string IconPath)[] ToolDefinitions =
    {
        (AnnotationToolKind.Select, "editorToolSelect", "M5 3l14 8-6 1.6L10 19z"),
        (AnnotationToolKind.Rectangle, "editorToolRectangle", "M4 5h16v14H4z"),
        (AnnotationToolKind.Ellipse, "editorToolEllipse", "M12 5a8 7 0 1 0 0 14a8 7 0 1 0 0-14"),
        (AnnotationToolKind.Arrow, "editorToolArrow", "M4 20 19 5M19 5h-6M19 5v6"),
        (AnnotationToolKind.Pen, "editorToolPen", "m4 20 4.2-1 9.8-9.8-3-3L5.2 16 4 20ZM13.6 5.6l3 3"),
        (AnnotationToolKind.Text, "editorToolText", "M5 6h14M12 6v13M9 19h6"),
        (AnnotationToolKind.Highlighter, "editorToolHighlighter", "m5 15 7-11 4 2.6-7 11zM4 19h16"),
        (AnnotationToolKind.Mosaic, "editorToolMosaic", "M4 4h5v5H4zM15 4h5v5h-5zM9.5 9.5h5v5h-5zM4 15h5v5H4zM15 15h5v5h-5z"),
        (AnnotationToolKind.Blur, "editorToolBlur", "M9 6a3 3 0 1 0 0 6a3 3 0 1 0 0-6M15.5 10a4.5 4.5 0 1 0 0 9a4.5 4.5 0 1 0 0-9M4 19h4M17 4h3"),
        (AnnotationToolKind.Step, "editorToolStep", "M12 4a8 8 0 1 0 0 16a8 8 0 1 0 0-16M11 8.5h2V16M10 16h4"),
        (AnnotationToolKind.Crop, "editorToolCrop", "M6 3v15h15M3 6h15v15")
    };

    private static readonly (string Hex, string Name)[] Swatches =
    {
        ("#FF4757", "red"),
        ("#FFB020", "amber"),
        ("#17C964", "green"),
        ("#22D3EE", "cyan"),
        ("#5B5FF0", "indigo"),
        ("#C084FC", "violet"),
        ("#FFFFFF", "white"),
        ("#111827", "ink")
    };

    private readonly CaptureFrame _frame;
    private readonly ScreenshotResult _result;
    private readonly ILocalizer _localizer;
    private readonly IAnnotationRenderer _renderer;
    private readonly ScreenshotService _screenshots;
    private readonly IClipboardService _clipboard;
    private readonly Func<Task>? _retake;
    private readonly AnnotationEditorSession _session;
    private readonly AnnotationEditorCanvas _canvas;
    private readonly Dictionary<AnnotationToolKind, Button> _toolButtons = new();
    private readonly Dictionary<AnnotationToolKind, string> _toolLabelKeys = new();
    private readonly Dictionary<string, Button> _swatchButtons = new();
    private string _color = AnnotationStyle.DefaultColor;
    private int _strength = 4;
    private bool _updatingStyle;
    private DomainPixelPoint? _textPoint;
    private bool _closed;

    public AnnotationEditorWindow(
        ScreenshotResult result,
        CaptureFrame frame,
        ILocalizer localizer,
        IAnnotationRenderer renderer,
        ScreenshotService screenshots,
        IClipboardService clipboard,
        Func<Task>? retake = null)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _screenshots = screenshots ?? throw new ArgumentNullException(nameof(screenshots));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _retake = retake;

        InitializeComponent();

        _session = new AnnotationEditorSession(frame.Width, frame.Height);
        _canvas = new AnnotationEditorCanvas(frame, _session, renderer);
        _canvas.TextInputRequested += OnTextInputRequested;
        CanvasHost.Children.Add(_canvas);
        _session.Changed += OnSessionChanged;
        _localizer.LanguageChanged += OnLanguageChanged;

        BuildToolRail();
        WireControls();
        BuildSwatches();
        ApplyLocalization();
        UpdateState();
        SelectTool(AnnotationToolKind.Select);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _canvas.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closed)
        {
            _closed = true;
            _localizer.LanguageChanged -= OnLanguageChanged;
            _session.Changed -= OnSessionChanged;
        }

        base.OnClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta) ||
                      e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (command && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                Redo();
            }
            else
            {
                Undo();
            }

            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
            return;
        }

        if (command && e.Key == Key.C)
        {
            _ = CopyAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (TextInputBox.IsVisible)
            {
                HideTextInput();
            }
            else
            {
                Close();
            }

            e.Handled = true;
        }
    }

    private void BuildToolRail()
    {
        foreach (var (kind, labelKey, iconPath) in ToolDefinitions)
        {
            var icon = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(iconPath),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            var button = new Button { Content = icon };
            button.Classes.Add("tool");
            button.Click += (_, _) => SelectTool(kind);
            ToolTip.SetTip(button, _localizer[labelKey]);
            ToolRail.Children.Add(button);
            _toolButtons[kind] = button;
            _toolLabelKeys[kind] = labelKey;
        }
    }

    private void BuildSwatches()
    {
        foreach (var (hex, _) in Swatches)
        {
            var button = new Button { Background = new SolidColorBrush(Color.Parse(hex)) };
            button.Classes.Add("swatch");
            button.Click += (_, _) => SetColor(hex);
            ToolTip.SetTip(button, hex);
            SwatchPanel.Children.Add(button);
            _swatchButtons[hex] = button;
        }

        SetColor(_color);
    }

    private void WireControls()
    {
        WidthSlider.Value = _session.Style.StrokeWidth;
        FontSlider.Value = _session.Style.FontSize;
        WidthValue.Text = ((int)WidthSlider.Value).ToString();
        FontValue.Text = ((int)FontSlider.Value).ToString();

        WidthSlider.ValueChanged += (_, _) =>
        {
            if (_updatingStyle)
            {
                return;
            }

            WidthValue.Text = ((int)Math.Round(WidthSlider.Value)).ToString();
            UpdateStyleFromControls();
        };
        FontSlider.ValueChanged += (_, _) =>
        {
            if (_updatingStyle)
            {
                return;
            }

            FontValue.Text = ((int)Math.Round(FontSlider.Value)).ToString();
            UpdateStyleFromControls();
        };

        StrengthSmall.Click += (_, _) => SetStrength(2);
        StrengthMedium.Click += (_, _) => SetStrength(4);
        StrengthLarge.Click += (_, _) => SetStrength(8);
        SetStrength(_strength);

        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => Redo();
        CopyButton.Click += (_, _) => _ = CopyAsync();
        SaveButton.Click += (_, _) => _ = SaveAsync();
        PinButton.Click += (_, _) => Pin();
        OcrButton.Click += (_, _) => ShowComingSoon();
        UploadButton.Click += (_, _) => ShowComingSoon();
        DoneButton.Click += (_, _) => Close();
        RetakeButton.Click += (_, _) => Retake();

        FormatBox.ItemsSource = new[] { "png", "jpg", "webp" };
        FormatBox.SelectedIndex = 0;

        TextInputBox.KeyDown += OnTextInputKeyDown;
    }

    private void SelectTool(AnnotationToolKind kind)
    {
        _session.SelectTool(kind);
        foreach (var (toolKind, button) in _toolButtons)
        {
            SetClass(button, "active", toolKind == kind);
        }

        var definition = ToolDefinitions.First(item => item.Kind == kind);
        ToolChipIcon.Data = Geometry.Parse(definition.IconPath);
        ToolChipText.Text = _localizer[definition.LabelKey];
        _canvas.Cursor = kind switch
        {
            AnnotationToolKind.Select => new Cursor(StandardCursorType.Arrow),
            AnnotationToolKind.Text => new Cursor(StandardCursorType.Ibeam),
            _ => new Cursor(StandardCursorType.Cross)
        };
        _canvas.Focus();
    }

    private void SetColor(string hex)
    {
        _color = hex;
        foreach (var (swatch, button) in _swatchButtons)
        {
            SetClass(button, "active", string.Equals(swatch, hex, StringComparison.OrdinalIgnoreCase));
        }

        UpdateStyleFromControls();
    }

    private void SetStrength(int strength)
    {
        _strength = strength;
        SetClass(StrengthSmall, "active", strength == 2);
        SetClass(StrengthMedium, "active", strength == 4);
        SetClass(StrengthLarge, "active", strength == 8);
        UpdateStyleFromControls();
    }

    private void UpdateStyleFromControls()
    {
        _updatingStyle = true;
        try
        {
            _session.UpdateStyle(new AnnotationStyle(
                _color,
                (int)Math.Round(WidthSlider.Value),
                (int)Math.Round(FontSlider.Value),
                _strength));
        }
        finally
        {
            _updatingStyle = false;
        }
    }

    private void OnTextInputRequested(object? sender, TextInputRequest request)
    {
        _textPoint = request.DocumentPoint;
        OverlayCanvas.IsHitTestVisible = true;
        TextInputBox.IsVisible = true;
        TextInputBox.Text = string.Empty;

        var left = Math.Clamp(
            request.DisplayPoint.X,
            4,
            Math.Max(4, OverlayCanvas.Bounds.Width - TextInputBox.Width - 4));
        var top = Math.Clamp(
            request.DisplayPoint.Y,
            4,
            Math.Max(4, OverlayCanvas.Bounds.Height - TextInputBox.Height - 4));
        Canvas.SetLeft(TextInputBox, left);
        Canvas.SetTop(TextInputBox, top);
        TextInputBox.Focus();
        TextInputBox.SelectAll();
    }

    private void OnTextInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitText();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideTextInput();
            e.Handled = true;
        }
    }

    private void CommitText()
    {
        var point = _textPoint;
        var text = TextInputBox.Text ?? string.Empty;
        HideTextInput();
        if (point is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _session.Commit(new AnnotationToolInput(point.Value, point.Value, Text: text));
    }

    private void HideTextInput()
    {
        TextInputBox.IsVisible = false;
        TextInputBox.Text = string.Empty;
        OverlayCanvas.IsHitTestVisible = false;
        _textPoint = null;
        _canvas.Focus();
    }

    private void Undo()
    {
        if (_session.Undo())
        {
            UpdateState();
        }
    }

    private void Redo()
    {
        if (_session.Redo())
        {
            UpdateState();
        }
    }

    private async Task CopyAsync()
    {
        try
        {
            var frame = _renderer.Render(_frame, _session.Document);
            await _clipboard.CopyImageAsync(frame);
            StatusText.Text = _localizer["editorCopied"];
        }
        catch (Exception exception)
        {
            StatusText.Text = $"{_localizer["editorActionFailed"]}: {exception.Message}";
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var format = FormatBox.SelectedItem as string ?? "png";
            var frame = _renderer.Render(_frame, _session.Document);
            var saved = await _screenshots.SaveAsync(frame, CaptureKind.Screenshot, format);
            FileNameText.Text = Path.GetFileName(saved.FilePath);
            StatusText.Text = string.Format(_localizer["editorSaved"], Path.GetFileName(saved.FilePath));
        }
        catch (Exception exception)
        {
            StatusText.Text = $"{_localizer["editorActionFailed"]}: {exception.Message}";
        }
    }

    private void Pin()
    {
        var frame = _renderer.Render(_frame, _session.Document);
        new PinWindow(frame, _localizer).Show();
        StatusText.Text = _localizer["editorPinned"];
    }

    private void ShowComingSoon() => StatusText.Text = _localizer["editorComingSoon"];

    private void Retake()
    {
        Close();
        if (_retake is not null)
        {
            _ = _retake();
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e) => UpdateState();

    private void UpdateState()
    {
        var crop = _session.Document.Crop;
        DimensionsText.Text = $"{crop.Width} × {crop.Height}";
        UndoCountText.Text = _session.History.UndoCount.ToString();
        RedoCountText.Text = _session.History.RedoCount.ToString();
        UndoButton.IsEnabled = _session.History.CanUndo;
        RedoButton.IsEnabled = _session.History.CanRedo;
        UndoButton.Opacity = _session.History.CanUndo ? 1 : 0.4;
        RedoButton.Opacity = _session.History.CanRedo ? 1 : 0.4;
    }

    private void OnLanguageChanged(object? sender, string language) => ApplyLocalization();

    private void ApplyLocalization()
    {
        Title = _localizer["editorTitle"];
        RetakeText.Text = _localizer["editorRetake"];
        DoneText.Text = _localizer["editorDone"];
        CopyText.Text = _localizer["editorCopy"];
        SaveText.Text = _localizer["editorSave"];
        ColorLabel.Text = _localizer["editorColor"];
        WidthLabel.Text = _localizer["editorWidth"];
        FontLabel.Text = _localizer["editorFontSize"];
        StrengthLabel.Text = _localizer["editorStrength"];
        FileNameText.Text = Path.GetFileName(_result.FilePath);
        ToolTip.SetTip(OcrButton, _localizer["editorOcr"]);
        ToolTip.SetTip(UploadButton, _localizer["editorUpload"]);
        ToolTip.SetTip(PinButton, _localizer["editorPin"]);
        ToolTip.SetTip(UndoButton, _localizer["editorUndo"]);
        ToolTip.SetTip(RedoButton, _localizer["editorRedo"]);

        foreach (var (kind, button) in _toolButtons)
        {
            ToolTip.SetTip(button, _localizer[_toolLabelKeys[kind]]);
        }

        if (string.IsNullOrWhiteSpace(StatusText.Text))
        {
            StatusText.Text = _localizer["editorStatusReady"];
        }

        SelectTool(_session.ActiveTool);
        UpdateState();
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
