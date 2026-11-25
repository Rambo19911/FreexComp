using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// What <c>-g</c> asks of the emitter (CMP-420…424): the container flag, whether the code is optimised (DBG-101), and how to
/// read a source file's bytes on disk for its MD5 checksum (DBG-103; null → the decoded text's UTF-8 bytes).
/// </summary>
internal sealed record DebugInfoOptions(DebugFormat Format, bool IsOptimized)
{
    public Func<string, byte[]?>? ReadFile { get; init; }
}

/// <summary>
/// The LLVM debug metadata of one module (CMP-420…423, debug-info.md §1–§6): the compile unit, files, subprograms, locations,
/// variables and the C-like type descriptions. Nodes are numbered in creation order (DBG-106): <c>!0</c> the compile unit,
/// <c>!1</c> <c>"Debug Info Version"</c>, <c>!2</c> the container flag, <c>!3</c> the unit's file, then first use. Identical
/// uniqued nodes (locations, tuples, expressions-free nodes) are written once.
/// </summary>
internal sealed class DebugInfoEmitter
{
    private const int CompileUnit = 0;

    private readonly DebugInfoOptions _options;
    private readonly List<string?> _nodes = [];
    private readonly Dictionary<string, int> _uniqued = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TypeSymbol, int> _types = [];
    private readonly Dictionary<ClassTypeSymbol, int> _classes = [];
    private readonly Dictionary<RoutineSymbol, int> _subprograms = [];
    private readonly Dictionary<RoutineSymbol, string> _linkageNames = [];
    private readonly List<int> _enums = [];
    private readonly List<int> _globals = [];
    private int? _byte;

    private readonly TargetInfo _target;

    /// <summary>Bits of a code or data address (CMP-1009).</summary>
    private int PointerBits => _target.PointerSize * 8;

    public DebugInfoEmitter(DebugInfoOptions options, TargetInfo target, string unitName, string unitPath, SourceText? unitText)
    {
        _options = options;
        _target = target;
        Sidecar = new PdiFragmentBuilder(target, unitName, unitPath, PdiSidecar.ContainerName(options.Format));
        Reserve();                                                                  // !0 the compile unit, written last
        Add("!{i32 2, !\"Debug Info Version\", i32 3}");
        Add(options.Format == DebugFormat.Dwarf ? "!{i32 7, !\"Dwarf Version\", i32 5}" : "!{i32 2, !\"CodeView\", i32 1}");
        MainPath = unitPath;
        MainFile = File(unitPath, unitText);
    }

    /// <summary>The unit's own source path.</summary>
    public string MainPath { get; }

    /// <summary>CMP-425: the unit's sidecar fragment, filled with every subprogram and variable this emitter describes (DBG-145).</summary>
    public PdiFragmentBuilder Sidecar { get; }

    /// <summary>The <c>DIFile</c> of the unit's own source.</summary>
    public int MainFile { get; }

    // ------------------------------------------------------------------ files and locations

    /// <summary>DBG-102/103: the <c>DIFile</c> of an absolute path (file name, directory without trailing separator, MD5).</summary>
    public int File(string path, SourceText? text = null)
    {
        if (_files.TryGetValue(path, out var known))
        {
            return known;
        }

        var name = Path.GetFileName(path);
        var directory = Path.GetDirectoryName(path) ?? "";
        if (directory.Length > 3)
        {
            directory = directory.TrimEnd('\\');
        }

        var bytes = _options.ReadFile?.Invoke(path) ?? (text is null ? null : Encoding.UTF8.GetBytes(text.Text));
        var checksum = bytes is null ? "" : $", checksumkind: CSK_MD5, checksum: \"{Convert.ToHexStringLower(MD5.HashData(bytes))}\"";
        var id = Add($"!DIFile(filename: {Quote(name)}, directory: {Quote(directory)}{checksum})");
        _files[path] = id;
        return id;
    }

