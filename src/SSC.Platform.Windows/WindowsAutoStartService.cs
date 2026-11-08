using SSC.Application.Abstractions;

namespace SSC.Platform.Windows;

/// <summary>
/// Registry Run-key integration lands with the release milestone.
/// </summary>
public sealed class WindowsAutoStartService : IAutoStartService
{
    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }
}
