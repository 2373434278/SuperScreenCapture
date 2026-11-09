namespace SSC.Application.Abstractions;

public enum AutoStartState
{
    Disabled = 0,
    Enabled = 1,
    RequiresApproval = 2,
    Unavailable = 3
}

public sealed record AutoStartStatus(AutoStartState State, string? Detail = null)
{
    public bool IsEnabled => State == AutoStartState.Enabled;
}

public sealed record AutoStartChangeResult(bool Success, AutoStartState State, string? Error = null);

public interface IAutoStartService
{
    AutoStartStatus GetStatus();

    AutoStartChangeResult SetEnabled(bool enabled);
}
