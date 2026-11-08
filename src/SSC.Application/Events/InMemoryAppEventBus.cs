using SSC.Application.Abstractions;

namespace SSC.Application.Events;

public sealed class InMemoryAppEventBus : IAppEventBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<T>(T message)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message);

        Delegate[] handlers;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0)
            {
                return;
            }

            handlers = list.ToArray();
        }

        foreach (var handler in handlers)
        {
            ((Action<T>)handler)(message);
        }
    }

    public IDisposable Subscribe<T>(Action<T> handler)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }

            list.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                {
                    list.Remove(handler);
                }
            }
        });
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;

        public Subscription(Action dispose) => _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
