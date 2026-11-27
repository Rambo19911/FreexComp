using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Syntax;

namespace FreexComp.LanguageService.Formatting;

/// <summary>
/// Writes the formatted text from a <see cref="LayoutPlan"/>: tokens, comments and directives in their original order and text,
/// with new whitespace between them (CMP-1437, CMP-1439 … CMP-1446). Disabled text and <c>format:off</c> regions are copied
/// as they are.
/// </summary>
internal sealed partial class LayoutWriter
{
    private enum Piece : byte
    {
        Token,
        Whitespace,
        EndOfLine,
        Comment,
        LineComment,
        Directive,

        /// <summary>Disabled text copied as it is; <see cref="El.StartsLine"/> / <see cref="El.EndsLine"/> say where it sits.</summary>
        Verbatim,

        /// <summary>A <c>format:off</c> region from the start of the off comment's line to the end of the on comment's line.</summary>
        Region,
    }

    private readonly record struct El(Piece Kind, int Start, int End, int Token = -1, bool StartsLine = false, bool EndsLine = false);

    /// <summary>The text between two tokens before (<c>Old…</c>, in the source) and after (<c>New…</c>, in the result).</summary>
    public readonly record struct Gap(int OldStart, int OldEnd, int NewStart, int NewEnd);

    private readonly string _text;
    private readonly LayoutPlan _plan;
    private readonly FormatProfile _profile;
    private readonly CancellationToken _ct;
    private readonly string _eol;
    private readonly StringBuilder _sb;
    private readonly List<El> _els = [];

    private bool _started;          // something was written
    private bool _atLineStart;      // the current output line is empty (after copied lines of disabled text)
    private bool _lineHasToken;     // a token (or a format:off region) is on the current output line
    private bool _regionToEnd;      // the last region runs to the end of the text

    public LayoutWriter(string text, LayoutPlan plan, FormatProfile profile, CancellationToken ct)
    {
        _text = text;
        _plan = plan;
        _profile = profile;
        _ct = ct;
        _eol = DominantLineEnding(text);
        _sb = new StringBuilder(text.Length + text.Length / 8 + 16);
    }

