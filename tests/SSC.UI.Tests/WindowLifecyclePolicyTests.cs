using Avalonia.Controls.ApplicationLifetimes;
using SSC.Platform.Abstractions;
using SSC.UI.Platform;
using Xunit;

namespace SSC.UI.Tests;

public sealed class WindowLifecyclePolicyTests
{
    [Theory]
    [InlineData(PlatformKind.MacOS, true, true)]
    [InlineData(PlatformKind.MacOS, false, true)]
    [InlineData(PlatformKind.Windows, true, true)]
    [InlineData(PlatformKind.Windows, false, false)]
    [InlineData(PlatformKind.Mock, true, true)]
    [InlineData(PlatformKind.Mock, false, false)]
    public void ShouldHideOnClose_FollowsPlatformConvention(PlatformKind platform, bool closeToTray, bool expected) =>
        Assert.Equal(expected, WindowLifecyclePolicy.ShouldHideOnClose(platform, closeToTray));

    [Theory]
    [InlineData(ActivationKind.File, false)]
    [InlineData(ActivationKind.OpenUri, false)]
    [InlineData(ActivationKind.Background, false)]
    public void ShouldShowOnActivation_IgnoresNonReopenKinds(ActivationKind kind, bool expected) =>
        Assert.Equal(expected, WindowLifecyclePolicy.ShouldShowOnActivation(kind));

    [Fact]
    public void ShouldShowOnActivation_ShowsForReopen() =>
        Assert.True(WindowLifecyclePolicy.ShouldShowOnActivation(ActivationKind.Reopen));
}
