using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Text;

/// <summary>
/// Reads a <c>.form</c> text (grammar: docs/interfaces/form-format.md §1; C# rules: form-document.md §3). The first
/// syntax error stops parsing with exactly one <c>F0001</c> and no document (DSG-FD-025).
/// </summary>
public static class FormParser
{
    public static FormParseResult Parse(string text, string path, ComponentCatalog catalog)
    {
        var parser = new Parser(text, path, catalog);
        try
        {
            var document = parser.ParseFile();
            return new FormParseResult(document, [.. document.Diagnostics]);
        }
        catch (SyntaxErrorException error)
        {
            return new FormParseResult(null, [parser.SyntaxDiagnostic(error)]);
        }
    }

    private sealed class SyntaxErrorException(int position, string detail, string? objectName) : Exception(detail)
    {
        public int Position { get; } = position;

        public string Detail { get; } = detail;

        public string? ObjectName { get; } = objectName;
    }

    private sealed class Parser(string text, string path, ComponentCatalog catalog)
    {
        private int _pos;
        private string? _lineEnding;
        private string? _currentObject;

        public FormDocument ParseFile()
        {
            if (text.Length > 0 && text[0] == '﻿')
            {
                _pos = 1; // DSG-FD-020: optional BOM
            }

            SkipBlankLines();
            var root = ParseObject();
            SkipBlankLines();
            if (_pos < text.Length)
            {
                throw Error("end of file");
            }

            return new FormDocument(root, _lineEnding ?? "\n");
        }

        public FormDiagnostic SyntaxDiagnostic(SyntaxErrorException error)
        {
            var (line, col) = LineCol(error.Position);
            return new FormDiagnostic("F0001", DiagnosticSeverity.Error, "Syntax error: " + error.Detail, path,
                line, col, line, col, error.ObjectName, null);
        }

        private FormObject ParseObject()
        {
            var start = _pos;
            SkipSpaces();
            var keywordStart = _pos;
            var keyword = ReadIdentifier("object");
            var kind = KeywordKind(keyword) ?? throw ErrorAt(keywordStart, "object", Quote(keyword));
            SkipSpaces();
            var name = ReadIdentifier("identifier");
            SkipSpaces();
            Expect(':');
            SkipSpaces();
            var typeName = ReadIdentifier("identifier");
            ExpectLineEnd();

            var parentObject = _currentObject;
            _currentObject = name;
            var obj = new FormObject(kind, name, typeName, catalog.Find(typeName), default);
            while (PeekWord() is { } word && !IsKeyword(word))
            {
                obj.AddProperty(ParseProperty(obj));
            }

            while (PeekWord() is { } word && KeywordKind(word) is not null && IsKeyword(word))
            {
                obj.AddChild(ParseObject());
            }

            SkipSpaces();
            var endStart = _pos;
            var end = PeekIdentifier();
            if (!string.Equals(end, "end", StringComparison.OrdinalIgnoreCase))
            {
                throw ErrorAt(endStart, "end", Found(endStart));
            }

            _pos += end.Length;
            ExpectLineEnd();
            _currentObject = parentObject;
            obj.SourceSpan = new TextSpan(start, _pos - start);
            return obj;
        }

        private FormProperty ParseProperty(FormObject obj)
        {
            SkipSpaces();
            var start = _pos;
            var parts = new List<string> { ReadIdentifier("identifier") };
            while (Peek() == '.')
            {
                _pos++;
                parts.Add(ReadIdentifier("identifier"));
            }

            var propertyPath = string.Join('.', parts);
            SkipSpaces();
            Expect('=');
            SkipSpaces();

            var (info, eventInfo) = Bind(obj.Info, parts);
            var valueStart = _pos;
            var value = ParseValue(info);
            var raw = text[valueStart.._pos].Replace("\r\n", "\n", StringComparison.Ordinal);
            var end = _pos;
            ExpectLineEnd();
            return new FormProperty(propertyPath, value, info, eventInfo, raw, new TextSpan(start, end - start));
        }

        /// <summary>Metadata of a property path: an event, a property, or a sub-property of an object property.</summary>
        private (PropertyInfo? Property, EventInfo? Event) Bind(ComponentInfo? info, List<string> parts)
        {
            if (info is null)
            {
                return (null, null);
            }

            if (parts.Count == 1 && info.FindEvent(parts[0]) is { } ev)
            {
                return (null, ev);
            }

            var property = info.FindProperty(parts[0]);
            for (var i = 1; i < parts.Count && property is not null; i++)
            {
                property = catalog.FindType(property.Type)?.Properties
                    .FirstOrDefault(p => string.Equals(p.Name, parts[i], StringComparison.OrdinalIgnoreCase));
            }

            return (property, null);
        }

