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

            default:
                throw new InvalidOperationException($"{call.Intrinsic.Name} with a non-constant result is not an M2.1 intrinsic.");
        }
    }

    public static LStatement Rewrite(LoweringContext context, BoundIntrinsicCallStatement call)
    {
        switch (call.Intrinsic.Intrinsic)
        {
            case IntrinsicKind.Inc or IntrinsicKind.Dec:
            {
                var target = call.Arguments[0];
                var step = call.Arguments.Length == 2 ? context.Lower(call.Arguments[1]) : null;
                var op = call.Intrinsic.Intrinsic == IntrinsicKind.Inc ? LBinaryOperator.Add : LBinaryOperator.Subtract;
                return new LAssign(context.Lower(target), context.Step(context.Lower(target), target.Type, op, step, CheckRewriter.StateOf(call)));
            }

            case IntrinsicKind.RunError:
                return context.CallNoReturn(context.RunError,
                    call.Arguments.IsEmpty ? context.Constant(BigInteger.Zero, context.Types.Integer) : context.Lower(call.Arguments[0]));
            default:
                throw new InvalidOperationException($"Unknown intrinsic statement {call.Intrinsic.Name}.");
        }
    }

    /// <summary>Evaluates <paramref name="value"/> once; <paramref name="body"/> reads it through the given reference factory.</summary>
    private static LTemp Once(LoweringContext context, LExpression value, Func<Func<LExpression>, LExpression> body)
    {
        var temp = context.NewValue(value.Type);
        return new LTemp(temp, value, body(() => new LTempRef(temp)));
    }
}
