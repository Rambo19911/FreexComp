using System.Collections.Immutable;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Forms;

/// <summary>An event property <c>OnXxx = Handler</c> of a form object, with the 1-based position of the handler name.</summary>
public sealed record FormEvent(string Property, string Handler, int Line, int Column);

/// <summary>
/// One object of a <c>.form</c> file (form-format.md §1): keyword (<c>object</c> / <c>inherited</c> / <c>inline</c>), name, class
/// name, the 1-based position of its name, its event properties and its child objects. The contents of a nested <c>inherited</c>
/// object outside a frame instance are skipped (form-format.md §2.6), so they are empty. A nested <c>inline</c> object (a frame
/// instance, LANG-952…954) keeps its events and, as children, its <c>inherited</c> override blocks (<see cref="IsOverride"/>, nested
/// ones too, each with its events); an <c>object</c> or <c>inline</c> inside an instance is skipped (the reader reports F0110).
/// </summary>
public sealed record FormObject(string Keyword, string Name, string ClassName, int Line, int Column,
    ImmutableArray<FormEvent> Events, ImmutableArray<FormObject> Children)
{
    /// <summary>An <c>inherited</c> override block inside a frame instance: its name is a component of the frame, not of the root (LANG-952).</summary>
    public bool IsOverride { get; init; }

    /// <summary>The object's descendants, depth first, in file order.</summary>
    public IEnumerable<FormObject> Descendants() => Children.SelectMany(c => c.Descendants().Prepend(c));
}

/// <summary>The result of <see cref="FormFileScanner.Scan"/>: the root object, or null after the first syntax error (<c>F0001</c>).</summary>
public sealed record FormScanResult(FormObject? Root, ImmutableArray<Diagnostic> Diagnostics);

/// <summary>
/// LANG-670 (C-114): the compiler's minimal reader of <c>.form</c> files — the whole grammar of form-format.md §1 and the lexical
/// rules L1…L8 are checked, but only objects (keyword, name, class) and event properties (a plain identifier value of a property
/// whose name starts with <c>On</c>) are kept. The first syntax error ends the scan with one <c>F0001</c> error at its position.
/// </summary>
public static class FormFileScanner
{
    public static FormScanResult Scan(string path, string text)
    {
        var scanner = new Scanner(path, text);
        try
        {
            var root = scanner.File();
            return new FormScanResult(root, []);
        }
        catch (FormSyntaxException e)
        {
            return new FormScanResult(null, [scanner.Error(e)]);
        }
    }

