using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// Lowered trees of one unit → IR through <see cref="IIrWriter"/> (architecture.md §3, CMP-125). This part covers the module
/// skeleton, scalar globals (CMP-341), routines with parameters and locals (CMP-310), integer, Boolean and real
/// expressions (CMP-312, 315…317), conversions (CMP-305, 313), the always-on division tests of CMP-312, the overflow and
/// range checks the <see cref="CheckRewriter"/> marked (CMP-312 <c>{$Q+}</c>, CMP-314), control flow
/// (CMP-318…322), calls (ABI-080…082) and nested routines with frame records and static links (CMP-345). Not yet:
/// program and unit bodies with <c>$init</c>/<c>$final</c>/<c>$main</c> (C-044; they are ignored).
/// </summary>
internal static class IrEmitter
{
    /// <summary><c>attributes #0</c>: every Pascal routine (CMP-302, the <c>-O0</c> form until options reach the emitter).</summary>
    public const int RoutineGroup = 0;

    /// <summary><c>attributes #1</c>: noreturn helpers, on declarations and call sites (CMP-302, 312).</summary>
    public const int NoReturnGroup = 1;

    public static void EmitUnit(LoweredUnit unit, IIrWriter writer)
    {
        var path = unit.Unit.Locations.IsEmpty ? unit.Unit.Name + ".pas" : unit.Unit.Locations[0].Path;
        writer.BeginModule(unit.Unit.Name, Path.GetFileName(path));
        var interfaceMembers = unit.Unit.InterfaceMembers.ToHashSet();
        var module = new ModuleState(writer);

        foreach (var variable in unit.Unit.InterfaceMembers.Concat(unit.Unit.ImplementationMembers).OfType<VariableSymbol>())
        {
            var layout = TypeLayout.Of(variable.Type);
            var initializer = variable.Initializer is { } value ? StorageConstant(value, variable.Type) : new IrZeroConstant(layout.StorageType);
            module.Globals[variable] = writer.DefineGlobal(new IrGlobalDefinition(Mangler.GlobalVariable(variable),
                interfaceMembers.Contains(variable) ? IrLinkage.External : IrLinkage.Internal, IsConstant: false, initializer, layout.Alignment));
        }

        // CMP-345 frame record types, in definition order (CMP-301: named struct types first)
        foreach (var body in unit.Routines)
        {
            if (body.Frame is { } frame)
            {
                var type = writer.DeclareStruct(Mangler.FrameType(body.Routine!),
                    new StructIrType([IrType.Ptr, .. frame.Variables.Select(v => FieldLayout(v).StorageType)]));
                module.Frames[body.Routine!] = new FrameRecord(type, frame.Variables);
            }
        }

        // CMP-301: definition order, each nested routine directly after its parent
        foreach (var body in InEmissionOrder(unit.Routines))
        {
            var routine = body.Routine!;
            var linkage = !routine.IsNested && interfaceMembers.Contains(routine) ? IrLinkage.External : IrLinkage.Internal;
            new FunctionEmitter(module, body, linkage).Emit();
            module.DeclarePending();
        }

        writer.DefineAttributeGroup(RoutineGroup, [IrAttribute.Enum("uwtable"), IrAttribute.String("frame-pointer", "all")]);
        if (module.UsesNoReturn)
        {
            writer.DefineAttributeGroup(NoReturnGroup, [IrAttribute.Enum("noreturn"), IrAttribute.Enum("uwtable")]);
        }

        writer.EndModule();
    }

    private static IEnumerable<LoweredBody> InEmissionOrder(ImmutableArray<LoweredBody> routines)
    {
        IEnumerable<LoweredBody> WithNested(LoweredBody body) =>
            routines.Where(r => ReferenceEquals(r.Routine!.ContainingSymbol, body.Routine)).SelectMany(WithNested).Prepend(body);

        return routines.Where(r => !r.Routine!.IsNested).SelectMany(WithNested);
    }

    /// <summary>The IR parameter types of <paramref name="routine"/> (ABI-080, 082): static link, then per parameter value or <c>ptr</c>.</summary>
    internal static ImmutableArray<IrType> ParameterTypes(RoutineSymbol routine) =>
        [.. (routine.IsNested ? [IrType.Ptr] : Array.Empty<IrType>()),
         .. routine.Parameters.Select(p => p.IsByReference ? IrType.Ptr : TypeLayout.Of(p.Type).StorageType)];

    internal static IrType ReturnType(RoutineSymbol routine) => routine.ReturnType is { } type ? TypeLayout.Of(type).StorageType : IrType.Void;

    /// <summary>Layout of a frame field: the variable's storage, or a <c>ptr</c> for a by-reference parameter (CMP-345).</summary>
    internal static TypeLayout FieldLayout(Symbol variable) => variable switch
    {
        ParameterSymbol { IsByReference: true } => new TypeLayout(8, 8, IrType.Ptr),
        ParameterSymbol parameter => TypeLayout.Of(parameter.Type),
        VariableSymbol local => TypeLayout.Of(local.Type),
        _ => throw new InvalidOperationException($"'{variable.Name}' cannot live in a frame record."),
    };

    /// <summary>The SSA form of a value of <paramref name="type"/>: <c>i1</c> for the Boolean family (CMP-305), else the storage type.</summary>
    internal static IrType ValueType(TypeSymbol type) => type is BooleanTypeSymbol ? IrType.I1 : TypeLayout.Of(type).StorageType;

