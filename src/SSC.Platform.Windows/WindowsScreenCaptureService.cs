using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;

namespace SSC.Platform.Windows;

public sealed class WindowsScreenCaptureService : IScreenCaptureService
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow"
    };

    public Task<IReadOnlyList<DisplayDescriptor>> GetDisplaysAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DisplayDescriptor>>(() =>
        {
            var monitors = EnumerateMonitors();
            if (monitors.Count == 0)
            {
                throw new InvalidOperationException("No displays are available.");
            }

            var displays = new List<DisplayDescriptor>(monitors.Count);
            for (var index = 0; index < monitors.Count; index++)
            {
                var monitor = monitors[index];
                displays.Add(DisplayDescriptor.Create(
                    monitor.Handle.ToInt64().ToString(CultureInfo.InvariantCulture),
                    $"Display {index + 1}",
                    new PixelRect(
                        monitor.Info.Monitor.Left,
                        monitor.Info.Monitor.Top,
                        monitor.Info.Monitor.Width,
                        monitor.Info.Monitor.Height),
                    monitor.Scale,
                    (monitor.Info.Flags & WindowsNativeMethods.MonitorInfoPrimary) != 0));
            }

            return displays;
        }, cancellationToken);

    public Task<CaptureFrame> CaptureDisplayAsync(
        string displayId,
        PixelRect? region = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (!long.TryParse(displayId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var handleValue))
            {
                throw new ArgumentException($"Invalid display id '{displayId}'.", nameof(displayId));
            }

            var monitors = EnumerateMonitors();
            var monitor = monitors.FirstOrDefault(candidate => candidate.Handle.ToInt64() == handleValue);
            if (monitor.Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Display '{displayId}' is not available.");
            }

            var rect = monitor.Info.Monitor;
            var frame = CaptureScreenRect(rect.Left, rect.Top, rect.Width, rect.Height);
            return region is { IsEmpty: false } crop ? frame.Crop(crop) : frame;
        }, cancellationToken);

    public Task<CaptureFrame> CaptureWindowAsync(
        string windowId,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (!long.TryParse(windowId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var handleValue))
            {
                throw new ArgumentException($"Invalid window id '{windowId}'.", nameof(windowId));
            }

            var window = new IntPtr(handleValue);
            if (!WindowsNativeMethods.GetWindowRect(window, out var rect) || rect.Width <= 0 || rect.Height <= 0)
            {
                throw new InvalidOperationException($"Window '{windowId}' is not capturable.");
            }

            return CaptureBitmap(rect.Width, rect.Height, (memoryDc, screenDc) =>
            {
                if (WindowsNativeMethods.PrintWindow(window, memoryDc, WindowsNativeMethods.PwRenderFullContent))
                {
                    return;
                }

                if (!WindowsNativeMethods.BitBlt(
                        memoryDc,
                        0,
                        0,
                        rect.Width,
                        rect.Height,
                        screenDc,
                        rect.Left,
                        rect.Top,
                        WindowsNativeMethods.Srccopy | WindowsNativeMethods.CaptureBlt))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to capture the window.");
                }
            });
        }, cancellationToken);

    public Task<WindowDescriptor?> GetWindowAtPointAsync(
        PixelPoint point,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var window = WindowsNativeMethods.GetTopWindow(IntPtr.Zero);
            while (window != IntPtr.Zero)
            {
                if (WindowsNativeMethods.IsWindowVisible(window))
                {
                    WindowsNativeMethods.GetWindowThreadProcessId(window, out var processId);
                    var className = GetClassName(window);
                    if (processId != Environment.ProcessId
                        && !IgnoredClasses.Contains(className)
                        && WindowsNativeMethods.GetWindowRect(window, out var rect)
                        && rect.Width > 0
                        && rect.Height > 0
                        && point.X >= rect.Left
                        && point.X < rect.Right
                        && point.Y >= rect.Top
                        && point.Y < rect.Bottom)
                    {
                        return new WindowDescriptor(
                            window.ToInt64().ToString(CultureInfo.InvariantCulture),
                            GetWindowTitle(window),
                            new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height),
                            string.Empty);
                    }
                }

                window = WindowsNativeMethods.GetWindow(window, WindowsNativeMethods.GwHwndNext);
            }

            return null;
        }, cancellationToken);

    private static CaptureFrame CaptureScreenRect(int x, int y, int width, int height) =>
        CaptureBitmap(width, height, (memoryDc, screenDc) =>
        {
            if (!WindowsNativeMethods.BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    screenDc,
                    x,
                    y,
                    WindowsNativeMethods.Srccopy | WindowsNativeMethods.CaptureBlt))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to capture the display.");
            }
        });

    private static CaptureFrame CaptureBitmap(
        int width,
        int height,
        Action<IntPtr, IntPtr> draw)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Capture dimensions must be positive.");
        }

        var screenDc = WindowsNativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to acquire the screen device context.");
        }

        var memoryDc = WindowsNativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = WindowsNativeMethods.CreateCompatibleBitmap(screenDc, width, height);
        var previous = WindowsNativeMethods.SelectObject(memoryDc, bitmap);

        try
        {
            draw(memoryDc, screenDc);

            var info = new WindowsNativeMethods.BitmapInfo
            {
                Header = new WindowsNativeMethods.BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<WindowsNativeMethods.BitmapInfoHeader>(),
                    Width = width,
                    Height = height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = (uint)(width * height * 4)
                }
            };

            var stride = width * 4;
            var bottomUp = new byte[stride * height];
            var handle = GCHandle.Alloc(bottomUp, GCHandleType.Pinned);
            try
            {
                var lines = WindowsNativeMethods.GetDIBits(
                    memoryDc,
                    bitmap,
                    0,
                    (uint)height,
                    handle.AddrOfPinnedObject(),
                    ref info,
                    WindowsNativeMethods.DibRgbColors);

                if (lines == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read captured pixels.");
                }
            }
            finally
            {
                handle.Free();
            }

            var pixels = new byte[stride * height];
            for (var row = 0; row < height; row++)
            {
                Buffer.BlockCopy(bottomUp, (height - 1 - row) * stride, pixels, row * stride, stride);
            }

            for (var index = 3; index < pixels.Length; index += 4)
            {
                pixels[index] = 255;
            }

            return new CaptureFrame(width, height, pixels, DateTimeOffset.UtcNow);
        }
        finally
        {
            WindowsNativeMethods.SelectObject(memoryDc, previous);
            WindowsNativeMethods.DeleteObject(bitmap);
            WindowsNativeMethods.DeleteDC(memoryDc);
            WindowsNativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static List<(IntPtr Handle, WindowsNativeMethods.MonitorInfo Info, double Scale)> EnumerateMonitors()
    {
        var monitors = new List<(IntPtr Handle, WindowsNativeMethods.MonitorInfo Info, double Scale)>();
        WindowsNativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new WindowsNativeMethods.MonitorInfo
            {
                Size = Marshal.SizeOf<WindowsNativeMethods.MonitorInfo>()
            };

            if (!WindowsNativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            var scale = 1.0;
            try
            {
                if (WindowsNativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
                {
                    scale = dpiX / 96.0;
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            monitors.Add((monitor, info, scale));
            return true;
        };

        WindowsNativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return monitors;
    }

    private static string GetWindowTitle(IntPtr window)
    {
        var length = WindowsNativeMethods.GetWindowTextLength(window);
        if (length <= 0)
        {
            return "Window";
        }

        var title = new StringBuilder(length + 1);
        WindowsNativeMethods.GetWindowText(window, title, title.Capacity);
        return title.Length > 0 ? title.ToString() : "Window";
    }

    private static string GetClassName(IntPtr window)
    {
        var name = new StringBuilder(256);
        WindowsNativeMethods.GetClassName(window, name, name.Capacity);
        return name.ToString();
    }
}
