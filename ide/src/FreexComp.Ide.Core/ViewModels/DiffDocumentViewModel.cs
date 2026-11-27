using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Diff;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// One side of a diff document (IDE-DIFF-020): the label shown in the header and the tab title, the full path of its
/// tooltip (null: the label), and how its text is read (again on Refresh); null from the reader = it could not be read
/// (the reader has reported why). <see cref="LiveBuffer"/>: the editor buffer the side shows, when it is one (the right
/// side of Copy Block to Current, IDE-HIST-021).
/// </summary>
public sealed record DiffSource(string Label, string? Path, Func<Task<DiffInput?>> ReadAsync)
{
    public ITextBuffer? LiveBuffer { get; init; }
}

/// <summary>A request to the view: show <see cref="Row"/> at one third of the viewport, or (<see cref="Third"/> false) at its top.</summary>
public sealed record DiffReveal(int Row, bool Third);

/// <summary>
/// A diff document (diff-viewer.md §2, IDE-DIFF-020…026): a tab of kind <c>Diff</c> comparing two texts, never dirty and
/// not saved in the session. It owns the layout of both modes (<see cref="DiffLayout"/>), the read-only texts of its
/// three views, the caret row, the change-block navigation and the view options kept in the UI state
/// (<c>diff.mode</c>, <c>diff.ignoreWhitespace</c>, <c>diff.algorithm</c>). It is the target of the <c>diff.*</c> commands.
/// </summary>
public sealed class DiffDocumentViewModel : DocumentViewModel, ICommandTarget
{
    /// <summary>The kind, and the keymap context of the view (<c>E Diff</c> bindings, shortcuts.md).</summary>
    public const string DocumentKind = "Diff";

    public const string NextDifferenceCommandId = "diff.nextDifference";

    public const string PreviousDifferenceCommandId = "diff.previousDifference";

    /// <summary>IDE-HIST-021: Copy Block to Current.</summary>
    public const string CopyBlockToRightCommandId = "diff.copyBlockToRight";

    private const string Section = "diff";
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly ILayoutService? _layout;
    private readonly IStatusService? _status;
    private readonly Platform.IClipboardService? _clipboard;
    private DiffInput? _leftInput;
    private DiffInput? _rightInput;
    private DiffResult? _result;
    private DiffLayout _diff = DiffLayout.Build("", "", new DiffResult([], [], true, 0));
    private DiffViewMode _mode;
    private DiffReveal? _pendingReveal;

