using SSC.Application.Abstractions;

namespace SSC.Platform.Mock;

public sealed class MockHotkeyService : IHotkeyService
{
    private readonly Dictionary<string, Action> _callbacks = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Action> Registered => _callbacks;

    public bool Register(string id, string gesture, Action callback)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(gesture) || callback is null)
        {
            return false;
        }

        _callbacks[id] = callback;
        return true;
    }

    public bool Unregister(string id) =>
        !string.IsNullOrWhiteSpace(id) && _callbacks.Remove(id);

    public void UnregisterAll() => _callbacks.Clear();
}
