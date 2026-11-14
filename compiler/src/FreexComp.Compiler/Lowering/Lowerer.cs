using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// Bound trees of one unit → lowered trees (architecture.md §3.2, CMP-116). The input must be free of binder errors
/// (the lowerer reports nothing; it throws on error nodes). This skeleton translates the M2.1 statement and expression
/// set; the structured statements and intrinsics are delegated to <see cref="ForRewriter"/>, <see cref="CaseRewriter"/>,
/// <see cref="IntrinsicRewriter"/> and <see cref="WriteRewriter"/>. Variables of enclosing routines become <see cref="LOuterRef"/>;
/// their owner gets a <see cref="LoweredFrame"/> (CMP-345); range and overflow checks come from <see cref="CheckRewriter"/>.
/// String constants become <see cref="LStringLiteral"/> (CMP-320).
/// </summary>
internal static class Lowerer
{
    public static LoweredUnit Lower(BindingResult result)
    {
        var bodies = result.Bodies.Select(b => (b.Syntax, Lowered: LowerBody(result, b))).ToImmutableArray();
        return new(result.Unit, [.. result.Routines.Select(r => LowerRoutine(result, r))], [.. bodies.Select(b => b.Lowered)])
        {
            Initialization = bodies.FirstOrDefault(b => b.Syntax is InitializationSectionSyntax).Lowered,
            Finalization = bodies.FirstOrDefault(b => b.Syntax is FinalizationSectionSyntax).Lowered,
            Statics = [.. result.Routines.SelectMany(r => r.Scope.Symbols.OfType<ConstantSymbol>()).Select(c => c.Storage).OfType<VariableSymbol>()],
        };
    }

    public static LoweredBody LowerRoutine(BindingResult result, BoundRoutineBody body)
    {
        var captured = result.GetCapturedVariables(body.Routine);
        var context = new LoweringContext(result.Types, body.Routine, body.Scope.Symbols.OfType<VariableSymbol>())
        {
            Output = OutputOf(result),
            Input = SystemVariable(result, "Input"),
            Captured = [.. captured],
            AddressTaken = body.AddressTaken,
        };
        var lowered = context.Finish(body.Body);
        // CMP-345: a frame when nested routines capture something of this routine, and for a nested routine with nested
        // routines of its own (field 0 continues the static link chain they walk)
        var needsFrame = !captured.IsEmpty || body.Routine.IsNested && !body.NestedRoutines.IsEmpty;
        return needsFrame ? lowered with { Frame = new LoweredFrame(captured) } : lowered;
    }

    /// <summary>The program block or a unit initialization/finalization section (globals only, no locals).</summary>
    public static LoweredBody LowerBody(BindingResult result, BoundBlock block) =>
        new LoweringContext(result.Types, routine: null, locals: []) { Output = OutputOf(result), Input = SystemVariable(result, "Input") }.Finish(block);

    /// <summary>Types held in memory as <c>[N x T]</c> or structs (big sets, static arrays, records): never SSA values (see the aggregate nodes).</summary>
    public static bool IsAggregate(TypeSymbol type) =>
        type is SetTypeSymbol { IsSmall: false } or ArrayTypeSymbol or RecordTypeSymbol or StringTypeSymbol { Variant: StringVariant.Short };

    /// <summary>The variable <c>Output</c> of the bound <c>System</c> (default file of <c>Write</c>, LANG-910), or null without one.</summary>
    private static VariableSymbol? OutputOf(BindingResult result) => SystemVariable(result, "Output");

    /// <summary>The interface variable <paramref name="name"/> of the bound <c>System</c> (<c>Output</c>, <c>Input</c>), or null.</summary>
    private static VariableSymbol? SystemVariable(BindingResult result, string name) =>
        result.System?.InterfaceMembers.OfType<VariableSymbol>().FirstOrDefault(v => IdentifierComparer.Instance.Equals(v.Name, name));
}

/// <summary>Per-body lowering state (locals, temporaries, loop labels) and the translation of plain nodes.</summary>
internal sealed class LoweringContext
{
    private readonly Dictionary<VariableSymbol, LLocal> _variables = [];
    private readonly List<LLocal> _locals = [];
    private readonly Stack<LLoopLabel> _loops = new();
    private int _tempCount;
    private int _valueCount;
    private int _loopCount;

