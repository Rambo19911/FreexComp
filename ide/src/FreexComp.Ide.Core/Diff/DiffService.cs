using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Diff;

/// <summary>F12-014, I-329: line diff of two texts, applying it, and the unified patch text (diff-viewer.md §1).</summary>
public static class DiffService
{
    /// <summary>IDE-DIFF-001…006, IDE-DIFF-009: the hunks, notes and change count of <paramref name="left"/> → <paramref name="right"/>.</summary>
    public static DiffResult Compute(DiffInput left, DiffInput right, DiffOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.ContextLines);
        ct.ThrowIfCancellationRequested();

        var steps = new DiffSteps(ct);
        var leftLines = DiffText.Split(left.Text);
        var rightLines = DiffText.Split(right.Text);
        var ids = new Dictionary<string, int>(options.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var a = Keys(leftLines, ids, options, steps);
        var b = Keys(rightLines, ids, options, steps);
        var removed = new bool[a.Length];
        var added = new bool[b.Length];
        if (options.Algorithm == DiffAlgorithm.Patience)
            SequenceDiff.Patience(a, 0, a.Length, b, 0, b.Length, removed, added, steps);
        else
            SequenceDiff.Myers(a, 0, a.Length, b, 0, b.Length, removed, added, steps);

        var segments = Segments(removed, added);
        var hunks = Hunks(segments, leftLines, rightLines, options.ContextLines, steps);
        var changes = segments.Count(s => s.Change);
        var leftFinal = leftLines.Count > 0 && leftLines[^1].Break.Length > 0;
        var rightFinal = rightLines.Count > 0 && rightLines[^1].Break.Length > 0;
        return new DiffResult(hunks, Notes(left, right, leftLines, rightLines, leftFinal, rightFinal), changes == 0, changes)
        {
            Ends = new DiffEnds(leftLines.Count, rightLines.Count, leftFinal, rightFinal, DiffText.MostFrequentBreak(rightLines)),
        };
    }

    /// <summary>
    /// IDE-DIFF-007: <paramref name="left"/> with the hunks applied. Untouched lines keep their line breaks; inserted lines
    /// get the left text's most frequent break (without one: the right text's, else CRLF); the last line ends with a break
    /// exactly when the right text's last line does.
    /// </summary>
    public static string Apply(string left, DiffResult result)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(result);
        var lines = DiffText.Split(left);
        if (result.Ends is { } ends && ends.LeftLines != lines.Count)
            throw new ArgumentException("The diff was computed for a text with a different number of lines.", nameof(left));

        var lineBreak = DiffText.MostFrequentBreak(lines) ?? result.Ends?.RightBreak ?? "\r\n";
        var output = new List<DiffTextLine>(lines.Count);
        void Emit(DiffTextLine line)
        {
            if (output.Count > 0 && output[^1].Break.Length == 0)
                output[^1] = output[^1] with { Break = lineBreak };
            output.Add(line);
        }

        var next = 0;
        foreach (var hunk in result.Hunks)
        {
            if (hunk.LeftStart < next || hunk.LeftStart + hunk.LeftCount > lines.Count)
                throw new ArgumentException("The hunks do not fit the text.", nameof(result));
            while (next < hunk.LeftStart)
                Emit(lines[next++]);
            foreach (var line in hunk.Lines)
            {
                if (line.Kind != DiffLineKind.Added && line.LeftLine != next)
                    throw new ArgumentException("The hunk lines do not fit the text.", nameof(result));
                if (line.Kind == DiffLineKind.Context)
                    Emit(lines[next]);
                if (line.Kind == DiffLineKind.Added)
                    Emit(new DiffTextLine(line.Text, lineBreak));
                else
                    next++;
            }
        }

        while (next < lines.Count)
            Emit(lines[next++]);

        var finalBreak = result.Ends?.RightFinalBreak ?? (lines.Count > 0 && lines[^1].Break.Length > 0);
        if (output.Count > 0 && finalBreak != (output[^1].Break.Length > 0))
            output[^1] = output[^1] with { Break = finalBreak ? lineBreak : "" };

