using System.Runtime.InteropServices;

namespace SSC.UI.Motion;

/// <summary>读取系统"减少动态效果"偏好；测试可用 SSC_REDUCE_MOTION=1 强制开启。</summary>
public static class MotionPreferences
{
    private static bool? _reduced;

    public static bool IsReduced => _reduced ??= Detect();

    private static bool Detect()
    {
        var overrideValue = Environment.GetEnvironmentVariable("SSC_REDUCE_MOTION");
        if (overrideValue is "1" or "true" or "TRUE")
        {
            return true;
        }

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var workspaceClass = objc_getClass("NSWorkspace");
                if (workspaceClass == IntPtr.Zero)
                {
                    return false;
                }

                var workspace = objc_msgSend_IntPtr(workspaceClass, sel_registerName("sharedWorkspace"));
                return workspace != IntPtr.Zero &&
                       objc_msgSend_bool(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion"));
            }

            if (OperatingSystem.IsWindows())
            {
                return SystemParametersInfo(0x1042, 0, out var enabled, 0) && !enabled;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }

        return false;
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);
}