    public List<Gap> Write(out string formatted)
    {
        Flatten();
        MarkRegions();
        var gaps = new List<Gap>(_plan.Tokens.Length);
        var previous = -1;
        var first = 0;
        for (var i = 0; i < _els.Count; i++)
        {
            var el = _els[i];
            if (el.Kind != Piece.Token)
            {
                continue;
            }

            if ((el.Token & 1023) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            var newStart = _sb.Length;
            WriteGap(previous, first, i, el.Token);
            var token = _plan.Tokens[el.Token];
            gaps.Add(new Gap(previous < 0 ? 0 : _plan.Tokens[previous].Span.End, token.Span.Start, newStart, _sb.Length));
            if (token.Kind != SyntaxKind.EndOfFileToken)
            {
                _sb.Append(token.Text);
                _started = true;
                _atLineStart = false;
                _lineHasToken = true;
            }

            previous = el.Token;
            first = i + 1;
        }

        formatted = _sb.ToString();
        return gaps;
    }

    // ------------------------------------------------------------------ pieces

    private void Flatten()
    {
        for (var i = 0; i < _plan.Tokens.Length; i++)
        {
            var token = _plan.Tokens[i];
            foreach (var t in token.LeadingTrivia)
            {
                Add(t);
            }

            _els.Add(new El(Piece.Token, token.Span.Start, token.Span.End, i));
            foreach (var t in token.TrailingTrivia)
            {
                Add(t);
            }
        }
    }

    private void Add(SyntaxTrivia t)
    {
        var start = t.Span.Start;
        var end = t.Span.End;
        switch (t.Kind)
        {
            case SyntaxKind.WhitespaceTrivia:
                _els.Add(new El(Piece.Whitespace, start, end));
                break;
            case SyntaxKind.EndOfLineTrivia:
                _els.Add(new El(Piece.EndOfLine, start, end));
                break;
            case SyntaxKind.LineCommentTrivia or SyntaxKind.BlockCommentTrivia or SyntaxKind.DocCommentTrivia:
                _els.Add(new El(t.Text.StartsWith("//", StringComparison.Ordinal) ? Piece.LineComment : Piece.Comment, start, end));
                break;
            case SyntaxKind.DirectiveTrivia or SyntaxKind.IncludeDirectiveTrivia:
                _els.Add(new El(Piece.Directive, start, end));
                break;
            case SyntaxKind.DisabledTextTrivia:
                AddDisabled(start, t.Text);
                break;
            default:
                _els.Add(new El(Piece.Verbatim, start, end));
                break;
        }
    }

    /// <summary>
    /// CMP-1439: disabled text is copied byte for byte, except the whitespace after its directive on the directive's line
    /// (trailing whitespace) and the whitespace before the next directive on that directive's line (its indentation).
    /// </summary>
    private void AddDisabled(int start, string text)
    {
        var (coreStart, coreEnd, headEol) = DisabledParts(text);
        if (headEol < 0)
        {
            _els.Add(new El(IsBlank(text) ? Piece.Whitespace : Piece.Verbatim, start, start + text.Length));
            return;
        }

        var headIsBlank = coreStart > 0;
        if (headIsBlank)
        {
            if (headEol > 0)
            {
                _els.Add(new El(Piece.Whitespace, start, start + headEol));
            }

            _els.Add(new El(Piece.EndOfLine, start + headEol, start + coreStart));
        }

        if (coreEnd > coreStart)
        {
            _els.Add(new El(Piece.Verbatim, start + coreStart, start + coreEnd, StartsLine: headIsBlank, EndsLine: coreEnd < text.Length || EndsWithEol(text)));
        }

        if (coreEnd < text.Length)
        {
            _els.Add(new El(Piece.Whitespace, start + coreEnd, start + text.Length));
        }
    }

    /// <summary>
    /// The copied part of disabled text: after the first line break when the text before it is blank, up to and including
    /// the last line break when the text after it is blank. <c>HeadEol</c> is the first line break's index, -1 without one.
    /// </summary>
    private static (int CoreStart, int CoreEnd, int HeadEol) DisabledParts(string text)
    {
        var firstEol = text.AsSpan().IndexOfAny('\r', '\n');
        if (firstEol < 0)
        {
            return (0, text.Length, -1);
        }

        var firstEolEnd = firstEol + (text[firstEol] == '\r' && firstEol + 1 < text.Length && text[firstEol + 1] == '\n' ? 2 : 1);
        var coreStart = IsBlank(text.AsSpan(0, firstEol)) ? firstEolEnd : 0;
        var lastEolEnd = text.AsSpan().LastIndexOfAny('\r', '\n') + 1;
        var coreEnd = IsBlank(text.AsSpan(lastEolEnd)) ? lastEolEnd : text.Length;
        return (coreStart, Math.Max(coreStart, coreEnd), firstEol);
    }

    /// <summary>The part of disabled text the formatter keeps byte for byte (for the CMP-1437 check).</summary>
    internal static string DisabledCore(string text)
    {
        var (coreStart, coreEnd, headEol) = DisabledParts(text);
        return headEol < 0 && IsBlank(text) ? "" : text[coreStart..coreEnd];
    }

    private static bool EndsWithEol(string text) => text.Length > 0 && text[^1] is '\r' or '\n';

    private static bool IsBlank(ReadOnlySpan<char> s) => s.IndexOfAnyExcept(' ', '\t', '\f') < 0;

    [GeneratedRegex(@"^(//\s*format:(?<w>off|on)\s*|\{\s*format:(?<w>off|on)\s*\})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormatSwitch();

    /// <summary>
    /// CMP-1444: a <c>format:off</c> comment alone on its line up to the next <c>format:on</c> comment alone on its line (or the
    /// end of the file) becomes one <see cref="Piece.Region"/>; the tokens inside it are written as they are.
    /// </summary>
    private void MarkRegions()
    {
        for (var i = 0; i < _els.Count; i++)
        {
            if (Switch(i) != "off")
            {
                continue;
            }

            var start = i;
            while (start > 0 && _els[start - 1].Kind == Piece.Whitespace)
            {
                start--;
            }

            var end = -1;     // index of the last piece of the region
            for (var j = i + 1; j < _els.Count; j++)
            {
                if (Switch(j) == "on")
                {
                    end = j;
                    while (end + 1 < _els.Count && _els[end + 1].Kind == Piece.Whitespace)
                    {
                        end++;
                    }

                    break;
                }
            }

            if (end < 0)
            {
                end = _els.Count - 2;       // everything up to the end-of-file token
                _regionToEnd = true;
            }
            else
            {
                _regionToEnd = false;
            }

            var region = new El(Piece.Region, _els[start].Start, end >= start ? _els[end].End : _els[start].Start);
            _els.RemoveRange(start, end - start + 1);
            _els.Insert(start, region);
            i = start;
        }
    }

    /// <summary>"off" / "on" for a format switch comment alone on its line at <paramref name="i"/>, else null.</summary>
    private string? Switch(int i)
    {
        var el = _els[i];
        if (el.Kind is not (Piece.Comment or Piece.LineComment))
        {
            return null;
        }

        var match = FormatSwitch().Match(_text, el.Start, el.End - el.Start);
        if (!match.Success)
        {
            return null;
        }

        var before = i - 1;
        while (before >= 0 && _els[before].Kind == Piece.Whitespace)
        {
            before--;
        }

        var after = i + 1;
        while (after < _els.Count && _els[after].Kind == Piece.Whitespace)
        {
            after++;
        }

        var aloneBefore = before < 0 || _els[before].Kind == Piece.EndOfLine || (_els[before] is { Kind: Piece.Verbatim, EndsLine: true });
        var aloneAfter = after >= _els.Count || _els[after].Kind == Piece.EndOfLine
            || (_els[after].Kind == Piece.Token && _plan.Tokens[_els[after].Token].Kind == SyntaxKind.EndOfFileToken);
        return aloneBefore && aloneAfter ? match.Groups["w"].Value.ToLowerInvariant() : null;
    }

    // ------------------------------------------------------------------ writing

    /// <summary>Writes the pieces <c>[from, to)</c> between token <paramref name="p"/> (-1: none) and token <paramref name="n"/>, and the whitespace before <paramref name="n"/>.</summary>
    private void WriteGap(int p, int from, int to, int n)
    {
        var isEnd = _plan.Tokens[n].Kind == SyntaxKind.EndOfFileToken;
        var newlines = 0;
        var space = false;
        var spaceEl = default(El);
        var broke = false;          // a line break was written in this gap
        var afterItem = false;      // the last thing written is a comment, directive or copied text
        for (var k = from; k < to; k++)
        {
            var el = _els[k];
            switch (el.Kind)
            {
                case Piece.Whitespace:
                    space = true;
                    spaceEl = el;
                    continue;

                case Piece.EndOfLine:
                    newlines++;
                    space = false;
                    continue;

                case Piece.Verbatim when el.StartsLine:
                    if (_started && !_atLineStart)
                    {
                        Newlines(newlines);
                        broke = true;
                    }

                    Copy(el);
                    break;

                case Piece.Verbatim:
                    if (newlines > 0 && _started && !_atLineStart)
                    {
                        Break(newlines, p, n, first: !broke, last: false);
                        broke = true;
                        Indent(ItemLevel(k, to, n));
                    }

                    Copy(el);
                    break;

                case Piece.Region:
                    if (_started && !_atLineStart)
                    {
                        Break(Math.Max(newlines, 1), p, n, first: !broke, last: false);
                        broke = true;
                    }

                    Copy(el);
                    _lineHasToken = true;
                    break;

                default:        // comment or directive
                    if (!_started || _atLineStart)
                    {
                        Indent(ItemLevel(k, to, n));
                    }
                    else if (newlines == 0)
                    {
                        if (space)
                        {
                            AppendSpaceBeforeComment(spaceEl);
                        }
                    }
                    else
                    {
                        Break(newlines, p, n, first: !broke, last: false);
                        broke = true;
                        Indent(ItemLevel(k, to, n));
                    }

                    Copy(el);
                    break;
            }

            afterItem = true;
            newlines = 0;
            space = false;
        }

        if (isEnd)
        {
            if (_started && !_atLineStart && !_regionToEnd && EndsWithEol(_text))
            {
                _sb.Append(_eol);
            }

            return;
        }

        if (!_started || _atLineStart)
        {
            Indent(_plan.Levels[n]);
            return;
        }

        var breakLine = _plan.Rules[n] switch
        {
            GapRule.Line => newlines > 0 || _lineHasToken,
            GapRule.Join => afterItem && newlines > 0,
            _ => newlines > 0,
        };
        if (breakLine)
        {
            Break(Math.Max(newlines, 1), p, n, first: !broke, last: true);
            Indent(_plan.Levels[n]);
        }
        else if (afterItem)
        {
            if (space && _plan.Tokens[n].Kind is not (SyntaxKind.CommaToken or SyntaxKind.SemicolonToken or SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken))
            {
                _sb.Append(' ');
            }
        }
        else if (p >= 0 && Space(_plan.Tokens[p], _plan.Tokens[n]))
        {
            _sb.Append(' ');
        }
    }

    /// <summary>The spaces before a comment or directive on the same line as written (aligned comments stay aligned); a run with a tab becomes one space.</summary>
    private void AppendSpaceBeforeComment(El space)
    {
        if (_text.AsSpan(space.Start, space.End - space.Start).IndexOfAnyExcept(' ') < 0)
        {
            _sb.Append(_text, space.Start, space.End - space.Start);
        }
        else
        {
            _sb.Append(' ');
        }
    }

    private void Copy(El el)
    {
        _sb.Append(_text, el.Start, el.End - el.Start);
        _started = true;
        _atLineStart = el.Kind == Piece.Verbatim && el.EndsLine;
        if (_text.AsSpan(el.Start, el.End - el.Start).IndexOfAny('\r', '\n') >= 0)
        {
            _lineHasToken = false;
        }
    }

    /// <summary>The level of a comment or directive that starts a line: the next token's when it follows on that line, else the comment level before it.</summary>
    private int ItemLevel(int k, int to, int n)
    {
        if (_plan.Tokens[n].Kind == SyntaxKind.EndOfFileToken)
        {
            return 0;
        }

        for (var j = k + 1; j < to; j++)
        {
            if (_els[j].Kind is Piece.EndOfLine or Piece.Region || _els[j] is { Kind: Piece.Verbatim, StartsLine: true })
            {
                return _plan.CommentLevels[n];
            }
        }

        return _plan.Levels[n];
    }

    /// <summary>A line break with the blank lines CMP-1443 allows.</summary>
    private void Break(int newlines, int p, int n, bool first, bool last)
    {
        var blanks = Math.Min(Math.Max(newlines - 1, 0), _profile.MaxBlankLines);
        if (first && p >= 0 && _plan.NoBlankAfter[p])
        {
            blanks = 0;
        }
        else if (last && _plan.BlankBefore[n] == BlankRule.None)
        {
            blanks = 0;
        }
        else if (first && _plan.BlankBefore[n] == BlankRule.One)
        {
            blanks = Math.Min(1, _profile.MaxBlankLines);
        }

        Newlines(1 + blanks);
    }

    private void Newlines(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _sb.Append(_eol);
        }

        _lineHasToken = false;
    }

    private void Indent(int level)
    {
        _sb.Append(' ', Math.Max(level, 0) * _profile.IndentSize);
        _started = true;
        _atLineStart = false;
    }

    /// <summary>CMP-1446: CRLF unless more line breaks are LF (or CR) only.</summary>
    private static string DominantLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        return lf > crlf && lf >= cr ? "\n" : cr > crlf && cr > lf ? "\r" : "\r\n";
    }

    // ------------------------------------------------------------------ spacing (CMP-1442)

    /// <summary>True when one space goes between <paramref name="p"/> and <paramref name="n"/> on the same line.</summary>
    private bool Space(SyntaxToken p, SyntaxToken n)
    {
        var wanted = Wanted(p, n);
        return wanted || WouldMerge(p.Text, n.Text)
            || (p.Kind is SyntaxKind.IntegerLiteralToken or SyntaxKind.RealLiteralToken && n.Kind == SyntaxKind.DotToken);
    }

    private bool Wanted(SyntaxToken p, SyntaxToken n)
    {
        var pk = p.Kind;
        var nk = n.Kind;
        if (nk is SyntaxKind.CommaToken or SyntaxKind.SemicolonToken or SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken)
        {
            return false;
        }

        if (pk is SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken)
        {
            return false;
        }

        if (pk == SyntaxKind.CommaToken)
        {
            return _profile.SpaceAfterComma;
        }

        if (pk == SyntaxKind.SemicolonToken)
        {
            return true;
        }

        if (pk is SyntaxKind.DotToken or SyntaxKind.DotDotToken || nk is SyntaxKind.DotToken or SyntaxKind.DotDotToken)
        {
            return false;
        }

        if (nk == SyntaxKind.CaretToken && n.Parent is DereferenceExpressionSyntax)
        {
            return false;
        }

        if (IsPrefix(p))
        {
            return false;
        }

        if (pk == SyntaxKind.ColonEqualsToken || nk == SyntaxKind.ColonEqualsToken)
        {
            return _profile.SpaceAroundAssign;
        }

        if (nk == SyntaxKind.ColonToken)
        {
            return IsDeclarationColon(n) && _profile.SpaceBeforeColonInDecl;
        }

        if (pk == SyntaxKind.ColonToken)
        {
            return p.Parent is not ArgumentSyntax;
        }

        if ((nk is SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken && IsTypeList(n.Parent))
            || (pk == SyntaxKind.LessThanToken && IsTypeList(p.Parent)))
        {
            return false;
        }

        var pBinary = IsBinary(p);
        var nBinary = IsBinary(n);
        if (pBinary || nBinary)
        {
            return (pBinary && IsWord(p.Text)) || (nBinary && IsWord(n.Text)) || _profile.SpaceAroundOperators;
        }

        if (nk is SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken)
        {
            return !OpensTight(p);
        }

        return true;
    }

    /// <summary>A unary <c>-</c> / <c>+</c>, the address operator <c>@</c> or the <c>^</c> of a pointer type: no space after it.</summary>
    private static bool IsPrefix(SyntaxToken t) => t.Kind switch
    {
        SyntaxKind.MinusToken or SyntaxKind.PlusToken => t.Parent is UnaryExpressionSyntax,
        SyntaxKind.AtToken => t.Parent is AddressOfExpressionSyntax,
        SyntaxKind.CaretToken => t.Parent is PointerTypeSyntax,
        _ => false,
    };

    private static bool IsBinary(SyntaxToken t) => t.Parent is BinaryExpressionSyntax && t.Kind is not (SyntaxKind.OpenParenToken or SyntaxKind.CloseParenToken);

    private static bool IsTypeList(SyntaxNode? node) => node is TypeArgumentListSyntax or TypeParameterListSyntax;

    /// <summary>Not a case label, statement label, variant label or a write width.</summary>
    private static bool IsDeclarationColon(SyntaxToken t) =>
        t.Parent is not (ArgumentSyntax or CaseArmSyntax or LabeledStatementSyntax or VariantSyntax);

    /// <summary>A name, a closing bracket or one of the keywords written directly before its <c>(</c> / <c>[</c>.</summary>
    private static bool OpensTight(SyntaxToken p) => p.Kind switch
    {
        SyntaxKind.IdentifierToken => p.ContextualKind == SyntaxKind.IdentifierToken,      // not "default [", "index (" …
        SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken => true,
        SyntaxKind.CaretToken => p.Parent is DereferenceExpressionSyntax,
        SyntaxKind.GreaterThanToken => IsTypeList(p.Parent),
        SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword or SyntaxKind.ClassKeyword or SyntaxKind.InterfaceKeyword
            or SyntaxKind.DispInterfaceKeyword or SyntaxKind.ObjectKeyword or SyntaxKind.StringKeyword or SyntaxKind.ArrayKeyword
            or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword or SyntaxKind.InheritedKeyword => true,
        _ => false,
    };

    private static bool IsWord(string text) => text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_' || text[0] == '&');

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c > 0x7F;

    /// <summary>True when writing <paramref name="a"/> and <paramref name="b"/> together would lex differently.</summary>
    private static bool WouldMerge(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        var x = a[^1];
        var y = b[0];
        if (IsWordChar(x) && (IsWordChar(y) || y is '\'' or '#' or '$' or '%' or '&'))
        {
            return true;
        }

        if ((x is '\'' && y is '\'' or '#') || (x is '#' or '$' or '%' or '&' && (IsWordChar(y) || y == '$')))
        {
            return true;
        }

        return (x, y) switch
        {
            (':', '=') or ('<', '=') or ('>', '=') or ('<', '>') or ('.', '.') or ('(', '*') or ('*', ')') or ('/', '/')
                or ('(', '.') or ('.', ')') or ('@', '@') or ('{', _) or (_, '{') => true,
            _ => false,
        };
    }
}
