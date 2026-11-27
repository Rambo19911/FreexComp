using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Ide.Core.Diff;

/// <summary>IDE-DIFF-021: the two ways a diff document shows its texts.</summary>
public enum DiffViewMode
{
    SideBySide,
    Inline,
}

/// <summary>One code view of a diff document: the left and right views (side by side) or the single inline view.</summary>
public enum DiffPane
{
    Left,
    Right,
    Inline,
}

/// <summary>What a row of a diff view shows (IDE-DIFF-021/022).</summary>
public enum DiffRowKind
{
    Context,
    Removed,
    Added,

    /// <summary>A filler row opposite an added or removed line: no text, no line number (<c>diff.filler</c>).</summary>
    Filler,
}

/// <summary>
/// One row of a diff view: its kind, the 0-based line of the left and right text it shows (inline: both for a context
/// row; a filler row has neither), the original text and the intra-line spans within it (IDE-DIFF-005).
/// </summary>
public sealed record DiffRow(DiffRowKind Kind, int? LeftLine, int? RightLine, string Text, ImmutableArray<TextSpan> Changes);

/// <summary>A change block in the rows of one mode: first row, row count and which kinds of lines it holds (IDE-DIFF-023/024).</summary>
public sealed record DiffBlock(int FirstRow, int RowCount, bool HasRemoved, bool HasAdded);

/// <summary>IDE-DIFF-023: the colour of an overview-strip mark.</summary>
public enum DiffMarkKind
{
    Removed,
    Added,
    Changed,
}

/// <summary>IDE-DIFF-023: one mark of the overview strip; <see cref="Top"/> and <see cref="Height"/> are fractions of the rows.</summary>
public sealed record DiffOverviewMark(int Block, double Top, double Height, DiffMarkKind Kind);

/// <summary>
/// IDE-DIFF-021: the rows of both modes for a diff computed with <c>ContextLines = 0</c> (every hunk one change block).
/// Side by side, the left and right rows have the same count: in a block the i-th removed line is opposite the i-th added
/// line, the longer side's extra lines face filler rows. Inline, a block lists its removed lines above its added lines.
/// Context rows show the text of their own side (with the ignore options the right line may differ from the left one,
/// diff.md IDE-DIFF-002); an inline context row shows the right text.
/// </summary>
public sealed class DiffLayout
{
    private DiffLayout(ImmutableArray<DiffRow> left, ImmutableArray<DiffRow> right, ImmutableArray<DiffRow> inline,
        ImmutableArray<DiffBlock> sideBySideBlocks, ImmutableArray<DiffBlock> inlineBlocks)
    {
        Left = left;
        Right = right;
        Inline = inline;
        SideBySideBlocks = sideBySideBlocks;
        InlineBlocks = inlineBlocks;
    }

    public ImmutableArray<DiffRow> Left { get; }

    public ImmutableArray<DiffRow> Right { get; }

    public ImmutableArray<DiffRow> Inline { get; }

    public ImmutableArray<DiffBlock> SideBySideBlocks { get; }

    public ImmutableArray<DiffBlock> InlineBlocks { get; }

    public ImmutableArray<DiffRow> Rows(DiffPane pane) => pane switch
    {
        DiffPane.Left => Left,
        DiffPane.Right => Right,
        _ => Inline,
    };

    public ImmutableArray<DiffBlock> Blocks(DiffViewMode mode) => mode == DiffViewMode.SideBySide ? SideBySideBlocks : InlineBlocks;

    /// <summary>The text of a view: its rows joined by <c>\n</c> (a filler row is an empty line), so row r is document line r + 1.</summary>
    public string Text(DiffPane pane) => string.Join('\n', Rows(pane).Select(r => r.Text));

    /// <summary>IDE-DIFF-023: one mark per change block at its proportional position (a block at least one row high).</summary>
    public ImmutableArray<DiffOverviewMark> Marks(DiffViewMode mode)
    {
        var rows = Math.Max(1, mode == DiffViewMode.SideBySide ? Left.Length : Inline.Length);
        var blocks = Blocks(mode);
        return [.. blocks.Select((b, i) => new DiffOverviewMark(i, (double)b.FirstRow / rows, (double)Math.Max(1, b.RowCount) / rows,
            b.HasRemoved && b.HasAdded ? DiffMarkKind.Changed : b.HasRemoved ? DiffMarkKind.Removed : DiffMarkKind.Added))];
    }

    /// <summary>The row of <paramref name="mode"/> nearest to <paramref name="row"/> of the other mode, matched by line number.</summary>
    public int MapRow(DiffViewMode from, int row, DiffViewMode to)
    {
        if (from == to)
            return row;
        var source = from == DiffViewMode.SideBySide ? (IReadOnlyList<DiffRow>)Left : Inline;
        if (source.Count == 0)
            return 0;
        row = Math.Clamp(row, 0, source.Count - 1);
        int? left = from == DiffViewMode.SideBySide ? Left[row].LeftLine : Inline[row].LeftLine;
        int? right = from == DiffViewMode.SideBySide ? Right[row].RightLine : Inline[row].RightLine;
        return RowOf(to, left, right);
    }

    /// <summary>
    /// The first row of <paramref name="mode"/> showing left line <paramref name="left"/>, else the first showing right line
    /// <paramref name="right"/>; 0 when none. The left line wins: after a re-computation the right line may have moved.
    /// </summary>
    public int RowOf(DiffViewMode mode, int? left, int? right)
    {
        var count = mode == DiffViewMode.Inline ? Inline.Length : Left.Length;
        int? LeftAt(int i) => mode == DiffViewMode.Inline ? Inline[i].LeftLine : Left[i].LeftLine;
        int? RightAt(int i) => mode == DiffViewMode.Inline ? Inline[i].RightLine : Right[i].RightLine;
        for (var i = 0; left is not null && i < count; i++)
        {
            if (LeftAt(i) == left)
                return i;
        }

        for (var i = 0; right is not null && i < count; i++)
        {
            if (RightAt(i) == right)
                return i;
        }

        return 0;
    }

