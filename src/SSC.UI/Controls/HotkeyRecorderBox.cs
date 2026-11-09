using Avalonia.Controls;
using Avalonia.Input;
using SSC.Application.Hotkeys;
using SSC.Platform.Abstractions;

namespace SSC.UI.Controls;

/// <summary>
/// 只读快捷键录制框：聚焦后按下的组合键会转换成规范化显示串。
/// Esc 还原、Delete/Backspace 清空、Tab 不移动焦点。
/// </summary>
public sealed class HotkeyRecorderBox : TextBox
{
    private static readonly PlatformKind Platform =
        OperatingSystem.IsMacOS() ? PlatformKind.MacOS : PlatformKind.Windows;

    private string? _textBeforeCapture;
    private bool _capturing;

    public HotkeyRecorderBox()
    {
        IsReadOnly = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);

        GotFocus += (_, _) =>
        {
            if (_capturing)
            {
                return;
            }

            _capturing = true;
            _textBeforeCapture = Text;
        };
        LostFocus += (_, _) => _capturing = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_capturing)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.Key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Text = _textBeforeCapture ?? Text;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Back or Key.Delete)
        {
            Text = string.Empty;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            return;
        }

        var modifiers = ToModifiers(e.KeyModifiers);
        if (!TryMapKey(e.Key, out var key))
        {
            e.Handled = true;
            return;
        }

        if (modifiers == HotkeyModifiers.None &&
            !(key is >= HotkeyKey.F1 && key <= HotkeyKey.F12))
        {
            e.Handled = true;
            return;
        }

        Text = new HotkeyGesture(modifiers, key).ToDisplayString(Platform);
        e.Handled = true;
    }

    private static HotkeyModifiers ToModifiers(KeyModifiers modifiers)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            result |= HotkeyModifiers.Meta;
        }

        return result;
    }

    private static bool TryMapKey(Key key, out HotkeyKey hotkeyKey)
    {
        var code = (int)key;
        if (code >= (int)Key.A && code <= (int)Key.Z)
        {
            hotkeyKey = Enum.Parse<HotkeyKey>(((char)('A' + code - (int)Key.A)).ToString());
            return true;
        }

        if (code >= (int)Key.D0 && code <= (int)Key.D9)
        {
            hotkeyKey = Enum.Parse<HotkeyKey>("D" + (code - (int)Key.D0));
            return true;
        }

        if (code >= (int)Key.F1 && code <= (int)Key.F12)
        {
            hotkeyKey = Enum.Parse<HotkeyKey>("F" + (code - (int)Key.F1 + 1));
            return true;
        }

        hotkeyKey = key switch
        {
            Key.Space => HotkeyKey.Space,
            Key.Enter => HotkeyKey.Enter,
            Key.Tab => HotkeyKey.Tab,
            Key.Escape => HotkeyKey.Escape,
            Key.Back => HotkeyKey.Backspace,
            Key.Insert => HotkeyKey.Insert,
            Key.Home => HotkeyKey.Home,
            Key.End => HotkeyKey.End,
            Key.PageUp => HotkeyKey.PageUp,
            Key.PageDown => HotkeyKey.PageDown,
            Key.Left => HotkeyKey.Left,
            Key.Right => HotkeyKey.Right,
            Key.Up => HotkeyKey.Up,
            Key.Down => HotkeyKey.Down,
            Key.OemComma => HotkeyKey.Comma,
            Key.OemPeriod => HotkeyKey.Period,
            Key.OemQuestion => HotkeyKey.Slash,
            Key.OemSemicolon => HotkeyKey.Semicolon,
            Key.OemQuotes => HotkeyKey.Quote,
            Key.OemOpenBrackets => HotkeyKey.BracketLeft,
            Key.OemCloseBrackets => HotkeyKey.BracketRight,
            Key.OemPipe => HotkeyKey.Backslash,
            Key.OemMinus => HotkeyKey.Minus,
            Key.OemPlus => HotkeyKey.Equal,
            Key.OemTilde => HotkeyKey.Grave,
            _ => HotkeyKey.A
        };

        return key is Key.Space or Key.Enter or Key.Tab or Key.Escape or Key.Back
            or Key.Insert or Key.Home or Key.End or Key.PageUp or Key.PageDown
            or Key.Left or Key.Right or Key.Up or Key.Down
            or Key.OemComma or Key.OemPeriod or Key.OemQuestion or Key.OemSemicolon
            or Key.OemQuotes or Key.OemOpenBrackets or Key.OemCloseBrackets
            or Key.OemPipe or Key.OemMinus or Key.OemPlus or Key.OemTilde;
    }
}