    public LoweringContext(BuiltInTypes types, RoutineSymbol? routine, IEnumerable<VariableSymbol> locals)
    {
        Types = types;
        Routine = routine;
        foreach (var variable in locals)
        {
            var local = new LLocal(variable.Name, variable.Type, variable);
            _variables.Add(variable, local);
            _locals.Add(local);
        }

        RunError = new RuntimeHelper("_RunError", [types.Integer], ReturnType: null, IsNoReturn: true);
        Halt = new RuntimeHelper("_Halt", [types.Integer], ReturnType: null, IsNoReturn: true);
    }

    public BuiltInTypes Types { get; }

    public RoutineSymbol? Routine { get; }

    /// <summary><c>System.Output</c> as bound (LANG-910); null when the unit was bound without <c>System</c> (white-box tests).</summary>
    public VariableSymbol? Output { get; init; }

    /// <summary><c>System.Input</c> as bound (default file of <c>Read</c>/<c>Eof</c>, LANG-940); null without <c>System</c>.</summary>
    public VariableSymbol? Input { get; init; }

    /// <summary>This routine's variables, parameters and <c>Result</c> that nested routines reference (they live in the frame record).</summary>
    public ImmutableHashSet<Symbol> Captured { get; init; } = [];

    /// <summary>This routine's variables whose address is taken: never written directly as a hidden <c>Result</c> (C-062).</summary>
    public ImmutableHashSet<Symbol> AddressTaken { get; init; } = [];

    /// <summary><c>procedure _RunError(Code: Integer)</c>, noreturn (rtl-abi.md §9.1).</summary>
    public RuntimeHelper RunError { get; }

    /// <summary><c>procedure _Halt(Code: Integer)</c>, noreturn (CMP-356).</summary>
    public RuntimeHelper Halt { get; }

    public LoweredBody Finish(BoundBlock body)
    {
        var block = (LBlock)Lower(body);
        return ManagedRewriter.AddLifetimes(this, new LoweredBody(Routine, [.. _locals], block));
    }

    // ------------------------------------------------------------------ helpers for the rewriters

    /// <summary>A new compiler temporary <c>$tmpN</c> (storage).</summary>
    public LLocal NewTemp(TypeSymbol type)
    {
        var local = new LLocal("$tmp" + ++_tempCount, type, variable: null);
        _locals.Add(local);
        return local;
    }

    /// <summary>A new once-evaluated value <c>$vN</c> for <see cref="LTemp"/>.</summary>
    public LTempValue NewValue(TypeSymbol type) => new(type, ++_valueCount);

    public LLoopLabel NewLoop(string kind) => new(kind, ++_loopCount);

    /// <summary>Lowers a loop body with <paramref name="label"/> as the target of its <c>Break</c>/<c>Continue</c>.</summary>
    public LStatement LowerLoopBody(LLoopLabel label, BoundStatement body)
    {
        _loops.Push(label);
        try
        {
            return Lower(body);
        }
        finally
        {
            _loops.Pop();
        }
    }

    /// <summary>The type ordinal arithmetic is done in (LANG-222): integers per CMP-312, chars and Booleans in <c>Integer</c>.</summary>
    public TypeSymbol ComputationType(TypeSymbol ordinal) => ordinal switch
    {
        IntegerTypeSymbol integer => ConstantFolder.ComputationType(integer) switch
        {
            (32, true) => Types.Integer,
            (32, false) => Types.Cardinal,
            (_, true) => Types.Int64,
            _ => Types.UInt64,
        },
        CharTypeSymbol or BooleanTypeSymbol or EnumTypeSymbol => Types.Integer,
        _ => throw new InvalidOperationException($"'{ordinal.Name}' is not an ordinal type."),
    };

    public LExpression Convert(LExpression value, TypeSymbol type) => ReferenceEquals(value.Type, type) ? value : new LConvert(value, type);

    public LConst Constant(BigInteger value, TypeSymbol type) => new(new ConstantValue(value, type), type);

    public LCompare Compare(LCompareOperator op, LExpression left, LExpression right) => new(op, left, right, Types.Boolean);

    /// <summary>
    /// <c>x ± 1</c> (or <paramref name="step"/>) in the computation type, converted back to <paramref name="type"/>; with
    /// <paramref name="checks"/> the operation and the conversion back are checked per <see cref="CheckRewriter"/>.
    /// </summary>
    public LExpression Step(LExpression value, TypeSymbol type, LBinaryOperator op, LExpression? step = null, DirectiveState? checks = null)
    {
        var computation = ComputationType(type);
        var amount = step is null ? Constant(BigInteger.One, computation) : Convert(step, computation);
        return CheckRewriter.Convert(this, CheckRewriter.Binary(op, Convert(value, computation), amount, checks), type, checks);
    }

