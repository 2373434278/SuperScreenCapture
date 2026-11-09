using SSC.Application.Hotkeys;
using Xunit;

namespace SSC.Application.Tests;

public sealed class HotkeyKeyTableTests
{
    [Fact]
    public void EveryKey_HasBothPlatformCodes()
    {
        foreach (var key in Enum.GetValues<HotkeyKey>())
        {
            var codes = HotkeyKeyTable.Get(key);
            Assert.True(codes.MacKeyCode >= 0, $"{key} has no macOS key code.");
            Assert.True(codes.WindowsVirtualKey > 0, $"{key} has no Windows virtual key.");
        }
    }

    [Theory]
    [InlineData(HotkeyKey.D2, 0x13, 0x32)]
    [InlineData(HotkeyKey.A, 0x00, 0x41)]
    [InlineData(HotkeyKey.H, 0x04, 0x48)]
    [InlineData(HotkeyKey.F9, 0x65, 0x78)]
    [InlineData(HotkeyKey.Enter, 0x24, 0x0D)]
    public void SpotChecks_MatchPlatformTables(HotkeyKey key, int mac, int windows)
    {
        var codes = HotkeyKeyTable.Get(key);
        Assert.Equal(mac, codes.MacKeyCode);
        Assert.Equal(windows, codes.WindowsVirtualKey);
    }

    [Fact]
    public void ModifierMasks_MatchPlatformConstants()
    {
        const HotkeyModifiers all =
            HotkeyModifiers.Meta | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Control;

        Assert.Equal(
            HotkeyKeyTable.CarbonCommandKey | HotkeyKeyTable.CarbonShiftKey |
            HotkeyKeyTable.CarbonOptionKey | HotkeyKeyTable.CarbonControlKey,
            HotkeyKeyTable.ToCarbonModifiers(all));

        Assert.Equal(
            HotkeyKeyTable.WindowsModNoRepeat | HotkeyKeyTable.WindowsModWin |
            HotkeyKeyTable.WindowsModShift | HotkeyKeyTable.WindowsModAlt |
            HotkeyKeyTable.WindowsModControl,
            HotkeyKeyTable.ToWindowsModifiers(all));
    }
}
