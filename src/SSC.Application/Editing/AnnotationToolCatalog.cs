using SSC.Domain.Annotations;
using SSC.Domain.Geometry;

namespace SSC.Application.Editing;

/// <summary>
/// Strategy catalog for the ten annotation tools. Each tool validates its own gesture and
/// produces an immutable <see cref="Annotation"/>.
/// </summary>
public static class AnnotationToolCatalog
{
    public static IReadOnlyList<IAnnotationTool> Create() =>
        new IAnnotationTool[]
        {
            new ShapeTool(AnnotationToolKind.Rectangle, AnnotationKind.Rectangle),
            new ShapeTool(AnnotationToolKind.Ellipse, AnnotationKind.Ellipse),
            new LineTool(AnnotationToolKind.Arrow, AnnotationKind.Arrow),
            new PenTool(),
            new TextTool(),
            new LineTool(AnnotationToolKind.Highlighter, AnnotationKind.Highlighter),
            new ShapeTool(AnnotationToolKind.Mosaic, AnnotationKind.Mosaic),
            new ShapeTool(AnnotationToolKind.Blur, AnnotationKind.Blur),
            new StepTool(),
            new CropTool()
        };

    private abstract class AnnotationToolBase : IAnnotationTool
    {
        public abstract AnnotationToolKind Kind { get; }

        public virtual bool IsClickTool => false;

        public virtual bool IsPathTool => false;

        public abstract Annotation? Create(AnnotationToolInput input, AnnotationStyle style, int step);

        public virtual PixelRect? CreateCrop(AnnotationToolInput input) => null;

        protected static PixelRect Bounds(AnnotationToolInput input) =>
            PixelRect.FromPoints(input.Start, input.End);
    }

    private sealed class ShapeTool : AnnotationToolBase
    {
        private readonly AnnotationKind _kind;

        public ShapeTool(AnnotationToolKind toolKind, AnnotationKind kind)
        {
            Kind = toolKind;
            _kind = kind;
        }

        public override AnnotationToolKind Kind { get; }

        public override Annotation? Create(AnnotationToolInput input, AnnotationStyle style, int step)
        {
            var bounds = Bounds(input);
            return bounds.Width < 4 || bounds.Height < 4
                ? null
                : new Annotation(_kind, bounds, style);
        }
    }

    private sealed class LineTool : AnnotationToolBase
    {
        private readonly AnnotationKind _kind;

        public LineTool(AnnotationToolKind toolKind, AnnotationKind kind)
        {
            Kind = toolKind;
            _kind = kind;
        }

        public override AnnotationToolKind Kind { get; }

        public override Annotation? Create(AnnotationToolInput input, AnnotationStyle style, int step)
        {
            var dx = input.End.X - input.Start.X;
            var dy = input.End.Y - input.Start.Y;
            if (Math.Sqrt((dx * dx) + (dy * dy)) < 6)
            {
                return null;
            }

            return new Annotation(
                _kind,
                Bounds(input),
                style,
                new[] { input.Start, input.End });
        }
    }

    private sealed class PenTool : AnnotationToolBase
    {
        public override AnnotationToolKind Kind => AnnotationToolKind.Pen;

        public override bool IsPathTool => true;

        public override Annotation? Create(AnnotationToolInput input, AnnotationStyle style, int step)
        {
            var points = input.Points;
            if (points is null || points.Count < 2)
            {
                return null;
            }

            var left = points.Min(static point => point.X);
            var top = points.Min(static point => point.Y);
            var right = points.Max(static point => point.X);
            var bottom = points.Max(static point => point.Y);

            return new Annotation(
                AnnotationKind.Pen,
                new PixelRect(left, top, right - left, bottom - top),
                style,
                points);
        }
    }

    private sealed class TextTool : AnnotationToolBase
    {
        public override AnnotationToolKind Kind => AnnotationToolKind.Text;

        public override bool IsClickTool => true;

        public override Annotation? Create(AnnotationToolInput input, AnnotationStyle style, int step)
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                return null;
            }

            var width = Math.Max(1, (int)Math.Ceiling(input.Text.Length * style.FontSize * 0.72));
            var height = Math.Max(1, (int)Math.Ceiling(style.FontSize * 1.4));
            return new Annotation(
                AnnotationKind.Text,
                new PixelRect(input.Start.X, input.Start.Y, width, height),
                style,
                text: input.Text);
        }
    }

    private sealed class StepTool : AnnotationToolBase
    {
        public override AnnotationToolKind Kind => AnnotationToolKind.Step;

        public override bool IsClickTool => true;

        public override Annotation? Create(AnnotationToolInput input, AnnotationStyle style, int step)
        {
            var radius = Math.Max(12, (int)Math.Round(style.FontSize * 0.65));
            return new Annotation(
                AnnotationKind.Step,
                new PixelRect(input.Start.X - radius, input.Start.Y - radius, radius * 2, radius * 2),
                style,
                step: step);
        }
    }

    private sealed class CropTool : AnnotationToolBase
    {
        public override AnnotationToolKind Kind => AnnotationToolKind.Crop;

        public override Annotation? Create(AnnotationToolInput input, AnnotationStyle style, int step) => null;

        public override PixelRect? CreateCrop(AnnotationToolInput input)
        {
            var bounds = Bounds(input);
            return bounds.Width < 10 || bounds.Height < 10 ? null : bounds;
        }
    }
}
