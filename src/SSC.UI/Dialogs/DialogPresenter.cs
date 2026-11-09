using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SSC.UI.Platform;

namespace SSC.UI.Dialogs;

/// <summary>
/// macOS presents dialogs as a window-attached sheet (top edge, follows the owner);
/// Windows uses a centered modal dialog. Falls back to a free window when no owner exists.
/// </summary>
public static class DialogPresenter
{
    public static Window? ActiveWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
        ?.Windows.FirstOrDefault(window => window.IsActive);

    public static async Task ShowAsync(Window dialog, Window? owner = null)
    {
        owner ??= ActiveWindow;
        if (owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Show();
            return;
        }

        if (PlatformInfo.IsMacOS)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            PositionSheet(dialog, owner);
            void Reposition(object? sender, EventArgs args) => PositionSheet(dialog, owner);
            owner.PositionChanged += Reposition;
            owner.SizeChanged += Reposition;
            dialog.Closed += (_, _) =>
            {
                owner.PositionChanged -= Reposition;
                owner.SizeChanged -= Reposition;
            };
            dialog.Show(owner);
            return;
        }

        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        await dialog.ShowDialog(owner);
    }

    public static PixelPoint ComputeSheetPosition(
        PixelPoint ownerPosition,
        double ownerWidthDips,
        double dialogWidthDips,
        double renderScaling)
    {
        if (ownerWidthDips <= 0)
        {
            return ownerPosition;
        }

        var scaling = renderScaling <= 0 ? 1 : renderScaling;
        var offset = (int)Math.Round((ownerWidthDips - dialogWidthDips) / 2 * scaling);
        return new PixelPoint(ownerPosition.X + offset, ownerPosition.Y);
    }

    public static PixelPoint ComputeSheetPosition(Window owner, double dialogWidthDips) =>
        ComputeSheetPosition(owner.Position, owner.Bounds.Width, dialogWidthDips, owner.RenderScaling);

    private static void PositionSheet(Window dialog, Window owner) =>
        dialog.Position = ComputeSheetPosition(owner, dialog.Width);
}
