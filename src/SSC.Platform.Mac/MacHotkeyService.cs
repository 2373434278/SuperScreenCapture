using SSC.Application.Abstractions;

namespace SSC.Platform.Mac;

public sealed class MacHotkeyService : IHotkeyService
{
    public bool Register(string id, string gesture, Action callback) => false;

    public bool Unregister(string id) => false;

    public void UnregisterAll()
    {
    }
}
