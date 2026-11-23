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
/// syntax error stops parsing with exactly one <c>F0001</c> and no document (DSG-FD-025); semantic problems produce
/// diagnostics and preserved items (DSG-FD-026).
/// </summary>
public static class FormParser
{
    /// <summary>The deepest nesting of objects and of collections accepted; deeper text is a syntax error (F0001).</summary>
    public const int MaxNesting = 256;

    /// <param name="rootAncestorClass">
    /// The ancestor of the form class declared in the unit (code sync supplies it from <c>class(TForm)</c>). The root
    /// binds to its own class when the catalog has it, else to this class, else to the first catalog class of kind
    /// <c>form</c>; a root bound by either fallback never raises <c>F0101</c>.
    /// </param>
    public static FormParseResult Parse(string text, string path, ComponentCatalog catalog, string? rootAncestorClass = null)
    {
        var parser = new Parser(text, path, catalog, rootAncestorClass);
        try
        {
            var document = parser.ParseFile();
            FormValidator.Validate(document, catalog); // DSG-FD-070: validation runs after the parse
            return new FormParseResult(document, [.. document.Diagnostics]);
        }
        catch (SyntaxErrorException error)
        {
            return new FormParseResult(null, [parser.SyntaxDiagnostic(error)]);
        }
    }

    /// <summary>
    /// Fragment mode (design-surface.md DSG-SUR-120): a sequence of zero or more complete objects at any indentation, as
    /// the clipboard holds them. Lines whose first non-blank character is <c>;</c> are skipped (DSG-SUR-111). The objects
    /// are bound to the catalog like children of a form (unknown classes: preserved, <c>F0101</c>); they have no parent
    /// and are not validated. A syntax error gives no objects and exactly one <c>F0001</c>.
    /// </summary>
    public static FormFragmentResult ParseFragment(string text, string path, ComponentCatalog catalog)
    {
        var parser = new Parser(BlankCommentLines(text), path, catalog, null);
        try
        {
            var objects = parser.ParseObjects();
            return new FormFragmentResult([.. objects], [.. parser.Diagnostics]);
        }
        catch (SyntaxErrorException error)
        {
            return new FormFragmentResult([], [parser.SyntaxDiagnostic(error)]);
        }
    }

