using SSC.Application.Abstractions;

namespace SSC.Platform.Mac;

public sealed class MacAutoStartService : IAutoStartService
{
    public AutoStartStatus GetStatus()
    {
        try
        {
            var status = MacNativeMethods.ssc_autostart_status();
            return new AutoStartStatus(Map(status), status == 4 ? MacNativeMethods.GetLastError() : null);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return new AutoStartStatus(AutoStartState.Unavailable, exception.Message);
        }
    }

    public AutoStartChangeResult SetEnabled(bool enabled)
    {
        try
        {
            var status = MacNativeMethods.ssc_autostart_set(enabled ? 1 : 0);
            var success = status is 0 or 1 or 2;
            return new AutoStartChangeResult(
                success,
                Map(status),
                success ? null : MacNativeMethods.GetLastError());
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return new AutoStartChangeResult(false, AutoStartState.Unavailable, exception.Message);
        }
    }

    private static AutoStartState Map(int status) => status switch
    {
        0 => AutoStartState.Disabled,
        1 => AutoStartState.Enabled,
        2 => AutoStartState.RequiresApproval,
        _ => AutoStartState.Unavailable
    };
}
