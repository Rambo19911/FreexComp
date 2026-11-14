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
/// Big sets (32 bytes, CMP-336) live in memory: constructors fill a zeroed temporary (<see cref="LZero"/>, inline
/// <see cref="LSetBit"/>, <c>_SetRange</c>); <c>+ * -</c> call <c>_SetUnion</c>/<c>_SetInter</c>/<c>_SetDiff</c> into a temporary,
/// comparisons <c>_SetEq</c>/<c>_SetLe</c>; <c>in</c> is an inline <see cref="LTestBit"/> guarded by <c>x &lt;u 256</c>; operands
/// are passed by address (<see cref="LAddrOf"/>). An element outside 0..255 contributes nothing under <c>{$R-}</c> and is a
/// range error under <c>{$R+}</c> (types.md lead note 2026-11-14 for <c>set of Char</c>).
/// </summary>
internal static class SetRewriter
{
    /// <summary>Number of elements of a big set (rtl-abi.md §5).</summary>
    private const int BigCapacity = 256;

    public static LExpression Constructor(LoweringContext context, BoundSetConstructor constructor)
    {
        if (!constructor.SetType.IsSmall)
        {
            return BigConstructor(context, constructor);
        }

        var type = constructor.SetType;
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
        var setType = (SetTypeSymbol)binary.OperandType;
        if (!setType.IsSmall)
        {
            return BigBinary(context, binary);
        }

        if (binary.OperatorKind == BoundBinaryOperatorKind.In)
        {
            return In(context, context.Lower(binary.Left), context.Lower(binary.Right), setType);
        }

        var type = setType;
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
        var type = (SetTypeSymbol)target.Type;
        if (!type.IsSmall)
        {
            return BigElement(context, Address(context, context.Lower(target)), context.Lower(call.Arguments[1]), type, CheckRewriter.StateOf(call), include);
        }

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

    /// <summary>
    /// Under <c>{$R+}</c> the element must lie in the set's element type and its capacity (CMP-314 check, value unchanged; the
    /// capacity matters for <c>set of Char</c>).
    /// </summary>
    private static LExpression Checked(LoweringContext context, LExpression element, SetTypeSymbol type, DirectiveState state)
    {
        if (!state.RangeChecks || element is LConst || type.ElementType is not { } elementType
            || Ordinals.Bounds(elementType) is not { } declared || Ordinals.Bounds(element.Type) is not { } source)
        {
            return element;
        }

        var target = (Low: BigInteger.Max(declared.Low, 0), High: BigInteger.Min(declared.High, type.Capacity - 1));
        if (source.Low >= target.Low && source.High <= target.High)
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

    /// <summary>
    /// A set-to-set conversion (LANG-280 rule 8): small ↔ small as integers (bits that fit), big → big unchanged (same
    /// storage), small → big into a zeroed temporary, big → small as the leading bytes (ABI-050 bit order).
    /// </summary>
    public static LExpression Convert(LoweringContext context, LExpression value, SetTypeSymbol target)
    {
        var source = (SetTypeSymbol)value.Type;
        switch (source.IsSmall, target.IsSmall)
        {
            case (true, true):
                return context.Convert(value, target);
            case (false, false):
                return value;
            case (false, true):
                return new LView(value, target);
            default:
            {
                var temp = context.NewTemp(target);
                return new LSequence(new LBlock([
                    new LZero(Address(context, new LLocalRef(temp)), target),
                    new LAssign(new LView(new LLocalRef(temp), source), value)]), new LLocalRef(temp));
            }
        }
    }

    // ------------------------------------------------------------------ big sets (CMP-336)

    private static LExpression BigConstructor(LoweringContext context, BoundSetConstructor constructor)
    {
        var type = constructor.SetType;
        var state = CheckRewriter.StateOf(constructor);
        var temp = context.NewTemp(type);
        var address = Address(context, new LLocalRef(temp));
        var statements = new List<LStatement> { new LZero(address, type) };
        foreach (var element in constructor.Elements)
        {
            statements.Add(element.High is null
                ? BigElement(context, address, context.Lower(element.Low), type, state, include: true)
                : BigRange(context, address, context.Lower(element.Low), context.Lower(element.High), type, state));
        }

        return new LSequence(new LBlock([.. statements]), new LLocalRef(temp));
    }

    /// <summary>
    /// Sets or clears one element's bit inline; outside 0..255 nothing happens under <c>{$R-}</c> (<c>x &lt;u 256</c> guard,
    /// omitted when the element's type lies inside), a range error under <c>{$R+}</c>.
    /// </summary>
    private static LStatement BigElement(LoweringContext context, LExpression address, LExpression element, SetTypeSymbol type, DirectiveState state, bool include)
    {
        if (ConstantOrdinal(element) is { } ordinal)
        {
            return ordinal >= 0 && ordinal < BigCapacity
                ? new LSetBit(address, context.Constant(ordinal, context.Types.Integer), include)
                : new LBlock([]);
        }

        var value = Unsigned(context, Checked(context, element, type, state));
        if (state.RangeChecks || WithinCapacity(element.Type))
        {
            return new LSetBit(address, value, include);
        }

        var x = context.NewValue(value.Type);
        return new LIf(new LTemp(x, value, Below(context, new LTempRef(x))), new LSetBit(address, new LTempRef(x), include), @else: null);
    }

    /// <summary><c>_SetRange(tmp, lo, hi)</c>; under <c>{$R-}</c> the bounds are clipped to 0..256 / −1..255 first (still empty when lo &gt; hi).</summary>
    private static LStatement BigRange(LoweringContext context, LExpression address, LExpression low, LExpression high, SetTypeSymbol type, DirectiveState state)
    {
        var integer = context.Types.Integer;
        LExpression Bound(LExpression bound, BigInteger min, BigInteger max)
        {
            var checkedBound = Checked(context, bound, type, state);
            if (state.RangeChecks || checkedBound is LConst || Ordinals.Bounds(bound.Type) is { } b && b.Low >= min && b.High <= max)
            {
                return checkedBound is LConst { Value.Value: BigInteger c }
                    ? context.Constant(BigInteger.Max(min, BigInteger.Min(max, c)), integer)
                    : context.Convert(context.Convert(checkedBound, context.ComputationType(bound.Type)), integer);
            }

            var int64 = context.Types.Int64;
            var wide = context.Convert(context.Convert(checkedBound, context.ComputationType(bound.Type)), int64);
            var clipped = Once(context, wide, v => new LSelect(context.Compare(LCompareOperator.Less, v(), context.Constant(min, int64)), context.Constant(min, int64),
                new LSelect(context.Compare(LCompareOperator.Greater, v(), context.Constant(max, int64)), context.Constant(max, int64), v())));
            return context.Convert(clipped, integer);
        }

        var helper = new RuntimeHelper("_SetRange", [context.Types.Pointer, integer, integer], ReturnType: null, IsNoReturn: false);
        return new LCallStatement(new LHelperCallee(helper), [address, Bound(low, 0, BigCapacity), Bound(high, -1, BigCapacity - 1)]);
    }

    private static LExpression BigBinary(LoweringContext context, BoundBinaryExpression binary)
    {
        var left = context.Lower(binary.Left);
        var right = context.Lower(binary.Right);
        if (binary.OperatorKind == BoundBinaryOperatorKind.In)
        {
            return BigIn(context, left, right);
        }

        var pointer = context.Types.Pointer;
        switch (binary.OperatorKind)
        {
            case BoundBinaryOperatorKind.Add or BoundBinaryOperatorKind.Multiply or BoundBinaryOperatorKind.Subtract:
            {
                var name = binary.OperatorKind switch
                {
                    BoundBinaryOperatorKind.Add => "_SetUnion",
                    BoundBinaryOperatorKind.Multiply => "_SetInter",
                    _ => "_SetDiff",
                };
                var temp = context.NewTemp(binary.OperandType);
                var helper = new RuntimeHelper(name, [pointer, pointer, pointer], ReturnType: null, IsNoReturn: false);
                return new LSequence(new LCallStatement(new LHelperCallee(helper), [Address(context, new LLocalRef(temp)), Address(context, left), Address(context, right)]),
                    new LLocalRef(temp));
            }

            case BoundBinaryOperatorKind.Equal:
                return Helper(context, "_SetEq", Address(context, left), Address(context, right));
            case BoundBinaryOperatorKind.NotEqual:
                return new LUnary(LUnaryOperator.Not, Helper(context, "_SetEq", Address(context, left), Address(context, right)));
            case BoundBinaryOperatorKind.LessOrEqual:
                return Helper(context, "_SetLe", Address(context, left), Address(context, right));
            case BoundBinaryOperatorKind.GreaterOrEqual:
                // b <= a, with a evaluated first (LANG-306)
                return Once(context, Address(context, left), a => Once(context, Address(context, right), b => Helper(context, "_SetLe", b(), a())));
            default:
                throw new InvalidOperationException($"{binary.OperatorKind} is not a set operator.");
        }
    }

    /// <summary><c>x in s</c>: <c>x</c> first (LANG-306), then the set; False outside 0..255 (no load then).</summary>
    private static LExpression BigIn(LoweringContext context, LExpression element, LExpression set)
    {
        if (ConstantOrdinal(element) is { } ordinal)
        {
            return ordinal >= 0 && ordinal < BigCapacity
                ? new LTestBit(Address(context, set), context.Constant(ordinal, context.Types.Integer), context.Types.Boolean)
                : Once(context, Address(context, set), _ => new LConst(new ConstantValue(false, context.Types.Boolean), context.Types.Boolean));
        }

        var value = Unsigned(context, element);
        var inside = WithinCapacity(element.Type);
        return Once(context, value, x => Once(context, Address(context, set), s => inside
            ? new LTestBit(s(), x(), context.Types.Boolean)
            : new LBinary(LBinaryOperator.ConditionalAnd, Below(context, x()), new LTestBit(s(), x(), context.Types.Boolean))));
    }

    private static LCall Helper(LoweringContext context, string name, LExpression a, LExpression b) =>
        new(new LHelperCallee(new RuntimeHelper(name, [context.Types.Pointer, context.Types.Pointer], context.Types.Boolean, IsNoReturn: false)), [a, b]);

    /// <summary><c>x &lt;u 256</c>.</summary>
    private static LCompare Below(LoweringContext context, LExpression x) =>
        context.Compare(LCompareOperator.Less, x, context.Constant(BigCapacity, x.Type));

    private static bool WithinCapacity(TypeSymbol element) => Ordinals.Bounds(element) is { } b && b.Low >= 0 && b.High < BigCapacity;

    private static LAddrOf Address(LoweringContext context, LExpression set) => new(set, context.Types.Pointer);

    private static LTemp Once(LoweringContext context, LExpression value, Func<Func<LExpression>, LExpression> body)
    {
        var temp = context.NewValue(value.Type);
        return new LTemp(temp, value, body(() => new LTempRef(temp)));
    }
}
