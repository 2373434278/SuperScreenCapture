namespace SSC.Application.Abstractions;

public interface IHotkeyService
{
    bool Register(string id, string gesture, Action callback);

    bool Unregister(string id);

    void UnregisterAll();
}