        private FormValue ParseValue(PropertyInfo? info)
        {
            var c = Peek();
            if (c is '\'' or '#')
            {
                return new StringValue(ParseString());
            }

            if (c == '$' || char.IsAsciiDigit(c) || (c == '-' && char.IsAsciiDigit(Peek(1))))
            {
                return ParseNumber(info);
            }

            if (IsIdentifierStart(c))
            {
                var parts = new List<string> { ReadIdentifier("value") };
                while (Peek() == '.')
                {
                    _pos++;
                    parts.Add(ReadIdentifier("identifier"));
                }

                return parts.Count == 1 ? new IdentValue(parts[0]) : new QualifiedIdentValue([.. parts]);
            }

            throw Error("value");
        }

        /// <summary>DSG-FD-024: adjacent parts concatenate; <c>+</c> at the end of a line continues on the next.</summary>
        private string ParseString()
        {
            var result = new StringBuilder();
            while (true)
            {
                if (Peek() is not ('\'' or '#'))
                {
                    throw Error("string");
                }

                while (Peek() is '\'' or '#')
                {
                    if (Peek() == '\'')
                    {
                        ReadQuoted(result);
                    }
                    else
                    {
                        ReadCharCode(result);
                    }
                }

                var beforeSpaces = _pos;
                SkipSpaces();
                if (Peek() != '+')
                {
                    _pos = beforeSpaces;
                    return result.ToString();
                }

                _pos++;
                SkipSpaces();
                if (!AtLineEnd())
                {
                    throw Error("end of line");
                }

                if (_pos >= text.Length)
                {
                    throw Error("string");
                }

                ConsumeLineEnd();
                SkipSpaces();
            }
        }

        private void ReadQuoted(StringBuilder result)
        {
            var start = _pos;
            _pos++; // opening quote
            while (true)
            {
                if (_pos >= text.Length || text[_pos] is '\r' or '\n')
                {
                    throw ErrorAt(start, "closing quote", Found(_pos));
                }

                if (text[_pos] == '\'')
                {
                    if (Peek(1) == '\'')
                    {
                        result.Append('\'');
                        _pos += 2;
                        continue;
                    }

                    _pos++;
                    return;
                }

                result.Append(text[_pos++]);
            }
        }

        private void ReadCharCode(StringBuilder result)
        {
            var start = _pos;
            _pos++; // '#'
            ulong value;
            if (Peek() == '$')
            {
                _pos++;
                value = ReadDigits(16, start);
            }
            else if (char.IsAsciiDigit(Peek()))
            {
                value = ReadDigits(10, start);
            }
            else
            {
                throw Error("character code");
            }

            if (value > 0xFFFF)
            {
                throw ErrorAt(start, "character code 0..65535", Quote(text[start.._pos]));
            }

            result.Append((char)value);
        }

        private FormValue ParseNumber(PropertyInfo? info)
        {
            var start = _pos;
            if (Peek() == '$')
            {
                _pos++;
                var digitsStart = _pos;
                var value = ReadDigits(16, start);
                if (_pos - digitsStart > 16)
                {
                    throw IntegerOutOfRange(start);
                }

                // DSG-FD-023: a 32-bit pattern for 32-bit signed property types.
                var signed = info?.Type is { } type && value <= 0xFFFFFFFF
                    && (type.Equals("Integer", StringComparison.OrdinalIgnoreCase) || type.Equals("TColor", StringComparison.OrdinalIgnoreCase)
                        || type.Equals("TCursor", StringComparison.OrdinalIgnoreCase))
                    ? unchecked((int)(uint)value)
                    : unchecked((long)value);
                return new IntegerValue(signed, Hex: true);
            }

            if (Peek() == '-')
            {
                _pos++;
            }

            while (char.IsAsciiDigit(Peek()))
            {
                _pos++;
            }

            var isFloat = false;
            if (Peek() == '.' && char.IsAsciiDigit(Peek(1)))
            {
                isFloat = true;
                _pos++;
                while (char.IsAsciiDigit(Peek()))
                {
                    _pos++;
                }
            }

            if (Peek() is 'e' or 'E')
            {
                var save = _pos;
                _pos++;
                if (Peek() is '+' or '-')
                {
                    _pos++;
                }

                if (char.IsAsciiDigit(Peek()))
                {
                    isFloat = true;
                    while (char.IsAsciiDigit(Peek()))
                    {
                        _pos++;
                    }
                }
                else
                {
                    _pos = save;
                }
            }

            var literal = text[start.._pos];
            if (isFloat)
            {
                var d = double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (!double.IsFinite(d))
                {
                    throw new SyntaxErrorException(start, "float out of range", _currentObject);
                }

                return new FloatValue(d);
            }

            return long.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)
                ? new IntegerValue(l, Hex: false)
                : throw IntegerOutOfRange(start);
        }

        private SyntaxErrorException IntegerOutOfRange(int start) => new(start, "integer out of range", _currentObject);

