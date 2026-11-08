namespace SSC.Application.Abstractions;

public interface IAppEventBus
{
    void Publish<T>(T message)
        where T : class;

    IDisposable Subscribe<T>(Action<T> handler)
        where T : class;
}
