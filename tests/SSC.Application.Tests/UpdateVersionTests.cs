using SSC.Application.Updates;
using Xunit;

namespace SSC.Application.Tests;

public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V2.0.0+build.9", 2, 0, 0)]
    [InlineData("1.4", 1, 4, 0)]
    [InlineData("3", 3, 0, 0)]
    public void TryParse_AcceptsCommonForms(string text, int major, int minor, int patch)
    {
        Assert.True(AppVersion.TryParse(text, out var version));
        Assert.NotNull(version);
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1.2.3.4")]
    [InlineData("one.two.three")]
    [InlineData("1.2.3-")]
    public void TryParse_RejectsInvalid(string text) =>
        Assert.False(AppVersion.TryParse(text, out _));

    [Fact]
    public void Compare_OrdersBySemVer()
    {
        Assert.True(AppVersion.Parse("1.2.0") > AppVersion.Parse("1.1.9"));
        Assert.True(AppVersion.Parse("2.0.0") > AppVersion.Parse("1.99.99"));
        Assert.True(AppVersion.Parse("1.2.3") > AppVersion.Parse("1.2.3-preview.1"));
        Assert.True(AppVersion.Parse("1.2.3-preview.2") > AppVersion.Parse("1.2.3-preview.1"));
        Assert.True(AppVersion.Parse("1.2.3-preview.10") > AppVersion.Parse("1.2.3-preview.9"));
        Assert.True(AppVersion.Parse("1.2.3-rc.1") > AppVersion.Parse("1.2.3-beta.1"));
        Assert.Equal(AppVersion.Parse("v1.2.3+build"), AppVersion.Parse("1.2.3"));
    }
}
