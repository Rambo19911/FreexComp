using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// The only class that knows LLVM's textual syntax (architecture.md §4, CMP-125): formats the module of codegen.md
/// §1–§4 for the pinned LLVM (opaque pointers, no datalayout line). Output uses LF line endings and ends with a newline;
/// each section (struct types, globals, declarations, every function, attribute groups) is preceded by one blank line.
/// </summary>
internal sealed class LlvmTextWriter : IIrWriter
{
    private const string TargetTriple = "x86_64-pc-windows-msvc";

    private readonly List<string> _moduleAsm = [];
    private readonly List<string> _structs = [];
    private readonly HashSet<string> _declaredStructs = new(StringComparer.Ordinal);
    private readonly List<string> _globals = [];
    private readonly SortedDictionary<string, string> _declarations = new(StringComparer.Ordinal);
    private readonly List<string> _functions = [];
    private readonly SortedDictionary<int, string> _attributeGroups = [];
    private string? _header;
    private string? _text;

    private const string PersonalityName = "__C_specific_handler";

    private StringBuilder? _function;
    private int _headerEnd;
    private bool _usesEh;
    private bool _inBlock;
    private bool _blockTerminated;

    public void BeginModule(string moduleName, string sourceFileName)
    {
        if (_header != null)
        {
            throw new InvalidOperationException("BeginModule called twice.");
        }

        _header = $"; ModuleID = '{moduleName}'\nsource_filename = \"{Escape(sourceFileName)}\"\ntarget triple = \"{TargetTriple}\"\n";
    }

    public NamedStructIrType DeclareStruct(LinkName name, StructIrType body)
    {
        RequireModuleLevel();
        _declaredStructs.Add("%" + Name(name));
        _structs.Add($"%{Name(name)} = type {Type(body)}");
        return new NamedStructIrType(name);
    }

    public IrGlobal DefineGlobal(IrGlobalDefinition global)
    {
        RequireModuleLevel();
        var line = new StringBuilder("@").Append(Name(global.Name)).Append(" = ").Append(Linkage(global.Linkage));
        if (global.UnnamedAddr)
        {
            line.Append("unnamed_addr ");
        }

        line.Append(global.IsConstant ? "constant " : "global ").Append(Typed(global.Initializer)).Append(", align ").Append(Int(global.Alignment));
        _globals.Add(line.ToString());
        return new IrGlobal(global.Name);
    }

    public IrGlobal DeclareGlobal(LinkName name, IrType type, int alignment)
    {
        RequireModuleLevel();
        _globals.Add($"@{Name(name)} = external global {Type(type)}, align {Int(alignment)}");
        return new IrGlobal(name);
    }

    public IrGlobal DeclareFunction(IrFunctionDeclaration declaration)
    {
        RequireModuleLevel();
        var line = $"declare {(declaration.DllImport ? "dllimport " : "")}{Type(declaration.ReturnType)} @{Name(declaration.Name)}({string.Join(", ", declaration.ParameterTypes.Select(Type))})"
            + Group(declaration.AttributeGroup);
        if (_declarations.TryGetValue(declaration.Name.Text, out var existing) && existing != line)
        {
            throw new InvalidOperationException($"Conflicting declarations of '{declaration.Name.Text}'.");
        }

        _declarations[declaration.Name.Text] = line;
        return new IrGlobal(declaration.Name);
    }

    public void DefineAttributeGroup(int id, ImmutableArray<IrAttribute> attributes)
    {
        RequireModuleLevel();
        var text = $"attributes #{Int(id)} = {{ {string.Join(" ", attributes.Select(Attribute))} }}";
        if (!_attributeGroups.TryAdd(id, text))
        {
            throw new InvalidOperationException($"Attribute group #{id} defined twice.");
        }
    }

    public void AppendModuleAsm(string line)
    {
        RequireModuleLevel();
        _moduleAsm.Add($"module asm \"{Escape(line)}\"");
    }

    public ImmutableArray<IrLocal> BeginFunction(IrFunctionDefinition function)
    {
        RequireModuleLevel();
        var parameters = function.Parameters.Select(p => new IrLocal(p.Type, p.Name)).ToImmutableArray();
        _function = new StringBuilder()
            .Append("define ").Append(Linkage(function.Linkage)).Append(Type(function.ReturnType)).Append(" @").Append(Name(function.Name))
            .Append('(').AppendJoin(", ", parameters.Select(Typed)).Append(')').Append(Group(function.AttributeGroup));
        _headerEnd = _function.Length;
        _function.Append(" {\n");
        _inBlock = false;
        _blockTerminated = false;
        _usesEh = false;
        return parameters;
    }

