using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>
/// Pull lexer (CMP-110) for one file: <see cref="Lex"/> returns one token at a time and never throws. After the end of
/// the text it returns <see cref="SyntaxKind.EndOfFileToken"/>, which carries the remaining trivia. Directives go to
/// a <see cref="DirectiveProcessor"/> (conditionals, switches). With <c>expandIncludes</c>, an active include
/// directive pauses the lexer: <see cref="Lex"/> returns a <see cref="SyntaxKind.None"/> token and
/// <see cref="TakePendingInclude"/> names the directive; the next <see cref="Lex"/> resumes the same leading trivia.
/// </summary>
internal sealed class PascalLexer
{
    private readonly SourceText _source;
    private readonly string _text;
    private readonly ImmutableArray<SyntaxTrivia>.Builder _trivia = ImmutableArray.CreateBuilder<SyntaxTrivia>();
    private readonly PreprocessorContext _context;
    private readonly DirectiveProcessor _directives;
    private readonly bool _expandIncludes;
    private DirectiveTriviaInfo? _pendingInclude;
    private bool _resumeLeading;
    private int _pos;
    private bool _finished;

    public PascalLexer(SourceText source, ParseOptions? options = null)
        : this(source, new PreprocessorContext(options), expandIncludes: false)
    {
    }

    public PascalLexer(SourceText source, PreprocessorContext context, bool expandIncludes)
    {
        _source = source;
        _text = source.Text;
        _context = context;
        _expandIncludes = expandIncludes;
        _directives = new DirectiveProcessor(context, source, (code, span, args, severity) => Report(code, span.Start, span.Length, severity, args));
    }

    public SourceText Source => _source;

    /// <summary>Diagnostics of every file sharing this lexer's context, in the order they were reported.</summary>
    public ImmutableArray<Diagnostic> Diagnostics => [.. _context.Diagnostics];

    /// <summary>The include directive that paused the lexer (see the class summary); null when there is none.</summary>
    public DirectiveTriviaInfo? TakePendingInclude()
    {
        var include = _pendingInclude;
        _pendingInclude = null;
        return include;
    }

    /// <summary>
    /// Next token. Trivia attachment (LANG-106): trailing trivia runs up to and including the first line break
    /// after the token (or up to the next token); everything else is leading trivia of the next token. An include
    /// directive always starts leading trivia (it is never trailing), so included tokens can precede the next token.
    /// </summary>
    public SyntaxToken Lex()
    {
        if (!_resumeLeading)
        {
            _trivia.Clear();
        }

        _resumeLeading = false;
        if (ScanTrivia(trailing: false))
        {
            _resumeLeading = true;
            return default;
        }

        var leading = ToArray();
        var start = _pos;
        if (_pos >= _text.Length)
        {
            if (!_finished)
            {
                _finished = true;
                _directives.Finish();
            }

            return new SyntaxToken(SyntaxKind.EndOfFileToken, "", null, start, leading, [], _directives.State, _source);
        }

        var state = _directives.State;
        var (kind, value) = ScanToken();
        var text = _text[start.._pos];
        var recording = _context.Options?.ConditionEvaluator != null;
        var copying = recording && _context.LexerRecords;
        if (recording)
        {
            // the scope of later {$IF}s (LANG-862), already for the trailing trivia that may hold one
            _context.Pending = (kind, text, value, start, leading, state, _source);
        }

        _trivia.Clear();
        ScanTrivia(trailing: true);
        var token = new SyntaxToken(kind, text, value, start, leading, ToArray(), state, _source);
        if (recording)
        {
            _context.Pending = null;
            if (copying)
            {
                _context.Emitted.Add(token);
            }
        }

        return token;
    }

    /// <summary>Reports a diagnostic at <paramref name="span"/> of this lexer's file, with warning control applied.</summary>
    public void ReportAt(string code, TextSpan span, params object[] args) =>
        Report(code, span.Start, span.Length, DiagnosticCatalog.Get(code).DefaultSeverity, args);

    private ImmutableArray<SyntaxTrivia> ToArray() => _trivia.Count == 0 ? [] : _trivia.ToImmutable();

    // ------------------------------------------------------------------ tokens

