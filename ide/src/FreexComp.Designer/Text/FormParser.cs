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
    /// binds to its own class when the catalog has it, else to this class, else to the first concrete (not abstract)
    /// catalog class of kind <c>form</c> (D-133); a root bound by either fallback never raises <c>F0101</c>.
    /// </param>
    /// <param name="frames">
    /// The frames <c>inline</c> objects resolve against (DSG-FD-102, 106): without a catalog every <c>inline</c> object
    /// is <c>F0107</c> and preserved; with one, a known frame gives a resolved instance and an unknown one <c>F0101</c>.
    /// The document follows later changes of the catalog (DSG-FD-111).
    /// </param>
    /// <param name="ancestors">
    /// The designed classes (forms and frames) a document may inherit from (DSG-FD-122): an <c>inherited</c> root whose
    /// ancestor (<paramref name="rootAncestorClass"/>) is listed is a resolved descendant (DSG-FD-125), one whose ancestor
    /// is not listed <c>F0112</c>; an <c>object</c> root whose ancestor is listed is <c>F0113</c>. Without a catalog an
    /// <c>inherited</c> root is <c>F0107</c> and the whole form read-only.
    /// </param>
    public static FormParseResult Parse(string text, string path, ComponentCatalog catalog, string? rootAncestorClass = null,
        IAncestorCatalog? frames = null, IAncestorCatalog? ancestors = null)
    {
        var parser = new Parser(text, path, catalog, rootAncestorClass, frames, ancestors: ancestors);
        try
        {
            var document = parser.ParseFile();
            document.AttachFrames(frames, catalog, ancestors, rootAncestorClass);
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
    /// and are not validated. A syntax error gives no objects and exactly one <c>F0001</c>. With <paramref name="frames"/>
    /// <c>inline</c> objects resolve as in <see cref="Parse"/>; <paramref name="rootClass"/> (the class of the document
    /// the fragment is meant for) takes part in the <c>F0111</c> check.
    /// </summary>
    public static FormFragmentResult ParseFragment(string text, string path, ComponentCatalog catalog, IAncestorCatalog? frames = null,
        string? rootClass = null)
    {
        var parser = new Parser(BlankCommentLines(text), path, catalog, null, frames, rootClass);
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

    /// <summary>
    /// A whole document written by the writer, parsed again against the current catalogs (DSG-FD-134): not attached to the
    /// catalogs and not validated (the caller takes over its tree); null on a syntax error.
    /// </summary>
    internal static FormDocument? ParseAgain(string text, string path, ComponentCatalog catalog, string? rootAncestorClass,
        IAncestorCatalog? frames, IAncestorCatalog? ancestors)
    {
        var parser = new Parser(text, path, catalog, rootAncestorClass, frames, ancestors: ancestors);
        try
        {
            return parser.ParseFile();
        }
        catch (SyntaxErrorException)
        {
            return null;
        }
    }

    /// <summary>
    /// One instance written by the writer at level 0, parsed again against the current frames (DSG-FD-111), with the
    /// parser's diagnostics (positions in <paramref name="text"/>); null object on a syntax error.
    /// </summary>
    internal static (FormObject? Object, List<ParseDiagnostic> Diagnostics) ParseInstance(string text, string path, ComponentCatalog catalog,
        IAncestorCatalog frames, string rootClass)
    {
        var parser = new Parser(text, path, catalog, null, frames, rootClass);
        try
        {
            var objects = parser.ParseObjects();
            return (objects.Count == 1 ? objects[0] : null, parser.ParseDiagnostics);
        }
        catch (SyntaxErrorException)
        {
            return (null, []);
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

    private sealed class Parser(string text, string path, ComponentCatalog catalog, string? rootAncestorClass,
        IAncestorCatalog? frames = null, string? rootClass = null, IAncestorCatalog? ancestors = null)
    {
        private readonly List<ParseDiagnostic> _diagnostics = [];
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<FormObject> _fileScope = [];

        // DSG-FD-125, 126: the inherited objects of a resolved descendant's file scope by name, and its ancestor.
        private readonly Dictionary<string, FormObject> _inheritedScope = new(StringComparer.OrdinalIgnoreCase);
        private AncestorDefinition? _ancestor;
        private string? _rootClass = rootClass;
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

            var document = new FormDocument(root, _lineEnding ?? "\n", _fileScope) { SourcePath = path, Lines = Lines };
            document.ParseDiagnostics.AddRange(_diagnostics);
            return document;
        }

        public IEnumerable<FormDiagnostic> Diagnostics => _diagnostics.Select(d => d.Diagnostic);

        public List<ParseDiagnostic> ParseDiagnostics => _diagnostics;

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

        /// <summary>An object header: keyword, name and type with their positions (the line end is not consumed).</summary>
        private readonly record struct Header(int LineStart, int KeywordStart, string Keyword, FormObjectKind Kind, int NameStart, string Name,
            int TypeStart, string TypeName, int End);

        private Header ReadHeader()
        {
            var lineStart = _pos;
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
            return new Header(lineStart, keywordStart, keyword, kind, nameStart, name, typeStart, typeName, _pos);
        }

        private Header PeekHeader()
        {
            var save = _pos;
            var header = ReadHeader();
            _pos = save;
            return header;
        }

        /// <param name="parentPreserved">Inside a preserved object: nothing more is reported.</param>
        /// <param name="forcePreserved">A block the caller reports (<c>F0109</c>, <c>F0110</c>): preserved, nothing reported here.</param>
        /// <param name="registerNames">The object and its descendants belong to the file scope (DSG-FD-108).</param>
        private FormObject ParseObject(bool parentPreserved, bool isRoot, bool forcePreserved = false, bool registerNames = true)
        {
            EnterNesting(_pos);
            var header = ReadHeader();
            var (lineStart, keywordStart, keyword, kind, nameStart, name, typeStart, typeName, headerEnd) = header;
            ExpectLineEnd();
            if (isRoot)
            {
                _rootClass ??= typeName;
            }

            var info = catalog.Find(typeName);
            if (info is null && isRoot)
            {
                info = rootAncestorClass is not null
                    ? catalog.Find(rootAncestorClass)
                    : catalog.All.FirstOrDefault(c => c.Kind == ComponentKind.Form && !c.IsAbstract);
            }

            // DSG-FD-106, 107: an `inline` object of a known frame is a resolved instance; an unknown frame is F0101, a frame
            // that contains itself (or the document's root class) F0111.
            var asInstance = kind == FormObjectKind.Inline && frames is not null && !isRoot;
            AncestorDefinition? frame = null;
            string? repeated = null;
            if (asInstance && !parentPreserved && !forcePreserved)
            {
                var definition = frames!.Find(typeName);
                info = definition?.Document.Root.Info;
                if (definition is not null && info is not null)
                {
                    repeated = InheritedInstances.RepeatedClass(frames, definition.ClassName, _rootClass);
                    frame = repeated is null ? definition : null;
                }
            }

            // DSG-FD-122, 125, 127: the root keyword must fit the ancestor catalog.
            AncestorDefinition? ancestor = null;
            string? rootProblem = null;
            if (isRoot && ancestors is not null)
            {
                var found = rootAncestorClass is null ? null : ancestors.Find(rootAncestorClass);
                if (kind == FormObjectKind.Inherited)
                {
                    ancestor = IsAncestor(found, typeName) ? found : null;
                    rootProblem = ancestor is null
                        ? $"Class {typeName} has no ancestor form: its form file cannot start with \"inherited\": shown read-only"
                        : null;
                }
                else if (kind == FormObjectKind.Object && found is not null)
                {
                    rootProblem = $"Class {typeName} inherits the form of {found.ClassName}: its form file must start with \"inherited\": shown read-only";
                }
            }

            var parentObject = _currentObject;
            _currentObject = name;
            var obj = new FormObject(kind, name, typeName, info, default)
            {
                Catalog = catalog,
                HeaderSpan = new TextSpan(keywordStart, headerEnd - keywordStart),
            };

            // DSG-FD-026, 108: duplicate names (F0106) are checked over the file scope. An `inherited` object nested in a
            // component a descendant creates names no component (F0107, RTL-1133).
            var preserved = parentPreserved || forcePreserved;
            if (registerNames && !(kind == FormObjectKind.Inherited && _ancestor is not null))
            {
                if (!_names.Add(name))
                {
                    Report("F0106", DiagnosticSeverity.Error, $"A component named {name} already exists", nameStart, name.Length, obj, null);
                    preserved = true;
                }
                else
                {
                    _fileScope.Add(obj);
                }
            }

            if (!parentPreserved && !forcePreserved)
            {
                if (rootProblem is not null)
                {
                    Report(kind == FormObjectKind.Inherited ? "F0112" : "F0113", DiagnosticSeverity.Warning, rootProblem, keywordStart, keyword.Length, obj, null);
                    preserved = true;
                }
                else if (ancestor is not null)
                {
                    // A resolved descendant: its root binds to the ancestor's root (below).
                }
                else if (asInstance && frame is null)
                {
                    if (repeated is not null)
                    {
                        Report("F0111", DiagnosticSeverity.Warning, $"Frame {repeated} contains itself: shown read-only", keywordStart, keyword.Length, obj, null);
                    }
                    else
                    {
                        Report("F0101", DiagnosticSeverity.Warning, $"Class {typeName} not found", typeStart, typeName.Length, obj, null);
                    }

                    preserved = true;
                }
                else if (kind != FormObjectKind.Object && !asInstance)
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
            if (frame is not null && !preserved)
            {
                InheritedInstances.Merge(obj, frame);
            }

            if (ancestor is not null && !preserved)
            {
                InheritedInstances.MergeAncestor(obj, ancestor);
                _ancestor = ancestor;
                RegisterInherited(obj);
            }

            while (PeekWord() is { } word && !IsKeyword(word))
            {
                obj.AddProperty(ParseProperty(obj));
            }

            if (obj.IsFrameInstance)
            {
                ParseScopeBody(obj, obj, []);
            }
            else if (obj.IsInheritedRoot)
            {
                ParseOpenBody(obj, []);
            }
            else
            {
                // DSG-FD-108, form-format.md §2.1: the blocks inside an `inline` object (resolved or not) name components of
                // the frame; their names belong to no scope of the file.
                while (PeekWord() is { } word && KeywordKind(word) is not null && IsKeyword(word))
                {
                    obj.AddChild(ParseObject(preserved, isRoot: false, registerNames: registerNames && (isRoot || kind != FormObjectKind.Inline)));
                }
            }

            ParseEnd();
            _currentObject = parentObject;
            _depth--;
            obj.SourceSpan = new TextSpan(lineStart, _pos - lineStart);
            if (preserved && !parentPreserved)
            {
                obj.PreservedText = PreservedLines(lineStart, keywordStart, _endTokenEnd);
            }

            return obj;
        }

        private int _endTokenEnd;

        /// <summary>The <c>end</c> of an object and its line end.</summary>
        private void ParseEnd()
        {
            SkipSpaces();
            var endStart = _pos;
            var end = PeekIdentifier();
            if (!string.Equals(end, "end", StringComparison.OrdinalIgnoreCase))
            {
                throw ErrorAt(endStart, "end", Found(endStart));
            }

            _pos += end.Length;
            _endTokenEnd = _pos;
            ExpectLineEnd();
        }

        /// <summary>
        /// The nested blocks of an instance or an override block (DSG-FD-106, 107): each <c>inherited</c> block is matched
        /// by name in <paramref name="scope"/> and its lines become the overrides of that inherited object; a block that
        /// matches nothing (or another class) is <c>F0109</c>, an <c>object</c> or <c>inline</c> block <c>F0110</c>; both are
        /// kept in <paramref name="container"/> as preserved blocks at their position.
        /// </summary>
        private void ParseScopeBody(FormObject container, FormObject scope, HashSet<FormObject> matched)
        {
            string? after = null;
            while (PeekWord() is { } word && KeywordKind(word) is { } kind && IsKeyword(word))
            {
                var header = PeekHeader();
                var target = kind == FormObjectKind.Inherited ? scope.FindInScope(header.Name) : null;
                if (target is not null && string.Equals(target.TypeName, header.TypeName, StringComparison.OrdinalIgnoreCase) && matched.Add(target))
                {
                    ParseOverrideBlock(target, target.IsFrameInstance ? target : scope, target.IsFrameInstance ? [] : matched);
                    after = SlotAfter(container, target) ?? after;
                    continue;
                }

                var block = ParseObject(parentPreserved: false, isRoot: false, forcePreserved: true, registerNames: false);
                if (kind != FormObjectKind.Inherited)
                {
                    Report("F0110", DiagnosticSeverity.Warning, $"Components cannot be added to frame instance {scope.Name}: shown read-only",
                        header.KeywordStart, header.Keyword.Length, block, null);
                }
                else if (target is not null && matched.Contains(target))
                {
                    Report("F0106", DiagnosticSeverity.Error, $"A component named {header.Name} already exists", header.NameStart, header.Name.Length, block, null);
                }
                else
                {
                    Report("F0109", DiagnosticSeverity.Warning, $"Component {header.Name}: {header.TypeName} is not part of {scope.TypeName}: shown read-only",
                        header.NameStart, header.Name.Length, block, null);
                }

                container.AddPreservedBlock(block, after);
            }
        }

        /// <summary>
        /// DSG-FD-122: <paramref name="definition"/> can be the ancestor of a document of <paramref name="className"/> —
        /// listed, its root resolved as a form or a frame, and the class not repeated by the chain.
        /// </summary>
        private static bool IsAncestor([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] AncestorDefinition? definition, string className)
        {
            if (definition?.Document.Root is not { IsPreserved: false, Info.Kind: ComponentKind.Form or ComponentKind.Container })
            {
                return false;
            }

            var seen = new HashSet<FormDocument>(ReferenceEqualityComparer.Instance);
            for (var d = definition; d is not null && seen.Add(d.Document); d = d.Document.Ancestor)
            {
                if (string.Equals(d.ClassName, className, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(d.Document.Root.TypeName, className, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// DSG-FD-126: the inherited objects of a descendant that are not inside a frame instance join the file scope, in the
        /// definition's pre-order (an instance itself joins; its components belong to its own scope).
        /// </summary>
        private void RegisterInherited(FormObject container)
        {
            foreach (var child in container.Children)
            {
                if (_names.Add(child.Name))
                {
                    _fileScope.Add(child);
                    if (!child.IsPreserved)
                    {
                        _inheritedScope.Add(child.Name, child);
                    }
                }

                if (!child.IsFrameInstance)
                {
                    RegisterInherited(child);
                }
            }
        }

        /// <summary>
        /// DSG-FD-125: the open-scope body of a descendant's root or of one of its override blocks. An <c>inherited</c> block
        /// is matched by name among the inherited objects of the file scope, anywhere in the tree; its lines become that
        /// object's overrides. A block that matches nothing (or another class) is <c>F0109</c>, a second block for the same
        /// object <c>F0106</c>; both are kept in <paramref name="container"/> as preserved blocks at their position. An
        /// <c>object</c> or <c>inline</c> block is a new component, appended after the inherited children.
        /// </summary>
        private void ParseOpenBody(FormObject container, HashSet<FormObject> matched)
        {
            string? after = null;
            while (PeekWord() is { } word && KeywordKind(word) is { } kind && IsKeyword(word))
            {
                if (kind != FormObjectKind.Inherited)
                {
                    container.AddChild(ParseObject(parentPreserved: false, isRoot: false));
                    continue;
                }

                var header = PeekHeader();
                var target = _inheritedScope.GetValueOrDefault(header.Name);
                if (target is not null && string.Equals(target.TypeName, header.TypeName, StringComparison.OrdinalIgnoreCase) && matched.Add(target))
                {
                    ParseOverrideBlock(target, target, matched, open: true);
                    after = SlotAfter(container, target) ?? after;
                    continue;
                }

                var block = ParseObject(parentPreserved: false, isRoot: false, forcePreserved: true, registerNames: false);
                if (target is not null && matched.Contains(target))
                {
                    Report("F0106", DiagnosticSeverity.Error, $"A component named {header.Name} already exists", header.NameStart, header.Name.Length, block, null);
                }
                else
                {
                    Report("F0109", DiagnosticSeverity.Warning,
                        $"Component {header.Name}: {header.TypeName} is not part of {_ancestor!.ClassName}: shown read-only",
                        header.NameStart, header.Name.Length, block, null);
                }

                container.AddPreservedBlock(block, after);
            }
        }

        /// <summary>The name of the child of <paramref name="container"/> that holds <paramref name="target"/>, or null.</summary>
        private static string? SlotAfter(FormObject container, FormObject target)
        {
            for (var child = target; child.Parent is not null; child = child.Parent)
            {
                if (ReferenceEquals(child.Parent, container))
                {
                    return child.Name;
                }
            }

            return null;
        }

        /// <summary>
        /// An <c>inherited</c> block matched to <paramref name="target"/>: its lines, then its nested blocks — by the open-scope
        /// body when <paramref name="open"/> and the target is not a frame instance, else by the closed scope.
        /// </summary>
        private void ParseOverrideBlock(FormObject target, FormObject scope, HashSet<FormObject> matched, bool open = false)
        {
            EnterNesting(_pos);
            var header = ReadHeader();
            ExpectLineEnd();
            var parentObject = _currentObject;
            _currentObject = header.Name;
            target.HeaderSpan = new TextSpan(header.KeywordStart, header.End - header.KeywordStart);
            while (PeekWord() is { } word && !IsKeyword(word))
            {
                target.AddProperty(ParseProperty(target));
            }

            if (open && !target.IsFrameInstance)
            {
                ParseOpenBody(target, matched);
            }
            else
            {
                ParseScopeBody(target, scope, open ? [] : matched);
            }

            ParseEnd();
            _currentObject = parentObject;
            _depth--;
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
