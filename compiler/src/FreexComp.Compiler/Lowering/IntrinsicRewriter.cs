using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// CMP-412 / intrinsics.md §1 for the M2.1 intrinsics that are not constant: primitive operations (operands used twice
/// are evaluated once through <see cref="LTemp"/>), <c>Inc</c>/<c>Dec</c> as an inline assignment, <c>RunError</c> as a
/// noreturn helper call. Constant calls (always <c>Low</c>/<c>High</c>/<c>SizeOf</c>) never get here: they are constants.
/// Range and overflow checks follow the switches at the call (<see cref="CheckRewriter"/>): <c>Chr</c>, <c>Succ</c>/<c>Pred</c>, <c>Inc</c>/<c>Dec</c>,
/// <c>Abs</c>, <c>Sqr</c>; <c>Ord</c>, <c>Lo</c>, <c>Hi</c>, <c>Swap</c> are never checked.
/// </summary>
internal static class IntrinsicRewriter
{
    public static LExpression Rewrite(LoweringContext context, BoundIntrinsicCallExpression call)
    {
        if (call.Intrinsic.IsAtomic)
        {
            return Atomic(context, call);
        }

        if (call.Intrinsic.Intrinsic is IntrinsicKind.Pos or IntrinsicKind.UpCase)
        {
            return StringRewriter.Function(context, call);
        }

        var argument = call.Arguments[0];
        var operand = context.Lower(argument);
        var state = CheckRewriter.StateOf(call);
        switch (call.Intrinsic.Intrinsic)
        {
            case IntrinsicKind.Succ:
                return context.Step(operand, argument.Type, LBinaryOperator.Add, checks: state);
            case IntrinsicKind.Pred:
                return context.Step(operand, argument.Type, LBinaryOperator.Subtract, checks: state);
            case IntrinsicKind.Chr:
                return CheckRewriter.Convert(context, operand, call.Type, state);
            case IntrinsicKind.Ord or IntrinsicKind.Lo:
                return context.Convert(operand, call.Type);
            case IntrinsicKind.Odd:
                return context.Compare(LCompareOperator.NotEqual,
                    new LBinary(LBinaryOperator.And, operand, context.Constant(BigInteger.One, argument.Type)), context.Constant(BigInteger.Zero, argument.Type));
            case IntrinsicKind.Abs when argument.Type is RealTypeSymbol:
                return new LUnary(LUnaryOperator.Abs, operand);
            case IntrinsicKind.Abs:
                return Once(context, operand, v => new LSelect(
                    context.Compare(LCompareOperator.Less, v(), context.Constant(BigInteger.Zero, operand.Type)), CheckRewriter.Negate(v(), state), v()));
            case IntrinsicKind.Sqr:
                return Once(context, operand, v => CheckRewriter.Binary(LBinaryOperator.Multiply, v(), v(), state));
            case IntrinsicKind.Hi:
            {
                var computation = context.ComputationType(argument.Type);
                return context.Convert(new LBinary(LBinaryOperator.Shr, context.Convert(operand, computation), context.Constant(8, computation)), call.Type);
            }

            case IntrinsicKind.Swap:
            {
                var t = context.ComputationType(argument.Type);
                var swapped = Once(context, context.Convert(operand, t), v => new LBinary(LBinaryOperator.Or,
                    new LBinary(LBinaryOperator.Shl, new LBinary(LBinaryOperator.And, v(), context.Constant(0xFF, t)), context.Constant(8, t)),
                    new LBinary(LBinaryOperator.And, new LBinary(LBinaryOperator.Shr, v(), context.Constant(8, t)), context.Constant(0xFF, t))));
                return context.Convert(swapped, call.Type);
            }

            case IntrinsicKind.Length when argument.Type is DynamicArrayTypeSymbol:
                return new LArrayLength(operand, context.Types.Integer);                         // ABI-041 (C-064)
            case IntrinsicKind.High when argument.Type is DynamicArrayTypeSymbol:
                return new LBinary(LBinaryOperator.Subtract, new LArrayLength(operand, context.Types.Integer), context.Constant(BigInteger.One, context.Types.Integer));
            case IntrinsicKind.Length or IntrinsicKind.High when argument.Type is StringTypeSymbol { Variant: StringVariant.Short }:
                return StringConversions.ShortLength(context, operand);                         // C-063: the length byte
            case IntrinsicKind.Length or IntrinsicKind.High when argument.Type is StringTypeSymbol:
                return new LStringLength(operand, context.Types.Integer);                       // ABI-023 (High = Length, intrinsics.md §1)
            case IntrinsicKind.Assigned:
                return context.Compare(LCompareOperator.NotEqual, operand, Nil(context));
            case IntrinsicKind.Ptr:
                return new LConvert(operand, call.Type);                                     // inttoptr (CMP-313)
            case IntrinsicKind.Sqrt:
                return new LUnary(LUnaryOperator.Sqrt, operand);                             // llvm.sqrt.f64 (CMP-412)
            case IntrinsicKind.Trunc or IntrinsicKind.Round or IntrinsicKind.Int or IntrinsicKind.Frac:
            {
                // CMP-412: RTL helpers _Trunc/_Round (→ Int64) and _Int/_Frac (→ Double), rtl-abi.md §9.10; the operand is a Double
                var helper = new RuntimeHelper("_" + call.Intrinsic.Intrinsic, [context.Types.Double], call.Type, IsNoReturn: false);
                return new LCall(new LHelperCallee(helper), [operand]);
            }

            default:
                throw new InvalidOperationException($"{call.Intrinsic.Name} with a non-constant result is not an M2.1 intrinsic.");
        }
    }

