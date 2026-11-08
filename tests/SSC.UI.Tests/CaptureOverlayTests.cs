using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SSC.Domain.Capture;
using SSC.Infrastructure.Localization;
using SSC.UI.Capture;
using Xunit;

namespace SSC.UI.Tests;

public sealed class CaptureOverlayTests
{
    [AvaloniaFact]
    public void Overlay_RendersFrozenFrameAndHint()
    {
        var display = DisplayDescriptor.Create(
            "1",
            "Mock Display",
            new SSC.Domain.Geometry.PixelRect(0, 0, 480, 300),
            1,
            isPrimary: true);
        var frame = new CaptureFrame(480, 300, CreatePixels(480, 300), DateTimeOffset.UtcNow);
        var localizer = new JsonLocalizer();
        var window = new CaptureOverlayWindow(display, frame, localizer, _ => { });

        try
        {
            window.Show();
            var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            using var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-overlay.png"));
            rendered!.Save(stream, new PngBitmapEncoderOptions());
            Assert.True(rendered.PixelSize.Width >= 400);
            Assert.Equal(480, (int)window.Width);
            Assert.Equal(300, (int)window.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Overlay_SelectionCompletesWithPixelRect()
    {
        var display = DisplayDescriptor.Create(
            "1",
            "Mock Display",
            new SSC.Domain.Geometry.PixelRect(0, 0, 480, 300),
            1,
            isPrimary: true);
        var frame = new CaptureFrame(480, 300, CreatePixels(480, 300), DateTimeOffset.UtcNow);
        var localizer = new JsonLocalizer();
        CaptureSelection? captured = null;
        var window = new CaptureOverlayWindow(display, frame, localizer, selection => captured = selection);
        window.Show();

        window.MouseDown(new Point(100, 80), MouseButton.Left);
        window.MouseMove(new Point(340, 240));
        window.MouseUp(new Point(340, 240), MouseButton.Left);

        var selectionFrame = window.CaptureRenderedFrame();
        Assert.NotNull(selectionFrame);
        using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-overlay-selection.png")))
        {
            selectionFrame!.Save(stream, new PngBitmapEncoderOptions());
        }

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);

        Assert.NotNull(captured);
        Assert.InRange(captured!.Rect.X, 99, 101);
        Assert.InRange(captured.Rect.Y, 79, 81);
        Assert.InRange(captured.Rect.Width, 239, 241);
        Assert.InRange(captured.Rect.Height, 159, 161);
    }

    [AvaloniaFact]
    public async Task Overlay_WindowMode_HighlightsAndCompletesWindow()
    {
        var display = DisplayDescriptor.Create(
            "1",
            "Mock Display",
            new SSC.Domain.Geometry.PixelRect(0, 0, 480, 300),
            1,
            isPrimary: true);
        var frame = new CaptureFrame(480, 300, CreatePixels(480, 300), DateTimeOffset.UtcNow);
        var localizer = new JsonLocalizer();
        CaptureSelection? captured = null;
        var targetWindow = new WindowDescriptor(
            "123",
            "Mock Window",
            new SSC.Domain.Geometry.PixelRect(100, 80, 200, 140),
            "Mock");
        var window = new CaptureOverlayWindow(
            display,
            frame,
            localizer,
            selection => captured = selection,
            CaptureOverlayMode.Window,
            _ => Task.FromResult<WindowDescriptor?>(targetWindow));
        window.Show();

        window.MouseMove(new Point(200, 150));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var rendered = window.CaptureRenderedFrame();
        Assert.NotNull(rendered);
        using (var stream = File.Create(Path.Combine(Path.GetTempPath(), "ssc-ui-overlay-window.png")))
        {
            rendered!.Save(stream, new PngBitmapEncoderOptions());
        }

        window.MouseDown(new Point(200, 150), MouseButton.Left);

        Assert.NotNull(captured);
        Assert.NotNull(captured!.Window);
        Assert.Equal("123", captured.Window!.Id);
    }

    private static byte[] CreatePixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)(x * 255 / width);
                pixels[offset + 1] = (byte)(y * 255 / height);
                pixels[offset + 2] = 180;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }
}
