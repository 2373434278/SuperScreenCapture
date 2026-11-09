using SSC.Application.Hotkeys;
using SSC.Platform.Abstractions;
using Xunit;

namespace SSC.Application.Tests;

public sealed class HotkeyGestureTests
{
    [Fact]
    public void TryParse_MacSymbols_YieldsCanonicalGesture()
    {
        Assert.True(HotkeyGesture.TryParse("⌘⇧2", out var gesture, out var error));

        Assert.Equal(HotkeyParseError.None, error);
        Assert.Equal(HotkeyModifiers.Meta | HotkeyModifiers.Shift, gesture.Modifiers);
        Assert.Equal(HotkeyKey.D2, gesture.Key);
    }

    [Fact]
    public void TryParse_WindowsNotation_MatchesSymbolNotation()
    {
        Assert.True(HotkeyGesture.TryParse("⌘⇧2", out var mac, out _));
        Assert.True(HotkeyGesture.TryParse("Meta+Shift+2", out var verbose, out _));
        Assert.True(HotkeyGesture.TryParse("⇧⌘2", out var reordered, out _));

        Assert.Equal(mac, verbose);
        Assert.Equal(mac, reordered);
    }

    [Fact]
    public void TryParse_SupportsAliasesAndDashNotation()
    {
        Assert.True(HotkeyGesture.TryParse("Cmd+Opt+Ctrl+H", out var aliases, out _));
        Assert.Equal(
            HotkeyModifiers.Meta | HotkeyModifiers.Alt | HotkeyModifiers.Control,
            aliases.Modifiers);
        Assert.Equal(HotkeyKey.H, aliases.Key);

        Assert.True(HotkeyGesture.TryParse("Ctrl-Shift-6", out var dash, out _));
        Assert.Equal(HotkeyKey.D6, dash.Key);
    }

    [Fact]
    public void TryParse_AllowsFunctionKeysWithoutModifiers()
    {
        Assert.True(HotkeyGesture.TryParse("F9", out var gesture, out _));
        Assert.Equal(HotkeyModifiers.None, gesture.Modifiers);
        Assert.Equal(HotkeyKey.F9, gesture.Key);
    }

    [Theory]
    [InlineData("", HotkeyParseError.Empty)]
    [InlineData("⌘⇧", HotkeyParseError.MissingKey)]
    [InlineData("2", HotkeyParseError.MissingModifier)]
    [InlineData("Ctrl+Shift+Nope", HotkeyParseError.UnknownToken)]
    [InlineData("Hyper+X", HotkeyParseError.UnknownToken)]
    public void TryParse_RejectsInvalidInput(string text, HotkeyParseError expected)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void ToDisplayString_RoundTripsOnBothPlatforms()
    {
        Assert.True(HotkeyGesture.TryParse("⌃⇧O", out var gesture, out _));

        var mac = gesture.ToDisplayString(PlatformKind.MacOS);
        var windows = gesture.ToDisplayString(PlatformKind.Windows);

        Assert.Equal("⇧⌃O", mac);
        Assert.Equal("Ctrl+Shift+O", windows);
        Assert.True(HotkeyGesture.TryParse(mac, out var macAgain, out _));
        Assert.Equal(gesture, macAgain);
        Assert.True(HotkeyGesture.TryParse(windows, out var windowsAgain, out _));
        Assert.Equal(gesture, windowsAgain);
    }

    [Fact]
    public void ToDisplayString_UsesPlatformKeyNames()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+Space", out var gesture, out _));

        Assert.Equal("⌥⌃Space", gesture.ToDisplayString(PlatformKind.MacOS));
        Assert.Equal("Ctrl+Alt+Space", gesture.ToDisplayString(PlatformKind.Windows));
    }
}
