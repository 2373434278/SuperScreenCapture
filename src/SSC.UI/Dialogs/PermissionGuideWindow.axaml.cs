using Avalonia.Controls;
using Avalonia.Input;
using SSC.Application.Abstractions;
using SSC.UI.Platform;

namespace SSC.UI.Dialogs;

/// <summary>
/// P8 permission guide. macOS shows screen recording + microphone + camera;
/// Windows only needs microphone/camera for desktop apps.
/// </summary>
public partial class PermissionGuideWindow : Window
{
    private readonly IPermissionService _permissions;
    private readonly ILocalizer _localizer;

    public PermissionGuideWindow(IPermissionService permissions, ILocalizer localizer)
    {
        _permissions = permissions;
        _localizer = localizer;

        InitializeComponent();
        ScreenRow.IsVisible = PlatformInfo.IsMacOS;
        DialogButtonOrder.Apply(FooterActions, OpenSettingsButton);
        ApplyLocalization();

        Opened += async (_, _) => await RefreshAsync();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        CloseButton.Click += (_, _) => Close();
        LaterButton.Click += (_, _) => Close();
        OpenSettingsButton.Click += async (_, _) =>
        {
            await _permissions.OpenSettingsAsync(
                PlatformInfo.IsMacOS ? PermissionKind.ScreenRecording : PermissionKind.Microphone);
            await RefreshAsync();
        };
    }

    private void ApplyLocalization()
    {
        Title = _localizer["appTitle"];
        TitleText.Text = _localizer[PlatformInfo.IsMacOS ? "permissionGuideTitle" : "permissionGuideTitleWin"];
        DescText.Text = _localizer["permissionGuideDesc"];
        ScreenLabel.Text = _localizer["permissionScreenRecording"];
        MicrophoneLabel.Text = _localizer["permissionMicrophone"];
        CameraLabel.Text = _localizer["permissionCamera"];
        OpenSettingsText.Text = _localizer["permissionOpenSettings"];
        LaterText.Text = _localizer["permissionLater"];
        RestartText.Text = _localizer["permissionRestart"];
        NoteText.Text = _localizer[PlatformInfo.IsMacOS ? "permissionNoteMac" : "permissionNoteWin"];
    }

    private async Task RefreshAsync()
    {
        var screen = await SetStatusAsync(ScreenChip, ScreenChipText, PermissionKind.ScreenRecording);
        var microphone = await SetStatusAsync(MicrophoneChip, MicrophoneChipText, PermissionKind.Microphone);
        var camera = await SetStatusAsync(CameraChip, CameraChipText, PermissionKind.Camera);
        RestartText.IsVisible = PlatformInfo.IsMacOS && !(screen && microphone && camera);
    }

    private async Task<bool> SetStatusAsync(Border chip, TextBlock text, PermissionKind kind)
    {
        PermissionStatus status;
        try
        {
            status = await _permissions.CheckAsync(kind);
        }
        catch (Exception)
        {
            status = PermissionStatus.Unknown;
        }

        var granted = status == PermissionStatus.Granted;
        chip.Classes.Clear();
        chip.Classes.Add("chip");
        chip.Classes.Add(granted ? "ok" : "bad");
        text.Text = _localizer[granted ? "permissionGranted" : "permissionNotGranted"];
        return granted;
    }
}

public static class PermissionGuide
{
    public static Task ShowAsync(IPermissionService permissions, ILocalizer localizer) =>
        DialogPresenter.ShowAsync(new PermissionGuideWindow(permissions, localizer));
}
