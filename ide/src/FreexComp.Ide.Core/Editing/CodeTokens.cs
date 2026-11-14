using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Editing;

/// <summary>What a <see cref="CodeToken"/> is to bracket matching and word highlight (editor-features.md §7).</summary>
public enum CodeTokenKind
{
    /// <summary>An opening bracket or a block keyword (<c>begin</c>, <c>repeat</c>, …).</summary>
    Open,

    /// <summary>A closing bracket or a block-closing keyword (<c>end</c>, <c>until</c>).</summary>
    Close,

    /// <summary>An identifier (IDE-ED-074).</summary>
    Identifier,
}

/// <summary>
/// One lexer token that pairing or word highlight looks at; <see cref="LexIndex"/> is its index in the language's whole
/// token stream (the 20 000-token search limit of IDE-ED-070 counts all tokens).
/// </summary>
public readonly record struct CodeToken(int Offset, int Length, CodeTokenKind Kind, int LexIndex, bool IsKeyword = false)
{
    public int End => Offset + Length;
}

/// <summary>The bracket or keyword at the caret and its partner (null: none, IDE-ED-070).</summary>
public readonly record struct BracketMatch(CodeToken Bracket, CodeToken? Partner);

/// <summary>A guide drawn with <c>editor.indentGuide.active</c>: <see cref="Column"/> (0-based visual) over lines <see cref="FirstLine"/>…<see cref="LastLine"/>.</summary>
public readonly record struct ActiveGuide(int Column, int FirstLine, int LastLine);

/// <summary>
/// The pairing-relevant tokens of one text snapshot, sorted by offset, with their partners (IDE-ED-070…072). Built by
/// <see cref="Languages.ILanguageDefinition.ScanCodeTokens"/> from the language's lexer, so brackets and keywords inside
/// strings and comments never appear. Immutable.
/// </summary>
public sealed class CodeTokens
{
    /// <summary>IDE-ED-070: a partner further than this many tokens away is not searched.</summary>
    public const int SearchLimit = 20_000;

    private readonly int[] _partners;

