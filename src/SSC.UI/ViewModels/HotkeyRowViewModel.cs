using CommunityToolkit.Mvvm.ComponentModel;

namespace SSC.UI.ViewModels;

public sealed partial class HotkeyRowViewModel : ObservableObject
{
    public HotkeyRowViewModel(string actionKey, string labelKey, string actionLabel, string gesture)
    {
        ActionKey = actionKey;
        LabelKey = labelKey;
        ActionLabel = actionLabel;
        Gesture = gesture;
    }

    public string ActionKey { get; }

    public string LabelKey { get; }

    [ObservableProperty]
    private string actionLabel;

    [ObservableProperty]
    private string gesture;

    [ObservableProperty]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