    /// <summary>
    /// CMP-412 / ABI-172 (C-168): <c>AtomicExchange</c> → <c>atomicrmw xchg</c>, <c>AtomicCmpExchange(t, new, cmp)</c> →
    /// <c>cmpxchg t, cmp, new</c> (both return the previous value); <c>AtomicIncrement</c>/<c>AtomicDecrement(t[, n])</c> →
    /// <c>atomicrmw add|sub t, n</c> then <c>± n</c> again for the new value (wrapping, never checked). <c>n</c> is converted to the
    /// target type; on a pointer target the operation works on its <c>NativeInt</c> view with <c>n × SizeOf(element)</c> (bytes
    /// for the untyped <c>Pointer</c>) and the new value is converted back. A non-constant <c>n</c> is evaluated once, first.
    /// </summary>
    private static LExpression Atomic(LoweringContext context, BoundIntrinsicCallExpression call)
    {
        var target = context.Lower(call.Arguments[0]);
        switch (call.Intrinsic.Intrinsic)
        {
            case IntrinsicKind.AtomicExchange:
                return new LAtomic(LAtomicOperator.Exchange, target, context.Lower(call.Arguments[1]), comparand: null);
            case IntrinsicKind.AtomicCmpExchange:
                return new LAtomic(LAtomicOperator.CompareExchange, target, context.Lower(call.Arguments[1]), context.Lower(call.Arguments[2]));
        }

        var add = call.Intrinsic.Intrinsic == IntrinsicKind.AtomicIncrement;
        var count = call.Arguments.Length == 1 ? context.Constant(BigInteger.One, context.Types.Integer) : context.Lower(call.Arguments[1]);
        LExpression amount;
        if (target.Type is PointerTypeSymbol pointer)
        {
            target = new LView(target, context.Types.NativeInt);
            var size = Pointers.ElementSize(pointer);
            amount = count is LConst { Value.Value: BigInteger n }
                ? context.Constant(Wrap(n * size, context.Types.NativeInt), context.Types.NativeInt)
                : size == 1 ? context.Convert(count, context.Types.NativeInt)
                : new LBinary(LBinaryOperator.Multiply, context.Convert(count, context.Types.NativeInt), context.Constant(size, context.Types.NativeInt));
        }
        else
        {
            amount = count is LConst { Value.Value: BigInteger n }
                ? context.Constant(Wrap(n, target.Type), target.Type)
                : context.Convert(count, target.Type);
        }

        var op = add ? LAtomicOperator.Add : LAtomicOperator.Subtract;
        var binary = add ? LBinaryOperator.Add : LBinaryOperator.Subtract;
        LExpression updated = amount is LConst
            ? new LBinary(binary, new LAtomic(op, target, amount, comparand: null), amount)
            : Once(context, amount, n => new LBinary(binary, new LAtomic(op, target, n(), comparand: null), n()));
        return context.Convert(updated, call.Type);
    }

    /// <summary>A constant reduced to the bits of an integer <paramref name="type"/> (the wrap of an unchecked conversion).</summary>
    private static BigInteger Wrap(BigInteger value, TypeSymbol type)
    {
        var bits = (int)type.Size * 8;
        var modulus = BigInteger.One << bits;
        var low = ((value % modulus) + modulus) % modulus;
        return ((IntegerTypeSymbol)type).IsSigned && low >= modulus >> 1 ? low - modulus : low;
    }

