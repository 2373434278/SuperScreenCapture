using SSC.Application.Abstractions;

namespace SSC.Platform.Mock;

public sealed class MockAutoStartService : IAutoStartService
{
    public AutoStartState State { get; set; } = AutoStartState.Disabled;

    public bool FailNextChange { get; set; }

    public AutoStartStatus GetStatus() => new(State);

    public AutoStartChangeResult SetEnabled(bool enabled)
    {
        if (FailNextChange)
        {
            FailNextChange = false;
            return new AutoStartChangeResult(false, State, "mock failure");
        }

        State = enabled ? AutoStartState.Enabled : AutoStartState.Disabled;
        return new AutoStartChangeResult(true, State);
    }
}