    public void AppendBlock(IrBlock block)
    {
        var function = RequireFunction();
        if (_inBlock && !_blockTerminated)
        {
            throw new InvalidOperationException($"Block before '{block.Name}' has no terminator.");
        }

        function.Append(LocalName(block.Name)).Append(":\n");
        _inBlock = true;
        _blockTerminated = false;
    }

    public IrLocal Alloca(string name, IrType type, int alignment) =>
        Define(new IrLocal(IrType.Ptr, name), $"alloca {Type(type)}, align {Int(alignment)}");

    public IrLocal Load(string name, IrType type, IrValue address, int alignment)
    {
        RequirePointer(address);
        return Define(new IrLocal(type, name), $"load {Type(type)}, {Typed(address)}, align {Int(alignment)}");
    }

    public void Store(IrValue value, IrValue address, int alignment)
    {
        RequirePointer(address);
        Instruction($"store {Typed(value)}, {Typed(address)}, align {Int(alignment)}");
    }

    public IrLocal Binary(string name, IrBinaryOperator op, IrValue left, IrValue right)
    {
        RequireSameType(left, right);
        return Define(new IrLocal(left.Type, name), $"{BinaryMnemonic(op)} {Typed(left)}, {Value(right)}");
    }

    public IrLocal FNeg(string name, IrValue operand) => Define(new IrLocal(operand.Type, name), $"fneg {Typed(operand)}");

    public IrLocal ICmp(string name, IrIntPredicate predicate, IrValue left, IrValue right)
    {
        RequireSameType(left, right);
        return Define(new IrLocal(IrType.I1, name), $"icmp {predicate.ToString().ToLowerInvariant()} {Typed(left)}, {Value(right)}");
    }

    public IrLocal FCmp(string name, IrFloatPredicate predicate, IrValue left, IrValue right)
    {
        RequireSameType(left, right);
        return Define(new IrLocal(IrType.I1, name), $"fcmp {predicate.ToString().ToLowerInvariant()} {Typed(left)}, {Value(right)}");
    }

    public IrLocal Cast(string name, IrCastOperator op, IrValue value, IrType targetType) =>
        Define(new IrLocal(targetType, name), $"{op.ToString().ToLowerInvariant()} {Typed(value)} to {Type(targetType)}");

    public IrLocal Select(string name, IrValue condition, IrValue whenTrue, IrValue whenFalse)
    {
        RequireSameType(whenTrue, whenFalse);
        return Define(new IrLocal(whenTrue.Type, name), $"select {Typed(condition)}, {Typed(whenTrue)}, {Typed(whenFalse)}");
    }

    public IrLocal Phi(string name, IrType type, ImmutableArray<(IrValue Value, IrBlock Block)> incoming)
    {
        if (incoming.IsEmpty || incoming.Any(i => i.Value.Type != type))
        {
            throw new InvalidOperationException("A phi needs incoming values of its own type.");
        }

        return Define(new IrLocal(type, name),
            $"phi {Type(type)} {string.Join(", ", incoming.Select(i => $"[ {Value(i.Value)}, %{LocalName(i.Block.Name)} ]"))}");
    }

    public IrLocal GetElementPtr(string name, IrType sourceType, IrValue address, ImmutableArray<IrValue> indices, bool inBounds = true)
    {
        RequirePointer(address);
        var text = new StringBuilder("getelementptr ").Append(inBounds ? "inbounds " : "").Append(Type(sourceType)).Append(", ").Append(Typed(address));
        foreach (var index in indices)
        {
            text.Append(", ").Append(Typed(index));
        }

        return Define(new IrLocal(IrType.Ptr, name), text.ToString());
    }

    public IrLocal ExtractValue(string name, IrValue aggregate, int index)
    {
        var elementType = aggregate.Type switch
        {
            StructIrType s when index >= 0 && index < s.Fields.Length => s.Fields[index],
            ArrayIrType a when index >= 0 && index < a.Length => a.Element,
            _ => throw new InvalidOperationException($"extractvalue index {index} is not valid for {Type(aggregate.Type)}."),
        };
        return Define(new IrLocal(elementType, name), $"extractvalue {Typed(aggregate)}, {Int(index)}");
    }

