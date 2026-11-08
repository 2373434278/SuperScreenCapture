using SSC.Domain.Annotations;
using SSC.Domain.Geometry;

namespace SSC.Application.Editing;

public interface IAnnotationTool
{
    AnnotationToolKind Kind { get; }

    bool IsClickTool { get; }

    bool IsPathTool { get; }

    Annotation? Create(AnnotationToolInput input, AnnotationStyle style, int step);

    PixelRect? CreateCrop(AnnotationToolInput input);
}