    /// <summary>The text with every <c>;</c> line emptied (its line end kept, so positions keep their line numbers).</summary>
    private static string BlankCommentLines(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart(' ', '\t', '\uFEFF').StartsWith(';'))
            {
                lines[i] = lines[i].EndsWith('\r') ? "\r" : "";
            }
        }

        return string.Join('\n', lines);
    }

    private sealed class SyntaxErrorException(int position, string detail, string? objectName) : Exception(detail)
    {
        public int Position { get; } = position;

        public string Detail { get; } = detail;

        public string? ObjectName { get; } = objectName;
    }

    private sealed class Parser(string text, string path, ComponentCatalog catalog, string? rootAncestorClass)
    {
        private readonly List<ParseDiagnostic> _diagnostics = [];
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
        private int _pos;
        private string? _lineEnding;
        private string? _currentObject;
        private SourceLines? _lines;
        private int _depth;

        // Lines of the top-level property value being read (FormProperty.RawLines).
        private List<(int Level, string Text)> _rawLines = [];
        private int _rawLineStart;
        private int _rawLevel;

        public FormDocument ParseFile()
        {
            if (text.Length > 0 && text[0] == '﻿')
            {
                _pos = 1; // DSG-FD-020: optional BOM
            }

            SkipBlankLines();
            var root = ParseObject(parentPreserved: false, isRoot: true);
            SkipBlankLines();
            if (_pos < text.Length)
            {
                throw Error("end of file");
            }

            var document = new FormDocument(root, _lineEnding ?? "\n") { SourcePath = path, Lines = Lines };
            document.ParseDiagnostics.AddRange(_diagnostics);
            return document;
        }

        public IEnumerable<FormDiagnostic> Diagnostics => _diagnostics.Select(d => d.Diagnostic);

        /// <summary>Fragment mode: objects until the end of the text.</summary>
        public List<FormObject> ParseObjects()
        {
            if (text.Length > 0 && text[0] == '﻿')
            {
                _pos = 1;
            }

            var objects = new List<FormObject>();
            SkipBlankLines();
            while (_pos < text.Length)
            {
                objects.Add(ParseObject(parentPreserved: false, isRoot: false));
                SkipBlankLines();
            }

            return objects;
        }

        public FormDiagnostic SyntaxDiagnostic(SyntaxErrorException error)
        {
            var (line, col) = LineCol(error.Position);
            return new FormDiagnostic("F0001", DiagnosticSeverity.Error, "Syntax error: " + error.Detail, path,
                line, col, line, col, error.ObjectName, null);
        }

        /// <summary>One more nesting level of objects or collections (<see cref="MaxNesting"/>); the caller leaves it.</summary>
        private void EnterNesting(int position)
        {
            if (++_depth > MaxNesting)
            {
                throw new SyntaxErrorException(position, $"objects and collections nest more than {MaxNesting} levels deep", _currentObject);
            }
        }

        private FormObject ParseObject(bool parentPreserved, bool isRoot)
        {
            var lineStart = _pos;
            EnterNesting(lineStart);
            SkipSpaces();
            var keywordStart = _pos;
            var keyword = ReadIdentifier("object");
            var kind = KeywordKind(keyword) ?? throw ErrorAt(keywordStart, "object", Quote(keyword));
            SkipSpaces();
            var nameStart = _pos;
            var name = ReadIdentifier("identifier");
            SkipSpaces();
            Expect(':');
            SkipSpaces();
            var typeStart = _pos;
            var typeName = ReadIdentifier("identifier");
            var headerEnd = _pos;
            ExpectLineEnd();

            var info = catalog.Find(typeName);
            if (info is null && isRoot)
            {
                info = rootAncestorClass is not null
                    ? catalog.Find(rootAncestorClass)
                    : catalog.All.FirstOrDefault(c => c.Kind == ComponentKind.Form);
            }

            var parentObject = _currentObject;
            _currentObject = name;
            var obj = new FormObject(kind, name, typeName, info, default)
            {
                Catalog = catalog,
                HeaderSpan = new TextSpan(keywordStart, headerEnd - keywordStart),
            };

            // DSG-FD-026: duplicate names (F0106) are checked everywhere, as names are unique across the whole form.
            var preserved = parentPreserved;
            if (!_names.Add(name))
            {
                Report("F0106", DiagnosticSeverity.Error, $"A component named {name} already exists", nameStart, name.Length, obj, null);
                preserved = true;
            }

            if (!parentPreserved)
            {
                if (kind != FormObjectKind.Object)
                {
                    // F0107 is "info"; the shared diagnostics have no such level, so it is a Hint (lead decision 2026-11-20).
                    Report("F0107", DiagnosticSeverity.Hint, $"\"{keyword.ToLowerInvariant()}\" objects are not supported: shown read-only",
                        keywordStart, keyword.Length, obj, null);
                    preserved = true;
                }
                else if (info is null)
                {
                    Report("F0101", DiagnosticSeverity.Warning, $"Class {typeName} not found", typeStart, typeName.Length, obj, null);
                    preserved = true;
                }
            }

            obj.IsPreserved = preserved;
            while (PeekWord() is { } word && !IsKeyword(word))
            {
                obj.AddProperty(ParseProperty(obj));
            }

            while (PeekWord() is { } word && KeywordKind(word) is not null && IsKeyword(word))
            {
                obj.AddChild(ParseObject(preserved, isRoot: false));
            }

            SkipSpaces();
            var endStart = _pos;
            var end = PeekIdentifier();
            if (!string.Equals(end, "end", StringComparison.OrdinalIgnoreCase))
            {
                throw ErrorAt(endStart, "end", Found(endStart));
            }

            _pos += end.Length;
            var endTokenEnd = _pos;
            ExpectLineEnd();
            _currentObject = parentObject;
            _depth--;
            obj.SourceSpan = new TextSpan(lineStart, _pos - lineStart);
            if (preserved && !parentPreserved)
            {
                obj.PreservedText = PreservedLines(lineStart, keywordStart, endTokenEnd);
            }

            return obj;
        }

        /// <summary>The source lines of a preserved object, LF, with the header line's indentation removed from each.</summary>
        private string PreservedLines(int lineStart, int keywordStart, int end)
        {
            var indent = text[lineStart..keywordStart];
            var lines = text[lineStart..end].Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var result = new StringBuilder();
            foreach (var line in lines)
            {
                var common = 0;
                while (common < indent.Length && common < line.Length && line[common] == indent[common])
                {
                    common++;
                }

                result.Append(line, common, line.Length - common).Append('\n');
            }

            return result.ToString();
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
            var pathLength = _pos - start;
            SkipSpaces();
            Expect('=');
            SkipSpaces();

            PropertyInfo? info = null;
            EventInfo? eventInfo = null;
            if (obj.Info is { } component)
            {
                eventInfo = parts.Count == 1 ? component.FindEvent(propertyPath) : null;
                info = eventInfo is null ? catalog.FindPropertyPath(component, propertyPath) : null;
            }

            var valueStart = _pos;
            _rawLines = [];
            _rawLineStart = _pos;
            _rawLevel = 0;
            var value = ParseValue(info, 0);
            var end = _pos;
            _rawLines.Add((_rawLevel, text[_rawLineStart.._pos].Trim(' ', '\t')));
            var raw = text[valueStart..end].Replace("\r\n", "\n", StringComparison.Ordinal);
            ExpectLineEnd();

            var property = new FormProperty(propertyPath, value, info, eventInfo, raw, new TextSpan(start, end - start), [.. _rawLines])
            {
                ValueSpan = new TextSpan(valueStart, end - valueStart),
            };
            if (obj.IsPreserved || obj.Info is null)
            {
                return property;
            }

            if (info is null && eventInfo is null)
            {
                Report("F0102", DiagnosticSeverity.Warning, $"Property {propertyPath} does not exist on {obj.TypeName}",
                    start, pathLength, obj, property);
                property.IsPreserved = true;
            }
            else if (Mismatch(info, eventInfo, value) is { } detail)
            {
                Report("F0103", DiagnosticSeverity.Warning, $"Invalid value for property {propertyPath}: {detail}",
                    valueStart, end - valueStart, obj, property);
                property.IsPreserved = true;
                property.Value = new RawValue(raw);
            }

            return property;
        }

        /// <summary>The F0103 detail when the value kind does not fit the property type (form-document.md §3.1), else null.</summary>
        private string? Mismatch(PropertyInfo? info, EventInfo? eventInfo, FormValue value)
        {
            if (eventInfo is not null)
            {
                return value is IdentValue ? null : $"value kind does not fit type {eventInfo.Signature}";
            }

            var type = info!.Type;
            var kindMismatch = $"value kind does not fit type {type}";
            switch (info.Kind)
            {
                case PropertyKind.Object:
                    return kindMismatch;
                case PropertyKind.Component:
                    return value is IdentValue or QualifiedIdentValue ? null : kindMismatch;
                case PropertyKind.Collection:
                    return value is CollectionValue ? null : kindMismatch;
                case PropertyKind.Enum:
                    if (value is not IdentValue ident)
                    {
                        return kindMismatch;
                    }

                    return IsMember(catalog.FindType(type), ident.Name) ? null : $"'{ident.Name}' is not a member of {type}";
                case PropertyKind.Set:
                    if (value is not SetValue set)
                    {
                        return kindMismatch;
                    }

                    var element = catalog.FindType(type)?.ElementType is { } e ? catalog.FindType(e) : null;
                    var stranger = set.Elements.FirstOrDefault(m => !IsMember(element, m));
                    return stranger is null ? null : $"'{stranger}' is not a member of {type}";
            }

            var fits = type.ToLowerInvariant() switch
            {
                "integer" or "int64" or "tshortcut" => value is IntegerValue,
                "double" => value is FloatValue or IntegerValue,
                "boolean" => value is IdentValue { Name: var b } && (b.Equals("True", StringComparison.OrdinalIgnoreCase) || b.Equals("False", StringComparison.OrdinalIgnoreCase)),
                "char" => value is StringValue { Value.Length: 1 },
                "tcolor" or "tcursor" => value is IntegerValue or IdentValue,
                "tstrings" => value is StringListValue,
                _ => value is StringValue, // string (and unknown types, which the loader treats as string)
            };
            return fits ? null : kindMismatch;
        }

        private static bool IsMember(TypeInfo? enumType, string name) =>
            enumType is not null && enumType.Values.Contains(name, StringComparer.OrdinalIgnoreCase);

        private void Report(string code, DiagnosticSeverity severity, string message, int start, int length, FormObject owner, FormProperty? property)
        {
            var (line, col) = LineCol(start);
            var (endLine, endCol) = LineCol(start + length);
            _diagnostics.Add(new ParseDiagnostic(
                new FormDiagnostic(code, severity, message, path, line, col, endLine, endCol, owner.Name, property?.Path), owner, property));
        }

        /// <summary>DSG-FD-022: the first character decides the value kind.</summary>
        /// <param name="level">Nesting level of the current line relative to the property line (RawLines).</param>
        private FormValue ParseValue(PropertyInfo? info, int level)
        {
            var c = Peek();
            switch (c)
            {
                case '\'' or '#':
                    return new StringValue(ParseString(level));
                case '[':
                    return ParseSet();
                case '(':
                    return ParseStringList(level);
                case '{':
                    return ParseBinary(level);
                case '<':
                    return ParseCollection(level);
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

        private SetValue ParseSet()
        {
            _pos++; // '['
            SkipSpaces();
            var elements = new List<string>();
            if (Peek() == ']')
            {
                _pos++;
                return new SetValue([]);
            }

            while (true)
            {
                elements.Add(ReadIdentifier("identifier"));
                SkipSpaces();
                if (Peek() == ']')
                {
                    _pos++;
                    return new SetValue([.. elements]);
                }

                if (Peek() != ',')
                {
                    throw Error("\",\" or \"]\"");
                }

                _pos++;
                SkipSpaces();
            }
        }

        /// <summary><c>(</c> NL { string NL } <c>)</c>; <c>)</c> may also end the last item line (DSG-FD-055).</summary>
        private StringListValue ParseStringList(int level)
        {
            _pos++; // '('
            ValueLineEnd();
            var lines = new List<string>();
            while (true)
            {
                if (Peek() == ')')
                {
                    _rawLevel = level;
                    _pos++;
                    return new StringListValue([.. lines]);
                }

                _rawLevel = level + 1;
                if (Peek() is not ('\'' or '#'))
                {
                    throw Error("string or \")\"");
                }

                lines.Add(ParseString(level + 1));
                SkipSpaces();
                if (Peek() == ')')
                {
                    _pos++;
                    return new StringListValue([.. lines]);
                }

                ValueLineEnd();
            }
        }

        /// <summary><c>{</c> NL { hexLine NL } <c>}</c>.</summary>
        private BinaryValue ParseBinary(int level)
        {
            _pos++; // '{'
            ValueLineEnd();
            var bytes = new List<byte>();
            while (Peek() != '}')
            {
                _rawLevel = level + 1;
                if (!char.IsAsciiHexDigit(Peek()))
                {
                    throw Error("hexadecimal digit or \"}\"");
                }

                while (char.IsAsciiHexDigit(Peek()))
                {
                    if (!char.IsAsciiHexDigit(Peek(1)))
                    {
                        _pos++;
                        throw Error("hexadecimal digit");
                    }

                    bytes.Add(byte.Parse(text.AsSpan(_pos, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    _pos += 2;
                }

                ValueLineEnd();
            }

            _rawLevel = level;
            _pos++;
            return new BinaryValue([.. bytes]);
        }

        /// <summary><c>&lt;</c> NL { <c>item</c> NL { property } <c>end</c> NL } <c>&gt;</c>.</summary>
        private CollectionValue ParseCollection(int level)
        {
            EnterNesting(_pos);
            _pos++; // '<'
            ValueLineEnd();
            var items = new List<CollectionItem>();
            while (Peek() != '>')
            {
                _rawLevel = level + 1;
                var itemStart = _pos;
                var keyword = PeekIdentifier();
                if (!string.Equals(keyword, "item", StringComparison.OrdinalIgnoreCase))
                {
                    throw ErrorAt(itemStart, "\"item\" or \">\"", Found(itemStart));
                }

                _pos += keyword.Length;
                ValueLineEnd();
                var properties = new List<(string, FormValue, string?)>();
                while (true)
                {
                    var word = PeekIdentifier();
                    if (word.Length > 0 && string.Equals(word, "end", StringComparison.OrdinalIgnoreCase) && IsKeyword(word))
                    {
                        _rawLevel = level + 1;
                        _pos += word.Length;
                        ValueLineEnd();
                        break;
                    }

                    _rawLevel = level + 2;
                    var parts = new List<string> { ReadIdentifier("identifier or \"end\"") };
                    while (Peek() == '.')
                    {
                        _pos++;
                        parts.Add(ReadIdentifier("identifier"));
                    }

                    SkipSpaces();
                    Expect('=');
                    SkipSpaces();
                    var valueStart = _pos;
                    var value = ParseValue(null, level + 2);
                    properties.Add((string.Join('.', parts), value, text[valueStart.._pos].Replace("\r\n", "\n", StringComparison.Ordinal)));
                    ValueLineEnd();
                }

                items.Add(new CollectionItem([.. properties]));
            }

            _rawLevel = level;
            _pos++;
            _depth--;
            return new CollectionValue([.. items]);
        }

        /// <summary>
        /// Ends a line inside a multi-line value: trailing spaces, one line end, blank lines, leading spaces of the next
        /// line. Records the finished line for RawLines; the caller sets the level of the next line.
        /// </summary>
        private void ValueLineEnd()
        {
            SkipSpaces();
            if (!AtLineEnd())
            {
                throw Error("end of line");
            }

            _rawLines.Add((_rawLevel, text[_rawLineStart.._pos].Trim(' ', '\t')));
            if (_pos < text.Length)
            {
                ConsumeLineEnd();
                SkipBlankLines();
                SkipSpaces();
            }

            _rawLineStart = _pos;
        }

        /// <summary>DSG-FD-024: adjacent parts concatenate; <c>+</c> at the end of a line continues on the next.</summary>
        private string ParseString(int level)
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

                var plus = _pos++;
                SkipSpaces();
                if (!AtLineEnd())
                {
                    throw ErrorAt(plus, "end of line", Quote("+")); // a misplaced "+" is reported at the "+"
                }

                if (_pos >= text.Length)
                {
                    throw Error("string");
                }

                ValueLineEnd();
                _rawLevel = level + 1;
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

        private SourceLines Lines => _lines ??= new SourceLines(text);

        private (int Line, int Col) LineCol(int position) => Lines.LineCol(position);
    }
}
