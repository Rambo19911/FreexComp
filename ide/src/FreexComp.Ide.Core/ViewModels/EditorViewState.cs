using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// View state of a text document that survives its view (architecture.md §5, IDE-ARCH-111/112): caret, selection
/// anchor (null = no selection), overwrite mode and scroll offsets (DIP). The code editor view mirrors its state into
/// here and restores it when it is created. Folded regions are <see cref="FoldedLines"/>; zoom is <see cref="FontSize"/>,
/// word wrap <see cref="WordWrap"/>.
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

    /// <summary>Overwrite mode (IDE-ED-025): per document, for the IDE session only (not stored).</summary>
    public bool Overwrite
    {
        get => _overwrite;
        set => SetProperty(ref _overwrite, value);
    }

    /// <summary>
    /// The selection is a column block (IDE-ED-026): the lines from <see cref="SelectionAnchor"/> to <see cref="Caret"/>,
    /// between their visual columns. Not stored in the session.
    /// </summary>
    public bool RectangularSelection
    {
        get => _rectangularSelection;
        set => SetProperty(ref _rectangularSelection, value);
    }

    private bool _rectangularSelection;

    /// <summary>
    /// IDE-ED-039: the zoomed font size of this document (6…72); null follows <c>editor.fontSize</c>. Stored in the
    /// session as <c>zoom</c>, a percentage of <c>editor.fontSize</c>.
    /// </summary>
    public int? FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    private int? _fontSize;

    /// <summary>IDE-ED-040: word wrap of this document (initially <c>editor.wordWrap</c>); stored in the session.</summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set => SetProperty(ref _wordWrap, value && WordWrapAllowed);
    }

    private bool _wordWrap;

    /// <summary>False for a large file (IDE-P1-054): <see cref="WordWrap"/> then stays off.</summary>
    public bool WordWrapAllowed
    {
        get => _wordWrapAllowed;
        set
        {
            _wordWrapAllowed = value;
            if (!value)
                WordWrap = false;
        }
    }

    private bool _wordWrapAllowed = true;

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
