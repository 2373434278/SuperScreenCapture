using Avalonia.Controls;
using SSC.Application.Abstractions;
using AvaloniaApplication = Avalonia.Application;

namespace SSC.App;

internal sealed record TrayActions(
    Action ShowWindow,
    Action CaptureRegion,
    Action CaptureFull,
    Action CaptureDelayed,
    Action CaptureWindow,
    Action ComingSoon,
    Action Quit);

internal static class TrayIconFactory
{
    public static TrayIcon Create(
        AvaloniaApplication application,
        WindowIcon icon,
        ILocalizer localizer,
        TrayActions actions)
    {
        var trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = localizer["trayTooltip"],
            IsVisible = true
        };

        trayIcon.Menu = BuildMenu(localizer, actions);

        var icons = new TrayIcons();
        icons.Add(trayIcon);
        TrayIcon.SetIcons(application, icons);

        return trayIcon;
    }

    public static void Update(TrayIcon trayIcon, ILocalizer localizer, TrayActions actions)
    {
        trayIcon.Menu = BuildMenu(localizer, actions);
        trayIcon.ToolTipText = localizer["trayTooltip"];
    }

    private static NativeMenu BuildMenu(ILocalizer localizer, TrayActions actions)
    {
        var menu = new NativeMenu();

        menu.Items.Add(Item(localizer["trayCapture"], actions.CaptureRegion));
        menu.Items.Add(Item(localizer["trayCaptureFull"], actions.CaptureFull));
        menu.Items.Add(Item(localizer["trayCaptureDelay"], actions.CaptureDelayed));
        menu.Items.Add(Item(localizer["trayCaptureWindow"], actions.CaptureWindow));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item(localizer["trayRecord"], actions.ComingSoon));
        menu.Items.Add(Item(localizer["trayHistory"], actions.ComingSoon));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item(localizer["traySettings"], actions.ShowWindow));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item(localizer["trayQuit"], actions.Quit));

        return menu;
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }
}