    /// <summary>The file a token comes from (an include file's own, DBG-102).</summary>
    public static SourceText? FileOf(SyntaxToken token) => token.File ?? token.Parent?.SyntaxTree.Text;

    /// <summary>
    /// DBG-116/131: <c>!DILocation(line, column, scope)</c> of <paramref name="token"/> (1-based, UTF-16 column); a token of another
    /// file than the subprogram's is scoped by a <c>DILexicalBlockFile</c> of that file.
    /// </summary>
    public int? Location(SyntaxToken token, DebugSubprogram subprogram)
    {
        if (token.Kind == SyntaxKind.None || FileOf(token) is not { } file)
        {
            return null;
        }

        var position = file.Lines.GetLinePosition(token.Span.Start);
        var scope = subprogram.Node;
        if (!string.Equals(file.Path, subprogram.Path, StringComparison.OrdinalIgnoreCase))
        {
            scope = Unique($"!DILexicalBlockFile(scope: !{N(subprogram.Node)}, file: !{N(File(file.Path, file))}, discriminator: 0)");
        }

        return Unique($"!DILocation(line: {N(position.Line + 1)}, column: {N(position.Column + 1)}, scope: !{N(scope)})");
    }

    /// <summary>The 1-based line of <paramref name="token"/>, 0 when it has none.</summary>
    public static int LineOf(SyntaxToken token) =>
        token.Kind != SyntaxKind.None && FileOf(token) is { } file ? file.Lines.GetLinePosition(token.Span.Start).Line + 1 : 0;

    // ------------------------------------------------------------------ routines

    /// <summary>DBG-110…113: starts a <c>distinct !DISubprogram</c>; its node is written by <see cref="EndSubprogram"/>.</summary>
    /// <param name="self">The type of <c>Self</c> for a method (the sidecar's <c>self</c>), else null.</param>
    public DebugSubprogram BeginSubprogram(RoutineSymbol? routine, string name, string linkageName, SyntaxToken heading, SyntaxToken begin, SyntaxToken end,
        bool localToUnit, TypeSymbol? returnType, IEnumerable<DebugParameterType> parameters, TypeSymbol? self)
    {
        var node = Reserve();
        var parentLinkage = routine?.ContainingSymbol is RoutineSymbol enclosing && _linkageNames.TryGetValue(enclosing, out var p) ? p : null;
        if (routine is not null)
        {
            _subprograms[routine] = node;
            _linkageNames[routine] = linkageName;
        }

        var file = FileOf(heading) ?? FileOf(begin);
        var fileNode = file is null ? MainFile : File(file.Path, file);
        var scope = routine?.ContainingSymbol is RoutineSymbol parent && _subprograms.TryGetValue(parent, out var outer) ? outer : fileNode;
        ImmutableArray<string> types =
            [returnType is null ? "null" : "!" + N(Type(returnType)), .. parameters.Select(p => "!" + N(ParameterType(p)))];
        var type = Unique($"!DISubroutineType(types: !{N(Tuple(types))})");
        var flags = "DISPFlagDefinition" + (localToUnit ? " | DISPFlagLocalToUnit" : "") + (_options.IsOptimized ? " | DISPFlagOptimized" : "");
        var sidecar = Sidecar.AddRoutine(name, linkageName, file?.Path ?? MainPath, LineOf(heading), LineOf(begin), LineOf(end), self, parentLinkage);
        return new DebugSubprogram(node, file?.Path ?? "", fileNode, sidecar,
            $"distinct !DISubprogram(name: {Quote(name)}, linkageName: {Quote(linkageName)}, scope: !{N(scope)}, file: !{N(fileNode)}, "
            + $"line: {N(LineOf(heading))}, type: !{N(type)}, scopeLine: {N(LineOf(begin))}, spFlags: {flags}, unit: !{N(CompileUnit)}");
    }

    public void EndSubprogram(DebugSubprogram subprogram)
    {
        var retained = subprogram.Variables.Count == 0 ? "" : $", retainedNodes: !{N(Tuple([.. subprogram.Variables.Select(v => "!" + N(v))]))}";
        _nodes[subprogram.Node] = subprogram.Text + retained + ")";
    }

