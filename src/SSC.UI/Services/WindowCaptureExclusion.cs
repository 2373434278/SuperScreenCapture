using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace SSC.UI.Services;

/// <summary>
/// 把自有浮窗排除在系统捕获之外，避免贴图/控制条/快速访问被重复录入。
/// </summary>
internal static class WindowCaptureExclusion
{
    private const uint WdaExcludeFromCapture = 0x11;

    public static void Apply(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null)
            {
                return;
            }

            if (OperatingSystem.IsWindows() &&
                string.Equals(handle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
            {
                SetWindowDisplayAffinity(handle.Handle, WdaExcludeFromCapture);
            }
            else if (OperatingSystem.IsMacOS() &&
                     string.Equals(handle.HandleDescriptor, "NSWindow", StringComparison.OrdinalIgnoreCase))
            {
                var selector = sel_registerName("setSharingType:");
                objc_msgSend_IntPtr(handle.Handle, selector, 0);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, nint value);
}
