using SSC.Application.Hotkeys;

namespace SSC.Application.Abstractions;

public interface IHotkeyService : IDisposable
{
    HotkeyRegistrationResult Register(string id, HotkeyGesture gesture, Action callback);

    bool Unregister(string id);

    void UnregisterAll();
}