    /// <summary>A constant in SSA form (<see cref="ValueType"/>).</summary>
    internal static IrConstant ValueConstant(ConstantValue constant, TypeSymbol type) => (type, constant.Value) switch
    {
        (BooleanTypeSymbol, bool b) => IrIntConstant.I1(b),
        (BooleanTypeSymbol, BigInteger i) => IrIntConstant.I1(!i.IsZero),
        (RealTypeSymbol { RealKind: not RealKind.Currency }, BigInteger i) => new IrFloatConstant((FloatingIrType)ValueType(type), (double)i),
        (RealTypeSymbol { RealKind: not RealKind.Currency }, double d) => new IrFloatConstant((FloatingIrType)ValueType(type), d),
        (PointerTypeSymbol, null) => new IrNullConstant(),
        (IntegerTypeSymbol or CharTypeSymbol, BigInteger i) => new IrIntConstant((IntegerIrType)ValueType(type), i),
        _ => throw new NotSupportedException($"Constant {constant} of type '{type.Name}' is not emitted yet."),
    };

    /// <summary>A constant in memory form: Booleans as 0/1, C-style Booleans as 0/−1 (CMP-305).</summary>
    private static IrConstant StorageConstant(ConstantValue constant, TypeSymbol type)
    {
        var value = ValueConstant(constant, type);
        return type is BooleanTypeSymbol boolean
            ? new IrIntConstant((IntegerIrType)TypeLayout.Of(type).StorageType, ((IrIntConstant)value).Value.IsZero ? 0 : boolean.IsCStyle ? -1 : 1)
            : value;
    }
}

/// <summary>A routine's frame record type and its captured variables (field <c>i + 1</c> holds <c>Variables[i]</c>, CMP-345).</summary>
internal sealed record FrameRecord(NamedStructIrType Type, ImmutableArray<Symbol> Variables)
{
    public int FieldOf(Symbol variable) => Variables.IndexOf(variable) is var i and >= 0 ? i + 1 : -1;
}

/// <summary>Module-wide state shared by the function emitters.</summary>
internal sealed class ModuleState(IIrWriter writer)
{
    public IIrWriter Writer { get; } = writer;

    public Dictionary<VariableSymbol, IrGlobal> Globals { get; } = [];

    public Dictionary<RoutineSymbol, FrameRecord> Frames { get; } = [];

    /// <summary>The function to call: routines with a body in this unit directly, others through a declaration.</summary>
    public IrGlobal Routine(RoutineSymbol routine) =>
        routine.Definition is null
            ? Declare(new IrFunctionDeclaration(Mangler.Routine(routine), IrEmitter.ReturnType(routine), IrEmitter.ParameterTypes(routine)))
            : new IrGlobal(Mangler.Routine(routine));

    public bool UsesNoReturn { get; private set; }

    /// <summary>Declarations requested inside functions; the writer takes them at module level (<see cref="DeclarePending"/>).</summary>
    private readonly List<IrFunctionDeclaration> _pending = [];

    /// <summary>A void runtime helper without parameters, noreturn (<c>_DivZeroError</c>).</summary>
    public IrGlobal NoReturnHelper(string name)
    {
        UsesNoReturn = true;
        return Declare(new IrFunctionDeclaration(Mangler.RuntimeHelper(name), IrType.Void, [], IrEmitter.NoReturnGroup));
    }

    /// <summary>A runtime helper called by lowered code (<c>_Halt</c>, <c>_RunError</c>); noreturn ones carry <c>#1</c>.</summary>
    public IrGlobal Helper(RuntimeHelper helper)
    {
        UsesNoReturn |= helper.IsNoReturn;
        var returnType = helper.ReturnType is { } type ? TypeLayout.Of(type).StorageType : IrType.Void;
        return Declare(new IrFunctionDeclaration(helper.LinkName, returnType,
            [.. helper.ParameterTypes.Select(p => TypeLayout.Of(p).StorageType)], helper.IsNoReturn ? IrEmitter.NoReturnGroup : null));
    }

    public IrGlobal Intrinsic(string name, IrType returnType, ImmutableArray<IrType> parameterTypes) =>
        Declare(new IrFunctionDeclaration(new LinkName(name, AlwaysQuote: false), returnType, parameterTypes));

    /// <summary>Hands the collected declarations to the writer (it sorts them and merges duplicates, CMP-301).</summary>
    public void DeclarePending()
    {
        foreach (var declaration in _pending)
        {
            Writer.DeclareFunction(declaration);
        }

        _pending.Clear();
    }

    private IrGlobal Declare(IrFunctionDeclaration declaration)
    {
        _pending.Add(declaration);
        return new IrGlobal(declaration.Name);
    }
}

/// <summary>Emits one routine (CMP-310): entry block with all allocas and parameter spills, the body, block <c>exit</c> with the only <c>ret</c>.</summary>
internal sealed class FunctionEmitter
{
    private static readonly IrBlock Entry = new("entry");
    private static readonly IrBlock Exit = new("exit");