    /// <summary>DBG-136…139: a <c>DILocalVariable</c> of <paramref name="subprogram"/> (<paramref name="arg"/>: 1-based IR position).</summary>
    /// <param name="kind">The sidecar's variable kind (debug-info-sidecar.md §5): <c>param</c>, <c>varParam</c>, <c>self</c>, <c>result</c>, <c>local</c>.</param>
    /// <param name="fromLine">CMP-1184: the sidecar's line range of an inline variable (<paramref name="toLine"/> null: no upper end).</param>
    public int LocalVariable(DebugSubprogram subprogram, string name, int? arg, int line, TypeSymbol type, string? flags, string kind,
        int? fromLine = null, int? toLine = null)
    {
        var typeNode = Type(type);
        var text = $"!DILocalVariable(name: {Quote(name)}{(arg is { } a ? ", arg: " + N(a) : "")}, scope: !{N(subprogram.Node)}, file: !{N(subprogram.File)}, "
            + $"line: {N(line)}, type: !{N(typeNode)}{(flags is null ? "" : ", flags: " + flags)})";
        var node = Add(text);
        subprogram.Variables.Add(node);
        subprogram.Sidecar.AddVariable(name, kind, type, fromLine, toLine);
        return node;
    }

    /// <summary>DBG-140: a unit-level variable; returns the <c>DIGlobalVariableExpression</c> to attach to the global.</summary>
    public int GlobalVariable(VariableSymbol variable, string linkageName, bool isLocal)
    {
        var expression = Reserve();
        var location = variable.Locations.IsEmpty ? (Text.Location?)null : variable.Locations[0];
        var file = location is { } l ? File(l.Path) : MainFile;
        var line = location is { } at ? at.LineSpan.Start.Line + 1 : 0;
        var type = Type(variable.Type);
        var global = Add($"distinct !DIGlobalVariable(name: {Quote(variable.Name)}, linkageName: {Quote(linkageName)}, scope: !{N(CompileUnit)}, "
            + $"file: !{N(file)}, line: {N(line)}, type: !{N(type)}, isLocal: {(isLocal ? "true" : "false")}, isDefinition: true)");
        _nodes[expression] = $"!DIGlobalVariableExpression(var: !{N(global)}, expr: !DIExpression())";
        _globals.Add(expression);
        Sidecar.AddGlobal(variable.Name, linkageName, variable.Type);
        return expression;
    }

    // ------------------------------------------------------------------ types (CMP-423, debug-info.md §5)

    /// <summary>The debug type of a parameter as the IR passes it (DBG-113): by-address parameters as pointers.</summary>
    private int ParameterType(DebugParameterType parameter) => parameter switch
    {
        { Type: null } => Pointer(null),
        { ByAddress: true, Type: UntypedTypeSymbol } => Pointer(null),
        { ByAddress: true } => Pointer(Type(parameter.Type)),
        _ => Type(parameter.Type),
    };

