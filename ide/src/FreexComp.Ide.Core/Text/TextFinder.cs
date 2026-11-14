using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Text;

/// <summary>A match: <see cref="Length"/> UTF-16 units at <see cref="Offset"/>.</summary>
public readonly record struct TextMatch(int Offset, int Length)
{
    public int End => Offset + Length;
}

/// <summary>What to search for (IDE-P1-102/103): the text and the three options.</summary>
public sealed record FindQuery(string Text, bool MatchCase, bool WholeWord, bool Regex);

/// <summary>The matches of one search: at most the cap, <see cref="IsCapped"/> when there were more; or an error message.</summary>
public sealed record FindResult(IReadOnlyList<TextMatch> Matches, bool IsCapped, string? Error)
{
    public static readonly FindResult Empty = new([], false, null);
}

/// <summary>
/// The search engine of in-editor find (phase1-shell.md §10), pure functions over a text snapshot. Literal search:
/// ordinal (ignore-case: <see cref="StringComparison.OrdinalIgnoreCase"/>), never across a line break. Regex: .NET
/// <see cref="Regex"/> with Multiline + CultureInvariant (+ IgnoreCase), <c>^</c>/<c>$</c> at every line boundary
/// (CRLF, LF, lone CR) and <c>.</c> never matching a line break; <see cref="RegexTimeout"/> per match. Zero-length
/// matches are skipped; Whole word refuses a letter, digit or underscore around the match.
/// </summary>
public static class TextFinder
{
    /// <summary>Counting stops here (IDE-P1-106).</summary>
    public const int MatchCap = 10_000;

    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>The matches inside [<paramref name="start"/>, <paramref name="end"/>), at most <paramref name="cap"/>.</summary>
    public static FindResult FindAll(string text, FindQuery query, int start, int end, int cap = MatchCap) =>
        FindAll(text, query, start, end, CancellationToken.None, cap);

