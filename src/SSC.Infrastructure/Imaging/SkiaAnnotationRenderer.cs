using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;
using SSC.Application.Editing;
using SSC.Domain.Annotations;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using DomainPixelRect = SSC.Domain.Geometry.PixelRect;

namespace SSC.Infrastructure.Imaging;

public sealed class SkiaAnnotationRenderer : IAnnotationRenderer
{
    public CaptureFrame Render(CaptureFrame source, AnnotationDocument document, double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);

        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            scale = 1.0;
        }

        var crop = document.Crop.Intersect(new DomainPixelRect(0, 0, source.Width, source.Height));
        if (crop.IsEmpty)
        {
            crop = new DomainPixelRect(0, 0, source.Width, source.Height);
        }

        var outputWidth = Math.Max(1, (int)Math.Round(crop.Width * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(crop.Height * scale));

        var sourceInfo = new SKImageInfo(
            source.Width,
            source.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var sourceBitmap = new SKBitmap(sourceInfo);
        Marshal.Copy(source.Pixels, 0, sourceBitmap.GetPixels(), source.Pixels.Length);

        var outputInfo = new SKImageInfo(
            outputWidth,
            outputHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var output = new SKBitmap(outputInfo);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Black);

        var sourceRect = new SKRect(crop.X, crop.Y, crop.Right, crop.Bottom);
        var destinationRect = new SKRect(0, 0, outputWidth, outputHeight);
        using var sourceImage = SKImage.FromBitmap(sourceBitmap);
        using var imagePaint = new SKPaint { IsAntialias = true };
        var sampling = scale == 1.0
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(sourceImage, sourceRect, destinationRect, sampling, imagePaint);

        canvas.Save();
        canvas.ClipRect(destinationRect);
        foreach (var annotation in document.Annotations)
        {
            DrawAnnotation(canvas, sourceBitmap, annotation, crop, scale);
        }

        canvas.Restore();

        var pixels = new byte[checked(outputWidth * outputHeight * 4)];
        Marshal.Copy(output.GetPixels(), pixels, 0, pixels.Length);
        return new CaptureFrame(outputWidth, outputHeight, pixels, source.CapturedAt);
    }

    private static void DrawAnnotation(
        SKCanvas canvas,
        SKBitmap source,
        Annotation annotation,
        DomainPixelRect crop,
        double scale)
    {
        var style = annotation.Style.Normalize();
        var color = ParseColor(style.Color);
        var strokeWidth = (float)Math.Max(1, style.StrokeWidth * scale);

        switch (annotation.Kind)
        {
            case AnnotationKind.Rectangle:
                DrawRectangle(canvas, annotation.Bounds, crop, scale, color, strokeWidth);
                break;
            case AnnotationKind.Ellipse:
                DrawEllipse(canvas, annotation.Bounds, crop, scale, color, strokeWidth);
                break;
            case AnnotationKind.Arrow:
                DrawArrow(canvas, annotation, crop, scale, color, strokeWidth);
                break;
            case AnnotationKind.Pen:
                DrawPen(canvas, annotation, crop, scale, color, strokeWidth);
                break;
            case AnnotationKind.Text:
                DrawText(canvas, annotation, crop, scale, color);
                break;
            case AnnotationKind.Highlighter:
                DrawHighlighter(canvas, annotation, crop, scale, color, strokeWidth);
                break;
            case AnnotationKind.Mosaic:
                DrawMosaic(canvas, source, annotation.Bounds, crop, scale, style.Intensity);
                break;
            case AnnotationKind.Blur:
                DrawBlur(canvas, source, annotation.Bounds, crop, scale, style.Intensity);
                break;
            case AnnotationKind.Step:
                DrawStep(canvas, annotation, crop, scale, color);
                break;
        }
    }

    private static void DrawRectangle(
        SKCanvas canvas,
        DomainPixelRect bounds,
        DomainPixelRect crop,
        double scale,
        SKColor color,
        float strokeWidth)
    {
        var rect = MapRect(bounds, crop, scale);
        using var fill = new SKPaint
        {
            Color = color.WithAlpha(26),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var stroke = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth
        };
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }

    private static void DrawEllipse(
        SKCanvas canvas,
        DomainPixelRect bounds,
        DomainPixelRect crop,
        double scale,
        SKColor color,
        float strokeWidth)
    {
        var rect = MapRect(bounds, crop, scale);
        using var fill = new SKPaint
        {
            Color = color.WithAlpha(26),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var stroke = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth
        };
        canvas.DrawOval(rect, fill);
        canvas.DrawOval(rect, stroke);
    }

    private static void DrawArrow(
        SKCanvas canvas,
        Annotation annotation,
        DomainPixelRect crop,
        double scale,
        SKColor color,
        float strokeWidth)
    {
        if (annotation.Points.Count < 2)
        {
            return;
        }

        var start = MapPoint(annotation.Points[0], crop, scale);
        var end = MapPoint(annotation.Points[1], crop, scale);
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var head = Math.Max((float)(12 * scale), strokeWidth * 3.2f);
        var lineEnd = new SKPoint(
            (float)(end.X - (Math.Cos(angle) * head * 0.6)),
            (float)(end.Y - (Math.Sin(angle) * head * 0.6)));

        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(start, lineEnd, paint);

        using var headPath = new SKPath();
        headPath.MoveTo(end);
        headPath.LineTo(
            (float)(end.X - (Math.Cos(angle - 0.42) * head)),
            (float)(end.Y - (Math.Sin(angle - 0.42) * head)));
        headPath.LineTo(
            (float)(end.X - (Math.Cos(angle + 0.42) * head)),
            (float)(end.Y - (Math.Sin(angle + 0.42) * head)));
        headPath.Close();

        using var headPaint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPath(headPath, headPaint);
    }

    private static void DrawPen(
        SKCanvas canvas,
        Annotation annotation,
        DomainPixelRect crop,
        double scale,
        SKColor color,
        float strokeWidth)
    {
        if (annotation.Points.Count == 0)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        if (annotation.Points.Count == 1)
        {
            canvas.DrawCircle(MapPoint(annotation.Points[0], crop, scale), strokeWidth / 2, paint);
            return;
        }

        using var path = new SKPath();
        path.MoveTo(MapPoint(annotation.Points[0], crop, scale));
        for (var i = 1; i < annotation.Points.Count; i++)
        {
            path.LineTo(MapPoint(annotation.Points[i], crop, scale));
        }

        canvas.DrawPath(path, paint);
    }

    private static void DrawText(
        SKCanvas canvas,
        Annotation annotation,
        DomainPixelRect crop,
        double scale,
        SKColor color)
    {
        if (string.IsNullOrWhiteSpace(annotation.Text))
        {
            return;
        }

        var point = MapPoint(new PixelPoint(annotation.Bounds.X, annotation.Bounds.Y), crop, scale);
        var size = (float)Math.Max(8, annotation.Style.Normalize().FontSize * scale);
        using var typeface = ResolveTypeface(annotation.Text);
        using var font = new SKFont(typeface, size) { Embolden = true };
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true
        };

        var metrics = font.Metrics;
        canvas.DrawText(
            annotation.Text,
            point.X,
            point.Y - metrics.Ascent,
            font,
            paint);
    }

    private static void DrawHighlighter(
        SKCanvas canvas,
        Annotation annotation,
        DomainPixelRect crop,
        double scale,
        SKColor color,
        float strokeWidth)
    {
        if (annotation.Points.Count < 2)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Color = color.WithAlpha(90),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max((float)(10 * scale), strokeWidth * 4),
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine(
            MapPoint(annotation.Points[0], crop, scale),
            MapPoint(annotation.Points[1], crop, scale),
            paint);
    }

    private static void DrawMosaic(
        SKCanvas canvas,
        SKBitmap source,
        DomainPixelRect bounds,
        DomainPixelRect crop,
        double scale,
        int intensity)
    {
        var region = bounds.Intersect(crop);
        if (region.IsEmpty)
        {
            return;
        }

        var block = Math.Max(2, intensity);
        using var paint = new SKPaint { IsAntialias = false };

        for (var y = region.Y; y < region.Bottom; y += block)
        {
            for (var x = region.X; x < region.Right; x += block)
            {
                var sampleX = Math.Clamp(x + (block / 2), 0, source.Width - 1);
                var sampleY = Math.Clamp(y + (block / 2), 0, source.Height - 1);
                paint.Color = source.GetPixel(sampleX, sampleY);

                var width = Math.Min(block, region.Right - x);
                var height = Math.Min(block, region.Bottom - y);
                var destination = MapRect(new DomainPixelRect(x, y, width, height), crop, scale);
                destination.Right = (float)Math.Ceiling(destination.Right);
                destination.Bottom = (float)Math.Ceiling(destination.Bottom);
                canvas.DrawRect(destination, paint);
            }
        }
    }

    private static void DrawBlur(
        SKCanvas canvas,
        SKBitmap source,
        DomainPixelRect bounds,
        DomainPixelRect crop,
        double scale,
        int intensity)
    {
        var region = bounds.Intersect(crop).Intersect(new DomainPixelRect(0, 0, source.Width, source.Height));
        if (region.IsEmpty)
        {
            return;
        }

        var sigma = (float)Math.Max(2, intensity * 1.5) * (float)scale;
        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        using var paint = new SKPaint
        {
            ImageFilter = filter,
            IsAntialias = true
        };

        var sourceRect = new SKRect(region.X, region.Y, region.Right, region.Bottom);
        var destinationRect = MapRect(region, crop, scale);
        canvas.Save();
        canvas.ClipRect(destinationRect);
        canvas.DrawBitmap(source, sourceRect, destinationRect, paint);
        canvas.Restore();
    }

    private static void DrawStep(
        SKCanvas canvas,
        Annotation annotation,
        DomainPixelRect crop,
        double scale,
        SKColor color)
    {
        var style = annotation.Style.Normalize();
        var radius = (float)Math.Max(12, style.FontSize * 0.65) * (float)scale;
        var center = MapPoint(
            new PixelPoint(annotation.Bounds.X + (annotation.Bounds.Width / 2), annotation.Bounds.Y + (annotation.Bounds.Height / 2)),
            crop,
            scale);

        using var fill = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var outline = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 230),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.5f, (float)(2 * scale))
        };
        canvas.DrawCircle(center, radius, fill);
        canvas.DrawCircle(center, radius, outline);

        var text = annotation.Step.ToString(CultureInfo.InvariantCulture);
        using var typeface = ResolveTypeface("8");
        using var font = new SKFont(typeface, radius * 0.95f) { Embolden = true };
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        var metrics = font.Metrics;
        var textWidth = font.MeasureText(text);
        var baseline = center.Y - ((metrics.Ascent + metrics.Descent) / 2);
        canvas.DrawText(text, center.X - (textWidth / 2), baseline, font, textPaint);
    }

    private static SKRect MapRect(DomainPixelRect rect, DomainPixelRect crop, double scale) =>
        new(
            (float)((rect.X - crop.X) * scale),
            (float)((rect.Y - crop.Y) * scale),
            (float)(rect.Right - crop.X) * (float)scale,
            (float)(rect.Bottom - crop.Y) * (float)scale);

    private static SKPoint MapPoint(PixelPoint point, DomainPixelRect crop, double scale) =>
        new(
            (float)((point.X - crop.X) * scale),
            (float)((point.Y - crop.Y) * scale));

    private static SKTypeface ResolveTypeface(string text)
    {
        foreach (var character in text)
        {
            if (character <= 0x7F)
            {
                continue;
            }

            var matched = SKFontManager.Default.MatchCharacter(character);
            if (matched is not null)
            {
                return matched;
            }
        }

        return SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
    }

    private static SKColor ParseColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SKColors.Red;
        }

        var hex = value.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            hex = "FF" + hex;
        }

        if (hex.Length == 8 &&
            uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return new SKColor(
                (byte)(argb >> 16),
                (byte)(argb >> 8),
                (byte)argb,
                (byte)(argb >> 24));
        }

        return SKColors.Red;
    }
}
