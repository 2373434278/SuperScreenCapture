using SSC.Application.Editing;
using SSC.Domain.Annotations;
using SSC.Domain.Capture;
using SSC.Infrastructure.Imaging;
using Xunit;
using DomainPixelRect = SSC.Domain.Geometry.PixelRect;
using DomainPixelPoint = SSC.Domain.Geometry.PixelPoint;

namespace SSC.Infrastructure.Tests;

public sealed class SkiaAnnotationRendererTests
{
    private readonly SkiaAnnotationRenderer _renderer = new();

    [Fact]
    public void Render_CropChangesOutputDimensions()
    {
        var source = CreateFrame(120, 80);
        var document = AnnotationDocument.Create(120, 80).WithCrop(new DomainPixelRect(20, 10, 60, 40));

        var output = _renderer.Render(source, document);

        Assert.Equal(60, output.Width);
        Assert.Equal(40, output.Height);
        Assert.Equal(Pixel(source, 20, 10), Pixel(output, 0, 0));
    }

    [Fact]
    public void Render_ScaleMultipliesOutputAndAnnotationGeometry()
    {
        var source = CreateFrame(120, 80);
        var document = AnnotationDocument.Create(120, 80).WithAnnotation(new Annotation(
            AnnotationKind.Rectangle,
            new DomainPixelRect(10, 10, 40, 30),
            new AnnotationStyle("#FF0000", 4, 24, 4)));

        var output = _renderer.Render(source, document, 2.0);

        Assert.Equal(240, output.Width);
        Assert.Equal(160, output.Height);
        var pixel = Pixel(output, 20, 80);
        Assert.True(pixel.R > 180, $"Expected red stroke at scaled position, got {pixel}.");
    }

    [Fact]
    public void Render_RectangleDrawsRedStroke()
    {
        var source = CreateFrame(120, 80);
        var document = AnnotationDocument.Create(120, 80).WithAnnotation(new Annotation(
            AnnotationKind.Rectangle,
            new DomainPixelRect(10, 10, 40, 30),
            new AnnotationStyle("#FF0000", 6, 24, 4)));

        var output = _renderer.Render(source, document);

        var pixel = Pixel(output, 10, 25);
        Assert.True(pixel.R > 200 && pixel.G < 90 && pixel.B < 90, $"Expected red stroke, got {pixel}.");
    }

    [Fact]
    public void Render_ArrowDrawsLineAndHead()
    {
        var source = CreateFrame(120, 80);
        var document = AnnotationDocument.Create(120, 80).WithAnnotation(new Annotation(
            AnnotationKind.Arrow,
            new DomainPixelRect(10, 10, 80, 50),
            new AnnotationStyle("#00FF00", 4, 24, 4),
            new[] { new DomainPixelPoint(10, 60), new DomainPixelPoint(90, 20) }));

        var output = _renderer.Render(source, document);

        var linePixel = Pixel(output, 50, 40);
        Assert.True(linePixel.G > 120, $"Expected green arrow near the middle, got {linePixel}.");
    }

    [Fact]
    public void Render_PenDrawsPolyline()
    {
        var source = CreateFrame(120, 80);
        var document = AnnotationDocument.Create(120, 80).WithAnnotation(new Annotation(
            AnnotationKind.Pen,
            new DomainPixelRect(10, 10, 80, 50),
            new AnnotationStyle("#00FFFF", 4, 24, 4),
            new[]
            {
                new DomainPixelPoint(10, 20),
                new DomainPixelPoint(40, 40),
                new DomainPixelPoint(90, 20)
            }));

        var output = _renderer.Render(source, document);

        var pixel = Pixel(output, 40, 40);
        Assert.True(pixel.G > 140 && pixel.B > 140, $"Expected cyan pen stroke, got {pixel}.");
    }

    [Fact]
    public void Render_HighlighterIsTranslucent()
    {
        var source = CreateFrame(120, 80);
        var document = AnnotationDocument.Create(120, 80).WithAnnotation(new Annotation(
            AnnotationKind.Highlighter,
            new DomainPixelRect(10, 30, 80, 1),
            new AnnotationStyle("#FFFF00", 4, 24, 4),
            new[] { new DomainPixelPoint(10, 30), new DomainPixelPoint(90, 30) }));

        var output = _renderer.Render(source, document);

        var pixel = Pixel(output, 50, 30);
        Assert.True(pixel.R > 40 && pixel.G > 40, $"Expected blended highlighter, got {pixel}.");
        Assert.True(pixel.R < 255, "Highlighter should preserve some background detail.");
    }

    [Fact]
    public void Render_MosaicPixelsWithinBlockMatch()
    {
        var source = CreateCheckerboard(120, 80);
        var document = AnnotationDocument.Create(120, 80).WithAnnotation(new Annotation(
            AnnotationKind.Mosaic,
            new DomainPixelRect(20, 20, 40, 40),
            new AnnotationStyle("#FFFFFF", 4, 24, 8)));

        var output = _renderer.Render(source, document);

        Assert.Equal(Pixel(output, 20, 20), Pixel(output, 23, 23));
        Assert.Equal(Pixel(output, 28, 28), Pixel(output, 31, 31));
    }

