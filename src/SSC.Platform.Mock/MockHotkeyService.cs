using SSC.Application.Abstractions;
using SSC.Application.Hotkeys;

namespace SSC.Platform.Mock;

public sealed class MockHotkeyService : IHotkeyService
{
    private readonly Dictionary<string, HotkeyGesture> _gestures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action> _callbacks = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, HotkeyGesture> Registered => _gestures;

    public IReadOnlyDictionary<string, Action> Callbacks => _callbacks;

    /// <summary>测试可注入注册失败策略（例如模拟快捷键被系统占用）。</summary>
    public Func<string, HotkeyGesture, HotkeyRegistrationError?>? FailurePolicy { get; set; }

    public HotkeyRegistrationResult Register(string id, HotkeyGesture gesture, Action callback)
    {
        if (string.IsNullOrWhiteSpace(id) || gesture is null || callback is null)
        {
            return HotkeyRegistrationResult.Fail(HotkeyRegistrationError.InvalidGesture);
        }

        var failure = FailurePolicy?.Invoke(id, gesture);
        if (failure is not null)
        {
            return HotkeyRegistrationResult.Fail(failure.Value);
        }

        _gestures[id] = gesture;
        _callbacks[id] = callback;
        return HotkeyRegistrationResult.Ok;
    }

    public bool Unregister(string id)
    {
        _callbacks.Remove(id);
        return _gestures.Remove(id);
    }

    public void UnregisterAll()
    {
        _gestures.Clear();
        _callbacks.Clear();
    }

    public void Dispose() => UnregisterAll();
}