    /// <summary>The layout-correct C-like description of <paramref name="type"/> (DBG-143/144).</summary>
    public int Type(TypeSymbol type)
    {
        if (_types.TryGetValue(type, out var known))
        {
            return known;
        }

        switch (type)
        {
            case EnumTypeSymbol { BaseType: EnumTypeSymbol host }:
                return _types[type] = Type(host);
            case EnumTypeSymbol enumeration:
            {
                var node = Reserve();
                _types[type] = node;
                var underlying = Basic(UnsignedName(enumeration.Size), enumeration.Size, "DW_ATE_unsigned");
                var elements = Tuple([.. enumeration.EnumMembers.Select(m =>
                    "!" + N(Unique($"!DIEnumerator(name: {Quote(m.Name)}, value: {m.Ordinal.ToString(CultureInfo.InvariantCulture)}, isUnsigned: true)")))]);
                _nodes[node] = $"!DICompositeType(tag: DW_TAG_enumeration_type, name: {Quote(enumeration.Name)}, baseType: !{N(underlying)}, "
                    + $"size: {Bits(enumeration.Size)}, elements: !{N(elements)})";
                _enums.Add(node);
                return node;
            }

            case RecordTypeSymbol record:
            {
                var node = Reserve();
                _types[type] = node;
                var members = record.Fields.Select(f => "!" + N(Member(node, f.Name, Type(f.Type), f.Type.Size, f.Offset))).ToImmutableArray();
                _nodes[node] = Structure(record.IsAnonymous ? null : record.Name, record.Size, members);
                return node;
            }

            case ClassTypeSymbol @class:
            {
                var node = Reserve();
                _types[type] = node;
                _nodes[node] = $"!DIDerivedType(tag: DW_TAG_pointer_type, baseType: !{N(ClassStructure(@class))}, size: {PointerBits})";
                return node;
            }

            case PointerTypeSymbol pointer:
            {
                var node = Reserve();
                _types[type] = node;
                var target = pointer.PointedType is { } pointed ? "!" + N(Type(pointed)) : "null";
                _nodes[node] = $"!DIDerivedType(tag: DW_TAG_pointer_type, name: {Quote(pointer.Name)}, baseType: {target}, size: {PointerBits})";
                return node;
            }

            case ArrayTypeSymbol array:
            {
                var node = Reserve();
                _types[type] = node;
                var element = Type(array.ElementType);
                var range = Unique($"!DISubrange(count: {N(array.Length)}, lowerBound: {array.Low.ToString(CultureInfo.InvariantCulture)})");
                _nodes[node] = $"!DICompositeType(tag: DW_TAG_array_type, baseType: !{N(element)}, size: {Bits(array.Size)}, elements: !{N(Tuple(["!" + N(range)]))})";
                return node;
            }

            case DynamicArrayTypeSymbol dynamic:
            {
                var node = Reserve();
                _types[type] = node;
                _nodes[node] = $"!DIDerivedType(tag: DW_TAG_pointer_type, name: {Quote(dynamic.Name)}, baseType: !{N(Type(dynamic.ElementType))}, size: {PointerBits})";
                return node;
            }

            case OpenArrayTypeSymbol open:
                return _types[type] = Pointer(open.IsArrayOfConst ? null : Type(open.ElementType));      // DBG-138: pointer to element 0
            case ProceduralTypeSymbol procedural:
            {
                var node = Reserve();
                _types[type] = node;
                var signature = procedural.Signature;
                ImmutableArray<string> types = [signature.ReturnType is { } r ? "!" + N(Type(r)) : "null",
                    .. IrEmitter.WithHighCompanions(signature.Parameters).Select(p => "!" + N(ParameterType(new DebugParameterType(p.Type, IrEmitter.IsPassedByAddress(p)))))];
                var subroutine = Unique($"!DISubroutineType(types: !{N(Tuple(types))})");
                _nodes[node] = $"!DIDerivedType(tag: DW_TAG_pointer_type, name: {Quote(procedural.Name)}, baseType: !{N(subroutine)}, size: {PointerBits})";
                return node;
            }
        }

        var simple = type switch
        {
            IntegerTypeSymbol integer => Basic(integer.Name, integer.Size, integer.IsSigned ? "DW_ATE_signed" : "DW_ATE_unsigned"),
            BooleanTypeSymbol boolean => Basic(boolean.Name, boolean.Size, "DW_ATE_boolean"),
            CharTypeSymbol { IsWide: true } c => Basic(c.Name, 2, "DW_ATE_UTF"),
            CharTypeSymbol c => Basic(c.Name, 1, "DW_ATE_unsigned_char"),
            RealTypeSymbol { RealKind: RealKind.Currency } real => Basic(real.Name, 8, "DW_ATE_signed"),
            RealTypeSymbol real => Basic(real.Name, real.Size, "DW_ATE_float"),
            SetTypeSymbol { IsSmall: true } set => Basic(set.Name, set.Size, "DW_ATE_unsigned"),
            SetTypeSymbol set => ByteArray(set.Size),
            StringTypeSymbol { Variant: StringVariant.Short } s => ShortString(s),
            StringTypeSymbol { Variant: StringVariant.Unicode } => Typedef("string", Pointer(Basic("Char", 2, "DW_ATE_UTF"))),
            StringTypeSymbol s => Typedef(s.Name, Pointer(Basic("AnsiChar", 1, "DW_ATE_unsigned_char"))),
            ClassRefTypeSymbol or InterfaceTypeSymbol or UntypedTypeSymbol => Pointer(null),
            MethodPointerTypeSymbol m => Add(Structure(m.Name, m.Size, ["!" + N(Member(null, "Code", Pointer(null), _target.PointerSize, 0)), "!" + N(Member(null, "Data", Pointer(null), _target.PointerSize, _target.PointerSize))])),
            _ => Add(Structure(type.Name, Math.Max(0, type.Size), [])),              // Text and other managed/opaque types: raw storage
        };
        return _types[type] = simple;
    }