    /// <summary>Reads a file's bytes as UTF-8 (L7: invalid UTF-8 is a syntax error); null for invalid bytes.</summary>
    public static string? Decode(byte[] bytes)
    {
        try
        {
            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>form-format.md §5 <c>F0001</c> <c>Syntax error: &lt;detail&gt;</c>, an error in the compiler (LANG-670; F codes are not in the catalogue).</summary>
    internal static Diagnostic SyntaxError(Location location, string detail) =>
        new("F0001", DiagnosticSeverity.Error, "Syntax error: " + detail, location, []);

    private sealed class FormSyntaxException(string detail, int position) : Exception(detail)
    {
        public int Position { get; } = position;
    }

    private enum TokenKind { Identifier, Number, String, Symbol, NewLine, End }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private sealed class Scanner(string path, string text)
    {
        private readonly LineMap _lines = new(text);
        private int _position;
        private Token _token;
        private bool _started;

        public Diagnostic Error(FormSyntaxException e)
        {
            var at = _lines.GetLinePosition(Math.Min(e.Position, text.Length));
            return SyntaxError(new Location(path, new TextSpan(e.Position, 0), new LinePositionSpan(at, at)), e.Message);
        }

        private Token Current
        {
            get
            {
                if (!_started)
                {
                    _started = true;
                    _token = Lex();
                }

                return _token;
            }
        }

        private void Next() => _token = Lex();

        private FormSyntaxException Unexpected(string expected) =>
            new($"{expected} expected but {Describe(Current)} found", Current.Position);

        private static string Describe(Token token) => token.Kind switch
        {
            TokenKind.End => "end of file",
            TokenKind.NewLine => "end of line",
            _ => $"'{token.Text}'",
        };

        public FormObject File()
        {
            SkipNewLines();
            var root = Object(Contents.Ordinary, isOverride: false);
            SkipNewLines();
            if (Current.Kind != TokenKind.End)
            {
                throw Unexpected("end of file");
            }

            return root;
        }

        private void SkipNewLines()
        {
            while (Current.Kind == TokenKind.NewLine)
            {
                Next();
            }
        }

        private bool IsKeyword(string keyword) => Current.Kind == TokenKind.Identifier && string.Equals(Current.Text, keyword, StringComparison.OrdinalIgnoreCase);

        private Token Expect(TokenKind kind, string what)
        {
            var token = Current;
            if (token.Kind != kind)
            {
                throw Unexpected(what);
            }

            Next();
            return token;
        }

        private void ExpectSymbol(string symbol)
        {
            if (Current.Kind != TokenKind.Symbol || Current.Text != symbol)
            {
                throw Unexpected($"'{symbol}'");
            }

            Next();
        }

        private void EndOfLine()
        {
            if (Current.Kind != TokenKind.NewLine && Current.Kind != TokenKind.End)
            {
                throw Unexpected("end of line");
            }

            SkipNewLines();
        }

        /// <summary>What of an object's block is kept (form-format.md §2.6, LANG-952…954).</summary>
        private enum Contents
        {
            Ordinary,       // events and every child: an object's children are objects, an inline child an instance, an inherited child skipped
            Instance,       // a frame instance or one of its override blocks: events and the inherited override blocks
            Skipped,        // nothing: an inherited object outside an instance, an object added to an instance, and what they contain
        }

        private FormObject Object(Contents contents, bool isOverride)
        {
            if (!(IsKeyword("object") || IsKeyword("inherited") || IsKeyword("inline")))
            {
                throw Unexpected("'object'");
            }

            var keyword = Current.Text.ToLowerInvariant();
            Next();
            var name = Expect(TokenKind.Identifier, "identifier");
            ExpectSymbol(":");
            var type = Expect(TokenKind.Identifier, "class name");
            EndOfLine();
            var events = ImmutableArray.CreateBuilder<FormEvent>();
            var children = ImmutableArray.CreateBuilder<FormObject>();
            var objects = false;
            while (!IsKeyword("end"))
            {
                if (IsKeyword("object") || IsKeyword("inherited") || IsKeyword("inline"))
                {
                    var (inner, kept) = (contents, Current.Text.ToLowerInvariant()) switch
                    {
                        (Contents.Ordinary, "object") => (Contents.Ordinary, true),
                        (Contents.Ordinary, "inline") => (Contents.Instance, true),
                        (Contents.Ordinary, _) => (Contents.Skipped, true),
                        (Contents.Instance, "inherited") => (Contents.Instance, true),
                        _ => (Contents.Skipped, false),
                    };
                    var child = Object(inner, isOverride: contents == Contents.Instance);
                    if (kept)
                    {
                        children.Add(child);
                    }

                    objects = true;
                    continue;
                }

                if (objects && contents != Contents.Skipped)
                {
                    throw Unexpected("'end'");                                      // properties come before child objects
                }

                if (Property() is { } handler && contents != Contents.Skipped)
                {
                    events.Add(handler);
                }
            }

            Next();
            EndOfLine();
            var (line, column) = Position(name.Position);
            return new FormObject(keyword, name.Text, type.Text, line, column, events.ToImmutable(), children.ToImmutable()) { IsOverride = isOverride };
        }

        private (int Line, int Column) Position(int offset)
        {
            var at = _lines.GetLinePosition(offset);
            return (at.Line + 1, at.Column + 1);
        }

        /// <summary><c>qualIdent "=" value NL</c>; the event handler when the value is a plain identifier of an <c>On…</c> property.</summary>
        private FormEvent? Property()
        {
            var first = Expect(TokenKind.Identifier, "property name");
            var dotted = false;
            while (Current.Kind == TokenKind.Symbol && Current.Text == ".")
            {
                Next();
                Expect(TokenKind.Identifier, "identifier");
                dotted = true;
            }

            ExpectSymbol("=");
            FormEvent? result = null;
            var value = Current;
            switch (value.Kind)
            {
                case TokenKind.Identifier:
                {
                    Next();
                    var plain = true;
                    while (Current.Kind == TokenKind.Symbol && Current.Text == ".")
                    {
                        Next();
                        Expect(TokenKind.Identifier, "identifier");
                        plain = false;
                    }

                    if (plain && !dotted && first.Text.StartsWith("On", StringComparison.OrdinalIgnoreCase))
                    {
                        var (line, column) = Position(value.Position);
                        result = new FormEvent(first.Text, value.Text, line, column);
                    }

                    break;
                }

                case TokenKind.Number:
                    Next();
                    break;
                case TokenKind.String:
                    StringValue();
                    break;
                case TokenKind.Symbol when value.Text == "[":
                    SetValue();
                    break;
                case TokenKind.Symbol when value.Text == "(":
                    StringList();
                    break;
                case TokenKind.Symbol when value.Text == "{":
                    Binary();
                    break;
                case TokenKind.Symbol when value.Text == "<":
                    Collection();
                    break;
                default:
                    throw Unexpected("value");
            }

            EndOfLine();
            return result;
        }

        /// <summary><c>strPart { strPart } { "+" NL strPart { strPart } }</c>; adjacent parts are single tokens of kind String.</summary>
        private void StringValue()
        {
            Expect(TokenKind.String, "string");
            while (Current.Kind == TokenKind.String)
            {
                Next();
            }

            while (Current.Kind == TokenKind.Symbol && Current.Text == "+")
            {
                Next();
                if (Current.Kind != TokenKind.NewLine)
                {
                    throw Unexpected("end of line after '+'");
                }

                SkipNewLines();
                Expect(TokenKind.String, "string");
                while (Current.Kind == TokenKind.String)
                {
                    Next();
                }
            }
        }

        private void SetValue()
        {
            ExpectSymbol("[");
            if (Current.Kind == TokenKind.Identifier)
            {
                Next();
                while (Current.Kind == TokenKind.Symbol && Current.Text == ",")
                {
                    Next();
                    Expect(TokenKind.Identifier, "identifier");
                }
            }

            ExpectSymbol("]");
        }

        private void StringList()
        {
            ExpectSymbol("(");
            if (Current.Kind != TokenKind.NewLine)
            {
                throw Unexpected("end of line");
            }

            SkipNewLines();
            while (Current.Kind == TokenKind.String)
            {
                StringValue();
                if (Current.Kind != TokenKind.NewLine)
                {
                    throw Unexpected("end of line");
                }

                SkipNewLines();
            }

            ExpectSymbol(")");
        }

        /// <summary><c>"{" NL { hexLine NL } "}"</c>: hex lines are read directly from the text.</summary>
        private void Binary()
        {
            // the lexer stopped after '{': read raw lines of hex digits up to the closing brace
            SkipBlanks();
            if (!AtLineEnd())
            {
                throw new FormSyntaxException("end of line expected after '{'", _position);
            }

            while (true)
            {
                SkipLineEnds();
                SkipBlanks();
                if (_position < text.Length && text[_position] == '}')
                {
                    _position++;
                    Next();
                    return;
                }

                var start = _position;
                while (_position < text.Length && Uri.IsHexDigit(text[_position]))
                {
                    _position++;
                }

                SkipBlanks();
                if (_position == start || !AtLineEnd())
                {
                    throw new FormSyntaxException("hexadecimal digits expected", _position);
                }
            }
        }

        private void Collection()
        {
            ExpectSymbol("<");
            if (Current.Kind != TokenKind.NewLine)
            {
                throw Unexpected("end of line");
            }

            SkipNewLines();
            while (IsKeyword("item"))
            {
                Next();
                EndOfLine();
                while (!IsKeyword("end"))
                {
                    Property();
                }

                Next();
                EndOfLine();
            }

            ExpectSymbol(">");
        }

        private void SkipBlanks()
        {
            while (_position < text.Length && text[_position] is ' ' or '\t')
            {
                _position++;
            }
        }

        private bool AtLineEnd() => _position >= text.Length || text[_position] is '\r' or '\n';

        private void SkipLineEnds()
        {
            while (_position < text.Length && text[_position] is '\r' or '\n')
            {
                _position++;
            }
        }

        private Token Lex()
        {
            SkipBlanks();
            if (_position >= text.Length)
            {
                return new Token(TokenKind.End, "", _position);
            }

            var start = _position;
            var c = text[_position];
            if (c is '\r' or '\n')
            {
                SkipLineEnds();
                return new Token(TokenKind.NewLine, "", start);
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                while (_position < text.Length && (char.IsAsciiLetterOrDigit(text[_position]) || text[_position] == '_'))
                {
                    _position++;
                }

                return new Token(TokenKind.Identifier, text[start.._position], start);
            }

            if (char.IsAsciiDigit(c) || c == '$' || (c == '-' && _position + 1 < text.Length && char.IsAsciiDigit(text[_position + 1])))
            {
                return Number(start);
            }

            if (c is '\'' or '#')
            {
                return StringPart(start);
            }

            if (c is '=' or ':' or '.' or '[' or ']' or '(' or ')' or '{' or '}' or '<' or '>' or ',' or '+')
            {
                _position++;
                return new Token(TokenKind.Symbol, c.ToString(), start);
            }

            throw new FormSyntaxException($"unexpected character '{c}'", start);
        }

        /// <summary>L3/L4: decimal integers in the signed 64-bit range, <c>$</c> + 1…16 hex digits, floats with <c>.</c> and an optional exponent.</summary>
        private Token Number(int start)
        {
            if (text[_position] == '$')
            {
                _position++;
                var digits = _position;
                while (_position < text.Length && Uri.IsHexDigit(text[_position]))
                {
                    _position++;
                }

                if (_position == digits || _position - digits > 16)
                {
                    throw new FormSyntaxException("invalid hexadecimal number", start);
                }

                return new Token(TokenKind.Number, text[start.._position], start);
            }

            if (text[_position] == '-')
            {
                _position++;
            }

            while (_position < text.Length && char.IsAsciiDigit(text[_position]))
            {
                _position++;
            }

            var isFloat = false;
            if (_position + 1 < text.Length && text[_position] == '.' && char.IsAsciiDigit(text[_position + 1]))
            {
                isFloat = true;
                _position++;
                while (_position < text.Length && char.IsAsciiDigit(text[_position]))
                {
                    _position++;
                }

                if (_position < text.Length && text[_position] is 'e' or 'E')
                {
                    _position++;
                    if (_position < text.Length && text[_position] is '+' or '-')
                    {
                        _position++;
                    }

                    var exponent = _position;
                    while (_position < text.Length && char.IsAsciiDigit(text[_position]))
                    {
                        _position++;
                    }

                    if (_position == exponent)
                    {
                        throw new FormSyntaxException("invalid number", start);
                    }
                }
            }

            var literal = text[start.._position];
            if (!isFloat && !long.TryParse(literal, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                throw new FormSyntaxException("integer out of range", start);
            }

            return new Token(TokenKind.Number, literal, start);
        }

        /// <summary>L5: <c>'…'</c> with doubled quotes, or <c>#n</c> / <c>#$h</c> (one UTF-16 code unit).</summary>
        private Token StringPart(int start)
        {
            if (text[_position] == '\'')
            {
                _position++;
                while (true)
                {
                    if (_position >= text.Length || text[_position] is '\r' or '\n')
                    {
                        throw new FormSyntaxException("unterminated string", start);
                    }

                    if (text[_position] == '\'')
                    {
                        if (_position + 1 < text.Length && text[_position + 1] == '\'')
                        {
                            _position += 2;
                            continue;
                        }

                        _position++;
                        return new Token(TokenKind.String, text[start.._position], start);
                    }

                    _position++;
                }
            }

            _position++;                                                            // '#'
            var hex = _position < text.Length && text[_position] == '$';
            if (hex)
            {
                _position++;
            }

            var digits = _position;
            while (_position < text.Length && (hex ? Uri.IsHexDigit(text[_position]) : char.IsAsciiDigit(text[_position])))
            {
                _position++;
            }

            if (_position == digits
                || !int.TryParse(text[digits.._position], hex ? System.Globalization.NumberStyles.AllowHexSpecifier : System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var code)
                || code > 0xFFFF)
            {
                throw new FormSyntaxException("invalid character code", start);
            }

            return new Token(TokenKind.String, text[start.._position], start);
        }
    }
}