    [Fact]
    public void Render_BlurChangesPixelValues()
    {
        var source = CreateCheckerboard(120, 80);
        var document = AnnotationDocument.Create(120, 80).WithAnnotation(new Annotation(
            AnnotationKind.Blur,
            new DomainPixelRect(20, 20, 40, 40),
            new AnnotationStyle("#FFFFFF", 4, 24, 8)));

        var output = _renderer.Render(source, document);

        Assert.NotEqual(Pixel(source, 30, 30), Pixel(output, 30, 30));
    }

    [Fact]
    public void Render_StepDrawsCircleWithNumber()
    {
        var source = CreateFrame(120, 80);
        var document = AnnotationDocument.Create(120, 80).WithAnnotation(new Annotation(
            AnnotationKind.Step,
            new DomainPixelRect(35, 25, 30, 30),
            new AnnotationStyle("#FF4757", 4, 24, 4),
            step: 3));

        var output = _renderer.Render(source, document);

        var pixel = Pixel(output, 50, 40);
        Assert.True(pixel.R > 180, $"Expected step circle fill, got {pixel}.");
    }

    [Fact]
    public void Render_EmptyDocumentReturnsSourceSize()
    {
        var source = CreateFrame(120, 80);
        var output = _renderer.Render(source, AnnotationDocument.Create(120, 80));

        Assert.Equal(120, output.Width);
        Assert.Equal(80, output.Height);
        Assert.Equal(Pixel(source, 0, 0), Pixel(output, 0, 0));
    }

    [Fact]
    public async Task Render_AllTools_ProducesSamplePng()
    {
        var source = CreateFrame(360, 220);
        var document = AnnotationDocument.Create(360, 220)
            .WithAnnotation(new Annotation(
                AnnotationKind.Rectangle,
                new DomainPixelRect(20, 20, 90, 60),
                new AnnotationStyle("#FF4757", 4, 24, 4)))
            .WithAnnotation(new Annotation(
                AnnotationKind.Ellipse,
                new DomainPixelRect(130, 20, 90, 60),
                new AnnotationStyle("#FFB020", 4, 24, 4)))
            .WithAnnotation(new Annotation(
                AnnotationKind.Arrow,
                new DomainPixelRect(240, 20, 100, 70),
                new AnnotationStyle("#17C964", 4, 24, 4),
                new[] { new DomainPixelPoint(240, 80), new DomainPixelPoint(340, 30) }))
            .WithAnnotation(new Annotation(
                AnnotationKind.Pen,
                new DomainPixelRect(20, 100, 120, 60),
                new AnnotationStyle("#22D3EE", 4, 24, 4),
                new[]
                {
                    new DomainPixelPoint(20, 150),
                    new DomainPixelPoint(60, 110),
                    new DomainPixelPoint(100, 150),
                    new DomainPixelPoint(140, 110)
                }))
            .WithAnnotation(new Annotation(
                AnnotationKind.Highlighter,
                new DomainPixelRect(160, 100, 100, 20),
                new AnnotationStyle("#FFB020", 4, 24, 4),
                new[] { new DomainPixelPoint(160, 110), new DomainPixelPoint(260, 110) }))
            .WithAnnotation(new Annotation(
                AnnotationKind.Text,
                new DomainPixelRect(20, 180, 140, 30),
                new AnnotationStyle("#FFFFFF", 4, 20, 4),
                text: "Hello 中文"))
            .WithAnnotation(new Annotation(
                AnnotationKind.Mosaic,
                new DomainPixelRect(180, 150, 60, 50),
                new AnnotationStyle("#FFFFFF", 4, 24, 6)))
            .WithAnnotation(new Annotation(
                AnnotationKind.Blur,
                new DomainPixelRect(250, 150, 60, 50),
                new AnnotationStyle("#FFFFFF", 4, 24, 6)))
            .WithAnnotation(new Annotation(
                AnnotationKind.Step,
                new DomainPixelRect(320, 150, 34, 34),
                new AnnotationStyle("#5B5FF0", 4, 24, 4),
                step: 1));

        var output = _renderer.Render(source, document);
        var encoder = new SkiaImageEncoder();
        var bytes = await encoder.EncodeAsync(
            output,
            "png",
            95,
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(Path.GetTempPath(), "ssc-annotation-sample.png"),
            bytes,
            TestContext.Current.CancellationToken);

        Assert.Equal(360, output.Width);
        Assert.Equal(220, output.Height);
    }

    private static CaptureFrame CreateFrame(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 40;
            pixels[i + 1] = 60;
            pixels[i + 2] = 80;
            pixels[i + 3] = 255;
        }

        return new CaptureFrame(width, height, pixels, DateTimeOffset.UnixEpoch);
    }

    private static CaptureFrame CreateCheckerboard(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var value = ((x / 2) + (y / 2)) % 2 == 0 ? (byte)250 : (byte)20;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }

        return new CaptureFrame(width, height, pixels, DateTimeOffset.UnixEpoch);
    }

    private static (byte B, byte G, byte R, byte A) Pixel(CaptureFrame frame, int x, int y)
    {
        var offset = ((y * frame.Width) + x) * 4;
        return (frame.Pixels[offset], frame.Pixels[offset + 1], frame.Pixels[offset + 2], frame.Pixels[offset + 3]);
    }
}
