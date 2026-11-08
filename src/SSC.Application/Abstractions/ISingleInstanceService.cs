namespace SSC.Application.Abstractions;

public interface ISingleInstanceService : IDisposable
{
    bool TryAcquire();
}
