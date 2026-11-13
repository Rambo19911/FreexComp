using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Lowering;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// Lowered trees of one unit → IR through <see cref="IIrWriter"/> (architecture.md §3, CMP-125). This part covers the module
/// skeleton, scalar globals (CMP-341), routines with parameters and locals (CMP-310), integer, Boolean and real
/// expressions (CMP-312, 315…317), conversions (CMP-305, 313) and the always-on division tests of CMP-312. Not yet (they
/// throw <see cref="NotSupportedException"/>): control-flow statements (C-037), calls and nested routines (C-038), program
/// and unit bodies with <c>$init</c>/<c>$final</c>/<c>$main</c> (C-044).
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

        foreach (var body in unit.Routines)
        {
            var routine = body.Routine!;
            if (routine.IsNested)
            {
                throw new NotSupportedException($"Nested routine '{routine.Name}' needs a static link (C-038).");
            }

            new FunctionEmitter(module, body, interfaceMembers.Contains(routine) ? IrLinkage.External : IrLinkage.Internal).Emit();
            module.DeclarePending();
        }

        writer.DefineAttributeGroup(RoutineGroup, [IrAttribute.Enum("uwtable"), IrAttribute.String("frame-pointer", "all")]);
        if (module.UsesNoReturn)
        {
            writer.DefineAttributeGroup(NoReturnGroup, [IrAttribute.Enum("noreturn"), IrAttribute.Enum("uwtable")]);
        }

        writer.EndModule();
    }

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

/// <summary>Module-wide state shared by the function emitters.</summary>
internal sealed class ModuleState(IIrWriter writer)
{
    public IIrWriter Writer { get; } = writer;

    public Dictionary<VariableSymbol, IrGlobal> Globals { get; } = [];

    public bool UsesNoReturn { get; private set; }

    /// <summary>Declarations requested inside functions; the writer takes them at module level (<see cref="DeclarePending"/>).</summary>
    private readonly List<IrFunctionDeclaration> _pending = [];

    /// <summary>A void runtime helper without parameters, noreturn (<c>_DivZeroError</c>).</summary>
    public IrGlobal NoReturnHelper(string name)
    {
        UsesNoReturn = true;
        return Declare(new IrFunctionDeclaration(Mangler.RuntimeHelper(name), IrType.Void, [], IrEmitter.NoReturnGroup));
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
    private IrBlock _current = Entry;

    public FunctionEmitter(ModuleState module, LoweredBody body, IrLinkage linkage)
    {
        _module = module;
        _w = module.Writer;
        _body = body;
        _routine = body.Routine!;
        _linkage = linkage;
    }

    public void Emit()
    {
        var parameters = _routine.Parameters
            .Select(p => new IrParameter(p.IsByReference ? IrType.Ptr : TypeLayout.Of(p.Type).StorageType, Unique(p.Name)))
            .ToImmutableArray();
        var returnType = _routine.ReturnType is { } type ? TypeLayout.Of(type).StorageType : IrType.Void;
        var values = _w.BeginFunction(new IrFunctionDefinition(Mangler.Routine(_routine), _linkage, returnType, parameters, IrEmitter.RoutineGroup));

        _w.AppendBlock(Entry);
        var spills = new List<(IrValue Value, IrValue Address, int Alignment)>();
        for (var i = 0; i < _routine.Parameters.Length; i++)
        {
            var parameter = _routine.Parameters[i];
            if (parameter.IsByReference)
            {
                _parameterAddresses[parameter] = values[i];
                continue;
            }

            var layout = TypeLayout.Of(parameter.Type);
            var address = _w.Alloca(Unique(values[i].Name + ".addr"), layout.StorageType, layout.Alignment);
            _parameterAddresses[parameter] = address;
            spills.Add((values[i], address, layout.Alignment));
        }

        foreach (var local in _body.Locals)
        {
            var layout = TypeLayout.Of(local.Type);
            _locals[local] = _w.Alloca(Unique(local.Name + ".addr"), layout.StorageType, layout.Alignment);
        }

        foreach (var (value, address, alignment) in spills)
        {
            _w.Store(value, address, alignment);
        }

        Statement(_body.Body);
        _w.Br(Exit);

        _w.AppendBlock(Exit);
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
    }

    // ------------------------------------------------------------------ statements

    private void Statement(LStatement statement)
    {
        switch (statement)
        {
            case LBlock block:
                foreach (var inner in block.Statements)
                {
                    Statement(inner);
                }

                break;
            case LAssign assign:
                Store(Value(assign.Value), Address(assign.Target), assign.Target.Type);
                break;
            case LCallStatement:
                throw new NotSupportedException("Calls are emitted with C-038.");
            default:
                throw new NotSupportedException($"{statement.GetType().Name} is emitted with C-037.");
        }
    }

    // ------------------------------------------------------------------ storage

    private IrValue Address(LExpression variable) => variable switch
    {
        LLocalRef local => _locals[local.Local],
        LGlobalRef global => _module.Globals[global.Variable],
        LParamRef parameter => _parameterAddresses[parameter.Parameter],
        _ => throw new InvalidOperationException($"{variable.GetType().Name} is not a variable reference."),
    };

    /// <summary>Loads a value in SSA form: Booleans are compared <c>ne 0</c> after the load (CMP-305).</summary>
    private IrValue Load(IrValue address, TypeSymbol type)
    {
        var layout = TypeLayout.Of(type);
        var loaded = _w.Load(NewTemp(), layout.StorageType, address, layout.Alignment);
        return type is BooleanTypeSymbol ? _w.ICmp(NewTemp(), IrIntPredicate.Ne, loaded, new IrIntConstant((IntegerIrType)layout.StorageType, 0)) : loaded;
    }

    /// <summary>Stores an SSA value: <c>zext</c> for <c>Boolean</c>, <c>sext</c> (all ones) for C-style Booleans (CMP-305).</summary>
    private void Store(IrValue value, IrValue address, TypeSymbol type)
    {
        var layout = TypeLayout.Of(type);
        if (type is BooleanTypeSymbol boolean)
        {
            value = value is IrIntConstant constant
                ? new IrIntConstant((IntegerIrType)layout.StorageType, constant.Value.IsZero ? 0 : boolean.IsCStyle ? -1 : 1)
                : _w.Cast(NewTemp(), boolean.IsCStyle ? IrCastOperator.SExt : IrCastOperator.ZExt, value, layout.StorageType);
        }

        _w.Store(value, address, layout.Alignment);
    }

    // ------------------------------------------------------------------ expressions

    private IrValue Value(LExpression expression)
    {
        switch (expression)
        {
            case LConst constant:
                return IrEmitter.ValueConstant(constant.Value, constant.Type);
            case LLocalRef or LGlobalRef or LParamRef:
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
            case LSelect select:
            {
                var condition = Value(select.Condition);
                var whenTrue = Value(select.WhenTrue);
                return _w.Select(NewTemp(), condition, whenTrue, Value(select.WhenFalse));
            }

            case LCall:
                throw new NotSupportedException("Calls are emitted with C-038.");
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
        Start(zero);
        _w.Call(null, IrType.Void, _module.NoReturnHelper("_DivZeroError"), [], IrEmitter.NoReturnGroup);
        _w.Unreachable();
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