    /// <summary>The instance layout of a class (debug-info.md §5): inheritance from the parent, or <c>$vmt</c> at 0 for a root class.</summary>
    private int ClassStructure(ClassTypeSymbol @class)
    {
        if (_classes.TryGetValue(@class, out var known))
        {
            return known;
        }

        var node = Reserve();
        _classes[@class] = node;
        var members = new List<string>();
        if (@class.Parent is { } parent)
        {
            members.Add("!" + N(Unique($"!DIDerivedType(tag: DW_TAG_inheritance, scope: !{N(node)}, baseType: !{N(ClassStructure(parent))}, offset: 0)")));
        }
        else
        {
            members.Add("!" + N(Member(node, "$vmt", Pointer(null), _target.PointerSize, 0)));
        }

        members.AddRange(@class.Fields.Select(f => "!" + N(Member(node, f.Name, Type(f.Type), f.Type.Size, f.Offset))));
        _nodes[node] = Structure(@class.Name, @class.InstanceSize, [.. members]);
        return node;
    }

    private int ShortString(StringTypeSymbol type)
    {
        var node = Reserve();
        var length = Member(node, "length", Byte(), 1, 0);
        var chars = Unique($"!DICompositeType(tag: DW_TAG_array_type, baseType: !{N(Basic("AnsiChar", 1, "DW_ATE_unsigned_char"))}, size: {Bits(type.MaxLength)}, "
            + $"elements: !{N(Tuple(["!" + N(Unique($"!DISubrange(count: {N(type.MaxLength)}, lowerBound: 1)"))]))})");
        var member = Member(node, "chars", chars, type.MaxLength, 1);
        _nodes[node] = Structure(type.Name, type.Size, ["!" + N(length), "!" + N(member)]);
        return node;
    }

    private int ByteArray(long size) =>
        Unique($"!DICompositeType(tag: DW_TAG_array_type, baseType: !{N(Byte())}, size: {Bits(size)}, "
            + $"elements: !{N(Tuple(["!" + N(Unique($"!DISubrange(count: {N(size)}, lowerBound: 0)"))]))})");

    private int Byte() => _byte ??= Basic("Byte", 1, "DW_ATE_unsigned");

    private int Basic(string name, long size, string encoding) =>
        Unique($"!DIBasicType(name: {Quote(name)}, size: {Bits(size)}, encoding: {encoding})");

    private int Pointer(int? target) =>
        Unique($"!DIDerivedType(tag: DW_TAG_pointer_type, baseType: {(target is { } t ? "!" + N(t) : "null")}, size: {PointerBits})");

    private int Typedef(string name, int target) => Unique($"!DIDerivedType(tag: DW_TAG_typedef, name: {Quote(name)}, baseType: !{N(target)})");

