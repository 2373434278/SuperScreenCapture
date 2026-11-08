using System.Reflection;
using System.Runtime.InteropServices;

namespace SSC.Platform.Mac;

internal static class MacNativeMethods
{
    private const string LibraryName = "SSCMacKit";

    static MacNativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(MacNativeMethods).Assembly, Resolve);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SscImage
    {
        public uint Width;
        public uint Height;
        public uint Stride;
        public IntPtr Pixels;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SscDisplay
    {
        public uint Id;
        public int X;
        public int Y;
        public uint Width;
        public uint Height;
        public double Scale;
        public int Primary;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SscWindow
    {
        public uint Id;
        public int X;
        public int Y;
        public uint Width;
        public uint Height;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ssc_display_count();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ssc_display_get(int index, out SscDisplay display);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ssc_window_at_point(int x, int y, out SscWindow window);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ssc_capture_display(uint displayId, out SscImage image);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ssc_capture_window(uint windowId, out SscImage image);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ssc_preflight_permission();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ssc_request_permission();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ssc_copy_image_to_clipboard(
        byte[] pixels,
        uint width,
        uint height,
        uint stride);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ssc_last_error();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ssc_free_image(ref SscImage image);

    internal static string GetLastError()
    {
        var pointer = ssc_last_error();
        return pointer == IntPtr.Zero
            ? "Unknown native capture error."
            : Marshal.PtrToStringUTF8(pointer) ?? "Unknown native capture error.";
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "libSSCMacKit.dylib"),
            Path.Combine(Environment.CurrentDirectory, "artifacts", "native", "osx", "libSSCMacKit.dylib")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }
}