        var s = new StringBuilder(left.Length);
        foreach (var line in output)
            s.Append(line.Text).Append(line.Break);
        return s.ToString();
    }

    /// <summary>
    /// IDE-DIFF-008: <c>--- left</c>, <c>+++ right</c>, then per hunk <c>@@ -l,c +l,c @@</c> (1-based starts, <c>,c</c>
    /// omitted when 1, an empty range starts at the line before it) and its lines prefixed <c> </c>, <c>-</c>, <c>+</c>;
    /// <c>\ No newline at end of file</c> follows a last line without a break (a context line that is the last line of a
    /// side whose break differs from the other side's is written as removed and added); LF line breaks.
    /// </summary>
    public static string ToUnifiedPatch(DiffResult result, string leftName, string rightName)
    {
        ArgumentNullException.ThrowIfNull(result);
        const string NoNewline = "\\ No newline at end of file\n";
        var s = new StringBuilder();
        s.Append("--- ").Append(leftName).Append('\n').Append("+++ ").Append(rightName).Append('\n');
        var ends = result.Ends;
        foreach (var hunk in result.Hunks)
        {
            s.Append("@@ -").Append(Range(hunk.LeftStart, hunk.LeftCount)).Append(" +").Append(Range(hunk.RightStart, hunk.RightCount)).Append(" @@\n");
            foreach (var line in hunk.Lines)
            {
                var leftOpen = ends is { LeftFinalBreak: false } && line.LeftLine == ends.LeftLines - 1;
                var rightOpen = ends is { RightFinalBreak: false } && line.RightLine == ends.RightLines - 1;
                if (line.Kind == DiffLineKind.Context && leftOpen == rightOpen)
                {
                    s.Append(' ').Append(line.Text).Append('\n');
                    if (leftOpen)
                        s.Append(NoNewline);
                    continue;
                }

                if (line.Kind != DiffLineKind.Added)
                {
                    s.Append('-').Append(line.Text).Append('\n');
                    if (leftOpen)
                        s.Append(NoNewline);
                }

                if (line.Kind != DiffLineKind.Removed)
                {
                    s.Append('+').Append(line.Text).Append('\n');
                    if (rightOpen)
                        s.Append(NoNewline);
                }
            }
        }

        return s.ToString();
    }

    private static string Range(int start, int count) => count switch
    {
        0 => string.Create(CultureInfo.InvariantCulture, $"{start},0"),
        1 => (start + 1).ToString(CultureInfo.InvariantCulture),
        _ => string.Create(CultureInfo.InvariantCulture, $"{start + 1},{count}"),
    };

    /// <summary>IDE-DIFF-002: one key per distinct compared text (ordinal or ordinal-ignore-case, whitespace collapsed on request).</summary>
    private static int[] Keys(List<DiffTextLine> lines, Dictionary<string, int> ids, DiffOptions options, DiffSteps steps)
    {
        var keys = new int[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            steps.Step();
            var text = options.IgnoreWhitespace ? DiffText.CollapseWhitespace(lines[i].Text) : lines[i].Text;
            if (!ids.TryGetValue(text, out var id))
                ids.Add(text, id = ids.Count);
            keys[i] = id;
        }

        return keys;
    }

    /// <summary>A run of paired equal lines (<c>Change</c> false, equal counts) or a change block.</summary>
    private readonly record struct Segment(bool Change, int LeftStart, int LeftCount, int RightStart, int RightCount);

    /// <summary>The alternating runs of equal lines and change blocks described by the marks.</summary>
    private static List<Segment> Segments(bool[] removed, bool[] added)
    {
        var segments = new List<Segment>();
        int i = 0, j = 0;
        while (i < removed.Length || j < added.Length)
        {
            var (i0, j0) = (i, j);
            while (i < removed.Length && j < added.Length && !removed[i] && !added[j])
                (i, j) = (i + 1, j + 1);
            if (i > i0)
                segments.Add(new Segment(false, i0, i - i0, j0, j - j0));

            (i0, j0) = (i, j);
            while (i < removed.Length && removed[i])
                i++;
            while (j < added.Length && added[j])
                j++;
            if (i > i0 || j > j0)
                segments.Add(new Segment(true, i0, i - i0, j0, j - j0));
            else if (i < removed.Length || j < added.Length)
                throw new InvalidOperationException("Inconsistent diff marks.");
        }

        return segments;
    }

    /// <summary>IDE-DIFF-004: each change block with up to <paramref name="context"/> lines around it; blocks at most 2·context equal lines apart share a hunk.</summary>
    private static ImmutableArray<DiffHunk> Hunks(List<Segment> segments, List<DiffTextLine> left, List<DiffTextLine> right, int context, DiffSteps steps)
    {
        var hunks = ImmutableArray.CreateBuilder<DiffHunk>();
        for (var s = 0; s < segments.Count; s++)
        {
            if (!segments[s].Change)
                continue;
            var last = s;
            while (last + 2 < segments.Count && segments[last + 1].LeftCount <= 2 * context)
                last += 2;

            var lines = ImmutableArray.CreateBuilder<DiffLine>();
            var lead = s > 0 ? Math.Min(context, segments[s - 1].LeftCount) : 0;
            if (lead > 0)
            {
                var before = segments[s - 1];
                AddContext(lines, left, before.LeftStart + before.LeftCount - lead, before.RightStart + before.RightCount - lead, lead);
            }

            for (var k = s; k <= last; k++)
            {
                var segment = segments[k];
                if (segment.Change)
                    AddChange(lines, left, right, segment, steps);
                else
                    AddContext(lines, left, segment.LeftStart, segment.RightStart, segment.LeftCount);
            }

            if (last + 1 < segments.Count)
            {
                var after = segments[last + 1];
                AddContext(lines, left, after.LeftStart, after.RightStart, Math.Min(context, after.LeftCount));
            }

            var first = lines[0];
            var leftStart = first.LeftLine ?? segments[s].LeftStart;
            var rightStart = first.RightLine ?? segments[s].RightStart;
            var leftCount = lines.Count(l => l.Kind != DiffLineKind.Added);
            var rightCount = lines.Count(l => l.Kind != DiffLineKind.Removed);
            hunks.Add(new DiffHunk(leftStart, leftCount, rightStart, rightCount, lines.ToImmutable()));
            s = last;
        }

        return hunks.ToImmutable();
    }

    private static void AddContext(ImmutableArray<DiffLine>.Builder lines, List<DiffTextLine> left, int leftStart, int rightStart, int count)
    {
        for (var k = 0; k < count; k++)
            lines.Add(new DiffLine(DiffLineKind.Context, leftStart + k, rightStart + k, left[leftStart + k].Text, []));
    }

    /// <summary>All removed lines, then all added lines; the i-th removed and the i-th added line get intra-line spans (IDE-DIFF-005).</summary>
    private static void AddChange(ImmutableArray<DiffLine>.Builder lines, List<DiffTextLine> left, List<DiffTextLine> right, Segment block, DiffSteps steps)
    {
        var pairs = Math.Min(block.LeftCount, block.RightCount);
        var spans = new (ImmutableArray<TextSpan> Left, ImmutableArray<TextSpan> Right)[pairs];
        for (var k = 0; k < pairs; k++)
            spans[k] = IntraLine.Spans(left[block.LeftStart + k].Text, right[block.RightStart + k].Text, steps);
        for (var k = 0; k < block.LeftCount; k++)
            lines.Add(new DiffLine(DiffLineKind.Removed, block.LeftStart + k, null, left[block.LeftStart + k].Text, k < pairs ? spans[k].Left : []));
        for (var k = 0; k < block.RightCount; k++)
            lines.Add(new DiffLine(DiffLineKind.Added, null, block.RightStart + k, right[block.RightStart + k].Text, k < pairs ? spans[k].Right : []));
    }

    /// <summary>IDE-DIFF-006: line-ending kind, encoding and line break at the end of the file, when they differ.</summary>
    private static ImmutableArray<string> Notes(DiffInput left, DiffInput right, List<DiffTextLine> leftLines, List<DiffTextLine> rightLines,
        bool leftFinal, bool rightFinal)
    {
        var notes = ImmutableArray.CreateBuilder<string>();
        if (DiffText.BreakKind(leftLines) is { } leftKind && DiffText.BreakKind(rightLines) is { } rightKind && leftKind != rightKind)
            notes.Add(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Diff_NoteLineEndings, KindName(leftKind), KindName(rightKind)));
        if (left.EncodingName is { } leftEncoding && right.EncodingName is { } rightEncoding
            && !string.Equals(leftEncoding, rightEncoding, StringComparison.OrdinalIgnoreCase))
            notes.Add(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Diff_NoteEncoding, leftEncoding, rightEncoding));
        if (leftLines.Count > 0 && rightLines.Count > 0 && leftFinal != rightFinal)
            notes.Add(rightFinal ? Strings.Msg_Diff_NoteFinalBreakAdded : Strings.Msg_Diff_NoteFinalBreakRemoved);
        return notes.ToImmutable();
    }

    /// <summary>The status-bar names (CRLF, LF, Mixed); CR has no status-bar entry and is written as is.</summary>
    private static string KindName(string kind) => kind switch
    {
        "CRLF" => Strings.StatusBar_LineEnding_CrLf,
        "LF" => Strings.StatusBar_LineEnding_Lf,
        DiffText.Mixed => Strings.StatusBar_LineEnding_Mixed,
        _ => kind,
    };
}
