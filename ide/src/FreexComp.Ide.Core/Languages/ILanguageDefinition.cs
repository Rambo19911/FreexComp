using FreexComp.Ide.Core.Text;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Languages;

/// <summary>1-based, inclusive range of buffer lines.</summary>
public readonly record struct LineRange(int FirstLine, int LastLine);

/// <summary>
/// IDE-side coloured span (architecture.md §11): <see cref="TokenClass"/> is a colour-token name
/// (<c>syntax.keyword</c>, …; design-tokens.md §3.1). Not the compiler's <c>ClassifiedSpan</c>.
/// </summary>
public readonly record struct TokenClassSpan(int Offset, int Length, string TokenClass);

/// <summary>Colours a buffer (architecture.md §11). One instance per document; UI thread only.</summary>
public interface ITokenClassifier
{
    /// <summary>Spans touching <paramref name="lines"/>, sorted by offset, non-overlapping. Unclassified text has no span.</summary>
    IReadOnlyList<TokenClassSpan> Classify(ITextBuffer buffer, LineRange lines);
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
/// and filtered by line range (the same caching rule as the Pascal classifier, IDE-P1-056).
/// </summary>
public abstract class WholeTextClassifier : ITokenClassifier
{
    private ITextBuffer? _buffer;
    private long _version;
    private List<TokenClassSpan> _spans = [];

    public IReadOnlyList<TokenClassSpan> Classify(ITextBuffer buffer, LineRange lines)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!ReferenceEquals(buffer, _buffer) || buffer.Version != _version)
        {
            _spans = Lex(buffer.Text);
            _buffer = buffer;
            _version = buffer.Version;
        }

        int first = Math.Clamp(lines.FirstLine, 1, buffer.LineCount);
        int last = Math.Clamp(lines.LastLine, first, buffer.LineCount);
        int start = buffer.GetLine(first).Offset;
        var (lastOffset, lastLength, lastDelimiter) = buffer.GetLine(last);
        int end = lastOffset + lastLength + lastDelimiter;
        // Spans are sorted and non-overlapping, so their ends are sorted too: binary search the first one ending after start.
        int low = 0, high = _spans.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (_spans[mid].Offset + _spans[mid].Length > start) high = mid;
            else low = mid + 1;
        }
        var result = new List<TokenClassSpan>();
        for (int i = low; i < _spans.Count && _spans[i].Offset < end; i++)
            result.Add(_spans[i]);
        return result;
    }

    /// <summary>All spans of <paramref name="text"/>, sorted by offset. Must never throw.</summary>
    protected abstract List<TokenClassSpan> Lex(string text);
}
