using SSC.Domain.Annotations;

namespace SSC.Application.Editing;

public interface IEditorCommand
{
    AnnotationDocument Apply(AnnotationDocument document);
}

public sealed record AddAnnotationCommand(Annotation Annotation) : IEditorCommand
{
    public AnnotationDocument Apply(AnnotationDocument document) =>
        document.WithAnnotation(Annotation);
}

public sealed record CropCommand(SSC.Domain.Geometry.PixelRect Crop) : IEditorCommand
{
    public AnnotationDocument Apply(AnnotationDocument document) => document.WithCrop(Crop);
}

public sealed record RemoveAnnotationCommand(int Index) : IEditorCommand
{
    public AnnotationDocument Apply(AnnotationDocument document) =>
        document.RemoveAt(Index).RenumberSteps();
}

public sealed record ReplaceDocumentCommand(AnnotationDocument Next) : IEditorCommand
{
    public AnnotationDocument Apply(AnnotationDocument document) => Next;
}
