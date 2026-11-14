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

            case IntrinsicKind.Assigned:
                return context.Compare(LCompareOperator.NotEqual, operand, Nil(context));
            case IntrinsicKind.Ptr:
                return new LConvert(operand, call.Type);                                     // inttoptr (CMP-313)
            default:
                throw new InvalidOperationException($"{call.Intrinsic.Name} with a non-constant result is not an M2.1 intrinsic.");
        }
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
                return new LMemSet(context.Lower(call.Arguments[0]), context.Lower(call.Arguments[1]), context.Convert(context.Lower(call.Arguments[2]), context.Types.Byte));

            case IntrinsicKind.Move:
                return new LMemMove(context.Lower(call.Arguments[0]), context.Lower(call.Arguments[1]), context.Lower(call.Arguments[2]));

            case IntrinsicKind.Include or IntrinsicKind.Exclude:
                return SetRewriter.IncludeExclude(context, call, call.Intrinsic.Intrinsic == IntrinsicKind.Include);

            case IntrinsicKind.RunError:
                return context.CallNoReturn(context.RunError,
                    call.Arguments.IsEmpty ? context.Constant(BigInteger.Zero, context.Types.Integer) : context.Lower(call.Arguments[0]));
            case IntrinsicKind.Assert:
                return Assert(context, call);
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
        LExpression message = call.Arguments.Length == 2 ? new LStringLiteral((string)call.Arguments[1].ConstantValue!.Value!, types.String) : Nil(context);
        var file = new LStringLiteral(System.IO.Path.GetFileName(location.Path), types.String);
        var line = context.Constant(location.LineSpan.Start.Line + 1, types.Integer);
        var fail = new LBlock([new LCallStatement(new LHelperCallee(helper), [message, file, line]), new LUnreachable()]);
        return new LIf(new LUnary(LUnaryOperator.Not, context.Lower(call.Arguments[0])), fail, @else: null);
    }

    /// <summary>Evaluates <paramref name="value"/> once; <paramref name="body"/> reads it through the given reference factory.</summary>
    private static LTemp Once(LoweringContext context, LExpression value, Func<Func<LExpression>, LExpression> body)
    {
        var temp = context.NewValue(value.Type);
        return new LTemp(temp, value, body(() => new LTempRef(temp)));
    }
}