    private (SyntaxKind Kind, object? Value) ScanToken()
    {
        var c = _text[_pos];
        if (IsIdentifierStartAt(_pos, out _))
        {
            var start = _pos;
            ScanIdentifierRest();
            var kind = SyntaxFacts.GetReservedWordKind(_text.AsSpan(start, _pos - start));
            return kind == SyntaxKind.None ? (SyntaxKind.IdentifierToken, _text[start.._pos]) : (kind, null);
        }

        if (char.IsAsciiDigit(c) || IsRadixLiteralStart(_pos))
        {
            return ScanNumber();
        }

        if (IsStringPartStart(_pos))
        {
            return ScanString();
        }

        _pos++;
        switch (c)
        {
            case '+': return (SyntaxKind.PlusToken, null);
            case '-': return (SyntaxKind.MinusToken, null);
            case '*': return (SyntaxKind.AsteriskToken, null);
            case '/': return (SyntaxKind.SlashToken, null);
            case '=': return (SyntaxKind.EqualsToken, null);
            case ',': return (SyntaxKind.CommaToken, null);
            case ';': return (SyntaxKind.SemicolonToken, null);
            case ')': return (SyntaxKind.CloseParenToken, null);
            case '[': return (SyntaxKind.OpenBracketToken, null);
            case ']': return (SyntaxKind.CloseBracketToken, null);
            case '^': return (SyntaxKind.CaretToken, null);
            case '@': return (SyntaxKind.AtToken, null);
            case '#': return (SyntaxKind.HashToken, null);
            case '$': return (SyntaxKind.DollarToken, null);
            case '<':
                return Next('>') ? (SyntaxKind.LessThanGreaterThanToken, null)
                    : Next('=') ? (SyntaxKind.LessThanEqualsToken, null)
                    : (SyntaxKind.LessThanToken, null);
            case '>': return Next('=') ? (SyntaxKind.GreaterThanEqualsToken, null) : (SyntaxKind.GreaterThanToken, null);
            case ':': return Next('=') ? (SyntaxKind.ColonEqualsToken, null) : (SyntaxKind.ColonToken, null);
            case '(': return Next('.') ? (SyntaxKind.OpenBracketToken, null) : (SyntaxKind.OpenParenToken, null);
            case '.':
                return Next('.') ? (SyntaxKind.DotDotToken, null)
                    : Next(')') ? (SyntaxKind.CloseBracketToken, null)
                    : (SyntaxKind.DotToken, null);
            case '&':
                if (_pos < _text.Length && IsIdentifierStartAt(_pos, out _))
                {
                    // LANG-111: '&' + identifier or keyword is an identifier; the '&' is not part of the name.
                    var nameStart = _pos;
                    ScanIdentifierRest();
                    return (SyntaxKind.IdentifierToken, _text[nameStart.._pos]);
                }

                return (SyntaxKind.AmpersandToken, null);
            default:
                // Unreachable: ScanTrivia skips every character that cannot start a token.
                return (SyntaxKind.None, null);
        }
    }

    private bool Next(char expected)
    {
        if (_pos < _text.Length && _text[_pos] == expected)
        {
            _pos++;
            return true;
        }

        return false;
    }

    /// <summary><c>$</c> + hex digit, <c>%</c> + binary digit, <c>&amp;</c> + octal digit (lexical.md §6).</summary>
    private bool IsRadixLiteralStart(int index)
    {
        if (index + 1 >= _text.Length)
        {
            return false;
        }

        var next = _text[index + 1];
        return _text[index] switch
        {
            '$' => char.IsAsciiHexDigit(next),
            '%' => next is '0' or '1',
            '&' => next is >= '0' and <= '7',
            _ => false,
        };
    }

    /// <summary><c>'</c>, <c>#</c> + digit, or <c>#$</c> + hex digit (lexical.md §7).</summary>
    private bool IsStringPartStart(int index) =>
        _text[index] == '\''
        || (_text[index] == '#' && index + 1 < _text.Length
            && (char.IsAsciiDigit(_text[index + 1])
                || (_text[index + 1] == '$' && index + 2 < _text.Length && char.IsAsciiHexDigit(_text[index + 2]))));

