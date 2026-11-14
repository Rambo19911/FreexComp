using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// CMP-335 small sets (<c>iN</c>, N = 8…64) as integer operations on existing lowered nodes:
/// <list type="bullet">
/// <item><c>[x]</c> → <c>select (x &lt;u N), 1 shl x, 0</c> (the emitter masks the count); under <c>{$R+}</c> <c>x</c> is first
/// range-checked against the element type.</item>
/// <item><c>[lo..hi]</c> → computed in <c>Int64</c>: bounds clipped to 0..N−1, <c>(-1 lshr (63 − (hi − lo))) shl lo</c> when
/// <c>lo ≤ hi</c>, else 0 (the CMP-335 mask without an overflowing <c>1 shl 64</c>), truncated to <c>iN</c>.</item>
/// <item><c>+ * -</c> → <c>or</c>, <c>and</c>, <c>and</c> with <c>not</c>; <c>= &lt;&gt;</c> compare; <c>s &lt;= t</c> → <c>(s and not t) = 0</c>,
/// <c>s &gt;= t</c> → <c>(not s and t) = 0</c> (left operand evaluated first, LANG-306).</item>
/// <item><c>x in s</c> → <c>(x &lt;u N) and ((s lshr x) and 1 &lt;&gt; 0)</c>; <c>Include</c>/<c>Exclude</c> → <c>s := s or [x]</c> /
/// <c>s := s and not [x]</c>.</item>
/// </list>
/// Big sets (32 bytes) arrive with C-051 (<see cref="NotSupportedException"/> → E2900 from the driver).
/// </summary>
internal static class SetRewriter
{
    public static LExpression Constructor(LoweringContext context, BoundSetConstructor constructor)
    {
        var type = Small(constructor.SetType);
        var state = CheckRewriter.StateOf(constructor);
        LExpression? result = null;
        foreach (var element in constructor.Elements)
        {
            var part = element.High is null
                ? Element(context, context.Lower(element.Low), type, state)
                : Range(context, context.Lower(element.Low), context.Lower(element.High), type, state);
            result = result is null ? part : new LBinary(LBinaryOperator.Or, result, part);
        }

        return result ?? Zero(context, type);
    }

    public static LExpression Binary(LoweringContext context, BoundBinaryExpression binary)
    {
        if (binary.OperatorKind == BoundBinaryOperatorKind.In)
        {
            return In(context, context.Lower(binary.Left), context.Lower(binary.Right), Small((SetTypeSymbol)binary.OperandType));
        }

        var type = Small((SetTypeSymbol)binary.OperandType);
        var left = context.Lower(binary.Left);
        var right = context.Lower(binary.Right);
        return binary.OperatorKind switch
        {
            BoundBinaryOperatorKind.Add => new LBinary(LBinaryOperator.Or, left, right),
            BoundBinaryOperatorKind.Multiply => new LBinary(LBinaryOperator.And, left, right),
            BoundBinaryOperatorKind.Subtract => new LBinary(LBinaryOperator.And, left, Not(right)),
            BoundBinaryOperatorKind.Equal => context.Compare(LCompareOperator.Equal, left, right),
            BoundBinaryOperatorKind.NotEqual => context.Compare(LCompareOperator.NotEqual, left, right),
            BoundBinaryOperatorKind.LessOrEqual => context.Compare(LCompareOperator.Equal, new LBinary(LBinaryOperator.And, left, Not(right)), Zero(context, type)),
            BoundBinaryOperatorKind.GreaterOrEqual => context.Compare(LCompareOperator.Equal, new LBinary(LBinaryOperator.And, Not(left), right), Zero(context, type)),
            _ => throw new InvalidOperationException($"{binary.OperatorKind} is not a set operator."),
        };
    }

    /// <summary><c>Include(s, x)</c> (<paramref name="include"/>) or <c>Exclude(s, x)</c>.</summary>
    public static LStatement IncludeExclude(LoweringContext context, BoundIntrinsicCallStatement call, bool include)
    {
        var target = call.Arguments[0];
        var type = Small((SetTypeSymbol)target.Type);
        var bit = Element(context, context.Lower(call.Arguments[1]), type, CheckRewriter.StateOf(call));
        var value = include
            ? new LBinary(LBinaryOperator.Or, context.Lower(target), bit)
            : new LBinary(LBinaryOperator.And, context.Lower(target), Not(bit));
        return new LAssign(context.Lower(target), value);
    }

    /// <summary>The bit of one element: <c>select (x &lt;u N), 1 shl x, 0</c>; range-checked first under <c>{$R+}</c>.</summary>
    private static LExpression Element(LoweringContext context, LExpression element, SetTypeSymbol type, DirectiveState state)
    {
        if (ConstantOrdinal(element) is { } ordinal)
        {
            return context.Constant(ConstantFolder.SetMask(type, [(ordinal, ordinal)]), type);
        }

        var value = Unsigned(context, Checked(context, element, type, state));
        return Once(context, value, x => new LSelect(
            context.Compare(LCompareOperator.Less, x(), context.Constant(type.Capacity, value.Type)),
            new LBinary(LBinaryOperator.Shl, context.Constant(BigInteger.One, type), context.Convert(x(), type)),
            Zero(context, type)));
    }

