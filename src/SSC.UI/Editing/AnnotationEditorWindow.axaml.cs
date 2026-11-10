using Avalonia;
using SSC.UI.Motion;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Application.Hotkeys;
using SSC.Application.Settings;
using SSC.Application.Upload;
using SSC.Domain.Annotations;
using SSC.Domain.Capture;
using SSC.UI.Capture;
using SSC.UI.Ocr;
using SSC.UI.Platform;
using SSC.UI.Upload;
using DomainPixelPoint = SSC.Domain.Geometry.PixelPoint;
using SSC.UI.Dialogs;

namespace SSC.UI.Editing;

/// <summary>
/// 标注编辑器窗口（原型 P2）。工具轨、属性栏和底栏尺寸都按原型比例实现，
/// 绘制逻辑委托给 <see cref="AnnotationEditorCanvas"/> 与 <see cref="AnnotationEditorSession"/>。
/// </summary>
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
    private readonly IOcrService? _ocr;
    private readonly UploadPipeline? _uploads;
    private readonly SettingsService? _settings;
    private readonly IImageEncoder? _encoder;
    private readonly Func<Task>? _retake;
    private readonly AnnotationEditorSession _session;
    private readonly AnnotationEditorCanvas _canvas;
    private readonly Dictionary<AnnotationToolKind, Button> _toolButtons = new();
    private readonly Dictionary<AnnotationToolKind, string> _toolLabelKeys = new();
    private readonly Dictionary<string, Button> _swatchButtons = new();
    private string _color = AnnotationStyle.DefaultColor;
    private string _saveFormat = "png";
    private int _strength = 4;
    private bool _updatingStyle;
    private DomainPixelPoint? _textPoint;
    private bool _closed;

    /// <summary>由依赖注入构造编辑器窗口。</summary>
    public AnnotationEditorWindow(
        ScreenshotResult result,
        CaptureFrame frame,
        ILocalizer localizer,
        IAnnotationRenderer renderer,
        ScreenshotService screenshots,
        IClipboardService clipboard,
        IOcrService? ocr = null,
        UploadPipeline? uploads = null,
        SettingsService? settings = null,
        IImageEncoder? encoder = null,
        Func<Task>? retake = null)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _screenshots = screenshots ?? throw new ArgumentNullException(nameof(screenshots));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _ocr = ocr;
        _uploads = uploads;
        _settings = settings;
        _encoder = encoder;
        _retake = retake;

        InitializeComponent();
        SystemCaptionButtons.Localizer = _localizer;
        WindowingMotion.FadeIn(this);

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

        if (TextInputBox.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                HideTextInput();
                e.Handled = true;
            }

            return;
        }

        if (FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

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

        if (command && e.Key == Key.S)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _ = SaveAsAsync();
            }
            else
            {
                _ = SaveAsync();
            }

            e.Handled = true;
            return;
        }

        if (!command && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            var toolIndex = e.Key switch
            {
                >= Key.D1 and <= Key.D9 => (int)(e.Key - Key.D1),
                Key.D0 => 9,
                Key.C => 10,
                _ => -1
            };

            if (toolIndex >= 0 && toolIndex < ToolDefinitions.Length)
            {
                SelectTool(ToolDefinitions[toolIndex].Kind);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void BuildToolRail()
    {
        string[] shortcuts = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "C" };
        var index = 0;
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
            var label = $"{_localizer[labelKey]} ({shortcuts[index]})";
            ToolTip.SetTip(button, label);
            Avalonia.Automation.AutomationProperties.SetName(button, label);
            ToolRail.Children.Add(button);
            _toolButtons[kind] = button;
            _toolLabelKeys[kind] = labelKey;
            index++;
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
        PinButton.Click += (_, _) => Pin();
        OcrButton.Click += (_, _) => _ = RecognizeAsync();
        UploadButton.Click += (_, _) => _ = UploadAsync();
        DoneButton.Click += (_, _) => Close();
        RetakeButton.Click += (_, _) => Retake();

        TextInputBox.KeyDown += OnTextInputKeyDown;
        _canvas.ContextRequested += OnCanvasContextRequested;
    }

    private void OnCanvasContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var modifier = PlatformInfo.IsMacOS ? HotkeyModifiers.Meta : HotkeyModifiers.Control;
        var menu = ContextMenuFactory.Create(
            new ContextMenuEntry(_localizer["contextUndo"], Undo, new HotkeyGesture(modifier, HotkeyKey.Z)),
            new ContextMenuEntry(_localizer["contextRedo"], Redo, new HotkeyGesture(modifier | HotkeyModifiers.Shift, HotkeyKey.Z)),
            ContextMenuEntry.Separator(),
            new ContextMenuEntry(_localizer["contextCopy"], () => _ = CopyAsync(), new HotkeyGesture(modifier, HotkeyKey.C)),
            new ContextMenuEntry($"{_localizer["contextSave"]} PNG", () => { _saveFormat = "png"; _ = SaveAsync(); }, new HotkeyGesture(modifier, HotkeyKey.S)),
            new ContextMenuEntry($"{_localizer["contextSave"]} JPG", () => { _saveFormat = "jpg"; _ = SaveAsync(); }),
            new ContextMenuEntry($"{_localizer["contextSave"]} WebP", () => { _saveFormat = "webp"; _ = SaveAsync(); }),
            new ContextMenuEntry($"{_localizer["contextSaveAs"]}…", () => _ = SaveAsAsync()),
            ContextMenuEntry.Separator(),
            new ContextMenuEntry(_localizer["contextOcr"], () => _ = RecognizeAsync()),
            new ContextMenuEntry(_localizer["contextUpload"], () => _ = UploadAsync()),
            new ContextMenuEntry(_localizer["contextPin"], Pin));
        menu.Open(_canvas);
        e.Handled = true;
    }

    private void OnSavePng(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _saveFormat = "png";
        _ = SaveAsync();
    }

    private void OnSaveJpg(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _saveFormat = "jpg";
        _ = SaveAsync();
    }

    private void OnSaveWebp(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _saveFormat = "webp";
        _ = SaveAsync();
    }

    private void OnSaveAs(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _ = SaveAsAsync();

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

    public void UndoEdit() => Undo();

    public void RedoEdit() => Redo();

    public Task CopySelectionAsync() => CopyAsync();

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
            var format = _saveFormat;
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

    /// <summary>
    /// 另存为：使用系统保存面板选择路径后写入该文件。系统保存面板不可用时
    /// （如受限环境）退回设置目录直存，保证快捷键仍有可预期行为。
    /// </summary>
    private async Task SaveAsAsync()
    {
        var storage = StorageProvider;
        if (storage is null || !storage.CanSave)
        {
            await SaveAsync();
            return;
        }

        try
        {
            var frame = _renderer.Render(_frame, _session.Document);
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = _localizer["editorSaveAs"],
                SuggestedFileName = Path.GetFileNameWithoutExtension(_result.FilePath),
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } },
                    new FilePickerFileType("JPG") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                    new FilePickerFileType("WebP") { Patterns = new[] { "*.webp" } }
                }
            });

            // 用户取消：保持现状，不改文件、不改历史、不改状态栏。
            if (file is null)
            {
                return;
            }

            var path = file.Path.LocalPath;
            var saved = await _screenshots.SaveAsAsync(
                frame,
                CaptureKind.Screenshot,
                path,
                Path.GetExtension(path));
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

    private async Task RecognizeAsync()
    {
        if (_ocr is null)
        {
            ShowComingSoon();
            return;
        }

        try
        {
            var frame = _renderer.Render(_frame, _session.Document);
            StatusText.Text = _localizer["ocrRecognizing"];
            var result = await _ocr.RecognizeAsync(frame);
            new OcrResultWindow(frame, result, _ocr, _localizer).Show();
            StatusText.Text = _localizer["editorStatusReady"];
        }
        catch (Exception exception)
        {
            StatusText.Text = $"{_localizer["ocrFailed"]}: {exception.Message}";
        }
    }

    private async Task UploadAsync()
    {
        if (_uploads is null || _settings is null || _encoder is null)
        {
            ShowComingSoon();
            return;
        }

        try
        {
            var frame = _renderer.Render(_frame, _session.Document);
            var bytes = await _encoder.EncodeAsync(frame, "png", 95);
            var fileName = $"{Path.GetFileNameWithoutExtension(_result.FilePath)}.png";
            await DialogPresenter.ShowAsync(new UploadDialogWindow(bytes, fileName, _uploads, _settings, _localizer), this);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"{_localizer["uploadFailed"]}: {exception.Message}";
        }
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
        SaveAsMenuItem.Header = _localizer["editorSaveAs"];
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