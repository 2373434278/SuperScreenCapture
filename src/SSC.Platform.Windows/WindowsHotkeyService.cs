using System.ComponentModel;
using System.Runtime.InteropServices;
using SSC.Application.Abstractions;
using SSC.Application.Hotkeys;

namespace SSC.Platform.Windows;

/// <summary>
/// 在独立线程上创建 message-only window，用 RegisterHotKey 注册系统级快捷键。
/// 该线程只负责收发 WM_HOTKEY，回调由 App 层再切回 UI 线程。
/// </summary>
public sealed class WindowsHotkeyService : IHotkeyService
{
    private const int WmDestroy = 0x0002;
    private const int WmHotkey = 0x0312;
    private const int WmApp = 0x8000;
    private const int WmInvoke = WmApp + 1;
    private const int WmStop = WmApp + 2;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly object _gate = new();
    private readonly Dictionary<string, Registration> _registered = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly Queue<Action> _pending = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly WndProcDelegate _wndProc;
    private Thread? _thread;
    private string? _className;
    private IntPtr _hwnd = IntPtr.Zero;
    private int _nextId = 1;
    private Exception? _startupError;
    private bool _disposed;

    public WindowsHotkeyService()
    {
        _wndProc = WindowProc;
        StartThread();
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

        if (_startupError is not null)
        {
            return HotkeyRegistrationResult.Fail(HotkeyRegistrationError.Unknown, _startupError.Message);
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

        return Invoke(
            () =>
            {
                lock (_gate)
                {
                    if (_registered.Remove(id, out var existing))
                    {
                        UnregisterHotKey(_hwnd, existing.NativeId);
                        _callbacks.Remove(existing.NativeId);
                    }

                    var nativeId = _nextId++;
                    if (!RegisterHotKey(
                            _hwnd,
                            nativeId,
                            (uint)HotkeyKeyTable.ToWindowsModifiers(gesture.Modifiers),
                            (uint)codes.WindowsVirtualKey))
                    {
                        var error = Marshal.GetLastWin32Error();
                        return error == ErrorHotkeyAlreadyRegistered
                            ? HotkeyRegistrationResult.Fail(HotkeyRegistrationError.AlreadyInUse)
                            : HotkeyRegistrationResult.Fail(
                                HotkeyRegistrationError.Unknown,
                                new Win32Exception(error).Message);
                    }

                    _registered[id] = new Registration(nativeId, gesture);
                    _callbacks[nativeId] = callback;
                    return HotkeyRegistrationResult.Ok;
                }
            },
            HotkeyRegistrationResult.Fail(HotkeyRegistrationError.Unknown, "The hotkey thread is unavailable."));
    }

    public bool Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return Invoke(
            () =>
            {
                lock (_gate)
                {
                    if (!_registered.Remove(id, out var existing))
                    {
                        return false;
                    }

                    _callbacks.Remove(existing.NativeId);
                    return UnregisterHotKey(_hwnd, existing.NativeId);
                }
            },
            fallback: false);
    }

    public void UnregisterAll()
    {
        Invoke(
            () =>
            {
                UnregisterAllCore();
                return true;
            },
            fallback: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var hwnd = _hwnd;
        if (hwnd != IntPtr.Zero)
        {
            PostMessage(hwnd, WmStop, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(TimeSpan.FromSeconds(3));
        _ready.Dispose();
    }

    private void StartThread()
    {
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "SSC.GlobalHotkeys"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            _startupError = new TimeoutException("The global hotkey window did not initialize.");
        }
    }

    private void MessageLoop()
    {
        var module = GetModuleHandle(null);
        _className = "SSC.GlobalHotkeyWindow." + Guid.NewGuid().ToString("N");

        try
        {
            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = module,
                ClassName = _className
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _hwnd = CreateWindowEx(
                0,
                _className,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                HwndMessage,
                IntPtr.Zero,
                module,
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _ready.Set();

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _startupError = exception;
            _ready.Set();
        }
        finally
        {
            if (_className is not null)
            {
                UnregisterClass(_className, module);
            }
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmHotkey:
                RaiseCallback(wParam.ToInt32());
                return IntPtr.Zero;
            case WmInvoke:
                DrainPending();
                return IntPtr.Zero;
            case WmStop:
                UnregisterAllCore();
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            case WmDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private void RaiseCallback(int nativeId)
    {
        Action? callback;
        lock (_gate)
        {
            _callbacks.TryGetValue(nativeId, out callback);
        }

        try
        {
            callback?.Invoke();
        }
        catch
        {
            // A hotkey handler must never take down the message loop.
        }
    }

    private void DrainPending()
    {
        while (true)
        {
            Action? action;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                action = _pending.Dequeue();
            }

            action();
        }
    }

    private void UnregisterAllCore()
    {
        int[] ids;
        lock (_gate)
        {
            ids = _callbacks.Keys.ToArray();
            _registered.Clear();
            _callbacks.Clear();
        }

        foreach (var id in ids)
        {
            UnregisterHotKey(_hwnd, id);
        }
    }

    private T Invoke<T>(Func<T> action, T fallback)
    {
        if (Thread.CurrentThread == _thread)
        {
            return action();
        }

        var hwnd = _hwnd;
        if (hwnd == IntPtr.Zero)
        {
            return _startupError is null ? action() : fallback;
        }

        var result = fallback;
        Exception? error = null;
        using var completed = new ManualResetEventSlim(false);
        lock (_gate)
        {
            _pending.Enqueue(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    completed.Set();
                }
            });
        }

        if (!PostMessage(hwnd, WmInvoke, IntPtr.Zero, IntPtr.Zero) ||
            !completed.Wait(TimeSpan.FromSeconds(3)))
        {
            return fallback;
        }

        return error is null ? result : fallback;
    }

    private readonly record struct Registration(int NativeId, HotkeyGesture Gesture);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public IntPtr MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref Msg msg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