    public LStatement CallNoReturn(RuntimeHelper helper, LExpression argument) =>
        new LBlock([new LCallStatement(new LHelperCallee(helper), [argument]), new LUnreachable()]);

    // ------------------------------------------------------------------ statements

    public LStatement Lower(BoundStatement statement)
    {
        switch (statement)
        {
            case BoundBlock block:
                return new LBlock([.. block.Statements.Select(Lower)]);
            case BoundAssignmentStatement { Target.Type.IsManaged: true } assignment:
                return ManagedRewriter.Assign(this, Lower(assignment.Target), assignment.Value);
            case BoundAssignmentStatement { Target.Type: StringTypeSymbol { Variant: StringVariant.Short } } assignment:
                return ManagedRewriter.AssignShort(this, Lower(assignment.Target), assignment.Value);        // LANG-246 (C-063)
            case BoundAssignmentStatement assignment:
                return new LAssign(Lower(assignment.Target), Lower(assignment.Value));
            case BoundIfStatement ifStatement:
                return new LIf(Lower(ifStatement.Condition), Lower(ifStatement.Then), ifStatement.Else is null ? null : Lower(ifStatement.Else));
            case BoundWhileStatement loop:
            {
                var label = NewLoop("while");
                var condition = Lower(loop.Condition);
                return new LLoop(label, new LIf(condition, LowerLoopBody(label, loop.Body), new LBreak(label)), step: null);
            }

            case BoundRepeatStatement loop:
            {
                var label = NewLoop("repeat");
                var body = LowerLoopBody(label, loop.Body);
                return new LLoop(label, body, new LIf(Lower(loop.Condition), new LBreak(label), @else: null));
            }

            case BoundForStatement loop:
                return ForRewriter.Rewrite(this, loop);
            case BoundCaseStatement caseStatement:
                return CaseRewriter.Rewrite(this, caseStatement);
            case BoundBreakStatement:
                return new LBreak(_loops.Peek());
            case BoundContinueStatement:
                return new LContinue(_loops.Peek());
            case BoundExitStatement { Value: null }:
                return new LReturn();
            case BoundExitStatement { Value.Type.IsManaged: true } exit:
                return new LBlock([ManagedRewriter.Assign(this, new LLocalRef(_variables[Routine!.ResultVariable!]), exit.Value!), new LReturn()]);
            case BoundExitStatement { Value.Type: StringTypeSymbol { Variant: StringVariant.Short } } exit:
                return new LBlock([ManagedRewriter.AssignShort(this, new LLocalRef(_variables[Routine!.ResultVariable!]), exit.Value!), new LReturn()]);
            case BoundExitStatement exit:
                return new LBlock([new LAssign(new LLocalRef(_variables[Routine!.ResultVariable!]), Lower(exit.Value)), new LReturn()]);
            case BoundHaltStatement halt:
                return CallNoReturn(Halt, halt.ExitCode is null ? Constant(BigInteger.Zero, Types.Integer) : Lower(halt.ExitCode));
            case BoundCallStatement call:
                return CallStatement(call.Routine, call.Arguments);
            case BoundExpressionStatement { Expression: BoundCallExpression call }:
                return CallStatement(call.Routine, call.Arguments);
            case BoundExpressionStatement { Expression: BoundIntrinsicCallExpression { Intrinsic.IsAtomic: true } atomic }:
                return new LAssign(new LLocalRef(NewTemp(atomic.Type)), Lower(atomic));     // C-168: a discarded result
            case BoundIntrinsicCallStatement intrinsic:
                return IntrinsicRewriter.Rewrite(this, intrinsic);
            case BoundWriteStatement write:
                return WriteRewriter.Rewrite(this, write);
            default:
                throw new InvalidOperationException($"Cannot lower {statement.Kind}.");
        }
    }

    // ------------------------------------------------------------------ expressions