    private readonly ModuleState _module;
    private readonly IIrWriter _w;
    private readonly LoweredBody _body;
    private readonly RoutineSymbol _routine;
    private readonly IrLinkage _linkage;
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase) { Entry.Name, Exit.Name };
    private readonly Dictionary<LLocal, IrValue> _locals = [];
    private readonly Dictionary<ParameterSymbol, IrValue> _parameterAddresses = [];
    private readonly Dictionary<LTempValue, IrValue> _temps = [];
    private int _tempCount;
    private int _blockCount;
    private readonly Dictionary<LLoopLabel, (IrBlock Break, IrBlock Continue)> _loops = [];
    private IrBlock _current = Entry;
    private bool _terminated;
    private IrValue? _link;
    private FrameRecord? _frame;
    private IrValue? _frameAddress;

    public FunctionEmitter(ModuleState module, LoweredBody body, IrLinkage linkage)
    {
        _module = module;
        _w = module.Writer;
        _body = body;
        _routine = body.Routine!;
        _linkage = linkage;
    }

    /// <summary>
    /// Entry block: the frame record <c>%$frame</c> (CMP-345) first, then the allocas of parameters and locals that are not
    /// captured; then field 0 := static link (or null), <c>%name.addr</c> = field address of every captured variable (so the
    /// body addresses them like allocas), and the parameter spills in parameter order — into the frame for captured ones
    /// (by-reference parameters store their pointer there and keep using the incoming <c>ptr</c>).
    /// </summary>
    public void Emit()
    {
        var link = _routine.IsNested ? new IrParameter(IrType.Ptr, Unique("$link")) : null;
        var parameters = _routine.Parameters
            .Select(p => new IrParameter(p.IsByReference ? IrType.Ptr : TypeLayout.Of(p.Type).StorageType, Unique(p.Name)))
            .ToImmutableArray();
        var values = _w.BeginFunction(new IrFunctionDefinition(Mangler.Routine(_routine), _linkage, IrEmitter.ReturnType(_routine),
            link is null ? parameters : parameters.Insert(0, link), IrEmitter.RoutineGroup));
        if (link is not null)
        {
            _link = values[0];
            values = values.RemoveAt(0);
        }

        _w.AppendBlock(Entry);
        _module.Frames.TryGetValue(_routine, out _frame);
        if (_frame is not null)
        {
            _frameAddress = _w.Alloca(Unique("$frame"), _frame.Type, 8);
        }

        var spills = new List<(IrValue Value, IrValue Address, int Alignment)>();
        for (var i = 0; i < _routine.Parameters.Length; i++)
        {
            var parameter = _routine.Parameters[i];
            if (parameter.IsByReference)
            {
                _parameterAddresses[parameter] = values[i];
                continue;
            }

            if (!IsCaptured(parameter))
            {
                var layout = TypeLayout.Of(parameter.Type);
                _parameterAddresses[parameter] = _w.Alloca(Unique(values[i].Name + ".addr"), layout.StorageType, layout.Alignment);
            }
        }

        foreach (var local in _body.Locals.Where(l => !IsCaptured(l.Variable)))
        {
            var layout = TypeLayout.Of(local.Type);
            _locals[local] = _w.Alloca(Unique(local.Name + ".addr"), layout.StorageType, layout.Alignment);
        }

        if (_frame is not null)
        {
            _w.Store(_link ?? new IrNullConstant(), _frameAddress!, 8);
            foreach (var variable in _frame.Variables)
            {
                var address = _w.GetElementPtr(Unique(variable.Name + ".addr"), _frame.Type, _frameAddress!,
                    [IrIntConstant.I32(0), IrIntConstant.I32(_frame.FieldOf(variable))]);
                switch (variable)
                {
                    case ParameterSymbol { IsByReference: true } parameter:
                        spills.Add((_parameterAddresses[parameter], address, 8));
                        break;
                    case ParameterSymbol parameter:
                        _parameterAddresses[parameter] = address;
                        break;
                    default:
                        _locals[_body.Locals.Single(l => ReferenceEquals(l.Variable, variable))] = address;
                        break;
                }
            }
        }

        for (var i = 0; i < _routine.Parameters.Length; i++)
        {
            var parameter = _routine.Parameters[i];
            if (!parameter.IsByReference)
            {
                spills.Add((values[i], _parameterAddresses[parameter], TypeLayout.Of(parameter.Type).Alignment));
            }
        }

        foreach (var (value, address, alignment) in spills)
        {
            _w.Store(value, address, alignment);
        }

        Statement(_body.Body);
        FallThrough(Exit);

        Start(Exit);
        if (_routine.ResultVariable is { } result)
        {
            var local = _body.Locals.Single(l => ReferenceEquals(l.Variable, result));
            var layout = TypeLayout.Of(local.Type);
            _w.Ret(_w.Load(NewTemp(), layout.StorageType, _locals[local], layout.Alignment));
        }
        else
        {
            _w.Ret(null);
        }

        _w.EndFunction();
    }

    // ------------------------------------------------------------------ names (CMP-303)

    /// <summary><paramref name="name"/>, or with <c>.N</c> appended when it is taken (case-insensitively) or has the shape of a temporary.</summary>
    private string Unique(string name)
    {
        var candidate = name;
        for (var n = 1; IsTemporaryName(candidate) || !_names.Add(candidate); n++)
        {
            candidate = name + "." + n;
        }

        return candidate;
    }

    private static bool IsTemporaryName(string name) => name.Length > 1 && name[0] is 't' or 'T' && name.Skip(1).All(char.IsAsciiDigit);

    private string NewTemp()
    {
        string name;
        do
        {
            name = "t" + ++_tempCount;
        }
        while (!_names.Add(name));

        return name;
    }

    private int NewConstruct() => ++_blockCount;

    private IrBlock Block(string kind, int id) => new(kind + "." + id);

    private void Start(IrBlock block)
    {
        _w.AppendBlock(block);
        _current = block;
        _terminated = false;
    }

    // ------------------------------------------------------------------ statements

    /// <summary>
    /// Emits <paramref name="statement"/> into the current block. After a jump (<see cref="LBreak"/>, <see cref="LContinue"/>,
    /// <see cref="LReturn"/>, <see cref="LUnreachable"/>) the block is terminated and the rest of the enclosing
    /// <see cref="LBlock"/> is dead code: it is not emitted (the lowered tree has no labels, so nothing can jump into it).
    /// </summary>
    private void Statement(LStatement statement)
    {
        switch (statement)
        {
            case LBlock block:
                foreach (var inner in block.Statements)
                {
                    if (_terminated)
                    {
                        break;
                    }

                    Statement(inner);
                }

                break;
            case LAssign assign:
                Store(Value(assign.Value), Address(assign.Target), assign.Target.Type);
                break;
            case LIf ifStatement:
                If(ifStatement);
                break;
            case LLoop loop:
                Loop(loop);
                break;
            case LSwitch switchStatement:
                Switch(switchStatement);
                break;
            case LBreak or LContinue:
                Jump(JumpTarget(statement)!);
                break;
            case LReturn:
                Jump(Exit);
                break;
            case LUnreachable:
                _w.Unreachable();
                _terminated = true;
                break;
            case LCallStatement { Callee: LHelperCallee helper } call:
            {
                var callee = _module.Helper(helper.Helper);
                var arguments = call.Arguments.Select((a, i) => ToStorage(Value(a), helper.Helper.ParameterTypes[i])).ToImmutableArray();
                var returnType = helper.Helper.ReturnType is { } type ? TypeLayout.Of(type).StorageType : IrType.Void;
                _w.Call(null, returnType, callee, arguments, helper.Helper.IsNoReturn ? IrEmitter.NoReturnGroup : null);
                break;
            }

            case LCallStatement { Callee: LRoutineCallee callee } call:
                Call(named: false, callee.Routine, call.Arguments);
                break;
            default:
                throw new InvalidOperationException($"Cannot emit {statement.GetType().Name}.");
        }
    }

    // ------------------------------------------------------------------ control flow (CMP-318…322)

    private void Jump(IrBlock target)
    {
        _w.Br(target);
        _terminated = true;
    }

    /// <summary>Branches to <paramref name="block"/> unless the current block already ended with a jump.</summary>
    private void FallThrough(IrBlock block)
    {
        if (!_terminated)
        {
            _w.Br(block);
        }
    }

    /// <summary>The block a bare <see cref="LBreak"/>/<see cref="LContinue"/>/<see cref="LReturn"/> jumps to; null for other statements.</summary>
    private IrBlock? JumpTarget(LStatement statement) => statement switch
    {
        LBreak jump => _loops[jump.Label].Break,
        LContinue jump => _loops[jump.Label].Continue,
        LReturn => Exit,
        LBlock { Statements: [var single] } => JumpTarget(single),
        _ => null,
    };

    /// <summary>
    /// CMP-318: <c>if.then.N</c> / <c>if.else.N</c> / <c>if.end.N</c>. An arm that is only a jump (<c>if c then Break</c>) branches
    /// to its target directly instead of getting a block.
    /// </summary>
    private void If(LIf statement)
    {
        var condition = Value(statement.Condition);
        var id = NewConstruct();
        var end = Block("if.end", id);
        var thenTarget = JumpTarget(statement.Then);
        var elseTarget = statement.Else is null ? end : JumpTarget(statement.Else);
        var then = thenTarget ?? Block("if.then", id);
        var @else = elseTarget ?? Block("if.else", id);
        _w.CondBr(condition, then, @else);
        if (thenTarget is null)
        {
            Arm(then, statement.Then, end);
        }

        if (elseTarget is null)
        {
            Arm(@else, statement.Else!, end);
        }

        Start(end);
    }

    private void Arm(IrBlock block, LStatement body, IrBlock end)
    {
        Start(block);
        Statement(body);
        FallThrough(end);
    }

    /// <summary>
    /// CMP-319 with the label kind <c>K</c> (architecture.md, C-035 decision): <c>K.body.N</c>, then the step block
    /// (<c>for.inc.N</c>, <c>repeat.cond.N</c>, else <c>K.step.N</c>) back to the body, and <c>K.end.N</c> after the loop.
    /// A loop whose body is <c>if c then S else Break</c> without step (the <c>while</c> lowering) is emitted as
    /// <c>K.cond.N</c> → <c>K.body.N</c> → back to the condition. <c>Continue</c> jumps to the step, condition or body top.
    /// </summary>
    private void Loop(LLoop loop)
    {
        var kind = loop.Label.Kind;
        var id = NewConstruct();
        IrBlock body = Block(kind + ".body", id), end = Block(kind + ".end", id);
        if (loop is { Step: null, Body: LIf { Else: { } exit } test } && exit is LBreak { } leave && leave.Label == loop.Label)
        {
            var cond = Block(kind + ".cond", id);
            _loops[loop.Label] = (end, cond);
            Jump(cond);
            Start(cond);
            _w.CondBr(Value(test.Condition), body, end);
            _terminated = true;
            Arm(body, test.Then, cond);
            Start(end);
            return;
        }

        var step = loop.Step is null ? null : Block(kind switch { "for" => "for.inc", "repeat" => "repeat.cond", _ => kind + ".step" }, id);
        _loops[loop.Label] = (end, step ?? body);
        Jump(body);
        Arm(body, loop.Body, step ?? body);
        if (step is not null)
        {
            Arm(step, loop.Step!, body);
        }

        Start(end);
    }

    /// <summary>
    /// CMP-322: ranges first (<c>icmp ule (sub sel, lo), hi − lo</c>, section order, blocks <c>case.range.N.J</c>), then
    /// <c>switch</c> to <c>case.arm.N.K</c> (K = section, 1-based), default <c>case.else.N</c> or <c>case.end.N</c>.
    /// </summary>
    private void Switch(LSwitch statement)
    {
        var selector = Value(statement.Selector);
        var type = (IntegerIrType)selector.Type;
        var id = NewConstruct();
        var end = Block("case.end", id);
        var @default = statement.Default is null ? end : Block("case.else", id);
        var arms = statement.Sections.Select((_, k) => Block("case.arm." + id, k + 1)).ToImmutableArray();

        var rangeCount = 0;
        for (var k = 0; k < statement.Sections.Length; k++)
        {
            foreach (var (low, high) in statement.Sections[k].Ranges)
            {
                var offset = _w.Binary(NewTemp(), IrBinaryOperator.Sub, selector, new IrIntConstant(type, low));
                var inRange = _w.ICmp(NewTemp(), IrIntPredicate.Ule, offset, new IrIntConstant(type, high - low));
                var next = Block("case.range." + id, ++rangeCount);
                _w.CondBr(inRange, arms[k], next);
                Start(next);
            }
        }

        _w.Switch(selector, @default, [.. statement.Sections.SelectMany((section, k) => section.Values.Select(v => (new IrIntConstant(type, v), arms[k])))]);
        _terminated = true;
        for (var k = 0; k < statement.Sections.Length; k++)
        {
            Arm(arms[k], statement.Sections[k].Body, end);
        }

        if (statement.Default is { } body)
        {
            Arm(@default, body, end);
        }

        Start(end);
    }

    // ------------------------------------------------------------------ storage

    private IrValue Address(LExpression variable) => variable switch
    {
        LLocalRef local => _locals[local.Local],
        LGlobalRef global => _module.Globals[global.Variable],
        LParamRef parameter => _parameterAddresses[parameter.Parameter],
        LOuterRef outer => OuterAddress(outer),
        _ => throw new InvalidOperationException($"{variable.GetType().Name} is not a variable reference."),
    };

    private bool IsCaptured(Symbol? variable) => variable is not null && _frame?.FieldOf(variable) > 0;

    /// <summary>
    /// CMP-345: field of the owner's frame record, reached from <c>%$link</c> (the parent's frame) by loading field 0 once
    /// per further level; a captured <c>var</c>/<c>out</c> parameter's field holds the pointer to the variable.
    /// </summary>
    private IrValue OuterAddress(LOuterRef outer)
    {
        var frame = _module.Frames[outer.Owner];
        var address = FrameOf(outer.Owner);
        var field = _w.GetElementPtr(NewTemp(), frame.Type, address, [IrIntConstant.I32(0), IrIntConstant.I32(frame.FieldOf(outer.Variable))]);
        return outer.Variable is ParameterSymbol { IsByReference: true } ? _w.Load(NewTemp(), IrType.Ptr, field, 8) : field;
    }

    /// <summary>The frame record of <paramref name="routine"/>, this routine or an enclosing one (null value when it has none).</summary>
    private IrValue FrameOf(RoutineSymbol routine)
    {
        if (ReferenceEquals(routine, _routine))
        {
            return _frameAddress ?? new IrNullConstant();
        }

        var frame = _link ?? throw new InvalidOperationException($"'{_routine.Name}' is not nested in '{routine.Name}'.");
        for (var level = (RoutineSymbol)_routine.ContainingSymbol!; !ReferenceEquals(level, routine); level = (RoutineSymbol)level.ContainingSymbol!)
        {
            frame = _w.Load(NewTemp(), IrType.Ptr, frame, 8);
        }

        return frame;
    }

    /// <summary>
    /// A direct call (ABI-082): the static link of a nested callee (its parent's frame) first, then value arguments in storage
    /// form and <c>var</c>/<c>out</c> arguments as addresses (<see cref="LAddrOf"/>). <paramref name="named"/> false discards the result.
    /// </summary>
    private IrLocal? Call(bool named, RoutineSymbol routine, ImmutableArray<LExpression> arguments)
    {
        var values = ImmutableArray.CreateBuilder<IrValue>();
        if (routine.IsNested)
        {
            values.Add(FrameOf((RoutineSymbol)routine.ContainingSymbol!));
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = routine.Parameters[i];
            var value = Value(arguments[i]);
            values.Add(parameter.IsByReference ? value : ToStorage(value, parameter.Type));
        }

        return _w.Call(named ? NewTemp() : null, IrEmitter.ReturnType(routine), _module.Routine(routine), values.ToImmutable());
    }

    /// <summary>Loads a value in SSA form: Booleans are compared <c>ne 0</c> after the load (CMP-305).</summary>
    private IrValue Load(IrValue address, TypeSymbol type)
    {
        var layout = TypeLayout.Of(type);
        var loaded = _w.Load(NewTemp(), layout.StorageType, address, layout.Alignment);
        return type is BooleanTypeSymbol ? _w.ICmp(NewTemp(), IrIntPredicate.Ne, loaded, new IrIntConstant((IntegerIrType)layout.StorageType, 0)) : loaded;
    }

    /// <summary>Stores an SSA value: <c>zext</c> for <c>Boolean</c>, <c>sext</c> (all ones) for C-style Booleans (CMP-305).</summary>
    private void Store(IrValue value, IrValue address, TypeSymbol type) =>
        _w.Store(ToStorage(value, type), address, TypeLayout.Of(type).Alignment);

    /// <summary>An SSA value in memory/argument form: <c>zext</c> for <c>Boolean</c>, <c>sext</c> (all ones) for C-style Booleans.</summary>
    private IrValue ToStorage(IrValue value, TypeSymbol type)
    {
        if (type is not BooleanTypeSymbol boolean)
        {
            return value;
        }

        var storage = (IntegerIrType)TypeLayout.Of(type).StorageType;
        return value is IrIntConstant constant
            ? new IrIntConstant(storage, constant.Value.IsZero ? 0 : boolean.IsCStyle ? -1 : 1)
            : _w.Cast(NewTemp(), boolean.IsCStyle ? IrCastOperator.SExt : IrCastOperator.ZExt, value, storage);
    }

    // ------------------------------------------------------------------ expressions

    private IrValue Value(LExpression expression)
    {
        switch (expression)
        {
            case LConst constant:
                return IrEmitter.ValueConstant(constant.Value, constant.Type);
            case LLocalRef or LGlobalRef or LParamRef or LOuterRef:
                return Load(Address(expression), expression.Type);
            case LAddrOf addressOf:
                return Address(addressOf.Variable);
            case LTemp temp:
                _temps.Add(temp.Temp, Value(temp.Value));
                return Value(temp.Body);
            case LTempRef reference:
                return _temps[reference.Temp];
            case LUnary unary:
                return Unary(unary);
            case LBinary binary:
                return Binary(binary);
            case LCompare compare:
                return Compare(compare);
            case LConvert convert:
                return Convert(Value(convert.Operand), convert.Operand.Type, convert.Type);
            case LRangeCheck check:
                return RangeCheck(check);
            case LSelect select:
            {
                var condition = Value(select.Condition);
                var whenTrue = Value(select.WhenTrue);
                return _w.Select(NewTemp(), condition, whenTrue, Value(select.WhenFalse));
            }

            case LCall { Callee: LRoutineCallee callee } call:
            {
                var result = Call(named: true, callee.Routine, call.Arguments)!;
                return call.Type is BooleanTypeSymbol
                    ? _w.ICmp(NewTemp(), IrIntPredicate.Ne, result, new IrIntConstant((IntegerIrType)result.Type, 0))
                    : result;
            }

            default:
                throw new InvalidOperationException($"Cannot emit {expression.GetType().Name}.");
        }
    }

    private IrValue Unary(LUnary unary)
    {
        var operand = Value(unary.Operand);
        switch (unary.Operator)
        {
            case LUnaryOperator.Negate when operand.Type is FloatingIrType:
                return _w.FNeg(NewTemp(), operand);
            case LUnaryOperator.Negate when unary.IsChecked:
                return Overflow("sub", IsSigned(unary.Type), new IrIntConstant((IntegerIrType)operand.Type, 0), operand);
            case LUnaryOperator.Negate:
                return _w.Binary(NewTemp(), IrBinaryOperator.Sub, new IrIntConstant((IntegerIrType)operand.Type, 0), operand);
            case LUnaryOperator.Not:
                return _w.Binary(NewTemp(), IrBinaryOperator.Xor, operand, new IrIntConstant((IntegerIrType)operand.Type, -1));
            case LUnaryOperator.Abs:
            {
                var name = operand.Type == IrType.Float ? "llvm.fabs.f32" : "llvm.fabs.f64";
                var fabs = _module.Intrinsic(name, operand.Type, [operand.Type]);
                return _w.Call(NewTemp(), operand.Type, fabs, [operand])!;
            }

            default:
                throw new InvalidOperationException($"Unknown unary operator {unary.Operator}.");
        }
    }

    private IrValue Binary(LBinary binary)
    {
        switch (binary.Operator)
        {
            case LBinaryOperator.ConditionalAnd or LBinaryOperator.ConditionalOr:
                return ShortCircuit(binary);
            case LBinaryOperator.IntegerDivide or LBinaryOperator.Modulo:
                return Division(binary);
        }

        var left = Value(binary.Left);
        var right = Value(binary.Right);
        if (binary.IsChecked)
        {
            var kind = binary.Operator switch
            {
                LBinaryOperator.Add => "add",
                LBinaryOperator.Subtract => "sub",
                LBinaryOperator.Multiply => "mul",
                _ => throw new InvalidOperationException($"{binary.Operator} has no overflow check."),
            };
            return Overflow(kind, IsSigned(binary.Type), left, right);
        }

        var isReal = left.Type is FloatingIrType;
        IrBinaryOperator op;
        switch (binary.Operator)
        {
            case LBinaryOperator.Add:
                op = isReal ? IrBinaryOperator.FAdd : IrBinaryOperator.Add;
                break;
            case LBinaryOperator.Subtract:
                op = isReal ? IrBinaryOperator.FSub : IrBinaryOperator.Sub;
                break;
            case LBinaryOperator.Multiply:
                op = isReal ? IrBinaryOperator.FMul : IrBinaryOperator.Mul;
                break;
            case LBinaryOperator.Divide:
                op = IrBinaryOperator.FDiv;
                break;
            case LBinaryOperator.And:
                op = IrBinaryOperator.And;
                break;
            case LBinaryOperator.Or:
                op = IrBinaryOperator.Or;
                break;
            case LBinaryOperator.Xor:
                op = IrBinaryOperator.Xor;
                break;
            case LBinaryOperator.Shl or LBinaryOperator.Shr:
            {
                // CMP-312: the count is masked to the operand width
                var type = (IntegerIrType)left.Type;
                IrValue count = right is IrIntConstant constant
                    ? new IrIntConstant(type, constant.Value & (type.Bits - 1))
                    : _w.Binary(NewTemp(), IrBinaryOperator.And, right, new IrIntConstant(type, type.Bits - 1));
                return _w.Binary(NewTemp(), binary.Operator == LBinaryOperator.Shl ? IrBinaryOperator.Shl : IrBinaryOperator.LShr, left, count);
            }

            default:
                throw new InvalidOperationException($"Unknown binary operator {binary.Operator}.");
        }

        return _w.Binary(NewTemp(), op, left, right);
    }

    /// <summary>CMP-317: <c>and</c> → <c>and.rhs.N</c>/<c>and.end.N</c> with a phi; <c>or</c> symmetric.</summary>
    private IrValue ShortCircuit(LBinary binary)
    {
        var isAnd = binary.Operator == LBinaryOperator.ConditionalAnd;
        var kind = isAnd ? "and" : "or";
        var left = Value(binary.Left);
        var leftBlock = _current;
        var id = NewConstruct();
        IrBlock rhs = Block(kind + ".rhs", id), end = Block(kind + ".end", id);
        if (isAnd)
        {
            _w.CondBr(left, rhs, end);
        }
        else
        {
            _w.CondBr(left, end, rhs);
        }

        Start(rhs);
        var right = Value(binary.Right);
        var rightBlock = _current;
        _w.Br(end);

        Start(end);
        return _w.Phi(NewTemp(), IrType.I1, [(IrIntConstant.I1(!isAnd), leftBlock), (right, rightBlock)]);
    }

    /// <summary>CMP-312 division: zero test → <c>_DivZeroError</c>; signed −1 handled without <c>sdiv</c>; tests omitted for non-zero constants.</summary>
    private IrValue Division(LBinary binary)
    {
        var left = Value(binary.Left);
        var right = Value(binary.Right);
        var type = (IntegerIrType)left.Type;
        var isSigned = binary.Type is IntegerTypeSymbol { IsSigned: true };
        var isDiv = binary.Operator == LBinaryOperator.IntegerDivide;
        var op = (isDiv, isSigned) switch
        {
            (true, true) => IrBinaryOperator.SDiv,
            (true, false) => IrBinaryOperator.UDiv,
            (false, true) => IrBinaryOperator.SRem,
            _ => IrBinaryOperator.URem,
        };

        if (right is IrIntConstant { Value.IsZero: false } constant)
        {
            if (isSigned && constant.Value == BigInteger.MinusOne)
            {
                return isDiv ? _w.Binary(NewTemp(), IrBinaryOperator.Sub, new IrIntConstant(type, 0), left) : new IrIntConstant(type, 0);
            }

            return _w.Binary(NewTemp(), op, left, right);
        }

        var id = NewConstruct();
        IrBlock zero = Block("div.zero", id), ok = Block("div.ok", id);
        _w.CondBr(_w.ICmp(NewTemp(), IrIntPredicate.Eq, right, new IrIntConstant(type, 0)), zero, ok);
        ErrorBlock(zero, "_DivZeroError");
        Start(ok);
        if (!isSigned)
        {
            return _w.Binary(NewTemp(), op, left, right);
        }

        IrBlock negative = Block("div.neg", id), divide = Block("div.do", id), end = Block("div.end", id);
        _w.CondBr(_w.ICmp(NewTemp(), IrIntPredicate.Eq, right, new IrIntConstant(type, -1)), negative, divide);
        Start(negative);
        IrValue negated = isDiv ? _w.Binary(NewTemp(), IrBinaryOperator.Sub, new IrIntConstant(type, 0), left) : new IrIntConstant(type, 0);
        _w.Br(end);
        Start(divide);
        var quotient = _w.Binary(NewTemp(), op, left, right);
        _w.Br(end);
        Start(end);
        return _w.Phi(NewTemp(), type, [(negated, negative), (quotient, divide)]);
    }

    /// <summary>
    /// CMP-312 <c>{$Q+}</c>: <c>llvm.[s|u]&lt;kind&gt;.with.overflow.iN</c>, flag (field 1) → block <c>ovf.error.N</c> calling
    /// <c>_OverflowError</c>, else <c>ovf.ok.N</c> continuing with the result (field 0).
    /// </summary>
    private IrValue Overflow(string kind, bool signed, IrValue left, IrValue right)
    {
        var type = (IntegerIrType)left.Type;
        var pairType = new StructIrType([type, IrType.I1]);
        var intrinsic = _module.Intrinsic($"llvm.{(signed ? "s" : "u")}{kind}.with.overflow.i{type.Bits}", pairType, [type, type]);
        var pair = _w.Call(NewTemp(), pairType, intrinsic, [left, right])!;
        var overflowed = _w.ExtractValue(NewTemp(), pair, 1);
        var id = NewConstruct();
        IrBlock error = Block("ovf.error", id), ok = Block("ovf.ok", id);
        _w.CondBr(overflowed, error, ok);
        ErrorBlock(error, "_OverflowError");
        Start(ok);
        return _w.ExtractValue(NewTemp(), pair, 0);
    }

    /// <summary>
    /// CMP-314 <c>{$R+}</c>: <c>icmp ule (sub v, lo), hi − lo</c> (no <c>sub</c> when <c>lo = 0</c>) → <c>range.ok.N</c>, else block
    /// <c>range.error.N</c> calling <c>_RangeError</c>. The value itself is the result.
    /// </summary>
    private IrValue RangeCheck(LRangeCheck check)
    {
        var value = Value(check.Value);
        var type = (IntegerIrType)value.Type;
        var offset = check.Low.IsZero ? value : _w.Binary(NewTemp(), IrBinaryOperator.Sub, value, new IrIntConstant(type, check.Low));
        var inRange = _w.ICmp(NewTemp(), IrIntPredicate.Ule, offset, new IrIntConstant(type, check.High - check.Low));
        var id = NewConstruct();
        IrBlock error = Block("range.error", id), ok = Block("range.ok", id);
        _w.CondBr(inRange, ok, error);
        ErrorBlock(error, "_RangeError");
        Start(ok);
        return value;
    }

    /// <summary><paramref name="block"/>: <c>call void @"System.&lt;helper&gt;"() #1</c> + <c>unreachable</c>.</summary>
    private void ErrorBlock(IrBlock block, string helper)
    {
        Start(block);
        _w.Call(null, IrType.Void, _module.NoReturnHelper(helper), [], IrEmitter.NoReturnGroup);
        _w.Unreachable();
    }

    /// <summary>CMP-315: signed/unsigned predicates by type (chars and Booleans unsigned), reals ordered except <c>une</c>; signed vs <c>UInt64</c> mathematically.</summary>
    private IrValue Compare(LCompare compare)
    {
        var left = Value(compare.Left);
        var right = Value(compare.Right);
        var op = compare.Operator;
        if (left.Type is FloatingIrType)
        {
            var predicate = op switch
            {
                LCompareOperator.Equal => IrFloatPredicate.Oeq,
                LCompareOperator.NotEqual => IrFloatPredicate.Une,
                LCompareOperator.Less => IrFloatPredicate.Olt,
                LCompareOperator.LessOrEqual => IrFloatPredicate.Ole,
                LCompareOperator.Greater => IrFloatPredicate.Ogt,
                _ => IrFloatPredicate.Oge,
            };
            return _w.FCmp(NewTemp(), predicate, left, right);
        }

        var leftSigned = IsSigned(compare.Left.Type);
        var rightSigned = IsSigned(compare.Right.Type);
        if (leftSigned == rightSigned)
        {
            return _w.ICmp(NewTemp(), IntPredicate(op, leftSigned), left, right);
        }

        // LANG-302 / CMP-315: put the signed operand on the left; a negative one is below every unsigned value
        if (!leftSigned)
        {
            (left, right) = (right, left);
            op = Mirror(op);
        }

        var unsigned = _w.ICmp(NewTemp(), IntPredicate(op, signed: false), left, right);
        if (op is LCompareOperator.Less or LCompareOperator.LessOrEqual or LCompareOperator.NotEqual)
        {
            var negative = _w.ICmp(NewTemp(), IrIntPredicate.Slt, left, new IrIntConstant((IntegerIrType)left.Type, 0));
            return _w.Binary(NewTemp(), IrBinaryOperator.Or, negative, unsigned);
        }

        var nonNegative = _w.ICmp(NewTemp(), IrIntPredicate.Sge, left, new IrIntConstant((IntegerIrType)left.Type, 0));
        return _w.Binary(NewTemp(), IrBinaryOperator.And, nonNegative, unsigned);
    }

    private static bool IsSigned(TypeSymbol type) => type is IntegerTypeSymbol { IsSigned: true };

    private static LCompareOperator Mirror(LCompareOperator op) => op switch
    {
        LCompareOperator.Less => LCompareOperator.Greater,
        LCompareOperator.LessOrEqual => LCompareOperator.GreaterOrEqual,
        LCompareOperator.Greater => LCompareOperator.Less,
        LCompareOperator.GreaterOrEqual => LCompareOperator.LessOrEqual,
        _ => op,
    };

    private static IrIntPredicate IntPredicate(LCompareOperator op, bool signed) => op switch
    {
        LCompareOperator.Equal => IrIntPredicate.Eq,
        LCompareOperator.NotEqual => IrIntPredicate.Ne,
        LCompareOperator.Less => signed ? IrIntPredicate.Slt : IrIntPredicate.Ult,
        LCompareOperator.LessOrEqual => signed ? IrIntPredicate.Sle : IrIntPredicate.Ule,
        LCompareOperator.Greater => signed ? IrIntPredicate.Sgt : IrIntPredicate.Ugt,
        _ => signed ? IrIntPredicate.Sge : IrIntPredicate.Uge,
    };

    /// <summary>
    /// CMP-313 between scalar SSA values: integers and chars extend by the source's signedness (chars unsigned) or truncate;
    /// Booleans become 0/1 (<c>zext</c>) and an ordinal becomes a Boolean by <c>ne 0</c>; integer → real <c>sitofp</c>/<c>uitofp</c>;
    /// <c>fpext</c>/<c>fptrunc</c>; pointer ↔ integer <c>ptrtoint</c>/<c>inttoptr</c>.
    /// </summary>
    private IrValue Convert(IrValue value, TypeSymbol from, TypeSymbol to)
    {
        var target = IrEmitter.ValueType(to);
        if (value.Type == target && (from is BooleanTypeSymbol) == (to is BooleanTypeSymbol))
        {
            return value;
        }

        if (value is IrIntConstant constant && target is IntegerIrType integerTarget)
        {
            return to is BooleanTypeSymbol ? IrIntConstant.I1(!constant.Value.IsZero) : new IrIntConstant(integerTarget, constant.Value);
        }

        switch (value.Type, target)
        {
            case (IntegerIrType, IntegerIrType { Bits: 1 }):
                return _w.ICmp(NewTemp(), IrIntPredicate.Ne, value, new IrIntConstant((IntegerIrType)value.Type, 0));
            case (IntegerIrType source, IntegerIrType destination):
            {
                var op = destination.Bits < source.Bits ? IrCastOperator.Trunc
                    : IsSigned(from) ? IrCastOperator.SExt : IrCastOperator.ZExt;
                return _w.Cast(NewTemp(), op, value, target);
            }

            case (IntegerIrType, FloatingIrType):
                return _w.Cast(NewTemp(), IsSigned(from) ? IrCastOperator.SIToFP : IrCastOperator.UIToFP, value, target);
            case (FloatingIrType { Kind: FloatingKind.Float }, FloatingIrType { Kind: FloatingKind.Double }):
                return _w.Cast(NewTemp(), IrCastOperator.FPExt, value, target);
            case (FloatingIrType { Kind: FloatingKind.Double }, FloatingIrType { Kind: FloatingKind.Float }):
                return _w.Cast(NewTemp(), IrCastOperator.FPTrunc, value, target);
            case (PointerIrType, IntegerIrType):
                return _w.Cast(NewTemp(), IrCastOperator.PtrToInt, value, target);
            case (IntegerIrType, PointerIrType):
                return _w.Cast(NewTemp(), IrCastOperator.IntToPtr, value, target);
            default:
                throw new NotSupportedException($"Conversion from '{from.Name}' to '{to.Name}' is not emitted yet.");
        }
    }
}