    public IrLocal AtomicRmw(string name, IrAtomicOperator op, IrValue address, IrValue value, int alignment)
    {
        RequirePointer(address);
        return Define(new IrLocal(value.Type, name),
            $"atomicrmw {op.ToString().ToLowerInvariant()} {Typed(address)}, {Typed(value)} seq_cst, align {Int(alignment)}");
    }

    public IrLocal CmpXchg(string name, IrValue address, IrValue comparand, IrValue newValue, int alignment)
    {
        RequirePointer(address);
        RequireSameType(comparand, newValue);
        return Define(new IrLocal(new StructIrType([comparand.Type, IrType.I1]), name),
            $"cmpxchg {Typed(address)}, {Typed(comparand)}, {Typed(newValue)} seq_cst seq_cst, align {Int(alignment)}");
    }

    public IrLocal? Call(string? name, IrType returnType, IrValue callee, ImmutableArray<IrValue> arguments, int? attributeGroup = null, IrValue? funclet = null)
    {
        RequirePointer(callee);
        var text = $"call {Type(returnType)} {Value(callee)}({string.Join(", ", arguments.Select(Typed))}){Group(attributeGroup)}{Bundle(funclet)}";
        return CallLike(name, returnType, text, terminates: false);
    }

    public IrLocal? Invoke(string? name, IrType returnType, IrValue callee, ImmutableArray<IrValue> arguments, IrBlock normal, IrBlock unwind,
        int? attributeGroup = null, IrValue? funclet = null)
    {
        RequirePointer(callee);
        _usesEh = true;
        var text = $"invoke {Type(returnType)} {Value(callee)}({string.Join(", ", arguments.Select(Typed))}){Group(attributeGroup)}{Bundle(funclet)}\n"
            + $"          to label %{LocalName(normal.Name)} unwind label %{LocalName(unwind.Name)}";
        return CallLike(name, returnType, text, terminates: true);
    }

    private IrLocal? CallLike(string? name, IrType returnType, string text, bool terminates)
    {
        IrLocal? result = null;
        if (name is null)
        {
            Instruction(text);
        }
        else if (returnType is VoidIrType)
        {
            throw new InvalidOperationException("A void call has no result to name.");
        }
        else
        {
            result = Define(new IrLocal(returnType, name), text);
        }

        _blockTerminated |= terminates;
        return result;
    }

    private string Bundle(IrValue? funclet) => funclet is null ? "" : $" [ \"funclet\"({Typed(funclet)}) ]";

    public IrLocal CatchSwitch(string name, IrBlock handler, IrBlock? unwind)
    {
        _usesEh = true;
        var result = Define(new IrLocal(IrType.Token, name), $"catchswitch within none [label %{LocalName(handler.Name)}] {Unwind(unwind)}");
        _blockTerminated = true;
        return result;
    }

    public IrLocal CatchPad(string name, IrValue catchSwitch, ImmutableArray<IrValue> arguments) =>
        Define(new IrLocal(IrType.Token, name), $"catchpad within {Value(catchSwitch)} [{string.Join(", ", arguments.Select(Typed))}]");

    public void CatchRet(IrValue pad, IrBlock target) => Terminator($"catchret from {Value(pad)} to label %{LocalName(target.Name)}");

    public IrLocal CleanupPad(string name)
    {
        _usesEh = true;
        return Define(new IrLocal(IrType.Token, name), "cleanuppad within none []");
    }

    public void CleanupRet(IrValue pad, IrBlock? unwind) => Terminator($"cleanupret from {Value(pad)} {Unwind(unwind)}");

    private static string Unwind(IrBlock? unwind) => unwind is null ? "unwind to caller" : $"unwind label %{LocalName(unwind.Name)}";

    public void Br(IrBlock target) => Terminator($"br label %{LocalName(target.Name)}");

    public void CondBr(IrValue condition, IrBlock whenTrue, IrBlock whenFalse)
    {
        if (condition.Type != IrType.I1)
        {
            throw new InvalidOperationException("A branch condition must be i1.");
        }

        Terminator($"br {Typed(condition)}, label %{LocalName(whenTrue.Name)}, label %{LocalName(whenFalse.Name)}");
    }

