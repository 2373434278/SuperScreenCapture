using Avalonia.Controls;
using Avalonia.Input;
using SSC.Application.Abstractions;

namespace SSC.UI.Dialogs;

/// <summary>
/// 轻量的确认对话框：标题 + 说明 + 取消/确认。
/// macOS 走 <see cref="DialogPresenter"/> 的 sheet 定位，Windows 居中模态；
/// 关闭或按 Esc 都视为取消。
/// </summary>
public partial class ConfirmDialogWindow : Window
{
    private readonly TaskCompletionSource<bool> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;

    public ConfirmDialogWindow(ILocalizer localizer, string title, string description)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        InitializeComponent();
        Title = localizer["appTitle"];
        TitleText.Text = title;
        DescText.Text = description;
        CancelText.Text = localizer["dialogCancel"];
        ConfirmText.Text = localizer["dialogConfirm"];
        DialogButtonOrder.Apply(FooterActions, ConfirmButton);

        CancelButton.Click += (_, _) => Finish(false);
        ConfirmButton.Click += (_, _) => Finish(true);
        Closed += (_, _) => Finish(false);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(false);
                e.Handled = true;
            }
        };
    }

    /// <summary>完成时返回用户是否确认；对话框关闭（含 Esc/点关闭）视为取消。</summary>
    public Task<bool> Result => _result.Task;

    private void Finish(bool confirmed)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _result.TrySetResult(confirmed);
        Close();
    }

    public static async Task<bool> ShowAsync(ILocalizer localizer, string title, string description)
    {
        var dialog = new ConfirmDialogWindow(localizer, title, description);
        await DialogPresenter.ShowAsync(dialog).ConfigureAwait(true);
        return await dialog.Result.ConfigureAwait(true);
    }
}
