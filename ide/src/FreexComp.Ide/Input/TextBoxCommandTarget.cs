using System.Runtime.CompilerServices;
using Avalonia.Controls;
using FreexComp.Ide.Core.Commands;

namespace FreexComp.Ide.Input;

/// <summary>
/// The <c>edit.*</c> target of a focused <see cref="TextBox"/> (I-153): menu and toolbar invocations use the TextBox's
/// own clipboard and undo. Keys never get here; they stay with the TextBox (IDE-ARCH-042). One instance per TextBox, so
/// repeated focus reports compare equal.
/// </summary>
public sealed class TextBoxCommandTarget : ICommandTarget
{
    private static readonly ConditionalWeakTable<TextBox, TextBoxCommandTarget> Targets = [];

    private readonly TextBox _box;

    private TextBoxCommandTarget(TextBox box)
    {
        _box = box;
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CanCutProperty || e.Property == TextBox.CanCopyProperty || e.Property == TextBox.CanPasteProperty
                || e.Property == TextBox.CanUndoProperty || e.Property == TextBox.CanRedoProperty)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>A <c>Can…</c> property of the TextBox changed; command availability must be re-queried.</summary>
    public event EventHandler? StateChanged;

    public static TextBoxCommandTarget For(TextBox box) => Targets.GetValue(box, b => new TextBoxCommandTarget(b));

    public bool CanExecute(string commandId, string? argument) => commandId switch
    {
        "edit.cut" => _box.CanCut,
        "edit.copy" => _box.CanCopy,
        "edit.paste" => _box.CanPaste,
        "edit.undo" => _box.CanUndo,
        "edit.redo" => _box.CanRedo,
        "edit.selectAll" => true,
        _ => false,
    };

    public Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        switch (commandId)
        {
            case "edit.cut":
                _box.Cut();
                break;
            case "edit.copy":
                _box.Copy();
                break;
            case "edit.paste":
                _box.Paste();
                break;
            case "edit.undo":
                _box.Undo();
                break;
            case "edit.redo":
                _box.Redo();
                break;
            case "edit.selectAll":
                _box.SelectAll();
                break;
            default:
                return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }
}
