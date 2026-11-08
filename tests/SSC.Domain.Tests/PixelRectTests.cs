using SSC.Domain.Geometry;
using Xunit;

namespace SSC.Domain.Tests;

public sealed class PixelRectTests
{
    [Fact]
    public void FromPoints_NormalizesReverseDrag()
    {
        var rect = PixelRect.FromPoints(new PixelPoint(500, 400), new PixelPoint(100, 50));

        Assert.Equal(new PixelRect(100, 50, 400, 350), rect);
    }

    [Fact]
    public void Intersect_ClampsToOverlap()
    {
        var first = new PixelRect(-100, -50, 500, 300);
        var second = new PixelRect(200, 100, 500, 300);

        Assert.Equal(new PixelRect(200, 100, 200, 150), first.Intersect(second));
    }

    [Fact]
    public void Intersect_DisjointRects_ReturnsEmpty()
    {
        var first = new PixelRect(0, 0, 100, 100);
        var second = new PixelRect(200, 200, 50, 50);

        Assert.True(first.Intersect(second).IsEmpty);
    }

    [Fact]
    public void Contains_HandlesRightAndBottomExclusiveEdges()
    {
        var rect = new PixelRect(10, 10, 100, 50);

        Assert.True(rect.Contains(new PixelPoint(10, 10)));
        Assert.True(rect.Contains(new PixelPoint(109, 59)));
        Assert.False(rect.Contains(new PixelPoint(110, 59)));
        Assert.False(rect.Contains(new PixelPoint(109, 60)));
    }

    [Fact]
    public void Offset_PreservesSize()
    {
        var rect = new PixelRect(-1920, 0, 1920, 1080).Offset(1920, 20);

        Assert.Equal(new PixelRect(0, 20, 1920, 1080), rect);
    }

    [Fact]
    public void ClampTo_NegativeDesktop_KeepsVisiblePart()
    {
        var selection = new PixelRect(-2000, -100, 300, 300);
        var virtualDesktop = new PixelRect(-1920, 0, 3840, 1600);

        Assert.Equal(new PixelRect(-1920, 0, 220, 200), selection.ClampTo(virtualDesktop));
    }
}
