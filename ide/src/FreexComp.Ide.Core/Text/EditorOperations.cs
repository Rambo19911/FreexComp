using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Languages;

namespace FreexComp.Ide.Core.Text;

/// <summary>Anchor and caret offsets; equal = no selection.</summary>
public readonly record struct TextSelection(int Anchor, int Caret)
{
    public int Start => Math.Min(Anchor, Caret);
    public int End => Math.Max(Anchor, Caret);
    public bool IsEmpty => Anchor == Caret;

    public static TextSelection At(int offset) => new(offset, offset);
}

/// <summary>The indentation settings of editor-features.md §2.</summary>
public readonly record struct IndentOptions(int TabWidth, bool InsertSpaces, bool AutoIndent, bool SmartHome)
{
    /// <summary>One indent unit: tab-width spaces, or one tab.</summary>
    public string Unit => InsertSpaces ? new string(' ', TabWidth) : "\t";
}

/// <summary>
/// The result of an editor operation: non-overlapping <see cref="Edits"/> in original offsets (applied as one undo
/// step) and the selection afterwards, in offsets of the edited text.
/// </summary>
public sealed record EditResult(IReadOnlyList<TextEdit> Edits, TextSelection Selection);

/// <summary>
/// The text operations of editor-features.md §4 as pure functions over a buffer that they only read
/// (IDE-ARCH-110): each returns the edits and the new selection, or null when there is nothing to do. The document
/// applies a result as one undo step.
/// </summary>
public static class EditorOperations
{
    /// <summary>A placeholder path for the compiler lexer (it does not affect tokens).</summary>
    internal const string LexerPath = @"C:\Untitled.pas";

    /// <summary>IDE-ED-030: the selection's first to last line, without a last line on which a non-empty selection ends at column 1.</summary>
    public static (int First, int Last) TouchedLines(ITextBuffer buffer, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var first = buffer.GetPosition(selection.Start).Line;
        var end = buffer.GetPosition(selection.End);
        var last = !selection.IsEmpty && end.Column == 1 && end.Line > first ? end.Line - 1 : end.Line;
        return (first, last);
    }

    /// <summary>The text of a line without its line break.</summary>
    public static string LineText(ITextBuffer buffer, int line)
    {
        var (offset, length, _) = buffer.GetLine(line);
        return buffer.GetText(offset, length);
    }

    /// <summary>The 0-based visual column of <paramref name="index"/> in <paramref name="text"/>; tabs advance to the next multiple of <paramref name="tabWidth"/>.</summary>
    public static int VisualColumn(string text, int index, int tabWidth)
    {
        int column = 0;
        for (int i = 0; i < index && i < text.Length; i++)
            column = text[i] == '\t' ? (column / tabWidth + 1) * tabWidth : column + 1;
        return column;
    }