    public LExpression Lower(BoundExpression expression)
    {
        if (expression.HasErrors)
        {
            throw new InvalidOperationException("Cannot lower an erroneous expression.");
        }

        if (expression.ConstantValue is { } constant)
        {
            return constant.Value is string text ? new LStringLiteral(text, expression.Type) : new LConst(constant, expression.Type);
        }

        switch (expression)
        {
            case BoundVariableExpression { Variable: { IsGlobal: true } global }:
                return new LGlobalRef(global);
            case BoundVariableExpression variable:
                return _variables.TryGetValue(variable.Variable, out var local)
                    ? new LLocalRef(local)
                    : new LOuterRef(variable.Variable, variable.Variable.Type);
            case BoundParameterExpression parameter:
                return ReferenceEquals(parameter.Parameter.ContainingSymbol, Routine)
                    ? new LParamRef(parameter.Parameter)
                    : new LOuterRef(parameter.Parameter, parameter.Parameter.Type);
            case BoundConversionExpression { Type: StringTypeSymbol } conversion when !ReferenceEquals(conversion.Type, conversion.Operand.Type):
                return ManagedRewriter.Value(this, conversion);                         // LANG-245 (CMP-324, C-063)
            case BoundConversionExpression { IsExplicit: true, Type: PointerTypeSymbol { PointedType: not null } pointer, Operand.Type: StringTypeSymbol } conversion:
                return StringConversions.DataPointer(this, Lower(conversion.Operand), pointer);    // LANG-290: PChar(s)
            case BoundBinaryExpression { OperatorKind: BoundBinaryOperatorKind.Add, Type: StringTypeSymbol } concatenation:
                return ManagedRewriter.Value(this, concatenation);                      // LANG-243 (CMP-324)
            case BoundIndexExpression { IsStringElement: true } element:
                return StringElement(element);
            case BoundIndexExpression { Array.Type: DynamicArrayTypeSymbol } element:
                // ABI-042 / CMP-330/331: element i of the block (no copy-on-write), checked unsigned against the length under {$R+}
                return new LArrayElement(Lower(element.Array), Convert(Lower(element.Index), Types.Int64), CheckRewriter.StateOf(element).RangeChecks, element.Type);
            case BoundConversionExpression { Type: SetTypeSymbol target, Operand.Type: SetTypeSymbol } conversion:
                return SetRewriter.Convert(this, Lower(conversion.Operand), target);
            case BoundConversionExpression { IsExplicit: true, Type: PointerTypeSymbol, Operand.Type: { IsOrdinal: true, Size: < 8 } source } conversion:
                // LANG-290: an ordinal is extended to 64 bits by its own signedness before it becomes a pointer
                return Convert(Convert(Lower(conversion.Operand), source is IntegerTypeSymbol { IsSigned: true } ? Types.Int64 : Types.UInt64), conversion.Type);
            case BoundConversionExpression conversion:
                return conversion.IsExplicit
                    ? Convert(Lower(conversion.Operand), conversion.Type)
                    : CheckRewriter.Convert(this, Lower(conversion.Operand), conversion.Type, CheckRewriter.StateOf(conversion));
            case BoundVariableTypecastExpression typecast:
                return new LView(Lower(typecast.Operand), typecast.Type);              // LANG-290: the variable's storage as another type
            case BoundUnaryExpression { OperatorKind: BoundUnaryOperatorKind.Identity } unary:
                return Lower(unary.Operand);
            case BoundUnaryExpression { OperatorKind: BoundUnaryOperatorKind.Negate } unary:
                return CheckRewriter.Negate(Lower(unary.Operand), CheckRewriter.StateOf(unary));
            case BoundUnaryExpression unary:
                return new LUnary(LUnaryOperator.Not, Lower(unary.Operand));
            case BoundBinaryExpression binary:
                return Binary(binary);
            case BoundCallExpression call:
                return Call(call.Routine, call.Arguments);
            case BoundIntrinsicCallExpression { Intrinsic.Intrinsic: IntrinsicKind.Copy } copy:
                return ManagedRewriter.Value(this, copy);                               // C-062: a string value in a temporary
            case BoundIntrinsicCallExpression intrinsic:
                return IntrinsicRewriter.Rewrite(this, intrinsic);
            case BoundSetConstructor constructor:
                return SetRewriter.Constructor(this, constructor);
            case BoundIndexExpression index:
                return Index(index);
            case BoundFieldAccessExpression field:
                return new LField(Lower(field.Record), field.Field);                     // CMP-307
            case BoundAddressOfExpression { Routine: { } routine } addressOf:
                return new LRoutineAddress(routine, addressOf.Type);                    // CMP-340
            case BoundAddressOfExpression addressOf:
                return new LAddrOf(Lower(addressOf.Operand!), addressOf.Type);
            case BoundDereferenceExpression dereference:
                return new LDeref(Lower(dereference.Pointer), dereference.Type);
            case BoundPointerArithmeticExpression arithmetic:
                return PointerArithmetic(arithmetic);
            default:
                throw new InvalidOperationException($"Cannot lower {expression.Kind}.");
        }
    }

