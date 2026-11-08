using SSC.Domain.Annotations;

namespace SSC.Application.Editing;

/// <summary>
/// Command + Memento history. Every command returns a new immutable document; the previous
/// document snapshot is stored for undo, so commands never mutate shared state.
/// </summary>
public sealed class CommandHistory
{
    private readonly List<AnnotationDocument> _undo = new();
    private readonly List<AnnotationDocument> _redo = new();
    private readonly int _capacity;

    public CommandHistory(AnnotationDocument initial, int capacity = 100)
    {
        Current = initial ?? throw new ArgumentNullException(nameof(initial));
        _capacity = Math.Max(1, capacity);
    }

    public AnnotationDocument Current { get; private set; }

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var next = command.Apply(Current);
        if (ReferenceEquals(next, Current))
        {
            return false;
        }

        _undo.Add(Current);
        if (_undo.Count > _capacity)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Current = next;
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Add(Current);
        Current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Add(Current);
        Current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }
}
