namespace SSC.Application.Hotkeys;

public readonly record struct HotkeyKeyCodes(int MacKeyCode, int WindowsVirtualKey);

/// <summary>
/// 一张同时包含 macOS 虚拟键码与 Windows VK 的键表。
/// macOS 值为 Carbon/HIToolbox 虚拟键码，Windows 值为 VK_*。
/// </summary>
public static class HotkeyKeyTable
{
    public const int CarbonCommandKey = 0x0100;
    public const int CarbonShiftKey = 0x0200;
    public const int CarbonOptionKey = 0x0800;
    public const int CarbonControlKey = 0x1000;

    public const int WindowsModAlt = 0x0001;
    public const int WindowsModControl = 0x0002;
    public const int WindowsModShift = 0x0004;
    public const int WindowsModWin = 0x0008;
    public const int WindowsModNoRepeat = 0x4000;

    private static readonly IReadOnlyDictionary<HotkeyKey, HotkeyKeyCodes> Table = Build();

    public static HotkeyKeyCodes Get(HotkeyKey key) =>
        Table.TryGetValue(key, out var codes)
            ? codes
            : throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown hotkey key.");

    public static int ToCarbonModifiers(HotkeyModifiers modifiers)
    {
        var result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            result |= CarbonCommandKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= CarbonShiftKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= CarbonOptionKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            result |= CarbonControlKey;
        }

        return result;
    }

    public static int ToWindowsModifiers(HotkeyModifiers modifiers)
    {
        var result = WindowsModNoRepeat;
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            result |= WindowsModControl;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= WindowsModAlt;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= WindowsModShift;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Meta))
        {
            result |= WindowsModWin;
        }

        return result;
    }

    private static IReadOnlyDictionary<HotkeyKey, HotkeyKeyCodes> Build()
    {
        var table = new Dictionary<HotkeyKey, HotkeyKeyCodes>
        {
            [HotkeyKey.A] = new(0x00, 0x41),
            [HotkeyKey.B] = new(0x0B, 0x42),
            [HotkeyKey.C] = new(0x08, 0x43),
            [HotkeyKey.D] = new(0x02, 0x44),
            [HotkeyKey.E] = new(0x0E, 0x45),
            [HotkeyKey.F] = new(0x03, 0x46),
            [HotkeyKey.G] = new(0x05, 0x47),
            [HotkeyKey.H] = new(0x04, 0x48),
            [HotkeyKey.I] = new(0x22, 0x49),
            [HotkeyKey.J] = new(0x26, 0x4A),
            [HotkeyKey.K] = new(0x28, 0x4B),
            [HotkeyKey.L] = new(0x25, 0x4C),
            [HotkeyKey.M] = new(0x2E, 0x4D),
            [HotkeyKey.N] = new(0x2D, 0x4E),
            [HotkeyKey.O] = new(0x1F, 0x4F),
            [HotkeyKey.P] = new(0x23, 0x50),
            [HotkeyKey.Q] = new(0x0C, 0x51),
            [HotkeyKey.R] = new(0x0F, 0x52),
            [HotkeyKey.S] = new(0x01, 0x53),
            [HotkeyKey.T] = new(0x11, 0x54),
            [HotkeyKey.U] = new(0x20, 0x55),
            [HotkeyKey.V] = new(0x09, 0x56),
            [HotkeyKey.W] = new(0x0D, 0x57),
            [HotkeyKey.X] = new(0x07, 0x58),
            [HotkeyKey.Y] = new(0x10, 0x59),
            [HotkeyKey.Z] = new(0x06, 0x5A),

            [HotkeyKey.D0] = new(0x1D, 0x30),
            [HotkeyKey.D1] = new(0x12, 0x31),
            [HotkeyKey.D2] = new(0x13, 0x32),
            [HotkeyKey.D3] = new(0x14, 0x33),
            [HotkeyKey.D4] = new(0x15, 0x34),
            [HotkeyKey.D5] = new(0x17, 0x35),
            [HotkeyKey.D6] = new(0x16, 0x36),
            [HotkeyKey.D7] = new(0x1A, 0x37),
            [HotkeyKey.D8] = new(0x1C, 0x38),
            [HotkeyKey.D9] = new(0x19, 0x39),

            [HotkeyKey.F1] = new(0x7A, 0x70),
            [HotkeyKey.F2] = new(0x78, 0x71),
            [HotkeyKey.F3] = new(0x63, 0x72),
            [HotkeyKey.F4] = new(0x76, 0x73),
            [HotkeyKey.F5] = new(0x60, 0x74),
            [HotkeyKey.F6] = new(0x61, 0x75),
            [HotkeyKey.F7] = new(0x62, 0x76),
            [HotkeyKey.F8] = new(0x64, 0x77),
            [HotkeyKey.F9] = new(0x65, 0x78),
            [HotkeyKey.F10] = new(0x6D, 0x79),
            [HotkeyKey.F11] = new(0x67, 0x7A),
            [HotkeyKey.F12] = new(0x6F, 0x7B),

            [HotkeyKey.Space] = new(0x31, 0x20),
            [HotkeyKey.Enter] = new(0x24, 0x0D),
            [HotkeyKey.Tab] = new(0x30, 0x09),
            [HotkeyKey.Escape] = new(0x35, 0x1B),
            [HotkeyKey.Backspace] = new(0x33, 0x08),
            [HotkeyKey.Insert] = new(0x72, 0x2D),
            [HotkeyKey.Home] = new(0x73, 0x24),
            [HotkeyKey.End] = new(0x77, 0x23),
            [HotkeyKey.PageUp] = new(0x74, 0x21),
            [HotkeyKey.PageDown] = new(0x79, 0x22),
            [HotkeyKey.Left] = new(0x7B, 0x25),
            [HotkeyKey.Right] = new(0x7C, 0x27),
            [HotkeyKey.Up] = new(0x7E, 0x26),
            [HotkeyKey.Down] = new(0x7D, 0x28),

            [HotkeyKey.Comma] = new(0x2B, 0xBC),
            [HotkeyKey.Period] = new(0x2F, 0xBE),
            [HotkeyKey.Slash] = new(0x2C, 0xBF),
            [HotkeyKey.Semicolon] = new(0x29, 0xBA),
            [HotkeyKey.Quote] = new(0x27, 0xDE),
            [HotkeyKey.BracketLeft] = new(0x21, 0xDB),
            [HotkeyKey.BracketRight] = new(0x1E, 0xDD),
            [HotkeyKey.Backslash] = new(0x2A, 0xDC),
            [HotkeyKey.Minus] = new(0x1B, 0xBD),
            [HotkeyKey.Equal] = new(0x18, 0xBB),
            [HotkeyKey.Grave] = new(0x32, 0xC0)
        };

        return table;
    }
}
