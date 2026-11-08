using SSC.Domain.Capture;
using SSC.Domain.Annotations;

namespace SSC.Application.Editing;

public interface IAnnotationRenderer
{
    CaptureFrame Render(CaptureFrame source, AnnotationDocument document, double scale = 1.0);
}