    /// <summary>Builds both modes from the two texts and a result computed from them with <c>ContextLines = 0</c>.</summary>
    public static DiffLayout Build(string leftText, string rightText, DiffResult result)
    {
        ArgumentNullException.ThrowIfNull(leftText);
        ArgumentNullException.ThrowIfNull(rightText);
        ArgumentNullException.ThrowIfNull(result);
        var leftLines = DiffText.Split(leftText);
        var rightLines = DiffText.Split(rightText);
        var left = ImmutableArray.CreateBuilder<DiffRow>();
        var right = ImmutableArray.CreateBuilder<DiffRow>();
        var inline = ImmutableArray.CreateBuilder<DiffRow>();
        var sideBlocks = ImmutableArray.CreateBuilder<DiffBlock>();
        var inlineBlocks = ImmutableArray.CreateBuilder<DiffBlock>();
        int i = 0, j = 0;

        void Equal(int untilLeft)
        {
            while (i < untilLeft && i < leftLines.Count && j < rightLines.Count)
            {
                left.Add(new DiffRow(DiffRowKind.Context, i, null, leftLines[i].Text, []));
                right.Add(new DiffRow(DiffRowKind.Context, null, j, rightLines[j].Text, []));
                inline.Add(new DiffRow(DiffRowKind.Context, i, j, rightLines[j].Text, []));
                i++;
                j++;
            }
        }

        void Flush(List<DiffLine> removed, List<DiffLine> added)
        {
            if (removed.Count == 0 && added.Count == 0)
                return;
            var rows = Math.Max(removed.Count, added.Count);
            sideBlocks.Add(new DiffBlock(left.Count, rows, removed.Count > 0, added.Count > 0));
            inlineBlocks.Add(new DiffBlock(inline.Count, removed.Count + added.Count, removed.Count > 0, added.Count > 0));
            for (var k = 0; k < rows; k++)
            {
                left.Add(k < removed.Count
                    ? new DiffRow(DiffRowKind.Removed, removed[k].LeftLine, null, removed[k].Text, removed[k].Changes)
                    : new DiffRow(DiffRowKind.Filler, null, null, "", []));
                right.Add(k < added.Count
                    ? new DiffRow(DiffRowKind.Added, null, added[k].RightLine, added[k].Text, added[k].Changes)
                    : new DiffRow(DiffRowKind.Filler, null, null, "", []));
            }

            foreach (var line in removed)
                inline.Add(new DiffRow(DiffRowKind.Removed, line.LeftLine, null, line.Text, line.Changes));
            foreach (var line in added)
                inline.Add(new DiffRow(DiffRowKind.Added, null, line.RightLine, line.Text, line.Changes));
            i += removed.Count;
            j += added.Count;
            removed.Clear();
            added.Clear();
        }

        var pendingRemoved = new List<DiffLine>();
        var pendingAdded = new List<DiffLine>();
        foreach (var hunk in result.Hunks)
        {
            Equal(hunk.LeftStart);
            foreach (var line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case DiffLineKind.Removed:
                        if (pendingAdded.Count > 0)
                            Flush(pendingRemoved, pendingAdded);
                        pendingRemoved.Add(line);
                        break;
                    case DiffLineKind.Added:
                        pendingAdded.Add(line);
                        break;
                    default:
                        Flush(pendingRemoved, pendingAdded);
                        Equal(i + 1);
                        break;
                }
            }

            Flush(pendingRemoved, pendingAdded);
        }

        Equal(leftLines.Count);
        return new DiffLayout(left.ToImmutable(), right.ToImmutable(), inline.ToImmutable(), sideBlocks.ToImmutable(), inlineBlocks.ToImmutable());
    }

    /// <summary>
    /// IDE-DIFF-025: the original text of the rows between two offsets of a view's <see cref="Text"/> (in any order): filler
    /// rows are left out, the rows are joined by CRLF.
    /// </summary>
    public string Copy(DiffPane pane, int from, int to)
    {
        var rows = Rows(pane);
        if (rows.Length == 0)
            return "";
        var (start, end) = from <= to ? (from, to) : (to, from);
        var (startRow, startColumn) = Position(rows, start);
        var (endRow, endColumn) = Position(rows, end);
        var parts = new List<string>();
        for (var r = startRow; r <= endRow; r++)
        {
            if (rows[r].Kind == DiffRowKind.Filler)
                continue;
            var text = rows[r].Text;
            var first = r == startRow ? Math.Min(startColumn, text.Length) : 0;
            var last = r == endRow ? Math.Min(endColumn, text.Length) : text.Length;
            parts.Add(text[first..Math.Max(first, last)]);
        }

        return string.Join("\r\n", parts);
    }

    /// <summary>Row and column of an offset into the <c>\n</c>-joined rows.</summary>
    private static (int Row, int Column) Position(ImmutableArray<DiffRow> rows, int offset)
    {
        var rest = Math.Max(0, offset);
        for (var r = 0; r < rows.Length; r++)
        {
            if (rest <= rows[r].Text.Length)
                return (r, rest);
            rest -= rows[r].Text.Length + 1;
        }

        return (rows.Length - 1, rows[^1].Text.Length);
    }
}
