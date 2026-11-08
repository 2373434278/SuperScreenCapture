using SSC.Domain.Geometry;
using SSC.Domain.Recording;
using Xunit;

namespace SSC.Domain.Tests;

public sealed class RecordingOptionsTests
{
    [Fact]
    public void OutputSize_PreservesRegionWhenBelowMaxHeight()
    {
        var options = Create(new PixelRect(0, 0, 1280, 720), maxHeight: 1080);

        Assert.Equal(new PixelSize(1280, 720), options.OutputSize);
    }

    [Fact]
    public void OutputSize_ScalesDownToMaxHeight()
    {
        var options = Create(new PixelRect(0, 0, 2560, 1440), maxHeight: 1080);

        Assert.Equal(new PixelSize(1920, 1080), options.OutputSize);
    }

    [Fact]
    public void OutputSize_RoundsOddDimensionsToEven()
    {
        var options = Create(new PixelRect(0, 0, 101, 51), maxHeight: 0);

        Assert.Equal(new PixelSize(100, 50), options.OutputSize);
    }

    [Fact]
    public void Normalize_ClampsInvalidValues()
    {
        var options = new RecordingOptions(
            "1",
            new PixelRect(0, 0, 640, 480),
            DisplayScale: 0,
            Fps: 500,
            BitrateMbps: 0,
            MaxHeight: 100,
            IncludeMicrophone: true).Normalize();

        Assert.Equal(1, options.DisplayScale);
        Assert.Equal(120, options.Fps);
        Assert.Equal(1, options.BitrateMbps);
        Assert.Equal(240, options.MaxHeight);
    }

    private static RecordingOptions Create(PixelRect region, int maxHeight) =>
        new("1", region, 2, 30, 12, maxHeight, true);
}
