using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using SSC.UI.Editing;

namespace SSC.UI.Platform;

/// <summary>
/// Avalonia 12 has no ApplicationCommands, so the macOS Edit menu dispatches
/// explicitly to the focused TextBox or the annotation editor.
/// </summary>
public static class EditMenuRouter
{
    private static Window? ActiveWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
        ?.Windows.FirstOrDefault(window => window.IsActive);

    private static IInputElement? Focused => ActiveWindow?.FocusManager?.GetFocusedElement();

    public static void Undo()
    {
        if (Focused is TextBox textBox)
        {
            textBox.Undo();
        }
        else if (ActiveWindow is AnnotationEditorWindow editor)
        {
            editor.UndoEdit();
        }
    }

    public static void Redo()
    {
        if (Focused is TextBox textBox)
        {
            textBox.Redo();
        }
        else if (ActiveWindow is AnnotationEditorWindow editor)
        {
            editor.RedoEdit();
        }
    }

    public static void Cut()
    {
        if (Focused is TextBox textBox)
        {
            textBox.Cut();
        }
    }

    public static void Copy()
    {
        if (Focused is TextBox textBox)
        {
            textBox.Copy();
        }
        else if (ActiveWindow is AnnotationEditorWindow editor)
        {
            _ = editor.CopySelectionAsync();
        }
    }

    public static void Paste()
    {
        if (Focused is TextBox textBox)
        {
            textBox.Paste();
        }
    }

    public static void SelectAll()
    {
        if (Focused is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }
}
