using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SSC.Application.Abstractions;
using DomainPixelRect = SSC.Domain.Geometry.PixelRect;

namespace SSC.UI.Recording;

public partial class CameraPreviewWindow : Window
{
    private readonly ICameraPreviewService _camera;
    private readonly ILocalizer _localizer;
    private readonly DomainPixelRect? _region;
    private WriteableBitmap? _bitmap;
    private bool _mirrored;
    private bool _rounded = true;
    private bool _started;

    public CameraPreviewWindow(
        ICameraPreviewService camera,
        ILocalizer localizer,
        DomainPixelRect? region = null)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _region = region;

        InitializeComponent();
        BuildContextMenu();
        ApplyShape();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PositionBottomRight();
        _started = true;
        _ = StartCameraAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _started = false;
        _ = _camera.StopAsync();
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnClosed(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var next = Math.Clamp(Width + (e.Delta.Y > 0 ? 20 : -20), 120, 420);
        Width = next;
        Height = next;
        ApplyShape();
        e.Handled = true;
    }

    private async Task StartCameraAsync()
    {
        try
        {
            await _camera.StartAsync(OnFrame);
            if (!_started)
            {
                await _camera.StopAsync();
            }
        }
        catch (Exception exception)
        {
            if (!_started)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ErrorText.Text = $"{_localizer["cameraUnavailable"]}: {exception.Message}";
                ErrorPanel.IsVisible = true;
            });
        }
    }

    private void OnFrame(CameraFrame frame)
    {
        if (!_started)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => UpdateFrame(frame));
    }

    private void UpdateFrame(CameraFrame frame)
    {
        if (!_started)
        {
            return;
        }

        if (_bitmap is null ||
            _bitmap.PixelSize.Width != frame.Width ||
            _bitmap.PixelSize.Height != frame.Height)
        {
            var previous = _bitmap;
            _bitmap = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            PreviewImage.Source = _bitmap;
            previous?.Dispose();
        }

        using var buffer = _bitmap.Lock();
        for (var row = 0; row < frame.Height; row++)
        {
            Marshal.Copy(
                frame.Pixels,
                row * frame.Stride,
                IntPtr.Add(buffer.Address, row * buffer.RowBytes),
                Math.Min(frame.Stride, buffer.RowBytes));
        }

        PreviewImage.InvalidateVisual();
        ErrorPanel.IsVisible = false;
    }

    private void BuildContextMenu()
    {
        var mirror = new MenuItem { Header = _localizer["cameraMirror"] };
        mirror.Click += (_, _) =>
        {
            _mirrored = !_mirrored;
            ApplyMirror();
        };

        var rounded = new MenuItem { Header = _localizer["cameraRounded"] };
        rounded.Click += (_, _) =>
        {
            _rounded = !_rounded;
            ApplyShape();
        };

        var close = new MenuItem { Header = _localizer["cameraClose"] };
        close.Click += (_, _) => Close();

        ContextMenu = new ContextMenu
        {
            Items = { mirror, rounded, close }
        };
    }

    private void ApplyMirror()
    {
        PreviewImage.RenderTransformOrigin = RelativePoint.Center;
        PreviewImage.RenderTransform = _mirrored
            ? new ScaleTransform(-1, 1)
            : new ScaleTransform(1, 1);
    }

    private void ApplyShape()
    {
        if (_rounded)
        {
            FrameBorder.CornerRadius = new CornerRadius(Math.Max(1, Width / 2));
        }
        else
        {
            FrameBorder.CornerRadius = new CornerRadius(16);
        }
    }

    private void PositionBottomRight()
    {
        if (_region is { IsEmpty: false } region)
        {
            Position = new PixelPoint(
                region.Right - (int)Math.Round(Width) - 16,
                region.Bottom - (int)Math.Round(Height) - 16);
            return;
        }

        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        Position = new PixelPoint(
            area.X + area.Width - (int)Math.Round(Width) - 24,
            area.Y + area.Height - (int)Math.Round(Height) - 24);
    }
}
