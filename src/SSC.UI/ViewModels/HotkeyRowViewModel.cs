using CommunityToolkit.Mvvm.ComponentModel;

namespace SSC.UI.ViewModels;

/// <summary>快捷键设置页的一行：动作名称、组合键文本与冲突提示。</summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    public HotkeyRowViewModel(string actionKey, string labelKey, string actionLabel, string gesture)
    {
        ActionKey = actionKey;
        LabelKey = labelKey;
        ActionLabel = actionLabel;
        Gesture = gesture;
    }

    /// <summary>动作稳定键，用于持久化与冲突检测。</summary>
    public string ActionKey { get; }

    /// <summary>本地化标签键。</summary>
    public string LabelKey { get; }

    /// <summary>本地化后的动作名称。</summary>
    [ObservableProperty]
    private string actionLabel;

    /// <summary>用户输入的组合键文本，例如 ⌘⇧2。</summary>
    [ObservableProperty]
    private string gesture;

    /// <summary>冲突时展示的错误信息；为空表示无冲突。</summary>
    [ObservableProperty]
    private string? errorMessage;

    /// <summary>是否需要在行内显示错误。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
