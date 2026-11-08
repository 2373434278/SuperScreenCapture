using SSC.Domain.Annotations;
using SSC.Domain.Geometry;

namespace SSC.Application.Editing;

/// <summary>
/// Editing facade used by the UI: tool selection, style, command history and document state.
/// </summary>
public sealed class AnnotationEditorSession
{
    private readonly Dictionary<AnnotationToolKind, IAnnotationTool> _tools;

    public AnnotationEditorSession(
        int width,
        int height,
        int historyCapacity = 100,
        IEnumerable<IAnnotationTool>? tools = null)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        _tools = (tools ?? AnnotationToolCatalog.Create())
            .ToDictionary(static tool => tool.Kind);
        History = new CommandHistory(AnnotationDocument.Create(width, height), historyCapacity);
        Style = AnnotationStyle.Default;
        ActiveTool = AnnotationToolKind.Rectangle;
    }

    public event EventHandler? Changed;

    public CommandHistory History { get; }

    public AnnotationDocument Document => History.Current;

    public AnnotationToolKind ActiveTool { get; private set; }

    public AnnotationStyle Style { get; private set; }

    public IAnnotationTool? ActiveStrategy =>
        _tools.TryGetValue(ActiveTool, out var tool) ? tool : null;

    public bool IsClickTool => ActiveStrategy?.IsClickTool == true;

    public bool IsPathTool => ActiveStrategy?.IsPathTool == true;

    public void SelectTool(AnnotationToolKind kind)
    {
        if (kind != AnnotationToolKind.Select && !_tools.ContainsKey(kind))
        {
            return;
        }

        ActiveTool = kind;
    }

    public void UpdateStyle(AnnotationStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Style = style.Normalize();
    }

    public Annotation? BuildAnnotation(AnnotationToolInput input)
    {
        var tool = ActiveStrategy;
        return tool?.Create(input, Style, Document.NextStepNumber());
    }

    public PixelRect? BuildCrop(AnnotationToolInput input) =>
        ActiveStrategy?.CreateCrop(input);

    public bool Commit(AnnotationToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (ActiveTool == AnnotationToolKind.Crop)
        {
            var crop = BuildCrop(input);
            if (crop is null)
            {
                return false;
            }

            return Execute(new CropCommand(crop.Value));
        }

        var annotation = BuildAnnotation(input);
        return annotation is not null && Execute(new AddAnnotationCommand(annotation));
    }

    public bool CommitAnnotation(Annotation annotation) =>
        Execute(new AddAnnotationCommand(annotation));

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= Document.Annotations.Count)
        {
            return false;
        }

        return Execute(new RemoveAnnotationCommand(index));
    }

    public bool ApplyCrop(PixelRect crop) => Execute(new CropCommand(crop));

    public bool Undo()
    {
        if (!History.Undo())
        {
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!History.Redo())
        {
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool Execute(IEditorCommand command)
    {
        if (!History.Execute(command))
        {
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