    public void Switch(IrValue value, IrBlock defaultBlock, ImmutableArray<(IrIntConstant Value, IrBlock Block)> cases)
    {
        if (cases.Any(c => c.Value.Type != value.Type))
        {
            throw new InvalidOperationException("Switch case values must have the selector's type.");
        }

        var text = new StringBuilder("switch ").Append(Typed(value)).Append(", label %").Append(LocalName(defaultBlock.Name)).Append(" [\n");
        foreach (var (caseValue, block) in cases)
        {
            text.Append("    ").Append(Typed(caseValue)).Append(", label %").Append(LocalName(block.Name)).Append('\n');
        }

        Terminator(text.Append("  ]").ToString());
    }

    public void Ret(IrValue? value) => Terminator(value is null ? "ret void" : "ret " + Typed(value));

    public void Unreachable() => Terminator("unreachable");

    public void EndFunction()
    {
        var function = RequireFunction();
        if (!_inBlock || !_blockTerminated)
        {
            throw new InvalidOperationException("A function must end with a terminated block.");
        }

        if (_usesEh)
        {
            // CMP-390: the personality of every function with an invoke or a pad, declared once per module
            function.Insert(_headerEnd, " personality ptr @" + PersonalityName);
            _declarations[PersonalityName] = "declare i32 @" + PersonalityName + "(...)";
        }

        _functions.Add(function.Append("}\n").ToString());
        _function = null;
    }

    public void EndModule()
    {
        RequireModuleLevel();
        var text = new StringBuilder(_header);
        AppendSection(text, _moduleAsm);
        AppendSection(text, _structs);
        AppendSection(text, _globals);
        AppendSection(text, _declarations.Values);
        foreach (var function in _functions)
        {
            text.Append('\n').Append(function);
        }

        AppendSection(text, _attributeGroups.Values);
        _text = text.ToString();
    }

    /// <summary>The module text; available after <see cref="EndModule"/>.</summary>
    public string GetText() => _text ?? throw new InvalidOperationException("EndModule has not been called.");

    // ------------------------------------------------------------------ state checks

    private void RequireModuleLevel()
    {
        if (_header is null || _text != null)
        {
            throw new InvalidOperationException("No module is open.");
        }

        if (_function != null)
        {
            throw new InvalidOperationException("A function is still open.");
        }
    }

    private StringBuilder RequireFunction() => _function ?? throw new InvalidOperationException("No function is open.");

    private static void RequirePointer(IrValue address)
    {
        if (address.Type is not PointerIrType)
        {
            throw new InvalidOperationException("Expected a ptr operand.");
        }
    }

    private void RequireSameType(IrValue left, IrValue right)
    {
        if (left.Type != right.Type)
        {
            throw new InvalidOperationException($"Operand types differ: {Type(left.Type)} and {Type(right.Type)}.");
        }
    }

    private IrLocal Define(IrLocal result, string instruction)
    {
        Instruction($"%{LocalName(result.Name)} = {instruction}");
        return result;
    }

    private void Instruction(string text)
    {
        var function = RequireFunction();
        if (!_inBlock || _blockTerminated)
        {
            throw new InvalidOperationException("Instruction outside an open block.");
        }

        function.Append("  ").Append(text).Append('\n');
    }

    private void Terminator(string text)
    {
        Instruction(text);
        _blockTerminated = true;
    }

    private static void AppendSection(StringBuilder text, IEnumerable<string> lines)
    {
        var first = true;
        foreach (var line in lines)
        {
            if (first)
            {
                text.Append('\n');
                first = false;
            }

            text.Append(line).Append('\n');
        }
    }

    // ------------------------------------------------------------------ syntax

    private static string Linkage(IrLinkage linkage) => linkage switch
    {
        IrLinkage.External => "",
        IrLinkage.Internal => "internal ",
        IrLinkage.Private => "private ",
        IrLinkage.LinkOnceOdr => "linkonce_odr ",
        _ => throw new ArgumentOutOfRangeException(nameof(linkage)),
    };

    private static string Group(int? id) => id is { } n ? " #" + Int(n) : "";

    private static string Attribute(IrAttribute attribute) =>
        !attribute.IsString ? attribute.Key
        : attribute.Value is null ? $"\"{Escape(attribute.Key)}\""
        : $"\"{Escape(attribute.Key)}\"=\"{Escape(attribute.Value)}\"";

    private static string BinaryMnemonic(IrBinaryOperator op) => op.ToString().ToLowerInvariant();