    /// <summary>The first index of <paramref name="text"/> whose visual column is at least <paramref name="column"/> (clamped to the length).</summary>
    public static int IndexOfVisualColumn(string text, int column, int tabWidth)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (VisualColumn(text, i, tabWidth) >= column)
                return i;
        }

        return text.Length;
    }

    /// <summary>The leading run of spaces and tabs.</summary>
    public static string Indentation(string lineText)
    {
        int i = 0;
        while (i < lineText.Length && lineText[i] is ' ' or '\t')
            i++;
        return lineText[..i];
    }

    /// <summary>
    /// Where <paramref name="offset"/> lands after <paramref name="edits"/> (sorted, non-overlapping, original offsets).
    /// An insertion exactly at the offset puts it after the inserted text when <paramref name="stickAfter"/>; an offset
    /// inside replaced text keeps its place when the length is unchanged, else moves to the replacement's start (or end).
    /// </summary>
    public static int MapOffset(int offset, IReadOnlyList<TextEdit> edits, bool stickAfter)
    {
        ArgumentNullException.ThrowIfNull(edits);
        int delta = 0;
        foreach (var e in edits.OrderBy(e => e.Offset))
        {
            if (offset < e.Offset || (offset == e.Offset && e.Length == 0 && !stickAfter))
                break;
            if (offset >= e.Offset + e.Length)
            {
                delta += e.NewText.Length - e.Length;
                continue;
            }

            if (e.Length == e.NewText.Length)
                return offset + delta;
            return e.Offset + delta + (stickAfter ? e.NewText.Length : 0);
        }

        return offset + delta;
    }

    /// <summary>The selection after <paramref name="edits"/>: its start stays before text inserted there, its end follows it.</summary>
    public static TextSelection MapSelection(TextSelection selection, IReadOnlyList<TextEdit> edits)
    {
        if (selection.IsEmpty)
            return TextSelection.At(MapOffset(selection.Caret, edits, stickAfter: true));
        var start = MapOffset(selection.Start, edits, stickAfter: false);
        var end = MapOffset(selection.End, edits, stickAfter: true);
        return selection.Caret >= selection.Anchor ? new TextSelection(start, end) : new TextSelection(end, start);
    }

    private static EditResult? Mapped(TextSelection selection, List<TextEdit> edits) =>
        edits.Count == 0 ? null : new EditResult(edits, MapSelection(selection, edits));

    /// <summary>The line break that ends <paramref name="line"/>, or <paramref name="newLine"/> (the document's) for the last line.</summary>
    private static string BreakAfter(ITextBuffer buffer, int line, string newLine)
    {
        var (offset, length, delimiter) = buffer.GetLine(line);
        return delimiter > 0 ? buffer.GetText(offset + length, delimiter) : newLine;
    }

    /// <summary>IDE-ED-031 <c>edit.duplicateLine</c>.</summary>
    public static EditResult DuplicateLine(ITextBuffer buffer, TextSelection selection, string newLine)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var start = buffer.GetPosition(selection.Start);
        var end = buffer.GetPosition(selection.End);
        if (!selection.IsEmpty && start.Line == end.Line)
        {
            var copy = buffer.GetText(selection.Start, selection.End - selection.Start);
            return new EditResult([new TextEdit(selection.End, 0, copy)], new TextSelection(selection.End, selection.End + copy.Length));
        }

        var (first, last) = TouchedLines(buffer, selection);
        var blockStart = buffer.GetLine(first).Offset;
        var (lastOffset, lastLength, _) = buffer.GetLine(last);
        var block = buffer.GetText(blockStart, lastOffset + lastLength - blockStart);
        var lineBreak = BreakAfter(buffer, last, newLine);
        var insertAt = lastOffset + lastLength;
        var copyStart = insertAt + lineBreak.Length;
        if (selection.IsEmpty)
            return new EditResult([new TextEdit(insertAt, 0, lineBreak + block)], TextSelection.At(copyStart + start.Column - 1));
        return new EditResult([new TextEdit(insertAt, 0, lineBreak + block)], new TextSelection(copyStart, copyStart + block.Length));
    }

    /// <summary>IDE-ED-032 <c>edit.deleteLine</c>; the caret keeps its visual column on the line that takes their place.</summary>
    public static EditResult DeleteLine(ITextBuffer buffer, TextSelection selection, int tabWidth)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var (first, last) = TouchedLines(buffer, selection);
        var caret = buffer.GetPosition(selection.Caret);
        var visual = VisualColumn(LineText(buffer, caret.Line), caret.Column - 1, tabWidth);
        int removeStart, removeEnd, targetLine;
        if (last < buffer.LineCount)
        {
            (removeStart, removeEnd, targetLine) = (buffer.GetLine(first).Offset, buffer.GetLine(last + 1).Offset, last + 1);
        }
        else if (first > 1)
        {
            var (previousOffset, previousLength, _) = buffer.GetLine(first - 1);
            (removeStart, removeEnd, targetLine) = (previousOffset + previousLength, buffer.Length, first - 1);
        }
        else
        {
            return new EditResult([new TextEdit(0, buffer.Length, "")], TextSelection.At(0));
        }

        var target = LineText(buffer, targetLine);
        var targetOffset = buffer.GetLine(targetLine).Offset;
        var column = IndexOfVisualColumn(target, visual, tabWidth);
        if (targetOffset >= removeEnd)
            targetOffset -= removeEnd - removeStart;
        return new EditResult([new TextEdit(removeStart, removeEnd - removeStart, "")], TextSelection.At(targetOffset + column));
    }

    /// <summary>
    /// IDE-ED-033 <c>edit.moveLineUp/Down</c>: swaps the touched lines with the line above / below; null at the document
    /// edge. <paramref name="foldedRegions"/> (start and end line of each folded region) are moved over as a whole, and a
    /// folded region starting on the last touched line moves with the block.
    /// </summary>
    /// <summary>
    /// IDE-ED-033: the lines <c>edit.moveLineUp/Down</c> swaps — the touched lines (extended over a folded region
    /// starting on the last one) and the neighbour line (extended over a whole folded region); null at the document edge.
    /// </summary>
    public static (int First, int Last, int OtherFirst, int OtherLast)? MoveRanges(ITextBuffer buffer, TextSelection selection, bool up,
        IReadOnlyList<(int StartLine, int EndLine)> foldedRegions)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(foldedRegions);
        var (first, last) = TouchedLines(buffer, selection);
        foreach (var (s, e) in foldedRegions)
        {
            if (s == last)
                last = Math.Max(last, Math.Min(e, buffer.LineCount));
        }

        int otherFirst, otherLast;
        if (up)
        {
            if (first == 1)
                return null;
            otherFirst = otherLast = first - 1;
            foreach (var (s, e) in foldedRegions)
            {
                if (s < otherLast && otherLast <= e)
                    otherFirst = Math.Min(otherFirst, s);
            }
        }
        else
        {
            if (last >= buffer.LineCount)
                return null;
            otherFirst = otherLast = last + 1;
            foreach (var (s, e) in foldedRegions)
            {
                if (s == otherFirst)
                    otherLast = Math.Max(otherLast, Math.Min(e, buffer.LineCount));
            }
        }

        return (first, last, otherFirst, otherLast);
    }
    public static EditResult? MoveLines(ITextBuffer buffer, TextSelection selection, bool up, IReadOnlyList<(int StartLine, int EndLine)> foldedRegions, string newLine)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(foldedRegions);
        if (MoveRanges(buffer, selection, up, foldedRegions) is not (int first, int last, int otherFirst, int otherLast))
            return null;

        string Lines(int a, int b)
        {
            var o = buffer.GetLine(a).Offset;
            var (lo, ll, _) = buffer.GetLine(b);
            return buffer.GetText(o, lo + ll - o);
        }

        var block = Lines(first, last);
        var other = Lines(otherFirst, otherLast);
        // The break between the two chunks stays between them.
        var upper = up ? otherLast : last;
        var between = BreakAfter(buffer, upper, newLine);
        var regionStart = buffer.GetLine(up ? otherFirst : first).Offset;
        var newText = up ? block + between + other : other + between + block;
        var shift = (other.Length + between.Length) * (up ? -1 : 1);
        return new EditResult([new TextEdit(regionStart, newText.Length, newText)],
            new TextSelection(selection.Anchor + shift, selection.Caret + shift));
    }

    /// <summary>IDE-ED-034 <c>edit.toggleLineComment</c> with the language's line-comment <paramref name="prefix"/>.</summary>
    public static EditResult? ToggleLineComment(ITextBuffer buffer, TextSelection selection, string prefix, int tabWidth)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(prefix);
        var (first, last) = TouchedLines(buffer, selection);
        var lines = Enumerable.Range(first, last - first + 1)
            .Select(l => (Line: l, Text: LineText(buffer, l)))
            .Where(l => l.Text.Trim(' ', '\t').Length > 0)
            .ToList();
        if (lines.Count == 0)
            return null;
        var edits = new List<TextEdit>();
        if (lines.All(l => l.Text[Indentation(l.Text).Length..].StartsWith(prefix, StringComparison.Ordinal)))
        {
            foreach (var (line, text) in lines)
            {
                int at = Indentation(text).Length, length = prefix.Length;
                if (at + length < text.Length && text[at + length] == ' ')
                    length++;
                edits.Add(new TextEdit(buffer.GetLine(line).Offset + at, length, ""));
            }
        }
        else
        {
            var column = lines.Min(l => VisualColumn(l.Text, Indentation(l.Text).Length, tabWidth));
            foreach (var (line, text) in lines)
                edits.Add(new TextEdit(buffer.GetLine(line).Offset + IndexOfVisualColumn(text, column, tabWidth), 0, prefix + " "));
        }

        return Mapped(selection, edits);
    }

    /// <summary>
    /// IDE-ED-035 <c>edit.toggleBlockComment</c> for Object Pascal (<c>{ }</c>, <c>(* *)</c>). Comment tokens come from
    /// the compiler lexer. <paramref name="refusedMessage"/> is the status resource when the selection cannot be commented.
    /// </summary>
    public static EditResult? ToggleBlockComment(ITextBuffer buffer, TextSelection selection, out string? refusedMessage)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        refusedMessage = null;
        if (selection.IsEmpty)
        {
            var caret = selection.Caret;
            foreach (var comment in BlockComments(buffer.Text))
            {
                if (comment.Start < caret && caret < comment.End)
                    return Mapped(selection, Unwrap(buffer.GetText(comment.Start, comment.End - comment.Start), comment.Start));
            }

            return new EditResult([new TextEdit(caret, 0, "{}")], TextSelection.At(caret + 1));
        }

        var text = buffer.GetText(selection.Start, selection.End - selection.Start);
        var trimmed = text.Trim();
        var trimmedStart = selection.Start + (text.Length - text.TrimStart().Length);
        var whole = BlockComments(trimmed).ToList();
        if (whole.Count == 1 && whole[0].Start == 0 && whole[0].End == trimmed.Length)
            return Mapped(selection, Unwrap(trimmed, trimmedStart));

        var (open, close) = ("{", "}");
        if (text.Contains('}', StringComparison.Ordinal))
        {
            if (text.Contains("*)", StringComparison.Ordinal))
            {
                refusedMessage = "Status_CannotBlockComment";
                return null;
            }

            (open, close) = ("(*", "*)");
        }

        return Mapped(selection, [new TextEdit(selection.Start, 0, open), new TextEdit(selection.End, 0, close)]);
    }

    /// <summary>Removes the delimiters of the comment <paramref name="comment"/> starting at <paramref name="offset"/>.</summary>
    private static List<TextEdit> Unwrap(string comment, int offset)
    {
        var (open, close) = comment.StartsWith('{') ? (1, 1) : (2, 2);
        if (comment.Length < open + close || (open == 1 ? comment[^1] != '}' : !comment.EndsWith("*)", StringComparison.Ordinal)))
            close = 0; // an unterminated comment has no closer
        return [new TextEdit(offset, open, ""), new TextEdit(offset + comment.Length - close, close, "")];
    }

    /// <summary>The <c>{…}</c> and <c>(*…*)</c> comments of Object Pascal text (from the token trivia).</summary>
    private static IEnumerable<(int Start, int End)> BlockComments(string text)
    {
        foreach (var token in Lexer.Tokenize(SourceText.From(LexerPath, text)))
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                if (trivia.Kind is SyntaxKind.BlockCommentTrivia
                    || (trivia.Kind == SyntaxKind.DocCommentTrivia && !trivia.Text.StartsWith("//", StringComparison.Ordinal)))
                    yield return (trivia.Span.Start, trivia.Span.End);
            }
        }
    }

    /// <summary>IDE-ED-036 <c>edit.upperCase/lowerCase</c>: the selection, else the word under the caret (invariant culture).</summary>
    public static EditResult? ChangeCase(ITextBuffer buffer, TextSelection selection, bool upper)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var (start, end) = selection.IsEmpty ? WordAt(buffer, selection.Caret) : (selection.Start, selection.End);
        if (start == end)
            return null;
        var text = buffer.GetText(start, end - start);
        var changed = upper ? text.ToUpperInvariant() : text.ToLowerInvariant();
        return changed == text || changed.Length != text.Length ? null : new EditResult([new TextEdit(start, end - start, changed)], selection);
    }

    /// <summary>The identifier characters around <paramref name="offset"/> (empty when there are none).</summary>
    public static (int Start, int End) WordAt(ITextBuffer buffer, int offset)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var position = buffer.GetPosition(offset);
        var lineOffset = buffer.GetLine(position.Line).Offset;
        var text = LineText(buffer, position.Line);
        int start = position.Column - 1, end = start;
        while (start > 0 && IsWordChar(text[start - 1]))
            start--;
        while (end < text.Length && IsWordChar(text[end]))
            end++;
        return (lineOffset + start, lineOffset + end);
    }

    private static bool IsWordChar(char c) => c == '_' || char.IsLetterOrDigit(c);

    /// <summary>The selection spans a line break (Tab = <c>edit.indent</c>, IDE-ED-002).</summary>
    public static bool SpansLines(ITextBuffer buffer, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer.GetPosition(selection.Start).Line != buffer.GetPosition(selection.End).Line;
    }

    /// <summary>IDE-ED-001 Tab: spaces to the next tab stop (or one tab) replacing a selection within one line; IDE-ED-002 for one spanning lines.</summary>
    public static EditResult? Tab(ITextBuffer buffer, TextSelection selection, IndentOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (SpansLines(buffer, selection))
            return Indent(buffer, selection, options);
        var position = buffer.GetPosition(selection.Start);
        var column = VisualColumn(LineText(buffer, position.Line), position.Column - 1, options.TabWidth);
        var text = options.InsertSpaces ? new string(' ', options.TabWidth - column % options.TabWidth) : "\t";
        return new EditResult([new TextEdit(selection.Start, selection.End - selection.Start, text)], TextSelection.At(selection.Start + text.Length));
    }

    /// <summary>IDE-ED-002 <c>edit.indent</c>: one indent unit before every touched line that is not empty; the selection grows over it.</summary>
    public static EditResult? Indent(ITextBuffer buffer, TextSelection selection, IndentOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var (first, last) = TouchedLines(buffer, selection);
        var edits = new List<TextEdit>();
        for (int line = first; line <= last; line++)
        {
            var (offset, length, _) = buffer.GetLine(line);
            if (length > 0)
                edits.Add(new TextEdit(offset, 0, options.Unit));
        }

        return Mapped(selection, edits);
    }

    /// <summary>IDE-ED-003 <c>edit.outdent</c>: a leading tab, else up to tab-width leading spaces, from every touched line.</summary>
    public static EditResult? Outdent(ITextBuffer buffer, TextSelection selection, IndentOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var (first, last) = TouchedLines(buffer, selection);
        var edits = new List<TextEdit>();
        for (int line = first; line <= last; line++)
        {
            var text = LineText(buffer, line);
            int remove = 0;
            if (text.StartsWith('\t'))
                remove = 1;
            else
                while (remove < options.TabWidth && remove < text.Length && text[remove] == ' ')
                    remove++;
            if (remove > 0)
                edits.Add(new TextEdit(buffer.GetLine(line).Offset, remove, ""));
        }

        return Mapped(selection, edits);
    }

    /// <summary>
    /// IDE-ED-004 Backspace: the selection; else in leading spaces (with insertSpaces) back to the previous tab stop;
    /// else one character (a line break or a surrogate pair as one). Null at the start of the document.
    /// </summary>
    public static EditResult? Backspace(ITextBuffer buffer, TextSelection selection, IndentOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!selection.IsEmpty)
            return new EditResult([new TextEdit(selection.Start, selection.End - selection.Start, "")], TextSelection.At(selection.Start));
        var caret = selection.Caret;
        if (caret == 0)
            return null;
        var position = buffer.GetPosition(caret);
        int length;
        if (position.Column == 1)
        {
            var (previousOffset, previousLength, _) = buffer.GetLine(position.Line - 1);
            length = caret - (previousOffset + previousLength);
        }
        else
        {
            var left = LineText(buffer, position.Line)[..(position.Column - 1)];
            if (options.InsertSpaces && left.All(c => c == ' '))
                length = left.Length - (left.Length - 1) / options.TabWidth * options.TabWidth;
            else
                length = left.Length >= 2 && char.IsSurrogatePair(left[^2], left[^1]) ? 2 : 1;
        }

        return new EditResult([new TextEdit(caret - length, length, "")], TextSelection.At(caret - length));
    }

    /// <summary>
    /// IDE-ED-005…007 Enter: a line break (<paramref name="newLine"/>) replacing the selection; with auto-indent the new
    /// line gets the old line's indentation left of the caret (plus one unit when <paramref name="rules"/> say so) and
    /// whitespace before the break is removed.
    /// </summary>
    public static EditResult Enter(ITextBuffer buffer, TextSelection selection, IndentOptions options, IIndentRules? rules, string newLine)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(newLine);
        if (!options.AutoIndent)
            return new EditResult([new TextEdit(selection.Start, selection.End - selection.Start, newLine)], TextSelection.At(selection.Start + newLine.Length));
        var position = buffer.GetPosition(selection.Start);
        var left = LineText(buffer, position.Line)[..(position.Column - 1)];
        var indent = Indentation(left);
        if (rules is not null && rules.IncreasesIndent(left))
            indent += options.Unit;
        var start = selection.Start - (left.Length - left.TrimEnd(' ', '\t').Length);
        var text = newLine + indent;
        return new EditResult([new TextEdit(start, selection.End - start, text)], TextSelection.At(start + text.Length));
    }

    /// <summary>
    /// IDE-ED-008: after typing at <paramref name="caret"/>, a closer that starts its line and sits at the previous
    /// non-blank line's indentation or one unit deeper than its opener's line is re-indented to the opener's line.
    /// </summary>
    public static EditResult? ReindentCloser(ITextBuffer buffer, int caret, IndentOptions options, IIndentRules? rules)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (rules?.MatchingOpenerLine(buffer, caret) is not { } openerLine)
            return null;
        var line = buffer.GetPosition(caret).Line;
        var current = Indentation(LineText(buffer, line));
        var target = Indentation(LineText(buffer, openerLine));
        int Width(string indentation) => VisualColumn(indentation, indentation.Length, options.TabWidth);
        var previous = line - 1;
        while (previous >= 1 && LineText(buffer, previous).Trim(' ', '\t').Length == 0)
            previous--;
        var atPrevious = previous >= 1 && Width(Indentation(LineText(buffer, previous))) == Width(current);
        if (current == target || !(atPrevious || Width(current) == Width(target) + Width(options.Unit)))
            return null;
        var offset = buffer.GetLine(line).Offset;
        return new EditResult([new TextEdit(offset, current.Length, target)], TextSelection.At(caret - current.Length + target.Length));
    }

    /// <summary>IDE-ED-011 Home: the first non-blank character (smart Home), or column 1 when already there, on a blank line or without smart Home.</summary>
    public static TextSelection Home(ITextBuffer buffer, TextSelection selection, IndentOptions options, bool extend)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var position = buffer.GetPosition(selection.Caret);
        var lineOffset = buffer.GetLine(position.Line).Offset;
        var text = LineText(buffer, position.Line);
        var firstNonBlank = Indentation(text).Length;
        var target = options.SmartHome && firstNonBlank < text.Length && position.Column - 1 != firstNonBlank ? firstNonBlank : 0;
        return new TextSelection(extend ? selection.Anchor : lineOffset + target, lineOffset + target);
    }

    /// <summary>IDE-ED-037 <c>edit.trimTrailingWhitespace</c>: the touched lines with a selection, else every line.</summary>
    public static EditResult? TrimTrailingWhitespace(ITextBuffer buffer, TextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var (first, last) = selection.IsEmpty ? (1, buffer.LineCount) : TouchedLines(buffer, selection);
        var edits = new List<TextEdit>();
        for (int line = first; line <= last; line++)
        {
            var (offset, _, _) = buffer.GetLine(line);
            var text = LineText(buffer, line);
            var kept = text.TrimEnd(' ', '\t').Length;
            if (kept < text.Length)
                edits.Add(new TextEdit(offset + kept, text.Length - kept, ""));
        }

        return Mapped(selection, edits);
    }
}
