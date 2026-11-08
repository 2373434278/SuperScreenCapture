namespace SSC.Application.Abstractions;

public interface IAutoStartService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
