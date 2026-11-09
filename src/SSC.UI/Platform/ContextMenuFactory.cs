using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using SSC.Application.Hotkeys;

namespace SSC.UI.Platform;

public sealed record ContextMenuEntry(
    string Header,
    Action? Action = null,
    HotkeyGesture? Gesture = null,
    bool IsDanger = false,
    bool IsDefault = false,
    bool IsEnabled = true,
    bool IsSeparator = false)
{
    public static ContextMenuEntry Separator() => new(string.Empty, IsSeparator: true);
}

/// <summary>
/// Builds native-looking context menus. Windows bolds the default (first) action;
/// macOS leaves every item in the normal weight.
/// </summary>
public static class ContextMenuFactory
{
    public static ContextMenu Create(params ContextMenuEntry[] entries) => Create((IEnumerable<ContextMenuEntry>)entries);

    public static ContextMenu Create(IEnumerable<ContextMenuEntry> entries)
    {
        var menu = new ContextMenu();
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = entry.Header,
                IsEnabled = entry.IsEnabled,
                Command = entry.Action is null ? null : new RelayCommand(entry.Action)
            };

            if (entry.Gesture is { } gesture)
            {
                item.InputGesture = GestureMapper.ToKeyGesture(gesture);
            }

            if (entry.IsDanger)
            {
                item.Classes.Add("danger");
            }

            if (entry.IsDefault && PlatformInfo.IsWindows)
            {
                item.Classes.Add("ctx-default");
            }

            menu.Items.Add(item);
        }

        return menu;
    }
}
