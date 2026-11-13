using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// View state of a text document that survives its view (architecture.md §5, IDE-ARCH-111): caret, selection anchor
/// (null = no selection) and overwrite mode. Scroll, folds, zoom and word wrap are added with the editor view (I-043).
/// </summary>
public sealed class EditorViewState : ObservableObject
{
    private TextPosition _caret = new(1, 1);
    private TextPosition? _selectionAnchor;
    private bool _overwrite;

    public TextPosition Caret
    {
        get => _caret;
        set => SetProperty(ref _caret, value);
    }

    public TextPosition? SelectionAnchor
    {
        get => _selectionAnchor;
        set => SetProperty(ref _selectionAnchor, value);
    }

    public bool Overwrite
    {
        get => _overwrite;
        set => SetProperty(ref _overwrite, value);
    }
}
