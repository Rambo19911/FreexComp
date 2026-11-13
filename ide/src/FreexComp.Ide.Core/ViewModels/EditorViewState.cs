using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// View state of a text document that survives its view (architecture.md §5, IDE-ARCH-111/112): caret, selection
/// anchor (null = no selection), overwrite mode and scroll offsets (DIP). The code editor view mirrors its state into
/// here and restores it when it is created. Folds, zoom and word wrap join with their tasks.
/// </summary>
public sealed class EditorViewState : ObservableObject
{
    private TextPosition _caret = new(1, 1);
    private TextPosition? _selectionAnchor;
    private bool _overwrite;
    private double _horizontalOffset;
    private double _verticalOffset;

    public double HorizontalOffset
    {
        get => _horizontalOffset;
        set => SetProperty(ref _horizontalOffset, value);
    }

    public double VerticalOffset
    {
        get => _verticalOffset;
        set => SetProperty(ref _verticalOffset, value);
    }

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