    private int Member(int? scope, string name, int type, long size, long offset) =>
        Unique($"!DIDerivedType(tag: DW_TAG_member, name: {Quote(name)}{(scope is { } s ? ", scope: !" + N(s) : "")}, baseType: !{N(type)}, "
            + $"size: {Bits(size)}, offset: {Bits(offset)})");

    private string Structure(string? name, long size, ImmutableArray<string> members) =>
        $"distinct !DICompositeType(tag: DW_TAG_structure_type{(name is null ? "" : ", name: " + Quote(name))}, size: {Bits(size)}, elements: !{N(Tuple(members))})";

    private static string UnsignedName(long size) => size switch { 1 => "Byte", 2 => "Word", _ => "Cardinal" };

    // ------------------------------------------------------------------ module

    /// <summary>Writes <c>!llvm.dbg.cu</c>, <c>!llvm.module.flags</c> and every node (CMP-420).</summary>
    /// <param name="asyncEh">CMP-398: the module has try regions, so its flags include <c>eh-asynch</c>.</param>
    public void Finish(IIrWriter writer, bool asyncEh)
    {
        var flags = asyncEh ? $", !{N(Add(IrEmitter.AsyncEhFlag))}" : "";
        var enums = _enums.Count == 0 ? "" : $", enums: !{N(Tuple([.. _enums.Select(e => "!" + N(e))]))}";
        var globals = _globals.Count == 0 ? "" : $", globals: !{N(Tuple([.. _globals.Select(g => "!" + N(g))]))}";
        _nodes[CompileUnit] = $"distinct !DICompileUnit(language: DW_LANG_Pascal83, file: !{N(MainFile)}, producer: {Quote("FreexComp opc " + CompilerInfo.Version)}, "
            + $"isOptimized: {(_options.IsOptimized ? "true" : "false")}, runtimeVersion: 0, emissionKind: FullDebug{enums}{globals}, "
            + "splitDebugInlining: false, nameTableKind: None)";
        writer.AppendMetadata("!llvm.dbg.cu = !{!0}");
        writer.AppendMetadata($"!llvm.module.flags = !{{!1, !2{flags}}}");
        for (var i = 0; i < _nodes.Count; i++)
        {
            writer.AppendMetadata($"!{N(i)} = {_nodes[i] ?? throw new InvalidOperationException($"Metadata node !{i} was never written.")}");
        }
    }

    // ------------------------------------------------------------------ nodes

    private int Reserve()
    {
        _nodes.Add(null);
        return _nodes.Count - 1;
    }

    private int Add(string text)
    {
        _nodes.Add(text);
        return _nodes.Count - 1;
    }

    private int Unique(string text)
    {
        if (!_uniqued.TryGetValue(text, out var node))
        {
            node = Add(text);
            _uniqued[text] = node;
        }

        return node;
    }

    private int Tuple(ImmutableArray<string> elements) => Unique("!{" + string.Join(", ", elements) + "}");

    private static string Bits(long bytes) => (bytes * 8).ToString(CultureInfo.InvariantCulture);

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Quote(string text) => "\"" + LlvmTextWriter.Escape(text) + "\"";
}

/// <summary>A parameter's type as the IR passes it (DBG-113); a null type is an untyped pointer (static link).</summary>
internal sealed record DebugParameterType(TypeSymbol? Type, bool ByAddress);

/// <summary>A subprogram being emitted: its node, file and the variables for <c>retainedNodes</c>.</summary>
internal sealed class DebugSubprogram(int node, string path, int file, PdiRoutine sidecar, string text)
{
    /// <summary>The routine's entry in the sidecar fragment (CMP-425).</summary>
    public PdiRoutine Sidecar { get; } = sidecar;

    public int Node { get; } = node;

    /// <summary>The path of the subprogram's file (locations in other files get a lexical block file).</summary>
    public string Path { get; } = path;

    public int File { get; } = file;

    public string Text { get; } = text;

    public List<int> Variables { get; } = [];
}
