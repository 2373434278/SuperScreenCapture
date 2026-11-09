using Avalonia.Controls;
using SSC.Application.Abstractions;
using SSC.Application.Hotkeys;
using SSC.UI.Platform;
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
    Action StartGif,
    Action ShowHistory,
    Action ShowAbout,
    Action PickColor,
    Action Quit);

/// <summary>
/// 托盘图标与菜单。macOS 上 TrayIcon.Menu 不能在导出后替换成新的 NativeMenu
/// （Avalonia.Native 会抛出 "The menu being updated does not match"），
/// 因此所有本地化文案都在创建时保存引用，语言切换时原地更新 Header。
/// </summary>
internal sealed class TrayMenu
{
    private readonly List<TrayItem> _items = new();

    internal TrayMenu(TrayIcon icon, TrayActions actions)
    {
        Icon = icon;
        Menu = new NativeMenu();

        Add("trayCapture", "captureRegion", actions.CaptureRegion);
        Add("trayCaptureFull", "captureFull", actions.CaptureFull);
        Add("trayCaptureDelay", "captureDelay", actions.CaptureDelayed);
        Add("trayCaptureWindow", "captureWindow", actions.CaptureWindow);
        Add("trayScroll", null, actions.CaptureScroll);
        Menu.Items.Add(new NativeMenuItemSeparator());
        Add("trayRecord", "record", actions.StartRecording);
        Add("trayRecordFull", null, actions.StartFullScreenRecording);
        Add("trayGif", "recordGif", actions.StartGif);
        Add("trayColorPicker", "colorPicker", actions.PickColor);
        Add("trayHistory", "history", actions.ShowHistory);
        Menu.Items.Add(new NativeMenuItemSeparator());
        Add("traySettings", null, actions.ShowWindow);
        Add("trayAbout", null, actions.ShowAbout);
        Menu.Items.Add(new NativeMenuItemSeparator());
        Add("trayQuit", null, actions.Quit);
    }

    public TrayIcon Icon { get; }

    public NativeMenu Menu { get; }

    public void Relocalize(ILocalizer localizer, int delaySeconds)
    {
        foreach (var entry in _items)
        {
            var header = localizer[entry.HeaderKey];
            if (string.Equals(entry.HeaderKey, "trayCaptureDelay", StringComparison.Ordinal))
            {
                header = $"{header} ({delaySeconds}s)";
            }

            entry.Item.Header = header;
        }

        Icon.ToolTipText = localizer["trayTooltip"];
    }

    /// <summary>在菜单右侧显示当前注册的快捷键；未注册项保持为空。</summary>
    public void ApplyGestures(IReadOnlyDictionary<string, string> bindings)
    {
        foreach (var entry in _items)
        {
            if (entry.ActionKey is null)
            {
                continue;
            }

            entry.Item.Gesture = bindings.TryGetValue(entry.ActionKey, out var raw) &&
                                 HotkeyGesture.TryParse(raw, out var gesture, out _)
                ? GestureMapper.ToKeyGesture(gesture)
                : null;
        }
    }

    private void Add(string headerKey, string? actionKey, Action action)
    {
        var item = new NativeMenuItem();
        item.Click += (_, _) => action();
        _items.Add(new TrayItem(item, headerKey, actionKey));
        Menu.Items.Add(item);
    }

    private readonly record struct TrayItem(NativeMenuItem Item, string HeaderKey, string? ActionKey);
}

internal static class TrayIconFactory
{
    public static TrayMenu Create(
        AvaloniaApplication application,
        WindowIcon icon,
        ILocalizer localizer,
        TrayActions actions,
        int delaySeconds,
        IReadOnlyDictionary<string, string> hotkeys)
    {
        var trayIcon = new TrayIcon
        {
            Icon = icon,
            IsVisible = true
        };

        var menu = new TrayMenu(trayIcon, actions);
        menu.Relocalize(localizer, delaySeconds);
        menu.ApplyGestures(hotkeys);
        trayIcon.Menu = menu.Menu;

        if (PlatformInfo.IsWindows)
        {
            trayIcon.Clicked += (_, _) => actions.ShowWindow();
        }

        var icons = new TrayIcons();
        icons.Add(trayIcon);
        TrayIcon.SetIcons(application, icons);

        return menu;
    }

    public static void Update(TrayMenu menu, ILocalizer localizer, int delaySeconds) =>
        menu.Relocalize(localizer, delaySeconds);
}
