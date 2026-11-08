using SSC.Application.Settings;
using Xunit;

namespace SSC.Application.Tests;

public sealed class HotkeyConflictDetectorTests
{
    [Fact]
    public void Detect_FindsDuplicateGesture()
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["captureRegion"] = "Ctrl+Shift+2",
            ["captureFull"] = "CTRL+SHIFT+2",
            ["record"] = "Ctrl+Shift+6"
        };

        var conflicts = HotkeyConflictDetector.Detect(bindings);

        var conflict = Assert.Single(conflicts);
        Assert.Equal("captureRegion", conflict.FirstAction);
        Assert.Equal("captureFull", conflict.SecondAction);
    }

    [Fact]
    public void Detect_IgnoresEmptyAndUnknownActions()
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["captureRegion"] = " ",
            ["unknown"] = "Ctrl+X"
        };

        Assert.Empty(HotkeyConflictDetector.Detect(bindings));
    }

    [Fact]
    public void Detect_AllowsDistinctGestures()
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["captureRegion"] = "⌘⇧2",
            ["captureFull"] = "⌘⇧3",
            ["record"] = "⌘⇧6"
        };

        Assert.Empty(HotkeyConflictDetector.Detect(bindings));
    }
}
