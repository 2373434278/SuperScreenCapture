using SSC.Application.Settings;
using SSC.Platform.Abstractions;
using Xunit;

namespace SSC.Application.Tests;

public sealed class HotkeyCatalogTests
{
    [Fact]
    public void Defaults_ArePlatformSpecific()
    {
        var mac = HotkeyCatalog.CreateDefaults(PlatformKind.MacOS);
        var windows = HotkeyCatalog.CreateDefaults(PlatformKind.Windows);

        Assert.Equal("⌘⇧2", mac["captureRegion"]);
        Assert.Equal("Ctrl+Shift+2", windows["captureRegion"]);
    }

    [Fact]
    public void Defaults_ContainEveryAction()
    {
        var defaults = HotkeyCatalog.CreateDefaults(PlatformKind.MacOS);

        Assert.Equal(HotkeyCatalog.Actions.Count, defaults.Count);
        Assert.All(HotkeyCatalog.Actions, action => Assert.False(string.IsNullOrWhiteSpace(defaults[action.Key])));
    }

    [Fact]
    public void Defaults_HaveNoConflicts()
    {
        var defaults = HotkeyCatalog.CreateDefaults(PlatformKind.MacOS);

        Assert.Empty(HotkeyConflictDetector.Detect(defaults));
    }
}
