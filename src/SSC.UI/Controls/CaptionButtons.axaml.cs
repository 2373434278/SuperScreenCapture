using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SSC.Application.Abstractions;
using SSC.UI.Platform;

namespace SSC.UI.Controls;

/// <summary>
/// Windows 自定义系统按钮（– □ ×）。macOS 上隐藏（红绿灯由系统绘制），
/// 因此四个共用顶栏的窗口都可以无条件放置本控件。
/// </summary>
public partial class CaptionButtons : UserControl
{
    private Window? _hostWindow;
    private ILocalizer? _localizer;

    public CaptionButtons()
    {
        InitializeComponent();

        // Windows 自有 chrome；macOS 隐藏后不占位。
        IsVisible = PlatformInfo.IsWindows;

        if (PlatformInfo.IsWindows)
        {
            // Snap Layouts 依赖命中测试：窗口扩展进非客户区后，系统会按指针下的
            // 视觉元素重新取 HTMAXBUTTON，悬停最大化按钮即可恢复 Win11 布局浮层。
            Win32Properties.SetNonClientHitTestResult(
                MaximizeButton,
                Win32Properties.Win32HitTestValue.MaxButton);
        }

        MinimizeButton.Click += OnMinimizeClick;
        MaximizeButton.Click += OnMaximizeClick;
        CloseButton.Click += OnCloseClick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        UpdateMaximizeVisual(WindowState.Normal);
    }

    public Button MinimizeButton => MinimizeButtonPart;

    public Button MaximizeButton => MaximizeButtonPart;

    public Button CloseButton => CloseButtonPart;

    /// <summary>宿主窗口的本地化器；窗口构造时注入，语言切换时自动刷新文案。</summary>
    public ILocalizer? Localizer
    {
        get => _localizer;
        set
        {
            if (ReferenceEquals(_localizer, value))
            {
                return;
            }

            if (_localizer is not null)
            {
                _localizer.LanguageChanged -= OnLanguageChanged;
            }

            _localizer = value;

            if (_localizer is not null)
            {
                _localizer.LanguageChanged += OnLanguageChanged;
            }

            ApplyLocalization();
        }
    }

    /// <summary>最大化/还原的窗口状态切换（纯函数，便于单测）。</summary>
    public static WindowState NextWindowState(WindowState current) =>
        current == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (ReferenceEquals(window, _hostWindow))
        {
            return;
        }

        DetachHostWindow();
        _hostWindow = window;

        if (_hostWindow is not null)
        {
            _hostWindow.PropertyChanged += OnHostWindowPropertyChanged;
            UpdateMaximizeVisual(_hostWindow.WindowState);
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        DetachHostWindow();

    private void DetachHostWindow()
    {
        if (_hostWindow is null)
        {
            return;
        }

        _hostWindow.PropertyChanged -= OnHostWindowPropertyChanged;
        _hostWindow = null;
    }

    private void OnHostWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && _hostWindow is not null)
        {
            UpdateMaximizeVisual(_hostWindow.WindowState);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (ResolveHostWindow() is { } window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        if (ResolveHostWindow() is { } window)
        {
            window.WindowState = NextWindowState(window.WindowState);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => ResolveHostWindow()?.Close();

    private Window? ResolveHostWindow() => _hostWindow ?? TopLevel.GetTopLevel(this) as Window;

    private void UpdateMaximizeVisual(WindowState state)
    {
        var maximized = state == WindowState.Maximized;
        MaximizeIcon.IsVisible = !maximized;
        RestoreIcon.IsVisible = maximized;
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        SetButtonText(MinimizeButton, "windowMinimize", "Minimize");
        var maximized = _hostWindow?.WindowState == WindowState.Maximized;
        SetButtonText(
            MaximizeButton,
            maximized ? "windowRestore" : "windowMaximize",
            maximized ? "Restore" : "Maximize");
        SetButtonText(CloseButton, "windowClose", "Close");
    }

    private void SetButtonText(Button button, string key, string fallback)
    {
        var text = _localizer is null ? fallback : _localizer[key];
        ToolTip.SetTip(button, text);
        AutomationProperties.SetName(button, text);
    }

    private void OnLanguageChanged(object? sender, string language) => ApplyLocalization();
}
