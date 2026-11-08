using Avalonia.Controls;
using SSC.Application.Abstractions;
using AvaloniaApplication = Avalonia.Application;

namespace SSC.App;

internal static class TrayIconFactory
{
    public static TrayIcon Create(
        AvaloniaApplication application,
        WindowIcon icon,
        ILocalizer localizer,
        Action showWindow,
        Action comingSoon,
        Action quit)
    {
        var trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = localizer["trayTooltip"],
            IsVisible = true
        };

        trayIcon.Menu = BuildMenu(localizer, showWindow, comingSoon, quit);

        var icons = new TrayIcons();
        icons.Add(trayIcon);
        TrayIcon.SetIcons(application, icons);

        return trayIcon;
    }

    public static void Update(
        TrayIcon trayIcon,
        ILocalizer localizer,
        Action showWindow,
        Action comingSoon,
        Action quit)
    {
        trayIcon.Menu = BuildMenu(localizer, showWindow, comingSoon, quit);
        trayIcon.ToolTipText = localizer["trayTooltip"];
    }

    private static NativeMenu BuildMenu(
        ILocalizer localizer,
        Action showWindow,
        Action comingSoon,
        Action quit)
    {
        var menu = new NativeMenu();

        menu.Items.Add(Item(localizer["trayCapture"], comingSoon));
        menu.Items.Add(Item(localizer["trayRecord"], comingSoon));
        menu.Items.Add(Item(localizer["trayHistory"], comingSoon));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item(localizer["traySettings"], showWindow));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item(localizer["trayQuit"], quit));

        return menu;
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }
}