    /// <param name="partners">Per token the index of its partner in <paramref name="tokens"/>, or -1.</param>
    public CodeTokens(string text, IReadOnlyList<CodeToken> tokens, IReadOnlyList<int> partners, bool ignoreCase, long version = -1)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(partners);
        if (tokens.Count != partners.Count)
            throw new ArgumentException("One partner per token is required.", nameof(partners));
        Text = text;
        Tokens = tokens;
        IgnoreCase = ignoreCase;
        Version = version;
        _partners = [.. partners];
    }

    public static CodeTokens Empty { get; } = new("", [], [], ignoreCase: false);

    /// <summary>The text the tokens were scanned from.</summary>
    public string Text { get; }

    public IReadOnlyList<CodeToken> Tokens { get; }

    /// <summary>Identifiers compare case-insensitively (Object Pascal, IDE-ED-074).</summary>
    public bool IgnoreCase { get; }

    /// <summary>The buffer version of <see cref="Text"/> (-1 when built outside a buffer).</summary>
    public long Version { get; }

    internal CodeTokens WithVersion(long version) => new(Text, Tokens, _partners, IgnoreCase, version);

    /// <summary>The partner of the token at <paramref name="index"/> within <see cref="SearchLimit"/> tokens, or null.</summary>
    public CodeToken? PartnerOf(int index)
    {
        var p = _partners[index];
        return p >= 0 && Math.Abs(Tokens[p].LexIndex - Tokens[index].LexIndex) <= SearchLimit ? Tokens[p] : null;
    }

    /// <summary>
    /// UI-ED-053 / IDE-ED-070/071: a bracket right before the caret, else one right after it, else a block keyword the
    /// caret is inside of (or at either end of); with its partner.
    /// </summary>
    public BracketMatch? MatchAt(int caret) => IndexAt(caret) is { } i ? new BracketMatch(Tokens[i], PartnerOf(i)) : null;

    private int? IndexAt(int caret)
    {
        int? before = null, after = null, keyword = null;
        for (int i = FirstEndingAtOrAfter(caret); i < Tokens.Count && Tokens[i].Offset <= caret; i++)
        {
            var t = Tokens[i];
            if (t.Kind == CodeTokenKind.Identifier)
                continue;
            if (t.IsKeyword)
            {
                if (t.Offset <= caret && caret <= t.End)
                    keyword ??= i;
            }
            else if (t.End == caret)
                before = i;
            else if (t.Offset == caret)
                after = i;
        }

        return before ?? after ?? keyword;
    }

    /// <summary>The index of the first token whose end is at least <paramref name="offset"/> (tokens do not overlap).</summary>
    private int FirstEndingAtOrAfter(int offset)
    {
        int low = 0, high = Tokens.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (Tokens[mid].End >= offset) high = mid;
            else low = mid + 1;
        }

        return low;
    }

    /// <summary>The index of the last token starting before <paramref name="offset"/>, or -1.</summary>
    private int LastStartingBefore(int offset)
    {
        int low = 0, high = Tokens.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (Tokens[mid].Offset >= offset) high = mid;
            else low = mid + 1;
        }

        return low - 1;
    }

    /// <summary>
    /// IDE-ED-072: where <c>search.goToMatchingBracket</c> puts the caret — the partner of the bracket at the caret
    /// (just before an opener, just after a closer, the start of a keyword), else the nearest enclosing opener; null
    /// when there is none (or the bracket at the caret has no partner).
    /// </summary>
    public int? MatchingBracketTarget(int caret)
    {
        if (MatchAt(caret) is { } match)
        {
            if (match.Partner is not { } partner)
                return null;
            return partner.Kind == CodeTokenKind.Close && !partner.IsKeyword ? partner.End : partner.Offset;
        }

        return EnclosingOpener(caret) is { } opener ? Tokens[opener].Offset : null;
    }

    /// <summary>The index of the innermost opener before <paramref name="caret"/> whose block (or bracket) is still open there.</summary>
    private int? EnclosingOpener(int caret)
    {
        for (int i = LastStartingBefore(caret); i >= 0; i--)
        {
            if (Tokens[i].Kind != CodeTokenKind.Open)
                continue;
            if (PartnerOf(i) is not { } partner || partner.Offset >= caret)
                return i;
        }

        return null;
    }

    /// <summary>
    /// The innermost keyword pair (IDE-ED-052) containing <paramref name="caret"/>, including the keywords themselves:
    /// the opener and its closer (IDE-ED-073).
    /// </summary>
    public (CodeToken Opener, CodeToken Closer)? BlockAt(int caret)
    {
        if (IndexAt(caret) is { } at && Tokens[at].IsKeyword && PartnerOf(at) is { } partner)
            return Tokens[at].Kind == CodeTokenKind.Open ? (Tokens[at], partner) : (partner, Tokens[at]);
        for (int i = LastStartingBefore(caret); i >= 0; i--)
        {
            var t = Tokens[i];
            if (t.Kind == CodeTokenKind.Open && t.IsKeyword && PartnerOf(i) is { } closer && closer.Offset >= caret)
                return (t, closer);
        }

        return null;
    }

    /// <summary>
    /// IDE-ED-074 / UI-ED-054: with the caret on an identifier (inside or at either end) and no selection, or a selection
    /// that is exactly one identifier, the other identifiers with the same text (<see cref="IgnoreCase"/>); else none.
    /// </summary>
    public IReadOnlyList<CodeToken> Occurrences(int caret, TextSelection selection)
    {
        int? at = null;
        for (int i = FirstEndingAtOrAfter(caret); i < Tokens.Count && Tokens[i].Offset <= caret; i++)
        {
            if (Tokens[i].Kind == CodeTokenKind.Identifier)
                at ??= i;
        }

        if (at is not { } index)
            return [];
        var word = Tokens[index];
        if (!selection.IsEmpty && (selection.Start != word.Offset || selection.End != word.End))
            return [];
        var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var result = new List<CodeToken>();
        for (int i = 0; i < Tokens.Count; i++)
        {
            var t = Tokens[i];
            if (i != index && t.Kind == CodeTokenKind.Identifier && t.Length == word.Length
                && string.Compare(Text, t.Offset, Text, word.Offset, word.Length, comparison) == 0)
                result.Add(t);
        }

        return result;
    }

    /// <summary>
    /// Pairs brackets: <paramref name="isOpen"/>/<paramref name="pairKey"/> tell each bracket token's direction and pair
    /// (one stack per pair, so <c>( ]</c> does not break the parentheses). Returns the partner array.
    /// </summary>
    public static int[] PairBrackets(IReadOnlyList<CodeToken> tokens, Func<int, char> pairKey)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(pairKey);
        var partners = new int[tokens.Count];
        Array.Fill(partners, -1);
        var stacks = new Dictionary<char, Stack<int>>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.IsKeyword || t.Kind == CodeTokenKind.Identifier)
                continue;
            var key = pairKey(i);
            if (!stacks.TryGetValue(key, out var stack))
                stacks[key] = stack = new Stack<int>();
            if (t.Kind == CodeTokenKind.Open)
                stack.Push(i);
            else if (stack.Count > 0)
            {
                var open = stack.Pop();
                partners[open] = i;
                partners[i] = open;
            }
        }

        return partners;
    }
}
