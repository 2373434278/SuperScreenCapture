using SSC.Application.Abstractions;
using SSC.Application.Hotkeys;

namespace SSC.Platform.Mac;

public sealed class MacHotkeyService : IHotkeyService
{
    private const int HotkeyAlreadyExistsStatus = -9878;

    private readonly Dictionary<string, uint> _nativeIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, Action> _callbacks = new();
    private readonly object _gate = new();
    private readonly MacNativeMethods.SscHotkeyCallback _callback;
    private uint _nextId = 1;
    private bool _disposed;

    public MacHotkeyService()
    {
        _callback = OnHotkeyPressed;
    }

    public HotkeyRegistrationResult Register(string id, HotkeyGesture gesture, Action callback)
    {
        if (string.IsNullOrWhiteSpace(id) || gesture is null || callback is null)
        {
            return HotkeyRegistrationResult.Fail(HotkeyRegistrationError.InvalidGesture);
        }

        if (_disposed)
        {
            return HotkeyRegistrationResult.Fail(HotkeyRegistrationError.Unknown, "The hotkey service has been disposed.");
        }

        HotkeyKeyCodes codes;
        try
        {
            codes = HotkeyKeyTable.Get(gesture.Key);
        }
        catch (ArgumentOutOfRangeException)
        {
            return HotkeyRegistrationResult.Fail(HotkeyRegistrationError.InvalidGesture);
        }

        lock (_gate)
        {
            if (_nativeIds.ContainsKey(id))
            {
                UnregisterCore(id);
            }

            var nativeId = _nextId++;
            _callbacks[nativeId] = callback;

            try
            {
                var status = MacNativeMethods.ssc_hotkey_register(
                    nativeId,
                    (uint)codes.MacKeyCode,
                    (uint)HotkeyKeyTable.ToCarbonModifiers(gesture.Modifiers),
                    _callback,
                    IntPtr.Zero);

                if (status == 0)
                {
                    _nativeIds[id] = nativeId;
                    return HotkeyRegistrationResult.Ok;
                }

                _callbacks.Remove(nativeId);
                var detail = MacNativeMethods.GetLastError();
                return status == HotkeyAlreadyExistsStatus
                    ? HotkeyRegistrationResult.Fail(HotkeyRegistrationError.AlreadyInUse, detail)
                    : HotkeyRegistrationResult.Fail(HotkeyRegistrationError.Unknown, detail);
            }
            catch (DllNotFoundException)
            {
                _callbacks.Remove(nativeId);
                return HotkeyRegistrationResult.Fail(
                    HotkeyRegistrationError.Unsupported,
                    "The native capture library is not available.");
            }
            catch (EntryPointNotFoundException)
            {
                _callbacks.Remove(nativeId);
                return HotkeyRegistrationResult.Fail(
                    HotkeyRegistrationError.Unsupported,
                    "The native library does not export the hotkey API.");
            }
        }
    }

    public bool Unregister(string id)
    {
        lock (_gate)
        {
            return UnregisterCore(id);
        }
    }

    public void UnregisterAll()
    {
        lock (_gate)
        {
            try
            {
                MacNativeMethods.ssc_hotkey_unregister_all();
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                // Native library unavailable; nothing to unregister.
            }

            _nativeIds.Clear();
            _callbacks.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
    }

    private bool UnregisterCore(string id)
    {
        if (!_nativeIds.Remove(id, out var nativeId))
        {
            return false;
        }

        _callbacks.Remove(nativeId);
        try
        {
            MacNativeMethods.ssc_hotkey_unregister(nativeId);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }

        return true;
    }

    private void OnHotkeyPressed(uint id, IntPtr userData)
    {
        Action? callback;
        lock (_gate)
        {
            _callbacks.TryGetValue(id, out callback);
        }

        try
        {
            callback?.Invoke();
        }
        catch
        {
            // A hotkey handler must never take down the Carbon event loop.
        }
    }
}
