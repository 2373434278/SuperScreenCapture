using System.Runtime.InteropServices;
using System.Text;

namespace SSC.Platform.Windows;

internal static class WindowsNativeMethods
{
    internal const int MonitorInfoPrimary = 0x00000001;
    internal const uint Srccopy = 0x00CC0020;
    internal const uint CaptureBlt = 0x40000000;
    internal const uint DibRgbColors = 0;
    internal const uint PwRenderFullContent = 2;
    internal const uint GaRoot = 2;
    internal const uint GwHwndNext = 2;
    internal const uint GmemMoveable = 0x0002;
    internal const uint CfDib = 8;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public WinRect Monitor;
        public WinRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PointStruct
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public BitmapInfoHeader Header;
    }

    internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    internal static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint rop);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, IntPtr bits, ref BitmapInfo info, uint usage);

    [DllImport("user32.dll")]
    internal static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr window, out WinRect rect);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetTopWindow(IntPtr parent);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    internal static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    internal static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("user32.dll")]
    internal static extern bool CloseClipboard();

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    internal static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalFree(IntPtr memory);
}
