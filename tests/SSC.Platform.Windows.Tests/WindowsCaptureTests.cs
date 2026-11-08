using SSC.Platform.Windows;
using Xunit;

namespace SSC.Platform.Windows.Tests;

public sealed class WindowsCaptureTests
{
    [Fact]
    public async Task EnumerateDisplays_ReturnsAtLeastOne()
    {
        var service = new WindowsScreenCaptureService();

        var displays = await service.GetDisplaysAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(displays);
        Assert.Contains(displays, display => display.IsPrimary);
        Assert.All(displays, display =>
        {
            Assert.True(display.Bounds.Width > 0);
            Assert.True(display.Bounds.Height > 0);
            Assert.True(display.ScaleFactor > 0);
        });
    }

    [Fact]
    public async Task CapturePrimaryDisplay_ReturnsBgraPixels()
    {
        var service = new WindowsScreenCaptureService();
        var displays = await service.GetDisplaysAsync(TestContext.Current.CancellationToken);
        var primary = displays.First(display => display.IsPrimary);

        var frame = await service.CaptureDisplayAsync(
            primary.Id,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(frame.Width > 0);
        Assert.True(frame.Height > 0);
        Assert.Equal(frame.Width * frame.Height * 4, frame.Pixels.Length);
        Assert.Contains(frame.Pixels, value => value != 0);
    }

    [Fact]
    public async Task GetWindowAtPoint_ReturnsWindowOrNull()
    {
        var service = new WindowsScreenCaptureService();
        var displays = await service.GetDisplaysAsync(TestContext.Current.CancellationToken);
        var primary = displays.First(display => display.IsPrimary);
        var point = new SSC.Domain.Geometry.PixelPoint(
            primary.Bounds.X + (primary.Bounds.Width / 2),
            primary.Bounds.Y + (primary.Bounds.Height / 2));

        var window = await service.GetWindowAtPointAsync(
            point,
            TestContext.Current.CancellationToken);

        if (window is not null)
        {
            Assert.NotEmpty(window.Id);
            Assert.True(window.Bounds.Width > 0);
        }
    }
}
