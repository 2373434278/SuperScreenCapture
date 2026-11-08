using SSC.Domain.Annotations;
using SSC.Domain.Geometry;
using Xunit;

namespace SSC.Domain.Tests;

public sealed class AnnotationDocumentTests
{
    [Fact]
    public void Create_UsesFullFrameCrop()
    {
        var document = AnnotationDocument.Create(120, 80);

        Assert.Equal(120, document.Width);
        Assert.Equal(80, document.Height);
        Assert.Equal(new PixelRect(0, 0, 120, 80), document.Crop);
        Assert.Empty(document.Annotations);
    }

    [Fact]
    public void Constructor_ClampsCropToFrame()
    {
        var document = new AnnotationDocument(
            100,
            100,
            new PixelRect(-20, -10, 150, 150),
            Array.Empty<Annotation>());

        Assert.Equal(new PixelRect(0, 0, 100, 100), document.Crop);
    }

    [Fact]
    public void WithAnnotation_IsImmutable()
    {
        var document = AnnotationDocument.Create(100, 100);
        var annotation = new Annotation(
            AnnotationKind.Rectangle,
            new PixelRect(10, 10, 20, 20),
            AnnotationStyle.Default);

        var next = document.WithAnnotation(annotation);

        Assert.Empty(document.Annotations);
        Assert.Single(next.Annotations);
        Assert.Same(annotation, next.Annotations[0]);
    }

    [Fact]
    public void WithCrop_ClipsToCurrentCrop()
    {
        var document = AnnotationDocument.Create(100, 100)
            .WithCrop(new PixelRect(10, 10, 50, 50))
            .WithCrop(new PixelRect(40, 40, 100, 100));

        Assert.Equal(new PixelRect(40, 40, 20, 20), document.Crop);
    }

    [Fact]
    public void RemoveAt_RenumbersSteps()
    {
        var document = AnnotationDocument.Create(100, 100);
        document = document.WithAnnotation(CreateStep(1, 10));
        document = document.WithAnnotation(CreateStep(2, 30));
        document = document.WithAnnotation(CreateStep(3, 50));

        var next = document.RemoveAt(0).RenumberSteps();

        Assert.Equal(2, next.Annotations.Count);
        Assert.Equal(new[] { 1, 2 }, next.Annotations.Select(annotation => annotation.Step));
    }

    [Fact]
    public void FindLastAt_ReturnsTopmostAnnotation()
    {
        var document = AnnotationDocument.Create(100, 100)
            .WithAnnotation(new Annotation(
                AnnotationKind.Rectangle,
                new PixelRect(10, 10, 40, 40),
                AnnotationStyle.Default))
            .WithAnnotation(new Annotation(
                AnnotationKind.Rectangle,
                new PixelRect(20, 20, 40, 40),
                AnnotationStyle.Default));

        Assert.Equal(1, document.FindLastAt(new PixelPoint(25, 25)));
        Assert.Equal(0, document.FindLastAt(new PixelPoint(12, 12)));
        Assert.Equal(-1, document.FindLastAt(new PixelPoint(90, 90)));
    }

    [Fact]
    public void NextStepNumber_CountsExistingSteps()
    {
        var document = AnnotationDocument.Create(100, 100)
            .WithAnnotation(CreateStep(4, 10))
            .WithAnnotation(CreateStep(9, 20));

        Assert.Equal(10, document.NextStepNumber());
    }

    private static Annotation CreateStep(int step, int x) =>
        new(
            AnnotationKind.Step,
            new PixelRect(x, x, 30, 30),
            AnnotationStyle.Default,
            step: step);
}