    private static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    internal string Type(IrType type) => type switch
    {
        IntegerIrType i => "i" + Int(i.Bits),
        FloatingIrType { Kind: FloatingKind.Float } => "float",
        FloatingIrType => "double",
        PointerIrType => "ptr",
        VoidIrType => "void",
        TokenIrType => "token",
        ArrayIrType a => $"[{Int(a.Length)} x {Type(a.Element)}]",
        StructIrType s => Braces(s.IsPacked, s.Fields.Select(Type)),
        NamedStructIrType n => NamedStruct(n),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>
    /// <c>%"Name"</c>; a struct carrying its body (record types, CMP-306) is declared on first use, so every module declares
    /// exactly the record types it mentions, in first-use order (CMP-301: the struct section comes first).
    /// </summary>
    private string NamedStruct(NamedStructIrType type)
    {
        var name = "%" + Name(type.Name);
        if (type.Body is { } body && _declaredStructs.Add(name))
        {
            var slot = _structs.Count;
            _structs.Add("");
            _structs[slot] = $"{name} = type {Type(body)}";
        }

        return name;
    }

    private string Braces(bool packed, IEnumerable<string> items)
    {
        var list = string.Join(", ", items);
        var inner = list.Length == 0 ? "{}" : "{ " + list + " }";
        return packed ? "<" + inner + ">" : inner;
    }

    private string Typed(IrValue value) => value is IrAlignedPointer aligned
        ? "ptr align " + Int(aligned.Alignment) + " " + Value(aligned.Pointer)
        : Type(value.Type) + " " + Value(value);

    private string Value(IrValue value) => value switch
    {
        IrLocal local => "%" + LocalName(local.Name),
        IrGlobal global => "@" + Name(global.Name),
        IrIntConstant i => IntegerLiteral(i),
        IrFloatConstant f => FloatLiteral(f),
        IrNullConstant => "null",
        IrZeroConstant => "zeroinitializer",
        IrArrayConstant a => "[" + string.Join(", ", a.Elements.Select(Typed)) + "]",
        IrStructConstant s => Braces(s.StructType.IsPacked, s.Fields.Select(Typed)),
        IrGepConstant g => $"getelementptr inbounds ({Type(g.SourceType)}, {Typed(g.Base)}{string.Concat(g.Indices.Select(i => ", " + Typed(i)))})",
        IrIntToPtrConstant c => $"inttoptr (i64 {(long)(ulong)c.Address} to ptr)",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Decimal in the signed range of the type (two's complement); <c>true</c>/<c>false</c> for <c>i1</c>.</summary>
    private static string IntegerLiteral(IrIntConstant constant)
    {
        var bits = constant.IntegerType.Bits;
        var modulus = BigInteger.One << bits;
        var value = ((constant.Value % modulus) + modulus) % modulus;
        if (bits == 1)
        {
            return value.IsZero ? "false" : "true";
        }

        if (value >= modulus >> 1)
        {
            value -= modulus;
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Always the exact hexadecimal form (LangRef: decimal constants must be exactly representable); <c>float</c>
    /// constants are written as the double of their single-precision value, as LLVM requires.
    /// </summary>
    private static string FloatLiteral(IrFloatConstant constant)
    {
        var value = constant.FloatingType.Kind == FloatingKind.Float ? (double)(float)constant.Value : constant.Value;
        return "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
    }

    /// <summary>A global or type name: quoted when <see cref="LinkName.AlwaysQuote"/> or not a plain identifier (CMP-360).</summary>
    private static string Name(LinkName name) => name.AlwaysQuote || !IsPlainIdentifier(name.Text) ? Quote(name.Text) : name.Text;

    /// <summary>Locals and labels (CMP-303) are quoted only when they are not plain identifiers.</summary>
    private static string LocalName(string name) => IsPlainIdentifier(name) ? name : Quote(name);

    /// <summary><c>[-a-zA-Z$._][-a-zA-Z$._0-9]*</c> (LangRef "Identifiers").</summary>
    private static bool IsPlainIdentifier(string text) =>
        text.Length > 0 && !char.IsAsciiDigit(text[0]) && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '$' or '.' or '_');

    private static string Quote(string text) => "\"" + Escape(text) + "\"";

    /// <summary>UTF-8 bytes; <c>"</c>, <c>\</c>, control and non-ASCII bytes as <c>\XX</c>.</summary>
    private static string Escape(string text)
    {
        var result = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            if (b < 0x20 || b >= 0x7F || b == '"' || b == '\\')
            {
                result.Append('\\').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                result.Append((char)b);
            }
        }

        return result.ToString();
    }
}