        private ulong ReadDigits(int radix, int literalStart)
        {
            var digitsStart = _pos;
            ulong value = 0;
            var overflow = false;
            while (_pos < text.Length && (radix == 16 ? char.IsAsciiHexDigit(text[_pos]) : char.IsAsciiDigit(text[_pos])))
            {
                var digit = (ulong)(char.IsAsciiDigit(text[_pos]) ? text[_pos] - '0' : (char.ToUpperInvariant(text[_pos]) - 'A' + 10));
                overflow |= value > (ulong.MaxValue - digit) / (ulong)radix;
                value = unchecked((value * (ulong)radix) + digit);
                _pos++;
            }

            if (_pos == digitsStart)
            {
                throw Error(radix == 16 ? "hexadecimal digit" : "digit");
            }

            return overflow ? throw IntegerOutOfRange(literalStart) : value;
        }

        // ---- lexical helpers -------------------------------------------------------------------------------

        private static FormObjectKind? KeywordKind(string word) => word.ToLowerInvariant() switch
        {
            "object" => FormObjectKind.Object,
            "inherited" => FormObjectKind.Inherited,
            "inline" => FormObjectKind.Inline,
            _ => null,
        };

        /// <summary>
        /// The next word of a line starts an object header or ends the object (keywords are ASCII
        /// case-insensitive, DSG-FD-021) unless it is followed by <c>=</c> or <c>.</c> (then it is a property path).
        /// </summary>
        private bool IsKeyword(string word)
        {
            if (KeywordKind(word) is null && !string.Equals(word, "end", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var after = _pos;
            while (after < text.Length && text[after] is ' ' or '\t')
            {
                after++;
            }

            after += word.Length;
            while (after < text.Length && text[after] is ' ' or '\t')
            {
                after++;
            }

            return after >= text.Length || text[after] is not ('=' or '.');
        }

        /// <summary>The identifier at the start of the next line content (after spaces), without consuming it.</summary>
        private string? PeekWord()
        {
            var save = _pos;
            SkipSpaces();
            var word = PeekIdentifier();
            _pos = save;
            return word.Length == 0 ? null : word;
        }

        private string PeekIdentifier()
        {
            if (!IsIdentifierStart(Peek()))
            {
                return "";
            }

            var end = _pos + 1;
            while (end < text.Length && IsIdentifierPart(text[end]))
            {
                end++;
            }

            return text[_pos..end];
        }

        private string ReadIdentifier(string expected)
        {
            var word = PeekIdentifier();
            if (word.Length == 0)
            {
                throw Error(expected);
            }

            _pos += word.Length;
            return word;
        }

        private static bool IsIdentifierStart(char c) => char.IsAsciiLetter(c) || c == '_';

        private static bool IsIdentifierPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

        private char Peek(int offset = 0) => _pos + offset < text.Length ? text[_pos + offset] : '\0';

        private void SkipSpaces()
        {
            while (_pos < text.Length && text[_pos] is ' ' or '\t')
            {
                _pos++;
            }
        }

        private bool AtLineEnd() => _pos >= text.Length || text[_pos] is '\r' or '\n';

        private void ConsumeLineEnd()
        {
            if (_pos < text.Length && text[_pos] == '\r' && Peek(1) == '\n')
            {
                _lineEnding ??= "\r\n";
                _pos += 2;
            }
            else if (_pos < text.Length && text[_pos] == '\n')
            {
                _lineEnding ??= "\n";
                _pos++;
            }
            else
            {
                throw Error("end of line"); // a lone CR
            }
        }

        /// <summary>Trailing spaces, one line end (end of file counts as one), then any blank lines.</summary>
        private void ExpectLineEnd()
        {
            SkipSpaces();
            if (_pos >= text.Length)
            {
                return;
            }

            if (!AtLineEnd())
            {
                throw Error("end of line");
            }

            ConsumeLineEnd();
            SkipBlankLines();
        }

        private void SkipBlankLines()
        {
            while (true)
            {
                var save = _pos;
                SkipSpaces();
                if (_pos < text.Length && AtLineEnd())
                {
                    ConsumeLineEnd();
                    continue;
                }

                if (_pos >= text.Length)
                {
                    return;
                }

                _pos = save;
                return;
            }
        }

        private void Expect(char c)
        {
            if (Peek() != c || _pos >= text.Length)
            {
                throw Error(Quote(c.ToString()));
            }

            _pos++;
        }

        private SyntaxErrorException Error(string expected) => ErrorAt(_pos, expected, Found(_pos));

        private SyntaxErrorException ErrorAt(int position, string expected, string found) =>
            new(position, $"expected {expected} but found {found}", _currentObject);

        private string Found(int position)
        {
            if (position >= text.Length)
            {
                return "end of file";
            }

            if (text[position] is '\r' or '\n')
            {
                return "end of line";
            }

            var save = _pos;
            _pos = position;
            var word = PeekIdentifier();
            _pos = save;
            return Quote(word.Length > 0 ? word : text[position].ToString());
        }

        private static string Quote(string s) => "\"" + s + "\"";

        private (int Line, int Col) LineCol(int position)
        {
            var line = 1;
            var lineStart = 0;
            for (var i = 0; i < position && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            return (line, position - lineStart + 1);
        }
    }
}