    /// <summary>
    /// CMP-308: element <c>index − Low</c> in <c>Int64</c> (sign- or zero-extended by the index type; folded for a constant). Under
    /// <c>{$R+}</c> the index is checked against the declared bounds in its computation type when its type can lie outside
    /// (CMP-331, CMP-314); constant indexes were checked by the binder (E2032).
    /// </summary>
    private LExpression Index(BoundIndexExpression index)
    {
        var array = index.ArrayType;
        var target = Lower(index.Array);
        var value = Lower(index.Index);
        if (value is LConst { Value.Value: var constant })
        {
            var ordinal = constant is bool b ? (b ? BigInteger.One : BigInteger.Zero) : (BigInteger)constant!;
            return new LIndex(target, Constant(ordinal - array.Low, Types.Int64), array.ElementType);
        }

        if (CheckRewriter.StateOf(index).RangeChecks && Ordinals.Bounds(value.Type) is { } source && (source.Low < array.Low || source.High > array.High))
        {
            var computation = ComputationType(value.Type);
            var bounds = Ordinals.Bounds(computation)!.Value;
            value = new LRangeCheck(Convert(value, computation), BigInteger.Max(array.Low, bounds.Low), BigInteger.Min(array.High, bounds.High));
        }

        var wide = Convert(value, Types.Int64);
        return new LIndex(target, array.Low.IsZero ? wide : new LBinary(LBinaryOperator.Subtract, wide, Constant(array.Low, Types.Int64)), array.ElementType);
    }

    /// <summary>
    /// ABI-024: character <c>i − 1</c> (in <c>Int64</c>) of the string; under <c>{$R+}</c> checked against the length (CMP-331).
    /// A constant index is folded.
    /// </summary>
    private LExpression StringElement(BoundIndexExpression element)
    {
        var target = Lower(element.Array);
        var index = Lower(element.Index);
        if (element.Array.Type is StringTypeSymbol { Variant: StringVariant.Short } shortString)
        {
            // C-063: byte i of the storage (0 = the length byte); under {$R+} checked against 0..capacity like a static array
            var bytes = StringConversions.ShortBytes(this, target, shortString);
            if (index is LConst { Value.Value: BigInteger position })
            {
                return new LIndex(bytes, Constant(position, Types.Int64), Types.AnsiChar);
            }

            if (CheckRewriter.StateOf(element).RangeChecks)
            {
                var computation = ComputationType(index.Type);
                index = new LRangeCheck(Convert(index, computation), BigInteger.Zero, shortString.MaxLength);
            }

            return new LIndex(bytes, Convert(index, Types.Int64), Types.AnsiChar);
        }

        LExpression zeroBased = index is LConst { Value.Value: BigInteger constant }
            ? Constant(constant - 1, Types.Int64)
            : new LBinary(LBinaryOperator.Subtract, Convert(index, Types.Int64), Constant(BigInteger.One, Types.Int64));
        return new LStringIndex(target, zeroBased, CheckRewriter.StateOf(element).RangeChecks, element.Type);
    }

    /// <summary>
    /// CMP-342: <c>p ± n</c> = GEP over the element type (bytes for the untyped <c>Pointer</c>); <c>p - q</c> = the byte distance
    /// (<c>ptrtoint</c>, <c>sub</c>) divided by the element size.
    /// </summary>
    private LExpression PointerArithmetic(BoundPointerArithmeticExpression arithmetic)
    {
        var pointer = Lower(arithmetic.Pointer);
        var offset = Lower(arithmetic.Offset);
        var size = Pointers.ElementSize(arithmetic.PointerType);
        if (arithmetic.Difference)
        {
            var bytes = new LBinary(LBinaryOperator.Subtract, Convert(pointer, Types.NativeInt), Convert(offset, Types.NativeInt));
            return size == 1 ? bytes : new LBinary(LBinaryOperator.IntegerDivide, bytes, Constant(size, Types.NativeInt));
        }

        return Offset(pointer, arithmetic.Subtract ? Negated(offset) : offset, arithmetic.PointerType, arithmetic.Type);
    }