    /// <summary>
    /// A string literal: adjacent quoted and <c>#n</c> / <c>#$n</c> parts with no whitespace between (LANG-130…132).
    /// An unterminated quoted part (E1020 at its quote) ends at the line break and ends the literal; a code above
    /// $FFFF (E1021 at its part) contributes U+FFFD.
    /// </summary>
    private (SyntaxKind, object?) ScanString()
    {
        var value = new StringBuilder();
        while (_pos < _text.Length && IsStringPartStart(_pos))
        {
            var partStart = _pos;
            if (_text[_pos] == '\'')
            {
                _pos++;
                var terminated = false;
                while (_pos < _text.Length && _text[_pos] is not ('\r' or '\n'))
                {
                    if (_text[_pos] == '\'')
                    {
                        if (_pos + 1 < _text.Length && _text[_pos + 1] == '\'')
                        {
                            value.Append('\'');
                            _pos += 2;
                            continue;
                        }

                        _pos++;
                        terminated = true;
                        break;
                    }

                    value.Append(_text[_pos++]);
                }

                if (!terminated)
                {
                    Report("E1020", partStart, 1);
                    break;
                }
            }
            else
            {
                _pos++; // '#'
                var radix = 10;
                if (_text[_pos] == '$')
                {
                    radix = 16;
                    _pos++;
                }

                var digitsStart = _pos;
                while (_pos < _text.Length && IsDigit(_text[_pos], radix))
                {
                    _pos++;
                }

                var code = ParseInteger(_text.AsSpan(digitsStart, _pos - digitsStart), radix, out var overflow);
                if (overflow || code > 0xFFFF)
                {
                    Report("E1021", partStart, _pos - partStart);
                    value.Append('�');
                }
                else
                {
                    value.Append((char)code);
                }
            }
        }

        return (SyntaxKind.StringLiteralToken, value.ToString());
    }

    private static bool IsDigit(char c, int radix) => radix switch
    {
        16 => char.IsAsciiHexDigit(c),
        10 => char.IsAsciiDigit(c),
        8 => c is >= '0' and <= '7',
        _ => c is '0' or '1',
    };

    /// <summary>
    /// Integer and real literals (LANG-125…128). The token text includes letters/digits glued to the literal
    /// (E1013, one error); at most one diagnostic per literal: E1013, else E1010, else E1011 / E1012.
    /// Values: long when it fits, else ulong; reals are double.
    /// </summary>
    private (SyntaxKind, object?) ScanNumber()
    {
        var start = _pos;
        var radix = _text[_pos] switch { '$' => 16, '%' => 2, '&' => 8, _ => 10 };
        if (radix != 10)
        {
            _pos++;
        }

        var digitsStart = _pos;
        var badSeparator = -1;
        ScanDigits(radix, allowSeparators: radix != 8, ref badSeparator);
        var integerEnd = _pos;
        var isReal = false;
        var badExponent = false;
        var valueEnd = _pos;
        if (radix == 10)
        {
            // LANG-128: "1." followed by anything other than a digit is the integer 1 then '.'.
            if (_pos + 1 < _text.Length && _text[_pos] == '.' && char.IsAsciiDigit(_text[_pos + 1]))
            {
                _pos++;
                ScanDigits(10, allowSeparators: true, ref badSeparator);
                isReal = true;
            }

            valueEnd = _pos;
            if (_pos < _text.Length && _text[_pos] is 'e' or 'E')
            {
                var p = _pos + 1;
                if (p < _text.Length && _text[p] is '+' or '-')
                {
                    p++;
                }

                if (p < _text.Length && char.IsAsciiDigit(_text[p]))
                {
                    _pos = p;
                    while (_pos < _text.Length && char.IsAsciiDigit(_text[_pos]))
                    {
                        _pos++;
                    }

                    isReal = true;
                    valueEnd = _pos;
                }
                else
                {
                    badExponent = true;
                    _pos++; // the 'e' belongs to the invalid literal; a following sign does not
                }
            }
        }

        // LANG-128: letters (and digits invalid for the radix) glued to the literal are skipped as part of it.
        var junkStart = _pos;
        while (_pos < _text.Length)
        {
            if (char.IsAsciiDigit(_text[_pos]))
            {
                _pos++;
            }
            else if (IsIdentifierStartAt(_pos, out var width))
            {
                _pos += width;
            }
            else
            {
                break;
            }
        }

        var length = _pos - start;
        object value;
        var overflow = false;
        if (isReal)
        {
            var d = double.Parse(_text.AsSpan(start, valueEnd - start).ToString().Replace("_", "", StringComparison.Ordinal),
                                 NumberStyles.Float, CultureInfo.InvariantCulture);
            overflow = double.IsInfinity(d);
            value = d;
        }
        else
        {
            var v = ParseInteger(_text.AsSpan(digitsStart, integerEnd - digitsStart), radix, out overflow);
            value = v <= long.MaxValue ? (long)v : v;
        }

        if (badExponent || _pos > junkStart)
        {
            Report("E1013", start, length, _text.Substring(start, length));
        }
        else if (badSeparator >= 0)
        {
            Report("E1010", badSeparator, 1);
        }
        else if (overflow)
        {
            Report(isReal ? "E1012" : "E1011", start, length);
        }

        return (isReal ? SyntaxKind.RealLiteralToken : SyntaxKind.IntegerLiteralToken, value);
    }

