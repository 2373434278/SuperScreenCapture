using SSC.Application.Abstractions;

namespace SSC.Platform.Mac;

/// <summary>
/// SMAppService integration lands with the release milestone.
/// </summary>
public sealed class MacAutoStartService : IAutoStartService
{
    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }
}
