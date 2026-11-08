using System.ComponentModel;
using System.Runtime.InteropServices;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;

namespace SSC.Platform.Windows;

public sealed class WindowsClipboardService : IClipboardService
{
    public Task CopyImageAsync(CaptureFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Task.Run(() => CopyDib(frame), cancellationToken);
    }

    private static void CopyDib(CaptureFrame frame)
    {
        var opened = false;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (WindowsNativeMethods.OpenClipboard(IntPtr.Zero))
            {
                opened = true;
                break;
            }

            Thread.Sleep(40);
        }

        if (!opened)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the clipboard.");
        }

        try
        {
            if (!WindowsNativeMethods.EmptyClipboard())
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to clear the clipboard.");
            }

            var headerSize = Marshal.SizeOf<WindowsNativeMethods.BitmapInfoHeader>();
            var stride = frame.Width * 4;
            var totalSize = headerSize + (stride * frame.Height);
            var memory = WindowsNativeMethods.GlobalAlloc(WindowsNativeMethods.GmemMoveable, (UIntPtr)totalSize);
            if (memory == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to allocate clipboard memory.");
            }

            var pointer = WindowsNativeMethods.GlobalLock(memory);
            if (pointer == IntPtr.Zero)
            {
                WindowsNativeMethods.GlobalFree(memory);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to lock clipboard memory.");
            }

            try
            {
                var header = new WindowsNativeMethods.BitmapInfoHeader
                {
                    Size = (uint)headerSize,
                    Width = frame.Width,
                    Height = frame.Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = (uint)(stride * frame.Height)
                };
                Marshal.StructureToPtr(header, pointer, false);

                for (var row = 0; row < frame.Height; row++)
                {
                    Marshal.Copy(
                        frame.Pixels,
                        (frame.Height - 1 - row) * stride,
                        IntPtr.Add(pointer, headerSize + (row * stride)),
                        stride);
                }
            }
            finally
            {
                WindowsNativeMethods.GlobalUnlock(memory);
            }

            if (WindowsNativeMethods.SetClipboardData(WindowsNativeMethods.CfDib, memory) == IntPtr.Zero)
            {
                WindowsNativeMethods.GlobalFree(memory);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to place the image on the clipboard.");
            }
        }
        finally
        {
            WindowsNativeMethods.CloseClipboard();
        }
    }
}