    private static LConst Nil(LoweringContext context) => new(new ConstantValue(null, context.Types.Pointer), context.Types.Pointer);

    /// <summary>rtl-abi.md §9.2 helpers: <c>_GetMem</c>, <c>_AllocZero</c> (Size → Pointer), <c>_FreeMem</c>, <c>_ReallocMem</c>.</summary>
    private static RuntimeHelper Memory(LoweringContext context, string name) => name switch
    {
        "_FreeMem" => new RuntimeHelper(name, [context.Types.Pointer], ReturnType: null, IsNoReturn: false),
        "_ReallocMem" => new RuntimeHelper(name, [context.Types.Pointer, context.Types.NativeInt], context.Types.Pointer, IsNoReturn: false),
        _ => new RuntimeHelper(name, [context.Types.NativeInt], context.Types.Pointer, IsNoReturn: false),
    };

    /// <summary>
    /// LANG-268 / CMP-340: <c>New(p)</c> = <c>p := _AllocZero(SizeOf(p^))</c> (an empty record occupies one byte, ABI-060);
    /// <c>Dispose(p)</c> = <c>_FreeMem(p)</c> (no managed types yet); <c>GetMem</c>/<c>FreeMem</c>/<c>ReallocMem</c> map to their helpers.
    /// </summary>
    private static LStatement MemoryStatement(LoweringContext context, BoundIntrinsicCallStatement call)
    {
        var pointer = call.Arguments[0];
        switch (call.Intrinsic.Intrinsic)
        {
            case IntrinsicKind.New:
            {
                var size = Math.Max(1, ((PointerTypeSymbol)pointer.Type).PointedType!.Size);
                var allocate = new LCall(new LHelperCallee(Memory(context, "_AllocZero")), [context.Constant(size, context.Types.NativeInt)]);
                return new LAssign(context.Lower(pointer), allocate);
            }

            case IntrinsicKind.GetMem:
                return new LAssign(context.Lower(pointer), new LCall(new LHelperCallee(Memory(context, "_GetMem")), [context.Lower(call.Arguments[1])]));
            case IntrinsicKind.ReallocMem:
            {
                var target = context.Lower(pointer);
                var resized = new LCall(new LHelperCallee(Memory(context, "_ReallocMem")),
                    [context.Convert(context.Lower(pointer), context.Types.Pointer), context.Lower(call.Arguments[1])]);
                return new LAssign(target, resized);
            }

            default:                                                                         // Dispose, FreeMem
                return new LCallStatement(new LHelperCallee(Memory(context, "_FreeMem")), [context.Convert(context.Lower(pointer), context.Types.Pointer)]);
        }
    }

    public static LStatement Rewrite(LoweringContext context, BoundIntrinsicCallStatement call)
    {
        switch (call.Intrinsic.Intrinsic)
        {
            case IntrinsicKind.Inc or IntrinsicKind.Dec when call.Arguments[0].Type is PointerTypeSymbol pointer:
            {
                // intrinsics.md: a typed pointer moves by n elements (CMP-342)
                var target = call.Arguments[0];
                LExpression step = call.Arguments.Length == 2
                    ? context.Convert(context.Lower(call.Arguments[1]), context.Types.NativeInt)
                    : context.Constant(BigInteger.One, context.Types.NativeInt);
                if (call.Intrinsic.Intrinsic == IntrinsicKind.Dec)
                {
                    step = context.Negated(step);
                }

                return new LAssign(context.Lower(target), context.Offset(context.Lower(target), step, pointer, pointer));
            }

            case IntrinsicKind.Inc or IntrinsicKind.Dec:
            {
                var target = call.Arguments[0];
                var step = call.Arguments.Length == 2 ? context.Lower(call.Arguments[1]) : null;
                var op = call.Intrinsic.Intrinsic == IntrinsicKind.Inc ? LBinaryOperator.Add : LBinaryOperator.Subtract;
                return new LAssign(context.Lower(target), context.Step(context.Lower(target), target.Type, op, step, CheckRewriter.StateOf(call)));
            }

            case IntrinsicKind.New or IntrinsicKind.Dispose or IntrinsicKind.GetMem or IntrinsicKind.FreeMem or IntrinsicKind.ReallocMem:
                return MemoryStatement(context, call);

            case IntrinsicKind.FillChar:
                return PositiveCount(context, context.Lower(call.Arguments[1]),
                    count => new LMemSet(context.Lower(call.Arguments[0]), count, context.Convert(context.Lower(call.Arguments[2]), context.Types.Byte)));

            case IntrinsicKind.Move:
                return PositiveCount(context, context.Lower(call.Arguments[2]),
                    count => new LMemMove(context.Lower(call.Arguments[0]), context.Lower(call.Arguments[1]), count));

            case IntrinsicKind.Include or IntrinsicKind.Exclude:
                return SetRewriter.IncludeExclude(context, call, call.Intrinsic.Intrinsic == IntrinsicKind.Include);

            case IntrinsicKind.RunError:
                return context.CallNoReturn(context.RunError,
                    call.Arguments.IsEmpty ? context.Constant(BigInteger.Zero, context.Types.Integer) : context.Lower(call.Arguments[0]));
            case IntrinsicKind.Assert:
                return Assert(context, call);
            case IntrinsicKind.SetLength or IntrinsicKind.Delete or IntrinsicKind.Insert or IntrinsicKind.Str or IntrinsicKind.Val:
                return StringRewriter.Statement(context, call);
            default:
                throw new InvalidOperationException($"Unknown intrinsic statement {call.Intrinsic.Name}.");
        }
    }

