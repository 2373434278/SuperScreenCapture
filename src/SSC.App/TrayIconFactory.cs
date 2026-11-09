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
    Action CaptureScroll,
    Action StartRecording,
    Action StartFullScreenRecording,
    Action ShowHistory,
    Action Quit);

/// <summary>
/// 托盘图标与菜单。macOS 上 TrayIcon.Menu 不能在导出后替换成新的 NativeMenu
/// （Avalonia.Native 会抛出 "The menu being updated does not match"），
/// 因此所有本地化文案都在创建时保存引用，语言切换时原地更新 Header。
/// </summary>
internal sealed class TrayMenu
{
    private readonly List<(NativeMenuItem Item, string HeaderKey)> _localizedItems = new();

    internal TrayMenu(TrayIcon icon, TrayActions actions)
    {
        Icon = icon;
        Menu = new NativeMenu();

        Add("trayCapture", actions.CaptureRegion);
        Add("trayCaptureFull", actions.CaptureFull);
        Add("trayCaptureDelay", actions.CaptureDelayed);
        Add("trayCaptureWindow", actions.CaptureWindow);
        Add("trayScroll", actions.CaptureScroll);
        Menu.Items.Add(new NativeMenuItemSeparator());
        Add("trayRecord", actions.StartRecording);
        Add("trayRecordFull", actions.StartFullScreenRecording);
        Add("trayHistory", actions.ShowHistory);
        Menu.Items.Add(new NativeMenuItemSeparator());
        Add("traySettings", actions.ShowWindow);
        Menu.Items.Add(new NativeMenuItemSeparator());
        Add("trayQuit", actions.Quit);
    }

    public TrayIcon Icon { get; }

    public NativeMenu Menu { get; }

    public void Relocalize(ILocalizer localizer)
    {
        foreach (var (item, headerKey) in _localizedItems)
        {
            item.Header = localizer[headerKey];
        }

        Icon.ToolTipText = localizer["trayTooltip"];
    }

    private void Add(string headerKey, Action action)
    {
        var item = new NativeMenuItem();
        item.Click += (_, _) => action();
        _localizedItems.Add((item, headerKey));
        Menu.Items.Add(item);
    }
}

internal static class TrayIconFactory
{
    public static TrayMenu Create(
        AvaloniaApplication application,
        WindowIcon icon,
        ILocalizer localizer,
        TrayActions actions)
    {
        var trayIcon = new TrayIcon
        {
            Icon = icon,
            IsVisible = true
        };

        var menu = new TrayMenu(trayIcon, actions);
        menu.Relocalize(localizer);
        trayIcon.Menu = menu.Menu;

        var icons = new TrayIcons();
        icons.Add(trayIcon);
        TrayIcon.SetIcons(application, icons);

        return menu;
    }

    public static void Update(TrayMenu menu, ILocalizer localizer) => menu.Relocalize(localizer);
}