    /// <summary>The bits <c>lo..hi</c> clipped to 0..N−1, computed in <c>Int64</c>.</summary>
    private static LExpression Range(LoweringContext context, LExpression low, LExpression high, SetTypeSymbol type, DirectiveState state)
    {
        var int64 = context.Types.Int64;
        LExpression Wide(LExpression bound) => context.Convert(context.Convert(Checked(context, bound, type, state), context.ComputationType(bound.Type)), int64);
        LConst Const(long value) => context.Constant(value, int64);

        var mask = Once(context, Wide(low), lo => Once(context, Wide(high), hi =>
            Once(context, new LSelect(context.Compare(LCompareOperator.Less, lo(), Const(0)), Const(0), lo()), first =>
                Once(context, new LSelect(context.Compare(LCompareOperator.Greater, hi(), Const(type.Capacity - 1)), Const(type.Capacity - 1), hi()), last =>
                    new LSelect(
                        context.Compare(LCompareOperator.LessOrEqual, first(), last()),
                        new LBinary(LBinaryOperator.Shl,
                            new LBinary(LBinaryOperator.Shr, Const(-1), new LBinary(LBinaryOperator.Subtract, Const(63),
                                new LBinary(LBinaryOperator.Subtract, last(), first()))),
                            first()),
                        Const(0))))));
        return context.Convert(mask, type);
    }

    /// <summary><c>x in s</c>: <c>x</c> evaluated first (LANG-306); False outside the capacity (LANG-252).</summary>
    private static LExpression In(LoweringContext context, LExpression element, LExpression set, SetTypeSymbol type)
    {
        if (ConstantOrdinal(element) is { } ordinal)
        {
            // a constant element: a bit test against its mask (0 outside the capacity, so False; the set is still evaluated)
            return context.Compare(LCompareOperator.NotEqual,
                new LBinary(LBinaryOperator.And, set, context.Constant(ConstantFolder.SetMask(type, [(ordinal, ordinal)]), type)), Zero(context, type));
        }

        var value = Unsigned(context, element);
        return Once(context, value, x => new LBinary(LBinaryOperator.And,
            context.Compare(LCompareOperator.Less, x(), context.Constant(type.Capacity, value.Type)),
            context.Compare(LCompareOperator.NotEqual,
                new LBinary(LBinaryOperator.And, new LBinary(LBinaryOperator.Shr, set, context.Convert(x(), type)), context.Constant(BigInteger.One, type)),
                Zero(context, type))));
    }

    /// <summary>Under <c>{$R+}</c> the element must lie in the set's element type (CMP-314 check, value unchanged).</summary>
    private static LExpression Checked(LoweringContext context, LExpression element, SetTypeSymbol type, DirectiveState state)
    {
        if (!state.RangeChecks || element is LConst || type.ElementType is not { } elementType
            || Ordinals.Bounds(elementType) is not { } target || Ordinals.Bounds(element.Type) is not { } source
            || (source.Low >= target.Low && source.High <= target.High))
        {
            return element;
        }

        var computation = context.ComputationType(element.Type);
        var bounds = Ordinals.Bounds(computation)!.Value;
        return new LRangeCheck(context.Convert(element, computation), BigInteger.Max(target.Low, bounds.Low), BigInteger.Min(target.High, bounds.High));
    }

    /// <summary>The element's ordinal in the unsigned type of its computation width, so <c>&lt;u N</c> rejects negative values.</summary>
    private static LExpression Unsigned(LoweringContext context, LExpression element)
    {
        var computation = context.ComputationType(element.Type);
        var types = context.Types;
        var unsigned = ReferenceEquals(computation, types.Integer) ? types.Cardinal : ReferenceEquals(computation, types.Int64) ? types.UInt64 : computation;
        return context.Convert(context.Convert(element, computation), unsigned);
    }

    private static BigInteger? ConstantOrdinal(LExpression element) => element is LConst { Value.Value: var value }
        ? value switch { BigInteger i => i, bool b => b ? BigInteger.One : BigInteger.Zero, _ => null }
        : null;

    /// <summary>The complement (folded for a constant set).</summary>
    private static LExpression Not(LExpression value) => value is LConst { Value.Value: BigInteger mask, Type: SetTypeSymbol type }
        ? new LConst(new ConstantValue(~mask & ((BigInteger.One << type.Capacity) - 1), type), type)
        : new LUnary(LUnaryOperator.Not, value);

    private static LConst Zero(LoweringContext context, SetTypeSymbol type) => new(new ConstantValue(BigInteger.Zero, type), type);

    private static SetTypeSymbol Small(SetTypeSymbol type) =>
        type.IsSmall ? type : throw new NotSupportedException("big set operations (C-051)");

    private static LTemp Once(LoweringContext context, LExpression value, Func<Func<LExpression>, LExpression> body)
    {
        var temp = context.NewValue(value.Type);
        return new LTemp(temp, value, body(() => new LTempRef(temp)));
    }
}
