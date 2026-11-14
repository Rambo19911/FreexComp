using FreexComp.Ide.Core.Text;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Languages;

/// <summary>1-based, inclusive range of buffer lines.</summary>
public readonly record struct LineRange(int FirstLine, int LastLine);

/// <summary>
/// IDE-side coloured span (architecture.md §11): <see cref="TokenClass"/> is a colour-token name
/// (<c>syntax.keyword</c>, …; design-tokens.md §3.1). Not the compiler's <c>ClassifiedSpan</c>. A <c>syntax.invalid</c>
/// span may carry the lexer's diagnostic <paramref name="Code"/> and <paramref name="Message"/> (error tooltip, UI-ED-131).
/// </summary>
public readonly record struct TokenClassSpan(int Offset, int Length, string TokenClass, string? Code = null, string? Message = null);

/// <summary>Colours a buffer (architecture.md §11). One instance per document; UI thread only.</summary>
public interface ITokenClassifier
{
    /// <summary>Spans touching <paramref name="lines"/>, sorted by offset, non-overlapping. Unclassified text has no span.</summary>
    IReadOnlyList<TokenClassSpan> Classify(ITextBuffer buffer, LineRange lines);

    /// <summary>
    /// Spans computed in the background arrived (IDE-P1-053): the view redraws. Raised on the synchronization context
    /// of the <see cref="Classify"/> call that started the work (the UI thread), or on the thread pool without one.
    /// </summary>
    event EventHandler? ClassificationChanged;
}

/// <summary>A file type the editor knows (architecture.md §11 extension point "Language").</summary>
public interface ILanguageDefinition
{
    /// <summary>Stable id: <c>pascal</c>, <c>form</c>, <c>json</c>, <c>plaintext</c>.</summary>
    string Id { get; }

    /// <summary>Name shown in status-bar field R8 (main-window.md §5).</summary>
    string DisplayName { get; }

    /// <summary>File extensions with the dot, lower case.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Line-comment prefix, or null (comment toggling disabled, IDE-ED-034).</summary>
    string? LineComment { get; }

    /// <summary>Block-comment delimiters, or null.</summary>
    (string Start, string End)? BlockComment { get; }

    /// <summary>Bracket pairs for matching.</summary>
    IReadOnlyList<(char Open, char Close)> Brackets { get; }

    ITokenClassifier CreateClassifier();

    /// <summary>Fold regions of this file type (IDE-ED-051…053); null when it has none.</summary>
    Editing.IFoldingProvider? CreateFoldingProvider();

    /// <summary>Language-specific indentation (IDE-ED-007/008); null = only the language-neutral rules.</summary>
    IIndentRules? IndentRules => null;

    /// <summary>
    /// The brackets, block keywords and identifiers of <paramref name="text"/> from this language's lexer, paired
    /// (editor-features.md §7); none for a language without a lexer. Must never throw; may run on the thread pool.
    /// </summary>
    Editing.CodeTokens ScanCodeTokens(string text) => Editing.CodeTokens.Empty;
}

/// <summary>Bracket <see cref="Editing.CodeTokens"/> from the symbol spans of a <see cref="WholeTextClassifier"/> lexer.</summary>
internal static class SymbolSpanCodeTokens
{
    public static Editing.CodeTokens Scan(WholeTextClassifier lexer, string text, IReadOnlyList<(char Open, char Close)> brackets)
    {
        var spans = lexer.LexText(text);
        var tokens = new List<Editing.CodeToken>();
        var keys = new List<char>();
        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (span.Length != 1 || span.TokenClass != ColorTokens.Symbol)
                continue;
            var c = text[span.Offset];
            foreach (var (open, close) in brackets)
            {
                if (c == open || c == close)
                {
                    tokens.Add(new Editing.CodeToken(span.Offset, 1, c == open ? Editing.CodeTokenKind.Open : Editing.CodeTokenKind.Close, i));
                    keys.Add(open);
                }
            }
        }

        return new Editing.CodeTokens(text, tokens, Editing.CodeTokens.PairBrackets(tokens, k => keys[k]), ignoreCase: false);
    }
}

/// <summary>The language-specific indentation rules of editor-features.md §2.</summary>
public interface IIndentRules
{
    /// <summary>IDE-ED-007: Enter after <paramref name="textLeftOfCaret"/> (the old line's text left of the caret) indents one unit more.</summary>
    bool IncreasesIndent(string textLeftOfCaret);

    /// <summary>
    /// IDE-ED-008: when a closer word ends at <paramref name="closerEnd"/> and is the first token of its line, the
    /// 1-based line of its matching opener; otherwise (not a closer, or no opener) null.
    /// </summary>
    int? MatchingOpenerLine(ITextBuffer buffer, int closerEnd);
}