    /// <summary>
    /// intrinsics.md §5 / CMP-412: <c>if not c then _Assert(msg or nil, 'file', line)</c> under <c>{$C+}</c> at the statement's first
    /// token; nothing (the condition is not evaluated) under <c>{$C-}</c>. The file is the name of the source file holding the
    /// <c>Assert</c> token, without directory (ABI-103 prints it so; C-057 decision: the compiler passes no directory), the line
    /// is 1-based.
    /// </summary>
    private static LStatement Assert(LoweringContext context, BoundIntrinsicCallStatement call)
    {
        var token = call.Syntax.FirstToken();
        if (!token.DirectiveState.Assertions)
        {
            return new LBlock([]);
        }

        var types = context.Types;
        var location = DiagnosticBag.LocationOf(token);
        var helper = new RuntimeHelper("_Assert", [types.Pointer, types.Pointer, types.Integer], ReturnType: null, IsNoReturn: true);
        // a constant message is a literal; a string value (C-060) is passed as is (a borrowed reference, valid during the call)
        LExpression message = call.Arguments.Length == 2 ? context.Lower(call.Arguments[1]) : Nil(context);
        var file = new LStringLiteral(System.IO.Path.GetFileName(location.Path), types.String);
        var line = context.Constant(location.LineSpan.Start.Line + 1, types.Integer);
        var fail = new LBlock([new LCallStatement(new LHelperCallee(helper), [message, file, line]), new LUnreachable()]);
        return new LIf(new LUnary(LUnaryOperator.Not, context.Lower(call.Arguments[0])), fail, @else: null);
    }

    /// <summary>
    /// C-176 (intrinsics.md, rtl-abi.md: <c>FillChar</c>/<c>Move</c> with a count ≤ 0 do nothing): a constant count &gt; 0 is used
    /// as is, a constant count ≤ 0 gives nothing; any other count is stored once in a temporary that guards the operation
    /// (<c>t := count; if t &gt; 0 then op(t)</c>). The count is evaluated before the variable arguments' addresses.
    /// </summary>
    private static LStatement PositiveCount(LoweringContext context, LExpression count, Func<LExpression, LStatement> operation)
    {
        if (count is LConst { Value.Value: BigInteger constant })
        {
            return constant.Sign > 0 ? operation(count) : new LBlock([]);
        }

        var temp = context.NewTemp(count.Type);
        return new LBlock([
            new LAssign(new LLocalRef(temp), count),
            new LIf(context.Compare(LCompareOperator.Greater, new LLocalRef(temp), context.Constant(BigInteger.Zero, count.Type)), operation(new LLocalRef(temp)), @else: null),
        ]);
    }

    /// <summary>Evaluates <paramref name="value"/> once; <paramref name="body"/> reads it through the given reference factory.</summary>
    private static LTemp Once(LoweringContext context, LExpression value, Func<Func<LExpression>, LExpression> body)
    {
        var temp = context.NewValue(value.Type);
        return new LTemp(temp, value, body(() => new LTempRef(temp)));
    }
}
