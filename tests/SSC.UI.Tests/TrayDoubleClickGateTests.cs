using SSC.UI.Platform;
using Xunit;

namespace SSC.UI.Tests;

public sealed class TrayDoubleClickGateTests
{
    [Fact]
    public void RegisterClick_ReportsDoubleClickWithinThresholdAndResets()
    {
        var gate = new TrayDoubleClickGate();

        Assert.False(gate.RegisterClick(0));
        Assert.True(gate.RegisterClick(250));

        // The previous pair was consumed, so t=300 starts a fresh sequence and
        // a triple click must not fire a second time.
        Assert.False(gate.RegisterClick(300));
        Assert.True(gate.RegisterClick(400));
    }

    [Fact]
    public void RegisterClick_TreatsThresholdExactlyAsNewSequence()
    {
        var gate = new TrayDoubleClickGate();

        Assert.False(gate.RegisterClick(0));
        Assert.False(gate.RegisterClick(500));
        Assert.True(gate.RegisterClick(700));
    }

    [Fact]
    public void RegisterClick_AcceptsInjectedThreshold()
    {
        var gate = new TrayDoubleClickGate(thresholdMs: 1000);

        Assert.False(gate.RegisterClick(0));
        Assert.True(gate.RegisterClick(999));
        Assert.False(gate.RegisterClick(2000));
    }
}
