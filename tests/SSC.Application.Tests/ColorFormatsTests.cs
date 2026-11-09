using SSC.Application.Color;
using Xunit;

namespace SSC.Application.Tests;

public sealed class ColorFormatsTests
{
    [Fact]
    public void ToHex_FormatsUppercaseRgb()
    {
        Assert.Equal("#FF8800", ColorFormats.ToHex(255, 136, 0));
        Assert.Equal("#000000", ColorFormats.ToHex(0, 0, 0));
    }

    [Fact]
    public void ToRgb_FormatsCssSyntax()
    {
        Assert.Equal("rgb(12, 34, 56)", ColorFormats.ToRgb(12, 34, 56));
    }

    [Fact]
    public void ToHsl_HandlesPrimaries()
    {
        Assert.Equal("hsl(0, 100%, 50%)", ColorFormats.ToHsl(255, 0, 0));
        Assert.Equal("hsl(120, 100%, 50%)", ColorFormats.ToHsl(0, 255, 0));
        Assert.Equal("hsl(0, 0%, 0%)", ColorFormats.ToHsl(0, 0, 0));
    }

    [Fact]
    public void TryParseHex_RoundTrips()
    {
        var hex = ColorFormats.ToHex(18, 52, 86);
        Assert.True(ColorFormats.TryParseHex(hex, out var color));
        Assert.Equal((18, 52, 86), (color.R, color.G, color.B));

        Assert.False(ColorFormats.TryParseHex("123456", out _));
        Assert.False(ColorFormats.TryParseHex("#GGGGGG", out _));
    }
}
