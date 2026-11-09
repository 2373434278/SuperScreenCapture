using SSC.Application.Hotkeys;

namespace SSC.UI.Platform;

/// <summary>Per-platform accelerator labels for standard commands (⌘ on macOS, Ctrl on Windows).</summary>
public static class ShortcutHints
{
    public static string Command(HotkeyKey key, HotkeyModifiers extra = HotkeyModifiers.None)
    {
        var baseModifier = PlatformInfo.IsMacOS ? HotkeyModifiers.Meta : HotkeyModifiers.Control;
        return new HotkeyGesture(baseModifier | extra, key).ToDisplayString(PlatformInfo.Current);
    }

    public static string Undo => Command(HotkeyKey.Z);

    public static string Redo => Command(HotkeyKey.Z, HotkeyModifiers.Shift);

    public static string Save => Command(HotkeyKey.S);

    public static string SaveAs => Command(HotkeyKey.S, HotkeyModifiers.Shift);

    public static string Copy => Command(HotkeyKey.C);

    public static string Paste => Command(HotkeyKey.V);

    public static string SelectAll => Command(HotkeyKey.A);

    public static string Settings => Command(HotkeyKey.Comma);

    public static string Quit => Command(HotkeyKey.Q);

    public static string Close => Command(HotkeyKey.W);
}
