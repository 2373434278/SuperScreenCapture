using SSC.Application.Abstractions;

namespace SSC.Platform.Mock;

public sealed class MockAutoStartService : IAutoStartService
{
    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