/// <summary>Colour-token names, taken from the compiler area's single mapping (<see cref="Classifier.GetColorToken"/>).</summary>
internal static class ColorTokens
{
    public static readonly string Keyword = Classifier.GetColorToken(ClassificationKind.Keyword);
    public static readonly string Type = Classifier.GetColorToken(ClassificationKind.TypeName);
    public static readonly string String = Classifier.GetColorToken(ClassificationKind.StringLiteral);
    public static readonly string Number = Classifier.GetColorToken(ClassificationKind.NumberLiteral);
    public static readonly string Constant = Classifier.GetColorToken(ClassificationKind.Constant);
    public static readonly string Symbol = Classifier.GetColorToken(ClassificationKind.Punctuation);
    public static readonly string Invalid = Classifier.GetColorToken(ClassificationKind.Invalid);
}

/// <summary>
/// Classifier over a whole-text lexer: the spans are computed once per buffer <see cref="ITextBuffer.Version"/>
/// and filtered by line range (the same caching rule as the Pascal classifier, IDE-P1-056). A buffer larger than
/// <see cref="BackgroundThreshold"/> is lexed on the thread pool (IDE-ARCH-081): until the result for the current
/// version arrives, the previous spans follow the edits and edited text has no span (<c>syntax.text</c>, IDE-P1-053);
/// a result for an outdated snapshot is dropped and the current text is lexed again (IDE-ARCH-083).
/// </summary>
public abstract class WholeTextClassifier : ITokenClassifier
{
    /// <summary>Buffers longer than this (UTF-16 units, "200 KB") are lexed in the background.</summary>
    public const int BackgroundThreshold = 200 * 1024;

    private ITextBuffer? _buffer;
    private long _version = -1;
    private List<TokenClassSpan> _spans = [];
    private long _runningVersion = -1;

    public event EventHandler? ClassificationChanged;

    /// <summary>A background lex runs or its result has not been applied yet (tests drain it, I-179).</summary>
    internal bool IsLexing => _runningVersion >= 0;

    public IReadOnlyList<TokenClassSpan> Classify(ITextBuffer buffer, LineRange lines)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!ReferenceEquals(buffer, _buffer))
        {
            _buffer?.Changed -= OnBufferChanged;
            _buffer = buffer;
            _version = _runningVersion = -1;
            _spans = [];
            buffer.Changed += OnBufferChanged;
        }

        if (buffer.Version != _version)
        {
            if (buffer.Length > BackgroundThreshold)
                StartBackground(buffer);
            else
            {
                _spans = Lex(buffer.Text);
                _version = buffer.Version;
            }
        }

        int first = Math.Clamp(lines.FirstLine, 1, buffer.LineCount);
        int last = Math.Clamp(lines.LastLine, first, buffer.LineCount);
        int start = buffer.GetLine(first).Offset;
        var (lastOffset, lastLength, lastDelimiter) = buffer.GetLine(last);
        int end = lastOffset + lastLength + lastDelimiter;
        // Spans are sorted and non-overlapping, so their ends are sorted too: binary search the first one ending after start.
        var spans = _spans; // replaced, never mutated, so a snapshot is safe
        int low = 0, high = spans.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (spans[mid].Offset + spans[mid].Length > start) high = mid;
            else low = mid + 1;
        }
        var result = new List<TokenClassSpan>();
        for (int i = low; i < spans.Count && spans[i].Offset < end; i++)
            result.Add(spans[i]);
        return result;
    }

    private void StartBackground(ITextBuffer buffer)
    {
        if (_runningVersion >= 0)
            return; // one run at a time; a newer version is lexed when it ends
        var version = _runningVersion = buffer.Version;
        var text = buffer.Text;
        var context = SynchronizationContext.Current;
        _ = Task.Run(() => Lex(text)).ContinueWith(
            t => Arrived(buffer, version, t.Result),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            context is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Arrived(ITextBuffer buffer, long version, List<TokenClassSpan> spans)
    {
        _runningVersion = -1;
        if (!ReferenceEquals(buffer, _buffer))
            return;
        if (buffer.Version != version)
        {
            StartBackground(buffer); // outdated snapshot (IDE-ARCH-083)
            return;
        }

        _spans = spans;
        _version = version;
        ClassificationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps the current spans aligned with the text until it is lexed again; touched spans are dropped.</summary>
    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        int end = e.Offset + e.RemovedLength, delta = e.InsertedText.Length - e.RemovedLength;
        var shifted = new List<TokenClassSpan>(_spans.Count);
        foreach (var span in _spans)
        {
            if (span.Offset + span.Length <= e.Offset)
                shifted.Add(span);
            else if (span.Offset >= end)
                shifted.Add(span with { Offset = span.Offset + delta });
        }

        _spans = shifted;
    }

    /// <summary>All spans of <paramref name="text"/>, sorted by offset. Must never throw.</summary>
    protected abstract List<TokenClassSpan> Lex(string text);

    /// <summary><see cref="Lex"/> without the cache (bracket tokens, <see cref="SymbolSpanCodeTokens"/>); thread-safe.</summary>
    internal List<TokenClassSpan> LexText(string text) => Lex(text);
}