    /// <summary>Digits of <paramref name="radix"/> and '_' separators; records the first separator not followed by a digit.</summary>
    private void ScanDigits(int radix, bool allowSeparators, ref int badSeparator)
    {
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (IsDigit(c, radix))
            {
                _pos++;
            }
            else if (c == '_' && allowSeparators)
            {
                if (badSeparator < 0 && !(_pos + 1 < _text.Length && IsDigit(_text[_pos + 1], radix)))
                {
                    badSeparator = _pos;
                }

                _pos++;
            }
            else
            {
                break;
            }
        }
    }

    private static ulong ParseInteger(ReadOnlySpan<char> digits, int radix, out bool overflow)
    {
        ulong value = 0;
        overflow = false;
        foreach (var c in digits)
        {
            if (c == '_')
            {
                continue;
            }

            var d = (ulong)(c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10);
            if (value > (ulong.MaxValue - d) / (ulong)radix)
            {
                overflow = true;
                return 0;
            }

            value = value * (ulong)radix + d;
        }

        return value;
    }

    /// <summary>Consumes the identifier starting at <c>_pos</c> (whose first character is known to be valid).</summary>
    private void ScanIdentifierRest()
    {
        IsIdentifierStartAt(_pos, out var width);
        _pos += width;
        while (_pos < _text.Length)
        {
            if (char.IsAsciiDigit(_text[_pos]))
            {
                _pos++;
            }
            else if (IsIdentifierStartAt(_pos, out width))
            {
                _pos += width;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>LANG-110 letter at <paramref name="index"/>: A–Z, a–z, _, or a code point ≥ U+0080 of category L* or Nl.</summary>
    private bool IsIdentifierStartAt(int index, out int width)
    {
        var c = _text[index];
        width = 1;
        if (char.IsAsciiLetter(c) || c == '_')
        {
            return true;
        }

        if (c < 0x80)
        {
            return false;
        }

        if (Rune.DecodeFromUtf16(_text.AsSpan(index), out var rune, out width) != System.Buffers.OperationStatus.Done)
        {
            width = 1;
            return false;
        }

        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber;
    }

    private bool StartsToken(int index) =>
        IsIdentifierStartAt(index, out _) || char.IsAsciiDigit(_text[index]) || IsRadixLiteralStart(index) || _text[index] is
            '+' or '-' or '*' or '/' or '=' or '<' or '>' or ':' or '.' or ',' or ';' or '(' or ')' or '[' or ']'
            or '^' or '@' or '#' or '$' or '&' or '\'';

    // ------------------------------------------------------------------ trivia

    /// <summary>
    /// Appends trivia pieces to <c>_trivia</c>. Returns true when it stopped right after an active include directive
    /// that must be expanded (leading trivia only); in trailing mode it stops before any include directive.
    /// </summary>
    private bool ScanTrivia(bool trailing)
    {
        while (_pos < _text.Length)
        {
            var start = _pos;
            if (!_directives.IsActive)
            {
                // LANG-864: an inactive branch runs up to the directive that may end it (or to the end of file).
                var end = FindEndOfDisabledText(_pos);
                if (end > _pos)
                {
                    _pos = end;
                    _trivia.Add(new SyntaxTrivia(SyntaxKind.DisabledTextTrivia, _text[start.._pos], start));
                    continue;
                }
            }

            var c = _text[_pos];
            var next = _pos + 1 < _text.Length ? _text[_pos + 1] : '\0';
            if (c == '{' || (c == '(' && next == '*'))
            {
                if (trailing && IsIncludeDirectiveAt(_pos))
                {
                    break;
                }

                if (ScanBlockComment(closer: c == '{' ? "}" : "*)", openerLength: c == '{' ? 1 : 2))
                {
                    return true;
                }
            }
            else if (c == '/' && next == '/')
            {
                while (_pos < _text.Length && _text[_pos] is not ('\r' or '\n'))
                {
                    _pos++;
                }

                // LANG-142: "///" (but not "////") is a documentation comment.
                var isDoc = _pos - start >= 3 && _text[start + 2] == '/' && (_pos - start == 3 || _text[start + 3] != '/');
                _trivia.Add(new SyntaxTrivia(isDoc ? SyntaxKind.DocCommentTrivia : SyntaxKind.LineCommentTrivia, _text[start.._pos], start));
            }
            else if (IsWhitespace(c))
            {
                while (_pos < _text.Length && IsWhitespace(_text[_pos]))
                {
                    _pos++;
                }

                _trivia.Add(new SyntaxTrivia(SyntaxKind.WhitespaceTrivia, Spaces(start, _pos), start));
            }
            else if (c is '\r' or '\n')
            {
                _pos += c == '\r' && next == '\n' ? 2 : 1;
                _trivia.Add(new SyntaxTrivia(SyntaxKind.EndOfLineTrivia, _pos - start == 2 ? "\r\n" : c == '\n' ? "\n" : "\r", start));
                if (trailing)
                {
                    break;
                }
            }
            else if (!StartsToken(_pos))
            {
                SkipIllegalCharacter();
            }
            else
            {
                break;
            }
        }

        return false;
    }

    /// <summary>True when a <c>{$I file}</c> / <c>{$INCLUDE file}</c> directive starts at <paramref name="pos"/>.</summary>
    private bool IsIncludeDirectiveAt(int pos)
    {
        var openerLength = _text[pos] == '{' ? 1 : 2;
        var contentStart = pos + openerLength;
        if (contentStart >= _text.Length || _text[contentStart] != '$')
        {
            return false;
        }

        var end = _text.IndexOf(openerLength == 1 ? "}" : "*)", contentStart, StringComparison.Ordinal);
        var body = _text.AsSpan(contentStart + 1, (end < 0 ? _text.Length : end) - contentStart - 1);
        var nameLength = DirectiveNameLength(body);
        return DirectiveProcessor.IsInclude(body[..nameLength].ToString().ToUpperInvariant(), body[nameLength..].Trim().ToString());
    }

    /// <summary>
    /// <c>{ … }</c> or <c>(* … *)</c> (LANG-140, no nesting). A <c>$</c> right after the opener makes a directive
    /// (LANG-141); <c>{**</c> a documentation comment (LANG-142). Unterminated: E1030 at the opener, runs to the end.
    /// Returns true for an include directive that the caller must expand (LANG-875).
    /// </summary>
    private bool ScanBlockComment(string closer, int openerLength)
    {
        var start = _pos;
        var contentStart = start + openerLength;
        var end = _text.IndexOf(closer, contentStart, StringComparison.Ordinal);
        var contentEnd = end < 0 ? _text.Length : end;
        _pos = end < 0 ? _text.Length : end + closer.Length;
        if (end < 0)
        {
            Report("E1030", start, openerLength);
        }

        var text = _text[start.._pos];
        if (contentStart < _text.Length && _text[contentStart] == '$')
        {
            var body = _text.AsSpan(contentStart + 1, contentEnd - contentStart - 1);
            var nameLength = DirectiveNameLength(body);
            var info = new DirectiveTriviaInfo(
                body[..nameLength].ToString().ToUpperInvariant(),
                body[nameLength..].Trim().ToString(),
                new TextSpan(start, text.Length),
                IsActive: true);
            var isInclude = DirectiveProcessor.IsInclude(info.Name, info.Argument);
            _trivia.Add(new SyntaxTrivia(isInclude ? SyntaxKind.IncludeDirectiveTrivia : SyntaxKind.DirectiveTrivia, text, start, info));
            _directives.Process(info);
            if (isInclude && _expandIncludes && DirectiveProcessor.IncludeFileName(info).Length > 0)
            {
                _pendingInclude = info;
                return true;
            }

            return false;
        }

        var isDoc = closer == "}" && contentStart + 1 < _text.Length && _text[contentStart] == '*' && _text[contentStart + 1] == '*'
                    && contentStart + 1 < contentEnd;
        _trivia.Add(new SyntaxTrivia(isDoc ? SyntaxKind.DocCommentTrivia : SyntaxKind.BlockCommentTrivia, text, start));
        return false;
    }

    private static int DirectiveNameLength(ReadOnlySpan<char> body)
    {
        var length = 0;
        while (length < body.Length && (char.IsAsciiLetterOrDigit(body[length]) || body[length] == '_'))
        {
            length++;
        }

        return length;
    }

    /// <summary>
    /// LANG-864: the end of an inactive branch: the start of the first <c>{$ELSE}</c>, <c>{$ELSEIF}</c>,
    /// <c>{$ENDIF}</c> or <c>{$IFEND}</c> at nesting depth 0, else the end of the text. The text is not tokenised;
    /// only comments (<c>{ }</c>, <c>(* *)</c>, <c>//</c>) and quoted strings are tracked so that directives inside
    /// them are not seen. Unterminated comments and strings are not errors here.
    /// </summary>
    private int FindEndOfDisabledText(int pos)
    {
        var depth = 0;
        while (pos < _text.Length)
        {
            var c = _text[pos];
            var next = pos + 1 < _text.Length ? _text[pos + 1] : '\0';
            if (c == '\'')
            {
                pos++;
                while (pos < _text.Length && _text[pos] is not ('\'' or '\r' or '\n'))
                {
                    pos++;
                }

                pos = Math.Min(pos + 1, _text.Length);
            }
            else if (c == '/' && next == '/')
            {
                while (pos < _text.Length && _text[pos] is not ('\r' or '\n'))
                {
                    pos++;
                }
            }
            else if (c == '{' || (c == '(' && next == '*'))
            {
                var openerLength = c == '{' ? 1 : 2;
                var closer = c == '{' ? "}" : "*)";
                var contentStart = pos + openerLength;
                var end = _text.IndexOf(closer, contentStart, StringComparison.Ordinal);
                if (contentStart < _text.Length && _text[contentStart] == '$')
                {
                    var body = _text.AsSpan(contentStart + 1, (end < 0 ? _text.Length : end) - contentStart - 1);
                    var name = body[..DirectiveNameLength(body)].ToString().ToUpperInvariant();
                    if (DirectiveProcessor.OpensConditional(name))
                    {
                        depth++;
                    }
                    else if (depth == 0 && DirectiveProcessor.EndsDisabledText(name))
                    {
                        return pos;
                    }
                    else if (DirectiveProcessor.ClosesConditional(name))
                    {
                        depth--;
                    }
                }

                pos = end < 0 ? _text.Length : end + closer.Length;
            }
            else
            {
                pos++;
            }
        }

        return pos;
    }

    /// <summary>LANG-104: E1002, the character (or surrogate pair) becomes skipped-text trivia.</summary>
    private void SkipIllegalCharacter()
    {
        var start = _pos;
        string shown;
        int codePoint;
        if (Rune.DecodeFromUtf16(_text.AsSpan(_pos), out var rune, out var width) == System.Buffers.OperationStatus.Done)
        {
            codePoint = rune.Value;
            shown = rune.Value < 0x20 || rune.Value == 0x7F ? "" : rune.ToString();
        }
        else
        {
            codePoint = _text[_pos];
            width = 1;
            shown = "";
        }

        _pos += width;
        _trivia.Add(new SyntaxTrivia(SyntaxKind.SkippedTokensTrivia, _text[start.._pos], start));
        Report("E1002", start, width, shown, codePoint.ToString("X4", CultureInfo.InvariantCulture));
    }

    private static readonly string[] SpaceRuns = [.. Enumerable.Range(0, 33).Select(n => new string(' ', n))];

    /// <summary>C-244: the text of a whitespace run, shared for runs of up to 32 spaces (most trivia), else a new string.</summary>
    private string Spaces(int start, int end)
    {
        if (end - start < SpaceRuns.Length && _text.AsSpan(start, end - start).IndexOfAnyExcept(' ') < 0)
        {
            return SpaceRuns[end - start];
        }

        return _text[start..end];
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\f' or '\u00A0' or '\uFEFF';

    private void Report(string code, int start, int length, params object[] args) =>
        Report(code, start, length, DiagnosticCatalog.Get(code).DefaultSeverity, args);

    private void Report(string code, int start, int length, DiagnosticSeverity severity, object[] args)
    {
        var span = new TextSpan(start, length);
        var lines = _source.Lines;
        var location = new Location(_source.Path, span,
            new LinePositionSpan(lines.GetLinePosition(span.Start), lines.GetLinePosition(span.End)));
        _context.Diagnostics.Add(Diagnostic.Create(code, location, args) with { Severity = severity });
    }
}
