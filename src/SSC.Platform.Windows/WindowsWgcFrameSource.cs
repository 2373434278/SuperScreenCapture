using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace SSC.Platform.Windows;

internal interface IWindowsFrameSource : IDisposable
{
    void Start(Action<IntPtr, int> onFrame, Action<Exception> onError);

    void Stop();
}

internal sealed class WindowsWgcFrameSource : IWindowsFrameSource
{
    private readonly IntPtr _monitor;
    private readonly int _sourceLeft;
    private readonly int _sourceTop;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _staging;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private Action<IntPtr, int>? _onFrame;
    private Action<Exception>? _onError;
    private bool _running;

    public WindowsWgcFrameSource(IntPtr monitor, SSC.Domain.Geometry.PixelRect region)
    {
        _monitor = monitor;
        var levels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0
        };
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            levels,
            out ID3D11Device device,
            out ID3D11DeviceContext context).CheckError();
        _device = device;
        _context = context;

        var info = new WindowsNativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<WindowsNativeMethods.MonitorInfo>()
        };
        if (!WindowsNativeMethods.GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException("Unable to read the monitor bounds.");
        }

        _sourceLeft = region.X - info.Monitor.Left;
        _sourceTop = region.Y - info.Monitor.Top;
        _sourceWidth = Math.Max(2, region.Width);
        _sourceHeight = Math.Max(2, region.Height);
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_sourceWidth,
            Height = (uint)_sourceHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });
    }

    public void Start(Action<IntPtr, int> onFrame, Action<Exception> onError)
    {
        if (_running)
        {
            return;
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new PlatformNotSupportedException("Windows Graphics Capture is not supported.");
        }

        _onFrame = onFrame;
        _onError = onError;

        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<WindowsGraphicsInterop.IGraphicsCaptureItemInterop>();
        var iid = WindowsGraphicsInterop.GraphicsCaptureItemIid;
        var result = interop.CreateForMonitor(_monitor, ref iid, out var itemPointer);
        Marshal.ThrowExceptionForHR(result);
        _item = MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPointer);

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        var deviceResult = WindowsGraphicsInterop.CreateDirect3D11DeviceFromDXGIDevice(
            dxgiDevice.NativePointer,
            out var graphicsDevicePointer);
        Marshal.ThrowExceptionForHR(deviceResult);
        var direct3DDevice = MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevicePointer);

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _item.Size);
        _session = _framePool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = true;
        _framePool.FrameArrived += OnFrameArrived;
        _session.StartCapture();
        _running = true;
    }

    public void Stop()
    {
        if (_framePool is null && _session is null && _item is null)
        {
            return;
        }

        _running = false;
        if (_framePool is not null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            if (_framePool is IDisposable disposablePool)
            {
                disposablePool.Dispose();
            }
        }

        if (_session is IDisposable disposableSession)
        {
            disposableSession.Dispose();
        }

        _framePool = null;
        _session = null;
        _item = null;
    }

    public void Dispose()
    {
        Stop();
        _staging.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            var surface = frame.Surface;
            if (surface is null)
            {
                return;
            }

            var access = WinRT.CastExtensions.As<WindowsGraphicsInterop.IDirect3DDxgiInterfaceAccess>(surface);
            var iid = typeof(ID3D11Texture2D).GUID;
            var texturePointer = access.GetInterface(ref iid);
            using var texture = new ID3D11Texture2D(texturePointer);

            _context.CopySubresourceRegion(
                _staging,
                0,
                0,
                0,
                0,
                texture,
                0,
                new Box(
                    _sourceLeft,
                    _sourceTop,
                    0,
                    _sourceLeft + _sourceWidth,
                    _sourceTop + _sourceHeight,
                    1));

            var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                _onFrame?.Invoke(mapped.DataPointer, (int)mapped.RowPitch);
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }
        }
        catch (Exception exception)
        {
            _onError?.Invoke(exception);
        }
    }
}