    /// <summary><c>-x</c> without overflow check; a constant is negated at compile time.</summary>
    public LExpression Negated(LExpression value) =>
        value is LConst { Value.Value: BigInteger constant } ? Constant(-constant, value.Type) : new LUnary(LUnaryOperator.Negate, value);

    /// <summary><paramref name="pointer"/> moved by <paramref name="index"/> elements of <paramref name="pointerType"/> (CMP-342).</summary>
    public LExpression Offset(LExpression pointer, LExpression index, PointerTypeSymbol pointerType, TypeSymbol resultType) =>
        new LPointerOffset(pointer, index, pointerType.PointedType ?? Types.Byte, resultType);

    private LExpression Binary(BoundBinaryExpression binary)
    {
        if (binary.OperandType is SetTypeSymbol)
        {
            return SetRewriter.Binary(this, binary);
        }

        if (binary.OperandType is StringTypeSymbol)
        {
            return ManagedRewriter.Compare(this, binary);                               // LANG-244 (CMP-324)
        }

        var left = Lower(binary.Left);
        var right = Lower(binary.Right);
        LCompareOperator? comparison = binary.OperatorKind switch
        {
            BoundBinaryOperatorKind.Equal => LCompareOperator.Equal,
            BoundBinaryOperatorKind.NotEqual => LCompareOperator.NotEqual,
            BoundBinaryOperatorKind.Less => LCompareOperator.Less,
            BoundBinaryOperatorKind.LessOrEqual => LCompareOperator.LessOrEqual,
            BoundBinaryOperatorKind.Greater => LCompareOperator.Greater,
            BoundBinaryOperatorKind.GreaterOrEqual => LCompareOperator.GreaterOrEqual,
            _ => null,
        };
        if (comparison is { } op)
        {
            return Compare(op, left, right);
        }

        return CheckRewriter.Binary(binary.OperatorKind switch
        {
            BoundBinaryOperatorKind.Add => LBinaryOperator.Add,
            BoundBinaryOperatorKind.Subtract => LBinaryOperator.Subtract,
            BoundBinaryOperatorKind.Multiply => LBinaryOperator.Multiply,
            BoundBinaryOperatorKind.Divide => LBinaryOperator.Divide,
            BoundBinaryOperatorKind.IntegerDivide => LBinaryOperator.IntegerDivide,
            BoundBinaryOperatorKind.Modulo => LBinaryOperator.Modulo,
            BoundBinaryOperatorKind.And => LBinaryOperator.And,
            BoundBinaryOperatorKind.Or => LBinaryOperator.Or,
            BoundBinaryOperatorKind.Xor => LBinaryOperator.Xor,
            BoundBinaryOperatorKind.ConditionalAnd => LBinaryOperator.ConditionalAnd,
            BoundBinaryOperatorKind.ConditionalOr => LBinaryOperator.ConditionalOr,
            BoundBinaryOperatorKind.Shl => LBinaryOperator.Shl,
            BoundBinaryOperatorKind.Shr => LBinaryOperator.Shr,
            _ => throw new InvalidOperationException($"Unknown operator {binary.OperatorKind}."),
        }, left, right, CheckRewriter.StateOf(binary));
    }

    /// <summary>
    /// Value and <c>const</c> arguments as values; <c>var</c>/<c>out</c> arguments as the variable's address. ABI-080: an aggregate
    /// of size 1, 2, 4 or 8 stays an aggregate operand (the emitter loads it as <c>iN</c>); a larger value aggregate is copied
    /// into a fresh temporary whose address is passed, a larger <c>const</c> one passes the original's address (a copy when
    /// the original may be less aligned than its type, CMP-309). A <c>var</c>/<c>out</c> argument less aligned than its type (a
    /// field of a packed record) is passed as an aligned temporary copied in before and back after the call
    /// (<paramref name="writeBacks"/>), since the callee accesses it with the type's alignment (CMP-309).
    /// </summary>
    private ImmutableArray<LExpression> Arguments(RoutineSymbol routine, ImmutableArray<BoundExpression> arguments, List<LStatement> writeBacks) =>
        [.. arguments.Select((argument, i) => Argument(routine.Parameters[i], Lower(argument), writeBacks))];

