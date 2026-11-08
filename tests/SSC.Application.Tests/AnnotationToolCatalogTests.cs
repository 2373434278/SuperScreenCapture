using SSC.Application.Editing;
using SSC.Domain.Annotations;
using SSC.Domain.Geometry;
using Xunit;

namespace SSC.Application.Tests;

public sealed class AnnotationToolCatalogTests
{
    private static readonly AnnotationStyle Style = AnnotationStyle.Default;

    [Fact]
    public void Catalog_ContainsTenAnnotationTools()
    {
        var tools = AnnotationToolCatalog.Create();

        Assert.Equal(10, tools.Count);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Rectangle);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Ellipse);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Arrow);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Pen);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Text);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Highlighter);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Mosaic);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Blur);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Step);
        Assert.Contains(tools, tool => tool.Kind == AnnotationToolKind.Crop);
    }

    [Fact]
    public void Rectangle_RequiresMinimumSize()
    {
        var tool = Get(AnnotationToolKind.Rectangle);

        Assert.Null(tool.Create(Input(new PixelPoint(10, 10), new PixelPoint(12, 12)), Style, 1));
        var annotation = tool.Create(Input(new PixelPoint(10, 10), new PixelPoint(30, 30)), Style, 1);

        Assert.NotNull(annotation);
        Assert.Equal(AnnotationKind.Rectangle, annotation!.Kind);
        Assert.Equal(new PixelRect(10, 10, 20, 20), annotation.Bounds);
    }

    [Fact]
    public void Arrow_RequiresMinimumLength()
    {
        var tool = Get(AnnotationToolKind.Arrow);

        Assert.Null(tool.Create(Input(new PixelPoint(10, 10), new PixelPoint(12, 12)), Style, 1));
        var annotation = tool.Create(Input(new PixelPoint(10, 10), new PixelPoint(50, 50)), Style, 1);

        Assert.NotNull(annotation);
        Assert.Equal(2, annotation!.Points.Count);
    }

    [Fact]
    public void Pen_RequiresTwoPointsAndComputesBounds()
    {
        var tool = Get(AnnotationToolKind.Pen);
        var points = new[]
        {
            new PixelPoint(20, 30),
            new PixelPoint(10, 15),
            new PixelPoint(60, 40)
        };

        var annotation = tool.Create(
            new AnnotationToolInput(points[0], points[^1], points),
            Style,
            1);

        Assert.NotNull(annotation);
        Assert.Equal(new PixelRect(10, 15, 50, 25), annotation!.Bounds);
    }

    [Fact]
    public void Text_RequiresNonEmptyContent()
    {
        var tool = Get(AnnotationToolKind.Text);

        Assert.Null(tool.Create(
            new AnnotationToolInput(new PixelPoint(10, 10), new PixelPoint(10, 10), Text: "  "),
            Style,
            1));

        var annotation = tool.Create(
            new AnnotationToolInput(new PixelPoint(10, 10), new PixelPoint(10, 10), Text: "Hello"),
            Style,
            1);

        Assert.NotNull(annotation);
        Assert.Equal("Hello", annotation!.Text);
    }

    [Fact]
    public void Step_CentersCircleOnPointAndKeepsNumber()
    {
        var tool = Get(AnnotationToolKind.Step);
        var annotation = tool.Create(Input(new PixelPoint(50, 60), new PixelPoint(50, 60)), Style, 3);

        Assert.NotNull(annotation);
        Assert.Equal(3, annotation!.Step);
        Assert.Equal(50, annotation.Bounds.X + (annotation.Bounds.Width / 2));
        Assert.Equal(60, annotation.Bounds.Y + (annotation.Bounds.Height / 2));
    }

    [Fact]
    public void Crop_RequiresMinimumSize()
    {
        var tool = Get(AnnotationToolKind.Crop);

        Assert.Null(tool.CreateCrop(Input(new PixelPoint(10, 10), new PixelPoint(15, 15))));
        Assert.Equal(
            new PixelRect(10, 10, 40, 30),
            tool.CreateCrop(Input(new PixelPoint(10, 10), new PixelPoint(50, 40))));
    }

    private static IAnnotationTool Get(AnnotationToolKind kind) =>
        AnnotationToolCatalog.Create().Single(tool => tool.Kind == kind);

    private static AnnotationToolInput Input(PixelPoint start, PixelPoint end) => new(start, end);
}
