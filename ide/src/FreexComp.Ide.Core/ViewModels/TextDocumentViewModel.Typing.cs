using System.Text;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Typing aids of editor-features.md §3: auto-close (IDE-ED-020), type-over (021), pair delete (022), selection wrap
/// (023), overwrite mode (025) and column blocks (026). Typed text enters through <see cref="TypeText"/>.
/// </summary>
public partial class TextDocumentViewModel
{
    private const string ToggleOverwriteCommandId = "edit.toggleOverwrite";

    /// <summary>An auto-inserted bracket or quote pair still open for type-over and pair delete (offsets of opener and closer).</summary>
    private sealed class AutoPair(int opener, int closer)
    {
        public int Opener { get; } = opener;
        public int Closer { get; set; } = closer;
    }

    private readonly List<AutoPair> _pairs = [];

    /// <summary>The last copied column block, shared by the documents of one IDE (set by the document service).</summary>
    public BlockClipboard BlockClipboard { get; internal set; } = new();

    private void InitTyping()
    {
        Buffer.Changed += (_, e) => TrackPairs(e);
        ViewState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(EditorViewState.Caret))
                return;
            _pairs.RemoveAll(p => !Inside(p, OffsetOf(ViewState.Caret)));
            OnCaretMovedForUndo();
        };
    }

    private static bool Inside(AutoPair pair, int caret) => pair.Opener < caret && caret <= pair.Closer;

    /// <summary>
    /// IDE-ED-021: a pair is tracked while the edits stay inside it on its line; any other edit (another line, a line
    /// break, the pair's own characters) ends it.
    /// </summary>
    private void TrackPairs(TextChangedEventArgs e)
    {
        var delta = e.InsertedText.Length - e.RemovedLength;
        var breaks = e.InsertedText.AsSpan().IndexOfAny('\r', '\n') >= 0;
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            var pair = _pairs[i];
            if (!breaks && e.Offset > pair.Opener && e.Offset + e.RemovedLength <= pair.Closer)
                pair.Closer += delta;
            else
                _pairs.RemoveAt(i);
        }
    }

    private bool CanToggleOverwrite => true; // a view-state mode: available on read-only documents too

    private bool ToggleOverwrite()
    {
        ViewState.Overwrite = !ViewState.Overwrite;
        return true;
    }

    /// <summary>
    /// Typed text (the editor view's text input): typing aids first, then the selection (or, in overwrite mode, the
    /// character at the caret) is replaced through <see cref="TryInsertText"/>; a typed closer is re-indented in the
    /// same undo step (IDE-ED-008).
    /// </summary>
    public bool TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return false;
        Completion?.BeforeTyped(text); // IDE-ED-084: a commit character commits first
        var selection = Selection;
        var block = Block;
        // IDE-P1-091: consecutive typed characters at the caret join one undo step.
        var continues = block is null && selection.IsEmpty && ContinuesGroup(EditKind.Typing, selection.Caret, char.IsWhiteSpace(text[0]));
        var kind = EditKind.Typing;
        var done = Act(continues, () =>
        {
            if (block is { } b)
            {
                kind = EditKind.None;
                return ReplaceBlock(b, text);
            }

            if (text.Length == 1 && TypeAid(text[0], selection) is { } handled)
            {
                kind = EditKind.None;
                return handled;
            }

            return InsertTyped(selection, text);
        });
        EndEdit(done ? kind : EditKind.None, char.IsWhiteSpace(text[^1]));
        if (done)
            Completion?.AfterTyped(text); // IDE-ED-080/082
        return done;
    }

    /// <summary>Plain typed text: replaces the selection or, in overwrite mode, the character at the caret.</summary>
    private bool InsertTyped(TextSelection selection, string text)
    {
        var replaced = selection.End - selection.Start;
        if (selection.IsEmpty && ViewState.Overwrite && text.AsSpan().IndexOfAny('\r', '\n') < 0)
            replaced = OverwrittenLength(selection.Caret);
        var before = Buffer.Length;
        if (!TryInsertText(selection.Start, replaced, text))
            return false;
        Selection = TextSelection.At(selection.Start + replaced + Buffer.Length - before);
        Apply(EditorOperations.ReindentCloser(Buffer, Selection.Caret, IndentOptions, _language.IndentRules));
        return true;
    }

    /// <summary>IDE-ED-025: the character at the caret (a surrogate pair as one); none at the end of a line.</summary>
    private int OverwrittenLength(int caret)
    {
        if (caret >= Buffer.Length)
            return 0;
        var next = Buffer.GetText(caret, Math.Min(2, Buffer.Length - caret));
        if (next[0] is '\r' or '\n')
            return 0;
        return next.Length == 2 && char.IsSurrogatePair(next[0], next[1]) ? 2 : 1;
    }

    /// <summary>Type-over, selection wrap and auto-close for one typed character; null when none applies.</summary>
    private bool? TypeAid(char ch, TextSelection selection)
    {
        var caret = selection.Caret;
        if (selection.IsEmpty && ch is ')' or ']' or '\'' && _pairs.Find(p => p.Closer == caret) is { } pair
            && Buffer.GetText(caret, 1)[0] == ch)
        {
            _pairs.Remove(pair);
            Selection = TextSelection.At(caret + 1); // IDE-ED-021
            return true;
        }

        if (!Options.AutoCloseBrackets)
            return null;
        var closer = ch switch { '(' => ')', '[' => ']', '\'' => '\'', _ => '\0' };
        if (closer == '\0')
            return null;
        if (!selection.IsEmpty) // IDE-ED-023: wrap and keep the inner text selected
        {
            if (!TryEdit([new TextEdit(selection.Start, 0, ch.ToString()), new TextEdit(selection.End, 0, closer.ToString())]))
                return false;
            Selection = new TextSelection(selection.Anchor + 1, selection.Caret + 1);
            return true;
        }

        if (ViewState.Overwrite || !ClosesHere(ch, caret)) // IDE-ED-025: no auto-close in overwrite mode
            return null;
        if (!TryEdit([new TextEdit(caret, 0, $"{ch}{closer}")]))
            return false;
        _pairs.Add(new AutoPair(caret, caret + 1));
        Selection = TextSelection.At(caret + 1);
        return true;
    }

    /// <summary>IDE-ED-020: where a typed opener gets its closer.</summary>
    private bool ClosesHere(char opener, int caret)
    {
        if (opener == '\'')
        {
            var left = caret > 0 ? Buffer.GetText(caret - 1, 1)[0] : ' ';
            return !char.IsLetterOrDigit(left) && left != '\'' && !InStringOrComment(caret);
        }

        if (caret >= Buffer.Length)
            return true;
        var right = Buffer.GetText(caret, 1)[0];
        return char.IsWhiteSpace(right) || right is ')' or ']' or ';' or ',' or '.';
    }

    /// <summary>The lexer state at <paramref name="caret"/> (the language's own tokens): inside a string, comment or directive.</summary>
    private bool InStringOrComment(int caret)
    {
        var line = Buffer.GetPosition(caret).Line;
        foreach (var span in _language.CreateClassifier().Classify(Buffer, new LineRange(line, line)))
        {
            if (span.TokenClass is not ("syntax.string" or "syntax.char" or "syntax.comment" or "syntax.directive" or "syntax.invalid"))
                continue;
            var end = span.Offset + span.Length;
            if (span.Offset < caret && caret < end)
                return true;
            if (caret == end && span.Length > 0 && IsOpenAtEnd(Buffer.GetText(span.Offset, span.Length)))
                return true;
        }

        return false;
    }

    /// <summary>A token that the caret at its end is still inside: a line comment, an unterminated comment or string.</summary>
    private static bool IsOpenAtEnd(string token) =>
        token.StartsWith("//", StringComparison.Ordinal)
        || (token.StartsWith('{') && !token.EndsWith('}'))
        || (token.StartsWith("(*", StringComparison.Ordinal) && (token.Length < 4 || !token.EndsWith("*)", StringComparison.Ordinal)))
        || (token.StartsWith('\'') && token.Count(c => c == '\'') % 2 == 1);

    /// <summary>IDE-ED-022: Backspace between an auto-inserted pair deletes both; null when it does not apply.</summary>
    private bool? PairDelete(TextSelection selection)
    {
        if (!selection.IsEmpty || _pairs.Find(p => p.Opener == selection.Caret - 1 && p.Closer == selection.Caret) is not { } pair)
            return null;
        _pairs.Remove(pair);
        return Apply(new EditResult([new TextEdit(pair.Opener, 2, "")], TextSelection.At(pair.Opener)));
    }

    // IDE-ED-026: column blocks.

    /// <summary>A column block: lines and 0-based visual columns [Left, Right).</summary>
    private readonly record struct ColumnBlock(int FirstLine, int LastLine, int Left, int Right);

    /// <summary>The selection as a column block, or null when it is not rectangular.</summary>
    private ColumnBlock? Block
    {
        get
        {
            if (!ViewState.RectangularSelection || ViewState.SelectionAnchor is not { } anchor)
                return null;
            var caret = ViewState.Caret;
            int Visual(TextPosition p) => EditorOperations.VisualColumn(EditorOperations.LineText(Buffer, p.Line), p.Column - 1, Options.TabWidth);
            int a = Visual(anchor), c = Visual(caret);
            return new ColumnBlock(Math.Min(anchor.Line, caret.Line), Math.Max(anchor.Line, caret.Line), Math.Min(a, c), Math.Max(a, c));
        }
    }

    /// <summary>The part of <paramref name="line"/> between two visual columns, as line-relative indices.</summary>
    private (int Start, int End) Part(string line, int left, int right) =>
        (EditorOperations.IndexOfVisualColumn(line, left, Options.TabWidth), EditorOperations.IndexOfVisualColumn(line, right, Options.TabWidth));

    /// <summary>Spaces that extend <paramref name="line"/> to visual column <paramref name="column"/> when it is shorter.</summary>
    private string Padding(string line, int column) =>
        new(' ', Math.Max(0, column - EditorOperations.VisualColumn(line, line.Length, Options.TabWidth)));

    /// <summary>Typing (or deleting, with empty text) in a column block: every line's part is replaced in one undo step.</summary>
    private bool ReplaceBlock(ColumnBlock block, string text)
    {
        var edits = new List<TextEdit>();
        var columns = new List<int>();
        for (int line = block.FirstLine; line <= block.LastLine; line++)
        {
            var lineText = EditorOperations.LineText(Buffer, line);
            var (start, end) = Part(lineText, block.Left, block.Right);
            var pad = text.Length > 0 ? Padding(lineText, block.Left) : "";
            edits.Add(new TextEdit(Buffer.GetLine(line).Offset + start, end - start, pad + text));
            columns.Add(start + pad.Length + text.Length + 1);
        }

        if (!TryEdit(edits))
            return false;
        ViewState.SelectionAnchor = new TextPosition(block.FirstLine, columns[0]);
        ViewState.Caret = new TextPosition(block.LastLine, columns[^1]);
        ViewState.RectangularSelection = true;
        return true;
    }

    /// <summary>
    /// Copy (IDE-ED-026): the selected text; a column block as its lines joined by the document line ending, remembered
    /// in <see cref="BlockClipboard"/> so that pasting it inserts it line by line.
    /// </summary>
    public string CopySelection()
    {
        if (Block is not { } block)
        {
            BlockClipboard.Text = null;
            var s = Selection;
            return Buffer.GetText(s.Start, s.End - s.Start);
        }

        var lines = new List<string>();
        for (int line = block.FirstLine; line <= block.LastLine; line++)
        {
            var lineText = EditorOperations.LineText(Buffer, line);
            var (start, end) = Part(lineText, block.Left, block.Right);
            lines.Add(lineText[start..end]);
        }

        return BlockClipboard.Text = string.Join(LineEnding.NewLine, lines);
    }

    /// <summary>Cut after <see cref="CopySelection"/>: removes the selection (each line's part of a column block).</summary>
    public bool DeleteSelection() => Command(DeleteSelectionCore);

    private bool DeleteSelectionCore()
    {
        if (Block is { } block)
            return ReplaceBlock(block, "");
        var s = Selection;
        if (s.IsEmpty || !TryInsertText(s.Start, s.End - s.Start, ""))
            return false;
        Selection = TextSelection.At(s.Start);
        return true;
    }

    /// <summary>
    /// Paste (IDE-P1-076, IDE-ED-026): a block copied by <see cref="CopySelection"/> into an empty or rectangular selection
    /// goes in line by line at the same column; single-line text into a column block replaces every line's part; other
    /// text replaces the selection with the document's line breaks. One undo step.
    /// </summary>
    public bool Paste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Command(() => PasteCore(text));
    }

    private bool PasteCore(string text)
    {
        var block = Block;
        var multiLine = text.AsSpan().IndexOfAny('\r', '\n') >= 0;
        if (block is { } b && !multiLine)
            return ReplaceBlock(b, text);
        var selection = Selection;
        if (block is not null || (selection.IsEmpty && text == BlockClipboard.Text && multiLine))
            return PasteBlock(text, block);
        var before = Buffer.Length;
        if (!TryInsertText(selection.Start, selection.End - selection.Start, text))
            return false;
        Selection = TextSelection.At(selection.End + Buffer.Length - before);
        return true;
    }

    private bool PasteBlock(string text, ColumnBlock? target)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var caret = ViewState.Caret;
        var first = target?.FirstLine ?? caret.Line;
        var left = target?.Left ?? EditorOperations.VisualColumn(EditorOperations.LineText(Buffer, caret.Line), caret.Column - 1, Options.TabWidth);
        var right = target?.Right ?? left;
        var last = Math.Max(first + lines.Length - 1, target?.LastLine ?? first);
        var edits = new List<TextEdit>();
        var appended = new StringBuilder();
        TextPosition end = caret;
        for (int line = first; line <= last; line++)
        {
            var piece = line - first < lines.Length ? lines[line - first] : "";
            if (line > Buffer.LineCount)
            {
                var pad = new string(' ', left);
                appended.Append(LineEnding.NewLine).Append(pad).Append(piece);
                end = new TextPosition(line, pad.Length + piece.Length + 1);
                continue;
            }

            var lineText = EditorOperations.LineText(Buffer, line);
            var (start, stop) = target is null || line > target.Value.LastLine ? Part(lineText, left, left) : Part(lineText, left, right);
            var padding = piece.Length > 0 ? Padding(lineText, left) : "";
            edits.Add(new TextEdit(Buffer.GetLine(line).Offset + start, stop - start, padding + piece));
            if (line - first < lines.Length)
                end = new TextPosition(line, start + padding.Length + piece.Length + 1);
        }

        if (appended.Length > 0)
        {
            var at = edits.FindIndex(e => e.Offset + e.Length == Buffer.Length);
            if (at >= 0)
                edits[at] = edits[at] with { NewText = edits[at].NewText + appended };
            else
                edits.Add(new TextEdit(Buffer.Length, 0, appended.ToString()));
        }

        if (!TryEdit(edits))
            return false;
        ViewState.RectangularSelection = false;
        ViewState.SelectionAnchor = null;
        ViewState.Caret = end;
        return true;
    }
}
