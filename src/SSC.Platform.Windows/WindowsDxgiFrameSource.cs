using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace SSC.Platform.Windows;

internal sealed class WindowsDxgiFrameSource : IWindowsFrameSource
{
    private readonly IntPtr _monitor;
    private readonly int _sourceLeft;
    private readonly int _sourceTop;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _staging;
    private readonly IDXGIOutputDuplication _duplication;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private Action<IntPtr, int>? _onFrame;
    private Action<Exception>? _onError;

    public WindowsDxgiFrameSource(IntPtr monitor, SSC.Domain.Geometry.PixelRect region)
    {
        _monitor = monitor;
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? selectedAdapter = null;
        IDXGIOutput? selectedOutput = null;

        for (uint adapterIndex = 0; selectedOutput is null; adapterIndex++)
        {
            if (factory.EnumAdapters1(adapterIndex, out var adapter).Failure)
            {
                break;
            }

            for (uint outputIndex = 0; ; outputIndex++)
            {
                if (adapter.EnumOutputs(outputIndex, out var output).Failure)
                {
                    break;
                }

                if (output.Description.Monitor == monitor)
                {
                    selectedAdapter = adapter;
                    selectedOutput = output;
                    break;
                }

                output.Dispose();
            }

            if (selectedAdapter is null)
            {
                adapter.Dispose();
            }
        }

        if (selectedAdapter is null || selectedOutput is null)
        {
            throw new InvalidOperationException("No DXGI output matches the monitor.");
        }

        try
        {
            var levels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0
            };
            D3D11.D3D11CreateDevice(
                selectedAdapter,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                levels,
                out ID3D11Device device,
                out ID3D11DeviceContext context).CheckError();
            _device = device;
            _context = context;

            using var output1 = selectedOutput.QueryInterface<IDXGIOutput1>();
            _duplication = output1.DuplicateOutput(_device);

            var desktop = selectedOutput.Description.DesktopCoordinates;
            _sourceLeft = region.X - desktop.Left;
            _sourceTop = region.Y - desktop.Top;
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
        finally
        {
            selectedOutput.Dispose();
            selectedAdapter.Dispose();
        }
    }

    public void Start(Action<IntPtr, int> onFrame, Action<Exception> onError)
    {
        if (_loop is not null)
        {
            return;
        }

        _onFrame = onFrame;
        _onError = onError;
        _cancellation = new CancellationTokenSource();
        _loop = Task.Run(() => CaptureLoop(_cancellation.Token));
    }

    public void Stop()
    {
        if (_loop is null)
        {
            return;
        }

        _cancellation?.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _loop = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose()
    {
        Stop();
        _duplication.Dispose();
        _staging.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = _duplication.AcquireNextFrame(100, out _, out var resource);
                if (result == Vortice.DXGI.ResultCode.WaitTimeout)
                {
                    continue;
                }

                if (result.Failure)
                {
                    throw new InvalidOperationException($"DXGI duplication failed: {result.Description}");
                }

                try
                {
                    using (resource)
                    using (var texture = resource.QueryInterface<ID3D11Texture2D>())
                    {
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

                        var mapped = _context.Map(
                            _staging,
                            0,
                            MapMode.Read,
                            Vortice.Direct3D11.MapFlags.None);
                        try
                        {
                            _onFrame?.Invoke(mapped.DataPointer, (int)mapped.RowPitch);
                        }
                        finally
                        {
                            _context.Unmap(_staging, 0);
                        }
                    }
                }
                finally
                {
                    _duplication.ReleaseFrame();
                }
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _onError?.Invoke(exception);
        }
    }
}
