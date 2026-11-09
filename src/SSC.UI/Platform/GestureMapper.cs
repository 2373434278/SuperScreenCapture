using Avalonia.Input;
using SSC.Application.Hotkeys;

namespace SSC.UI.Platform;

/// <summary>Maps application hotkeys to Avalonia gestures for menus and tray labels.</summary>
public static class GestureMapper
{
    public static KeyGesture? ToKeyGesture(HotkeyGesture gesture)
    {
        var key = MapKey(gesture.Key);
        if (key is null)
        {
            return null;
        }

        var modifiers = KeyModifiers.None;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            modifiers |= KeyModifiers.Meta;
        }

        return new KeyGesture(key.Value, modifiers);
    }

    public static Key? MapKey(HotkeyKey key) => key switch
    {
        HotkeyKey.Comma => Key.OemComma,
        HotkeyKey.Period => Key.OemPeriod,
        HotkeyKey.Slash => Key.OemQuestion,
        HotkeyKey.Semicolon => Key.OemSemicolon,
        HotkeyKey.Quote => Key.OemQuotes,
        HotkeyKey.BracketLeft => Key.OemOpenBrackets,
        HotkeyKey.BracketRight => Key.OemCloseBrackets,
        HotkeyKey.Backslash => Key.OemPipe,
        HotkeyKey.Minus => Key.OemMinus,
        HotkeyKey.Equal => Key.OemPlus,
        HotkeyKey.Grave => Key.OemTilde,
        HotkeyKey.Backspace => Key.Back,
        _ => Enum.TryParse<Key>(key.ToString(), out var parsed) ? parsed : null
    };
}