    /// <summary>
    /// As <see cref="FindAll(string, FindQuery, int, int, int)"/>; a cancelled <paramref name="cancellation"/> stops the
    /// search with <see cref="OperationCanceledException"/> (IDE-ARCH-084).
    /// </summary>
    public static FindResult FindAll(string text, FindQuery query, int start, int end, CancellationToken cancellation, int cap = MatchCap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        var matches = new List<TextMatch>();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var (match, _) in Enumerate(text, query, start, end))
            {
                cancellation.ThrowIfCancellationRequested();
                if (matches.Count == cap)
                    return new FindResult(matches, true, null);
                matches.Add(match);
            }
        }
        catch (RegexParseException e)
        {
            return new FindResult([], false, Format(Strings.Msg_Find_RegexInvalid, e.Message));
        }
        catch (RegexMatchTimeoutException)
        {
            return new FindResult([], false, Format(Strings.Msg_Find_RegexInvalid, Strings.Reason_RegexTimeout));
        }

        return new FindResult(matches, false, null);
    }

    /// <summary>
    /// The next match starting at or after <paramref name="from"/> (<paramref name="forward"/>), or the last one
    /// starting before it; wraps around inside [<paramref name="start"/>, <paramref name="end"/>) (<c>Wrapped</c>).
    /// Null when there is none or the pattern is invalid.
    /// </summary>
    public static (TextMatch Match, bool Wrapped)? FindNext(string text, FindQuery query, int start, int end, int from, bool forward)
    {
        TextMatch? first = null, last = null, before = null;
        try
        {
            foreach (var (match, _) in Enumerate(text, query, start, end))
            {
                first ??= match;
                if (forward && match.Offset >= from)
                    return (match, false);
                if (!forward && match.Offset < from)
                    before = match;
                last = match;
            }
        }
        catch (Exception e) when (e is RegexParseException or RegexMatchTimeoutException)
        {
            return null;
        }

        if (forward)
            return first is { } f ? (f, true) : null;
        if (before is { } b)
            return (b, false);
        return last is { } l ? (l, true) : null;
    }

    /// <summary>
    /// Replace All (IDE-P1-107): one edit per match inside [<paramref name="start"/>, <paramref name="end"/>) with its
    /// <see cref="Expand">expanded</see> replacement; <c>Error</c> is the message of an invalid or too slow pattern.
    /// </summary>
    public static (IReadOnlyList<TextEdit> Edits, string? Error) Replacements(string text, FindQuery query, int start, int end, string replacement, string newLine)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        var edits = new List<TextEdit>();
        try
        {
            foreach (var (match, regex) in Enumerate(text, query, start, end))
                edits.Add(new TextEdit(match.Offset, match.Length, Expand(replacement, query, regex, newLine)));
        }
        catch (RegexParseException e)
        {
            return ([], Format(Strings.Msg_Find_RegexInvalid, e.Message));
        }
        catch (RegexMatchTimeoutException)
        {
            return ([], Format(Strings.Msg_Find_RegexInvalid, Strings.Reason_RegexTimeout));
        }

        return (edits, null);
    }

    /// <summary>
    /// True when [<paramref name="offset"/>, <paramref name="offset"/> + <paramref name="length"/>) is a match;
    /// <paramref name="match"/> is the regex match (for <see cref="Expand"/>) in regex mode.
    /// </summary>
    public static bool IsMatchAt(string text, FindQuery query, int offset, int length, out Match? match)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        match = null;
        if (length == 0 || offset < 0 || offset + length > text.Length || !IsWholeWord(text, offset, length, query))
            return false;
        if (!query.Regex)
        {
            return query.Text.Length == length && query.Text.AsSpan().IndexOfAny('\r', '\n') < 0
                && string.Compare(text, offset, query.Text, 0, length, query.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) == 0;
        }

        try
        {
            var m = CreateRegex(query).Match(text, offset);
            if (!m.Success || m.Index != offset || m.Length != length)
                return false;
            match = m;
            return true;
        }
        catch (Exception e) when (e is RegexParseException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// The replacement for a match (IDE-P1-107): literal text in literal mode; in regex mode <c>\n</c> (the document's
    /// <paramref name="newLine"/>), <c>\t</c>, <c>\\</c> escapes, then the .NET substitutions (<c>$1</c>, <c>${name}</c>, <c>$$</c>).
    /// </summary>
    public static string Expand(string replacement, FindQuery query, Match? match, string newLine)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(query);
        if (!query.Regex || match is null)
            return replacement;
        var unescaped = new StringBuilder(replacement.Length);
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] == '\\' && i + 1 < replacement.Length && replacement[i + 1] is 'n' or 't' or '\\')
            {
                unescaped.Append(replacement[++i] switch { 'n' => newLine, 't' => "\t", _ => "\\" });
            }
            else
            {
                unescaped.Append(replacement[i]);
            }
        }

        return match.Result(unescaped.ToString());
    }

    /// <summary>The regex of a regex-mode query; throws <see cref="ArgumentException"/> for an invalid pattern.</summary>
    public static Regex CreateRegex(FindQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var options = RegexOptions.Multiline | RegexOptions.CultureInvariant | (query.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new Regex(RewriteLineAnchors(query.Text), options, RegexTimeout);
    }

    private static IEnumerable<(TextMatch Match, Match? Regex)> Enumerate(string text, FindQuery query, int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        if (query.Text.Length == 0)
            yield break;
        if (query.Regex)
        {
            var regex = CreateRegex(query);
            for (var m = regex.Match(text, start); m.Success && m.Index < end; m = m.NextMatch())
            {
                if (m.Length > 0 && m.Index + m.Length <= end && IsWholeWord(text, m.Index, m.Length, query))
                    yield return (new TextMatch(m.Index, m.Length), m);
            }

            yield break;
        }

        if (query.Text.AsSpan().IndexOfAny('\r', '\n') >= 0)
            yield break; // the search field is single-line (IDE-P1-103)
        var comparison = query.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var length = query.Text.Length;
        for (var i = text.IndexOf(query.Text, start, end - start, comparison); i >= 0;)
        {
            var whole = IsWholeWord(text, i, length, query);
            if (whole)
                yield return (new TextMatch(i, length), null); // matches never overlap (Replace All edits must not)
            var next = whole ? i + length : i + 1;
            i = next < end ? text.IndexOf(query.Text, next, end - next, comparison) : -1;
        }
    }

    private static bool IsWholeWord(string text, int offset, int length, FindQuery query) =>
        !query.WholeWord
        || ((offset == 0 || !IsWordChar(text[offset - 1])) && (offset + length >= text.Length || !IsWordChar(text[offset + length])));

    private static bool IsWordChar(char c) => c == '_' || char.IsLetterOrDigit(c);

    /// <summary>
    /// Outside character classes: <c>.</c> → <c>[^\r\n]</c>, <c>^</c> → also after a lone CR, <c>$</c> → also before
    /// any CR (the .NET anchors know only LF). Escapes and classes are copied unchanged.
    /// </summary>
    private static string RewriteLineAnchors(string pattern)
    {
        var result = new StringBuilder(pattern.Length + 16);
        var inClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                result.Append(c).Append(pattern[++i]);
                continue;
            }

            if (inClass)
            {
                inClass = c != ']';
                result.Append(c);
                continue;
            }

            switch (c)
            {
                case '[':
                    inClass = true;
                    result.Append(c);
                    if (i + 1 < pattern.Length && pattern[i + 1] == '^')
                        result.Append(pattern[++i]);
                    if (i + 1 < pattern.Length && pattern[i + 1] == ']')
                        result.Append(pattern[++i]); // a leading ] is a literal
                    break;
                case '.':
                    result.Append(@"[^\r\n]");
                    break;
                case '^':
                    result.Append(@"(?:^|(?<=\r)(?!\n))");
                    break;
                case '$':
                    result.Append(@"(?=\r|$)");
                    break;
                default:
                    result.Append(c);
                    break;
            }
        }

        return result.ToString();
    }

    private static string Format(string format, object arg) => string.Format(CultureInfo.CurrentCulture, format, arg);
}
