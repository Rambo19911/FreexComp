using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// View state of a text document that survives its view (architecture.md §5, IDE-ARCH-111/112): caret, selection
/// anchor (null = no selection), overwrite mode and scroll offsets (DIP). The code editor view mirrors its state into
/// here and restores it when it is created. Folded regions are <see cref="FoldedLines"/>; zoom and word wrap join with
/// their tasks.
/// </summary>
public sealed class EditorViewState : ObservableObject
{
    private TextPosition _caret = new(1, 1);
    private TextPosition? _selectionAnchor;
    private bool _overwrite;
    private double _horizontalOffset;
    private double _verticalOffset;
    private int _topLine = 1;
    private IReadOnlyList<int> _foldedLines = [];

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

    /// <summary>
    /// 1-based line at the top of the view (architecture.md §8.2 <c>scroll.line</c>). Mirrored by the editor view; a view
    /// that is restored with <see cref="VerticalOffset"/> 0 scrolls to this line (session restore).
    /// </summary>
    public int TopLine
    {
        get => _topLine;
        set => SetProperty(ref _topLine, Math.Max(1, value));
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

    /// <summary>
    /// 1-based start lines of the folded regions, ascending (architecture.md §8.2 <c>folds</c>). Kept current by the
    /// document's <c>FoldingModel</c>; setting it (session restore) folds the regions starting at those lines.
    /// </summary>
    public IReadOnlyList<int> FoldedLines
    {
        get => _foldedLines;
        set => SetProperty(ref _foldedLines, value ?? throw new ArgumentNullException(nameof(value)));
    }
}