    private LExpression Argument(ParameterSymbol parameter, LExpression argument, List<LStatement> writeBacks)
    {
        var aligned = FunctionEmitter.AccessAlignment(argument) >= TypeLayout.Of(parameter.Type).Alignment;
        if (parameter.IsByReference)
        {
            if (aligned)
            {
                return new LAddrOf(argument, Types.Pointer);
            }

            var temporary = new LLocalRef(NewTemp(parameter.Type));
            writeBacks.Add(Copy(argument, temporary));
            return new LSequence(Copy(temporary, argument), new LAddrOf(temporary, Types.Pointer));
        }

        if (!IrEmitter.IsPassedByAddress(parameter))
        {
            return argument;
        }

        if (parameter.Mode == ParameterMode.Const && aligned)
        {
            return new LAddrOf(argument, Types.Pointer);
        }

        // ABI-080 / CMP-325 rule 6: the caller's copy of a managed record or array holds its own references (finalised with the
        // caller's temporaries)
        var copy = new LLocalRef(NewTemp(parameter.Type));
        return new LSequence(Copy(copy, argument), new LAddrOf(copy, Types.Pointer));
    }

    /// <summary><c>target := value</c> between storage of one type: a plain copy, or the managed assignment helper.</summary>
    private LStatement Copy(LExpression target, LExpression value) =>
        target.Type.IsManaged ? ManagedRewriter.Assign(this, target, value) : new LAssign(target, value);

    /// <summary>
    /// A call with a result. ABI-081: an aggregate result is assigned to a fresh temporary (the emitter passes its address as
    /// the hidden <c>Result</c> or stores the returned <c>iN</c>), which is the value; so is any result when arguments are
    /// written back after the call.
    /// </summary>
    private LExpression Call(RoutineSymbol routine, ImmutableArray<BoundExpression> arguments)
    {
        var writeBacks = new List<LStatement>();
        var call = new LCall(new LRoutineCallee(routine), Arguments(routine, arguments, writeBacks));
        if (!Lowerer.IsAggregate(call.Type) && !call.Type.IsManaged && writeBacks.Count == 0)
        {
            return call;
        }

        // a managed result goes to a managed temporary (CMP-325 rule 4/5), zeroed in the entry block and finalised at exit
        var result = new LLocalRef(NewTemp(call.Type));
        return new LSequence(new LBlock([new LAssign(result, call), .. writeBacks]), result);
    }

    /// <summary>
    /// CMP-325 rule 4: <c>target := F(args)</c> for a managed result. The target's address is the hidden <c>Result</c> only when
    /// <see cref="ReceivesResultDirectly"/>; otherwise a temporary receives it and is assigned with <c>_UStrAsg</c>.
    /// </summary>
    public LStatement CallInto(LExpression target, RoutineSymbol routine, ImmutableArray<BoundExpression> arguments)
    {
        var writeBacks = new List<LStatement>();
        var call = new LCall(new LRoutineCallee(routine), Arguments(routine, arguments, writeBacks));
        if (ReceivesResultDirectly(target, call))
        {
            return new LBlock([new LAssign(target, call), .. writeBacks]);
        }

        var result = new LLocalRef(NewTemp(call.Type));
        return new LBlock([new LAssign(result, call), .. writeBacks, ManagedRewriter.Assign(this, target, result)]);
    }

    /// <summary>
    /// CMP-325 rule 4: a declared local of this body (not <c>Result</c>, which is the caller's storage) that no nested routine
    /// captures, whose address is never taken (the callee could reach it through a pointer, C-062) and that the call's
    /// arguments do not mention.
    /// </summary>
    private bool ReceivesResultDirectly(LExpression target, LCall call) =>
        target is LLocalRef { Local: { Variable: { } variable } local }
        && !ReferenceEquals(variable, Routine?.ResultVariable)
        && !Captured.Contains(variable)
        && !AddressTaken.Contains(variable)
        && !call.Arguments.Any(a => ManagedRewriter.References(a, local));

    /// <summary>A call statement; a discarded aggregate or managed result still needs storage (ABI-081).</summary>
    private LStatement CallStatement(RoutineSymbol routine, ImmutableArray<BoundExpression> arguments)
    {
        if (routine.ReturnType is { } type && (Lowerer.IsAggregate(type) || type.IsManaged))
        {
            return ((LSequence)Call(routine, arguments)).First;
        }

        var writeBacks = new List<LStatement>();
        var call = new LCallStatement(new LRoutineCallee(routine), Arguments(routine, arguments, writeBacks));
        return writeBacks.Count == 0 ? call : new LBlock([call, .. writeBacks]);
    }
}