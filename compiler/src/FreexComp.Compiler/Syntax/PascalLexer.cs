using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>
/// Pull lexer (CMP-110): <see cref="Lex"/> returns one token at a time and never throws. After the end of the
/// text it returns <see cref="SyntaxKind.EndOfFileToken"/>, which carries the remaining trivia.
/// </summary>
internal sealed class PascalLexer
{
    private readonly SourceText _source;
    private readonly string _text;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly ImmutableArray<SyntaxTrivia>.Builder _trivia = ImmutableArray.CreateBuilder<SyntaxTrivia>();
    private int _pos;

    public PascalLexer(SourceText source)
    {
        _source = source;
        _text = source.Text;
    }

    public ImmutableArray<Diagnostic> Diagnostics => [.. _diagnostics];

    public SyntaxToken Lex()
    {
        var leading = ScanTrivia();
        var start = _pos;
        if (_pos >= _text.Length)
        {
            return new SyntaxToken(SyntaxKind.EndOfFileToken, "", null, start, leading, []);
        }

        var (kind, value) = ScanToken();
        return new SyntaxToken(kind, _text[start.._pos], value, start, leading, []);
    }

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

        if (char.IsAsciiDigit(c))
        {
            return ScanNumber();
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

    private (SyntaxKind, object?) ScanNumber()
    {
        var start = _pos;
        while (_pos < _text.Length && char.IsAsciiDigit(_text[_pos]))
        {
            _pos++;
        }

        return (SyntaxKind.IntegerLiteralToken,
            ulong.TryParse(_text.AsSpan(start, _pos - start), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? (object)(long)v : 0L);
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
        IsIdentifierStartAt(index, out _) || char.IsAsciiDigit(_text[index]) || _text[index] is
            '+' or '-' or '*' or '/' or '=' or '<' or '>' or ':' or '.' or ',' or ';' or '(' or ')' or '[' or ']'
            or '^' or '@' or '#' or '$' or '&';

    // ------------------------------------------------------------------ trivia

    private ImmutableArray<SyntaxTrivia> ScanTrivia()
    {
        _trivia.Clear();
        while (_pos < _text.Length)
        {
            var start = _pos;
            var c = _text[_pos];
            if (IsWhitespace(c))
            {
                while (_pos < _text.Length && IsWhitespace(_text[_pos]))
                {
                    _pos++;
                }

                _trivia.Add(new SyntaxTrivia(SyntaxKind.WhitespaceTrivia, _text[start.._pos], start));
            }
            else if (c is '\r' or '\n')
            {
                _pos += c == '\r' && _pos + 1 < _text.Length && _text[_pos + 1] == '\n' ? 2 : 1;
                _trivia.Add(new SyntaxTrivia(SyntaxKind.EndOfLineTrivia, _text[start.._pos], start));
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

        return _trivia.Count == 0 ? [] : _trivia.ToImmutable();
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

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\f' or ' ' or '﻿';

    private void Report(string code, int start, int length, params object[] args)
    {
        var span = new TextSpan(start, length);
        var lines = _source.Lines;
        var location = new Location(_source.Path, span,
            new LinePositionSpan(lines.GetLinePosition(span.Start), lines.GetLinePosition(span.End)));
        _diagnostics.Add(Diagnostic.Create(code, location, args));
    }
}
