using System.Text;
using SSC.Platform.Abstractions;

namespace SSC.Application.Hotkeys;

public enum HotkeyParseError
{
    None = 0,
    Empty = 1,
    UnknownToken = 2,
    MissingKey = 3,
    MissingModifier = 4
}

/// <summary>
/// 与平台无关的快捷键描述：修饰键集合 + 主键。
/// 解析结果按规范化比较，因此 ⇧⌘2 与 ⌘⇧2 相等。
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, HotkeyKey Key)
{
    private static readonly Dictionary<string, HotkeyKey> KeyAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["space"] = HotkeyKey.Space,
            ["spacebar"] = HotkeyKey.Space,
            ["enter"] = HotkeyKey.Enter,
            ["return"] = HotkeyKey.Enter,
            ["tab"] = HotkeyKey.Tab,
            ["esc"] = HotkeyKey.Escape,
            ["escape"] = HotkeyKey.Escape,
            ["delete"] = HotkeyKey.Backspace,
            ["backspace"] = HotkeyKey.Backspace,
            ["insert"] = HotkeyKey.Insert,
            ["home"] = HotkeyKey.Home,
            ["end"] = HotkeyKey.End,
            ["pageup"] = HotkeyKey.PageUp,
            ["pgup"] = HotkeyKey.PageUp,
            ["pagedown"] = HotkeyKey.PageDown,
            ["pgdn"] = HotkeyKey.PageDown,
            ["left"] = HotkeyKey.Left,
            ["arrowleft"] = HotkeyKey.Left,
            ["right"] = HotkeyKey.Right,
            ["arrowright"] = HotkeyKey.Right,
            ["up"] = HotkeyKey.Up,
            ["arrowup"] = HotkeyKey.Up,
            ["down"] = HotkeyKey.Down,
            ["arrowdown"] = HotkeyKey.Down,
            ["comma"] = HotkeyKey.Comma,
            ["period"] = HotkeyKey.Period,
            ["slash"] = HotkeyKey.Slash,
            ["semicolon"] = HotkeyKey.Semicolon,
            ["quote"] = HotkeyKey.Quote,
            ["bracketleft"] = HotkeyKey.BracketLeft,
            ["bracketright"] = HotkeyKey.BracketRight,
            ["backslash"] = HotkeyKey.Backslash,
            ["minus"] = HotkeyKey.Minus,
            ["equal"] = HotkeyKey.Equal,
            ["grave"] = HotkeyKey.Grave
        };

    public static bool TryParse(string? text, out HotkeyGesture gesture, out HotkeyParseError error)
    {
        gesture = null!;
        error = HotkeyParseError.None;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = HotkeyParseError.Empty;
            return false;
        }

        var trimmed = text.Trim();
        var modifiers = HotkeyModifiers.None;
        string keyText;

        if (trimmed.Contains('+'))
        {
            var parts = trimmed.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                error = HotkeyParseError.UnknownToken;
                return false;
            }

            for (var index = 0; index < parts.Length - 1; index++)
            {
                if (!TryParseModifier(parts[index], out var modifier))
                {
                    error = HotkeyParseError.UnknownToken;
                    return false;
                }

                modifiers |= modifier;
            }

            keyText = parts[^1];
        }
        else if (TrySplitDashNotation(trimmed, out var dashParts))
        {
            for (var index = 0; index < dashParts.Length - 1; index++)
            {
                TryParseModifier(dashParts[index], out var modifier);
                modifiers |= modifier;
            }

            keyText = dashParts[^1];
        }
        else
        {
            var index = 0;
            while (index < trimmed.Length && TryParseModifierSymbol(trimmed[index], out var modifier))
            {
                modifiers |= modifier;
                index++;
            }

            keyText = trimmed[index..].Trim();
        }

        if (keyText.Length == 0)
        {
            error = HotkeyParseError.MissingKey;
            return false;
        }

        if (!TryParseKey(keyText, out var key))
        {
            error = HotkeyParseError.UnknownToken;
            return false;
        }

        var isFunctionKey = key is >= HotkeyKey.F1 and <= HotkeyKey.F12;
        if (modifiers == HotkeyModifiers.None && !isFunctionKey)
        {
            error = HotkeyParseError.MissingModifier;
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public string ToDisplayString(PlatformKind platform)
    {
        if (platform == PlatformKind.Windows)
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotkeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Meta))
            {
                parts.Add("Win");
            }

            parts.Add(GetKeyDisplay(Modifiers, Key, windows: true));
            return string.Join("+", parts);
        }

        var builder = new StringBuilder(8);
        if (Modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            builder.Append('⌘');
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            builder.Append('⇧');
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            builder.Append('⌥');
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            builder.Append('⌃');
        }

        builder.Append(GetKeyDisplay(Modifiers, Key, windows: false));
        return builder.ToString();
    }

    public override string ToString() => ToDisplayString(PlatformKind.MacOS);

    private static bool TrySplitDashNotation(string text, out string[] parts)
    {
        parts = Array.Empty<string>();
        if (!text.Contains('-'))
        {
            return false;
        }

        var candidate = text.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (candidate.Length < 2)
        {
            return false;
        }

        for (var index = 0; index < candidate.Length - 1; index++)
        {
            if (!TryParseModifier(candidate[index], out _))
            {
                return false;
            }
        }

        parts = candidate;
        return true;
    }

    private static bool TryParseModifier(string token, out HotkeyModifiers modifier)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "cmd":
            case "command":
            case "meta":
            case "super":
            case "win":
            case "windows":
            case "⌘":
                modifier = HotkeyModifiers.Meta;
                return true;
            case "ctrl":
            case "control":
            case "⌃":
                modifier = HotkeyModifiers.Control;
                return true;
            case "alt":
            case "option":
            case "opt":
            case "⌥":
                modifier = HotkeyModifiers.Alt;
                return true;
            case "shift":
            case "⇧":
                modifier = HotkeyModifiers.Shift;
                return true;
            default:
                modifier = HotkeyModifiers.None;
                return false;
        }
    }

    private static bool TryParseModifierSymbol(char symbol, out HotkeyModifiers modifier)
    {
        switch (symbol)
        {
            case '⌘':
                modifier = HotkeyModifiers.Meta;
                return true;
            case '⌃':
                modifier = HotkeyModifiers.Control;
                return true;
            case '⌥':
                modifier = HotkeyModifiers.Alt;
                return true;
            case '⇧':
                modifier = HotkeyModifiers.Shift;
                return true;
            default:
                modifier = HotkeyModifiers.None;
                return false;
        }
    }

    private static bool TryParseKey(string text, out HotkeyKey key)
    {
        key = HotkeyKey.A;
        var value = text.Trim();
        if (value.Length == 1)
        {
            var upper = char.ToUpperInvariant(value[0]);
            if (upper is >= 'A' and <= 'Z')
            {
                key = Enum.Parse<HotkeyKey>(upper.ToString());
                return true;
            }

            if (upper is >= '0' and <= '9')
            {
                key = Enum.Parse<HotkeyKey>("D" + upper);
                return true;
            }

            switch (value[0])
            {
                case ',':
                    key = HotkeyKey.Comma;
                    return true;
                case '.':
                    key = HotkeyKey.Period;
                    return true;
                case '/':
                    key = HotkeyKey.Slash;
                    return true;
                case ';':
                    key = HotkeyKey.Semicolon;
                    return true;
                case '\'':
                    key = HotkeyKey.Quote;
                    return true;
                case '[':
                    key = HotkeyKey.BracketLeft;
                    return true;
                case ']':
                    key = HotkeyKey.BracketRight;
                    return true;
                case '\\':
                    key = HotkeyKey.Backslash;
                    return true;
                case '-':
                    key = HotkeyKey.Minus;
                    return true;
                case '=':
                    key = HotkeyKey.Equal;
                    return true;
                case '`':
                    key = HotkeyKey.Grave;
                    return true;
            }
        }

        if (value.Length is 2 or 3 &&
            (value[0] is 'f' or 'F') &&
            int.TryParse(value[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 12)
        {
            key = Enum.Parse<HotkeyKey>("F" + functionNumber);
            return true;
        }

        return KeyAliases.TryGetValue(value, out key);
    }

    private static string GetKeyDisplay(HotkeyModifiers modifiers, HotkeyKey key, bool windows)
    {
        var name = key.ToString();
        if (name.Length == 1)
        {
            return name;
        }

        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
        {
            return name[1].ToString();
        }

        if (name.Length <= 3 && name[0] == 'F' && int.TryParse(name[1..], out _))
        {
            return name;
        }

        return key switch
        {
            HotkeyKey.Space => "Space",
            HotkeyKey.Enter => "Enter",
            HotkeyKey.Tab => "Tab",
            HotkeyKey.Escape => "Esc",
            HotkeyKey.Backspace => "Backspace",
            HotkeyKey.Insert => "Insert",
            HotkeyKey.Home => "Home",
            HotkeyKey.End => "End",
            HotkeyKey.PageUp => "PageUp",
            HotkeyKey.PageDown => "PageDown",
            HotkeyKey.Left => "←",
            HotkeyKey.Right => "→",
            HotkeyKey.Up => "↑",
            HotkeyKey.Down => "↓",
            HotkeyKey.Comma => ",",
            HotkeyKey.Period => ".",
            HotkeyKey.Slash => "/",
            HotkeyKey.Semicolon => ";",
            HotkeyKey.Quote => "'",
            HotkeyKey.BracketLeft => "[",
            HotkeyKey.BracketRight => "]",
            HotkeyKey.Backslash => "\\",
            HotkeyKey.Minus => "-",
            HotkeyKey.Equal => "=",
            HotkeyKey.Grave => "`",
            _ => name
        };
    }
}