    public DiffDocumentViewModel(DiffSource left, DiffSource right, ILanguageDefinition leftLanguage, ILanguageDefinition rightLanguage,
        ITextBufferFactory buffers, ILayoutService? layout, IStatusService? status, Platform.IClipboardService? clipboard = null,
        bool showIdenticalBanner = false)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(buffers);
        LeftSource = left;
        RightSource = right;
        _layout = layout;
        _status = status;
        _clipboard = clipboard;
        ShowsIdenticalBanner = showIdenticalBanner;
        Title = left.Label + " ↔ " + right.Label;
        Left = Pane(buffers, left.Label, leftLanguage);
        Right = Pane(buffers, right.Label, rightLanguage);
        InlineText = Pane(buffers, right.Label, rightLanguage);
        _mode = layout?.GetUiValue(Section, "mode") == "inline" ? DiffViewMode.Inline : DiffViewMode.SideBySide;
        NextDifferenceCommand = new RelayCommand(() => NextDifference());
        PreviousDifferenceCommand = new RelayCommand(() => PreviousDifference());
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CopyBlockCommand = new AsyncRelayCommand(CopyBlockAsync, () => CanCopyBlock);
    }

    public DiffSource LeftSource { get; }

    public DiffSource RightSource { get; }

    public override string Title { get; }

    public override string? FilePath => null;

    public override bool IsModified => false;

    public override bool IsReadOnly => true;

    public override ICommandTarget? CommandTarget => this;

    public string LeftLabel => LeftSource.Label;

    public string RightLabel => RightSource.Label;

    /// <summary>The tooltip of the left label: the full path.</summary>
    public string LeftToolTip => LeftSource.Path ?? LeftSource.Label;

    public string RightToolTip => RightSource.Path ?? RightSource.Label;

    /// <summary>The read-only text of the left view (side by side).</summary>
    public TextDocumentViewModel Left { get; }

    /// <summary>The read-only text of the right view (side by side).</summary>
    public TextDocumentViewModel Right { get; }

    /// <summary>The read-only text of the inline view.</summary>
    public TextDocumentViewModel InlineText { get; }

    public TextDocumentViewModel TextOf(DiffPane pane) => pane switch
    {
        DiffPane.Left => Left,
        DiffPane.Right => Right,
        _ => InlineText,
    };

    /// <summary>The view with keyboard focus (or that had it last); set by the view.</summary>
    public DiffPane FocusedPane
    {
        get;
        set
        {
            if (Mode == DiffViewMode.Inline)
                value = DiffPane.Inline;
            else if (value == DiffPane.Inline)
                value = DiffPane.Left;
            SetProperty(ref field, value);
        }
    }

    /// <summary>The text of the focused view: what Find (Ctrl+F) works in (IDE-DIFF-025).</summary>
    public TextDocumentViewModel FocusedText => TextOf(FocusedPane);

    public DiffLayout Layout => _diff;

    /// <summary>The notes of the header (IDE-DIFF-006), one line; empty when there is none.</summary>
    public string NotesText { get; private set; } = "";

    public bool HasNotes => NotesText.Length > 0;

    /// <summary><c>Msg.Diff.Changes</c> / <c>Msg.Diff.ChangesOne</c>.</summary>
    public string ChangesText { get; private set; } = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Diff_Changes, 0);

    public bool IsIdentical { get; private set; } = true;

    /// <summary>IDE-DIFF-028: Compare Files shows <c>Msg.Diff.IdenticalBanner</c> for identical texts.</summary>
    public bool ShowsIdenticalBanner { get; }

    public bool IsBannerVisible => ShowsIdenticalBanner && IsIdentical && _result is not null;

    public string BannerText => Strings.Msg_Diff_IdenticalBanner;

    public DiffViewMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;
            var old = _mode;
            CaretRow = _diff.MapRow(old, CaretRow, value);
            TopRow = _diff.MapRow(old, TopRow, value);
            _mode = value;
            _layout?.SetUiValue(Section, "mode", value == DiffViewMode.Inline ? "inline" : "sideBySide");
            FocusedPane = value == DiffViewMode.Inline ? DiffPane.Inline : DiffPane.Left;
            CurrentBlock = BlockAt(CaretRow);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSideBySide));
            OnPropertyChanged(nameof(IsInline));
            OnPropertyChanged(nameof(Blocks));
            OnPropertyChanged(nameof(Marks));
            Reveal(new DiffReveal(TopRow, Third: false));
        }
    }

    /// <summary>The <c>Side by side</c> toggle of the header.</summary>
    public bool IsSideBySide
    {
        get => Mode == DiffViewMode.SideBySide;
        set
        {
            if (value)
                Mode = DiffViewMode.SideBySide;
            else
                OnPropertyChanged();
        }
    }

    /// <summary>The <c>Inline</c> toggle of the header.</summary>
    public bool IsInline
    {
        get => Mode == DiffViewMode.Inline;
        set
        {
            if (value)
                Mode = DiffViewMode.Inline;
            else
                OnPropertyChanged();
        }
    }

    /// <summary>IDE-DIFF-026: <c>Ignore whitespace</c> (UI state <c>diff.ignoreWhitespace</c>); changing it re-computes.</summary>
    public bool IgnoreWhitespace
    {
        get => _layout?.GetUiFlag(Section, "ignoreWhitespace") ?? field;
        set
        {
            if (IgnoreWhitespace == value)
                return;
            field = value;
            _layout?.SetUiFlag(Section, "ignoreWhitespace", value);
            OnPropertyChanged();
            if (_leftInput is not null && _rightInput is not null)
                Recomputing = ComputeAsync(keepPosition: true);
        }
    }

    /// <summary>The pending re-computation after an option changed (the view or a test may await it).</summary>
    public Task Recomputing { get; private set; } = Task.CompletedTask;

    /// <summary>IDE-DIFF-026: UI state <c>diff.algorithm</c> (<c>patience</c>; anything else = Myers).</summary>
    public DiffAlgorithm Algorithm => _layout?.GetUiValue(Section, "algorithm") == "patience" ? DiffAlgorithm.Patience : DiffAlgorithm.Myers;

    public ImmutableArray<DiffBlock> Blocks => _diff.Blocks(Mode);

    public ImmutableArray<DiffOverviewMark> Marks => _diff.Marks(Mode);

    /// <summary>The rows of the current mode (side by side: the left view's).</summary>
    public int RowCount => Mode == DiffViewMode.SideBySide ? _diff.Left.Length : _diff.Inline.Length;

    /// <summary>The 0-based row of the caret in the current mode; the view sets it when the caret moves.</summary>
    public int CaretRow
    {
        get;
        set
        {
            if (SetProperty(ref field, Math.Max(0, value)))
                CurrentBlock = BlockAt(field);
        }
    }

    /// <summary>The selected change block (−1: none).</summary>
    public int CurrentBlock
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                CopyBlockCommand?.NotifyCanExecuteChanged();
        }
    } = -1;

    /// <summary>IDE-HIST-021: the right side is a live editor buffer (the Copy Block button is shown).</summary>
    public bool HasLiveRight => RightSource.LiveBuffer is not null;

    /// <summary>
    /// The header shows <c>Refresh</c>. False where the owner of the view re-reads the sources itself (the Preview dialog
    /// of a refactoring, UI-DLG-364).
    /// </summary>
    public bool CanRefresh { get; init; } = true;

    /// <summary>IDE-HIST-021: a live, writable right side and the caret in a change block.</summary>
    public bool CanCopyBlock => RightSource.LiveBuffer is { IsReadOnly: false } && _rightInput is not null && CurrentBlock >= 0
        && CurrentBlock < Blocks.Length;

    /// <summary>The <c>Copy Block to Current</c> button of the header.</summary>
    public IAsyncRelayCommand CopyBlockCommand { get; }

    /// <summary>The context menu of the views (menus.md §12; set by the document service when the tab opens).</summary>
    public Func<IReadOnlyList<Menus.MenuItemModel>>? ContextMenu { get; internal set; }

    /// <summary>The first visible row of the current mode; the view sets it when it scrolls.</summary>
    public int TopRow { get; set; }

    public IRelayCommand NextDifferenceCommand { get; }

    public IRelayCommand PreviousDifferenceCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>The view should scroll as asked (the caret row is already set).</summary>
    public event EventHandler<DiffReveal>? RevealRequested;

    /// <summary>A new layout was computed: the views' texts, rows and blocks changed.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>The reveal no view has carried out yet (the first block on open), and forgets it.</summary>
    public DiffReveal? TakePendingReveal()
    {
        var pending = _pendingReveal;
        _pendingReveal = null;
        return pending;
    }

    /// <summary>Reads both sources and computes; false when a source could not be read. On open the first block is selected.</summary>
    public async Task<bool> LoadAsync()
    {
        if (!await ReadAsync())
            return false;
        await ComputeAsync(keepPosition: false);
        if (Blocks.Length > 0)
            SelectBlock(0);
        return true;
    }

    /// <summary>IDE-DIFF-026: re-reads both sources and re-computes, keeping the scroll position by line.</summary>
    public async Task RefreshAsync()
    {
        if (await ReadAsync())
            await ComputeAsync(keepPosition: true);
    }

    /// <summary>IDE-DIFF-024: the caret to the first row of the next block (scrolled to one third); false + status at the end.</summary>
    public bool NextDifference()
    {
        var blocks = Blocks;
        for (var i = 0; i < blocks.Length; i++)
        {
            if (blocks[i].FirstRow > CaretRow)
            {
                SelectBlock(i);
                return true;
            }
        }

        _status?.ShowMessage(Strings.Msg_Diff_NoMoreDifferences, StatusDuration);
        return false;
    }

    /// <summary>IDE-DIFF-024: the caret to the first row of the previous block; false + status at the start (no wrap).</summary>
    public bool PreviousDifference()
    {
        var blocks = Blocks;
        for (var i = blocks.Length - 1; i >= 0; i--)
        {
            if (blocks[i].FirstRow < CaretRow)
            {
                SelectBlock(i);
                return true;
            }
        }

        _status?.ShowMessage(Strings.Msg_Diff_NoMoreDifferences, StatusDuration);
        return false;
    }

    /// <summary>Selects block <paramref name="index"/>: caret to its first row, scrolled to one third (IDE-DIFF-023/024).</summary>
    public void SelectBlock(int index)
    {
        var blocks = Blocks;
        if (index < 0 || index >= blocks.Length)
            return;
        CaretRow = blocks[index].FirstRow;
        CurrentBlock = index;
        Reveal(new DiffReveal(CaretRow, Third: true));
    }

    /// <summary>
    /// UI-DLG-364: selects the first change block that shows left line <paramref name="leftLine"/> or right line
    /// <paramref name="rightLine"/> (0-based; null: none), as <see cref="SelectBlock"/>. When no block shows them — the
    /// lines are the same on both sides — the row of the left line is scrolled to one third instead and false is returned.
    /// </summary>
    public bool SelectBlockAt(int leftLine, int? rightLine)
    {
        var (first, second) = Mode == DiffViewMode.SideBySide ? (_diff.Left, _diff.Right) : (_diff.Inline, _diff.Inline);
        var blocks = Blocks;
        for (var i = 0; i < blocks.Length; i++)
        {
            for (var row = blocks[i].FirstRow; row < blocks[i].FirstRow + blocks[i].RowCount; row++)
            {
                if (first[row].LeftLine == leftLine || (rightLine is not null && second[row].RightLine == rightLine))
                {
                    SelectBlock(i);
                    return true;
                }
            }
        }

        CaretRow = _diff.RowOf(Mode, leftLine, rightLine);
        Reveal(new DiffReveal(CaretRow, Third: true));
        return false;
    }

    /// <summary>
    /// IDE-DIFF-023: a click on the overview strip at <paramref name="fraction"/> (0…1 of its height): the block of a mark
    /// within <paramref name="tolerance"/> (a fraction too) is selected; elsewhere the row there is scrolled to one third.
    /// </summary>
    public void ClickOverview(double fraction, double tolerance)
    {
        var marks = Marks;
        var hit = marks
            .Select(m => (Mark: m, Distance: fraction < m.Top ? m.Top - fraction : fraction > m.Top + m.Height ? fraction - m.Top - m.Height : 0))
            .Where(x => x.Distance <= tolerance)
            .OrderBy(x => x.Distance)
            .Select(x => (DiffOverviewMark?)x.Mark)
            .FirstOrDefault();
        if (hit is not null)
        {
            SelectBlock(hit.Block);
            return;
        }

        var row = Math.Clamp((int)(fraction * RowCount), 0, Math.Max(0, RowCount - 1));
        Reveal(new DiffReveal(row, Third: true));
    }

    /// <summary>IDE-DIFF-025: the original text between two offsets of a view's text, without filler rows.</summary>
    public string Copy(DiffPane pane, int from, int to) => _diff.Copy(pane, from, to);

    /// <summary>IDE-DIFF-025: puts the copied text on the clipboard (Ctrl+C in a view).</summary>
    public Task CopyToClipboardAsync(DiffPane pane, int from, int to) =>
        _clipboard?.SetTextAsync(Copy(pane, from, to)) ?? Task.CompletedTask;

    /// <summary>
    /// IDE-HIST-021: the right lines of the current block are replaced in the live buffer by its left lines (an empty left
    /// side deletes them) as one undo step; then the diff re-computes and the caret moves to the next block (else stays).
    /// A buffer changed since the last computation is compared again first and nothing is copied (the blocks moved).
    /// </summary>
    public async Task CopyBlockAsync()
    {
        if (!CanCopyBlock)
            return;
        var buffer = RightSource.LiveBuffer!;
        var compared = _rightInput!;
        if (!string.Equals(buffer.Text, compared.Text, StringComparison.Ordinal))
        {
            _rightInput = compared with { Text = buffer.Text };
            await ComputeAsync(keepPosition: true);
            return;
        }

        var index = CurrentBlock;
        var block = Blocks[index];
        var leftRows = Mode == DiffViewMode.SideBySide ? _diff.Left : _diff.Inline;
        var rightRows = Mode == DiffViewMode.SideBySide ? _diff.Right : _diff.Inline;
        var range = Enumerable.Range(block.FirstRow, block.RowCount).ToList();
        var left = range.Select(r => leftRows[r]).Where(r => r.Kind == DiffRowKind.Removed).Select(r => r.Text).ToList();
        var right = range.Select(r => rightRows[r]).Where(r => r.Kind == DiffRowKind.Added).Select(r => r.RightLine!.Value).ToList();
        int first;
        if (right.Count > 0)
        {
            first = right[0];
        }
        else
        {
            // Nothing on the right: the left lines go after the nearest right line above the block.
            var above = Enumerable.Range(0, block.FirstRow).Reverse().Select(r => rightRows[r].RightLine).FirstOrDefault(l => l is not null);
            first = above is { } line ? line + 1 : 0;
        }

        var edit = BlockEdit(buffer.Text, first, right.Count, left, LineEndingDetector.NewLineText(buffer.LineEnding));
        buffer.ApplyEdits([edit], Strings.Cmd_diff_copyBlockToRight);
        _rightInput = compared with { Text = buffer.Text };
        await ComputeAsync(keepPosition: true);
        if (index < Blocks.Length)
            SelectBlock(index); // the block after the copied one has its index now
    }

    /// <summary>
    /// The edit that replaces <paramref name="count"/> lines of <paramref name="text"/> from line <paramref name="first"/>
    /// (0-based; with no lines: inserts there) by <paramref name="lines"/>, each ended by <paramref name="eol"/>, except at
    /// the end of a text whose last line has no break.
    /// </summary>
    private static TextEdit BlockEdit(string text, int first, int count, IReadOnlyList<string> lines, string eol)
    {
        var split = DiffText.Split(text);
        var offsets = new int[split.Count + 1];
        for (var i = 0; i < split.Count; i++)
            offsets[i + 1] = offsets[i] + split[i].Text.Length + split[i].Break.Length;
        var inserted = string.Concat(lines.Select(l => l + eol));
        var openEnd = split.Count > 0 && split[^1].Break.Length == 0; // the text ends without a line break
        if (count > 0)
        {
            var start = offsets[first];
            var end = offsets[first + count];
            if (first + count == split.Count && openEnd)
            {
                if (inserted.Length > 0)
                    inserted = inserted[..^eol.Length];
                else if (first > 0)
                    start -= split[first - 1].Break.Length; // the line above becomes the last one
            }

            return new TextEdit(start, end - start, inserted);
        }

        if (first < split.Count)
            return new TextEdit(offsets[first], 0, inserted);
        return openEnd && inserted.Length > 0
            ? new TextEdit(text.Length, 0, eol + inserted[..^eol.Length])
            : new TextEdit(text.Length, 0, inserted);
    }

    public bool CanExecute(string commandId, string? argument) => commandId switch
    {
        NextDifferenceCommandId or PreviousDifferenceCommandId => true,
        CopyBlockToRightCommandId => CanCopyBlock,
        _ => false,
    };

    public async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        switch (commandId)
        {
            case NextDifferenceCommandId:
                NextDifference();
                return true;
            case PreviousDifferenceCommandId:
                PreviousDifference();
                return true;
            case CopyBlockToRightCommandId:
                await CopyBlockAsync();
                return true;
            default:
                return false;
        }
    }

    private static TextDocumentViewModel Pane(ITextBufferFactory buffers, string name, ILanguageDefinition language)
    {
        var buffer = buffers.Create("", LineEndingKind.Lf);
        buffer.IsReadOnly = true;
        return new TextDocumentViewModel(buffer, null, name, language, FileEncoding.Utf8, LineEndingKind.Lf);
    }

    private async Task<bool> ReadAsync()
    {
        var left = await LeftSource.ReadAsync();
        if (left is null)
            return false;
        var right = await RightSource.ReadAsync();
        if (right is null)
            return false;
        (_leftInput, _rightInput) = (left, right);
        return true;
    }

    private async Task ComputeAsync(bool keepPosition)
    {
        var (left, right) = (_leftInput!, _rightInput!);
        var options = new DiffOptions(Algorithm, IgnoreWhitespace, ContextLines: 0);
        var (result, layout) = await Task.Run(() =>
        {
            var computed = DiffService.Compute(left, right, options);
            return (computed, DiffLayout.Build(left.Text, right.Text, computed));
        });

        // IDE-DIFF-026: the top and caret rows keep the lines they showed.
        var (topLeft, topRight) = LinesAt(TopRow);
        var (caretLeft, caretRight) = LinesAt(CaretRow);
        _result = result;
        _diff = layout;
        Left.Reload(layout.Text(DiffPane.Left));
        Right.Reload(layout.Text(DiffPane.Right));
        InlineText.Reload(layout.Text(DiffPane.Inline));
        NotesText = string.Join("   ", result.Notes);
        IsIdentical = result.Identical;
        ChangesText = result.ChangeCount == 1
            ? Strings.Msg_Diff_ChangesOne
            : string.Format(CultureInfo.CurrentCulture, Strings.Msg_Diff_Changes, result.ChangeCount);
        if (keepPosition)
        {
            TopRow = layout.RowOf(Mode, topLeft, topRight);
            CaretRow = layout.RowOf(Mode, caretLeft, caretRight);
        }
        else
        {
            TopRow = 0;
            CaretRow = 0;
        }

        CurrentBlock = BlockAt(CaretRow);
        foreach (var name in new[] { nameof(Layout), nameof(NotesText), nameof(HasNotes), nameof(IsIdentical), nameof(ChangesText),
                     nameof(IsBannerVisible), nameof(Blocks), nameof(Marks), nameof(RowCount) })
            OnPropertyChanged(name);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        CopyBlockCommand.NotifyCanExecuteChanged();
        if (keepPosition)
            Reveal(new DiffReveal(TopRow, Third: false));
    }

    private (int? Left, int? Right) LinesAt(int row)
    {
        if (Mode == DiffViewMode.Inline)
            return row < _diff.Inline.Length ? (_diff.Inline[row].LeftLine, _diff.Inline[row].RightLine) : (null, null);
        return row < _diff.Left.Length ? (_diff.Left[row].LeftLine, _diff.Right[row].RightLine) : (null, null);
    }

    private int BlockAt(int row)
    {
        var blocks = Blocks;
        for (var i = 0; i < blocks.Length; i++)
        {
            if (row >= blocks[i].FirstRow && row < blocks[i].FirstRow + Math.Max(1, blocks[i].RowCount))
                return i;
        }

        return -1;
    }

    private void Reveal(DiffReveal reveal)
    {
        _pendingReveal = reveal;
        RevealRequested?.Invoke(this, reveal);
    }

    /// <summary>The view carried out a reveal: it is no longer pending.</summary>
    public void RevealDone() => _pendingReveal = null;
}
