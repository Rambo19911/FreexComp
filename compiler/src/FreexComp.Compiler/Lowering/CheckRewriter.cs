using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// Runtime checks per the <see cref="DirectiveState"/> at the source position (CMP-116 step 7). Like the other rewriters it
/// is a translator used while lowering:
/// <list type="bullet">
/// <item><c>{$Q+}</c> (LANG-223, CMP-312): integer <c>+ - *</c> and unary <c>-</c> in the computation type become checked
/// operations (<see cref="LBinary.IsChecked"/>, <see cref="LUnary.IsChecked"/>); <c>Abs</c>, <c>Sqr</c>, <c>Inc</c>/<c>Dec</c>,
/// <c>Succ</c>/<c>Pred</c> inherit it from the operations they lower to.</item>
/// <item><c>{$R+}</c> (LANG-351, CMP-314): an implicit conversion between integer/char types whose source range is not
/// inside the destination range gets an <see cref="LRangeCheck"/> against the destination bounds (intersected with the
/// source range). This covers assignments, value arguments, <c>Exit(x)</c>, <c>for</c> bounds, <c>Chr</c> and the
/// conversion back of <c>Inc</c>/<c>Dec</c>/<c>Succ</c>/<c>Pred</c>. Explicit typecasts and the <c>for</c> step are never checked.</item>
/// </list>
/// Division-by-zero tests are not checks: <c>IrEmitter</c> always emits them (CMP-312).
/// </summary>
internal static class CheckRewriter
{
    /// <summary>The switch state at the first token of <paramref name="node"/>.</summary>
    public static DirectiveState StateOf(BoundNode node) => node.Syntax.FirstToken().DirectiveState;

    /// <summary><c>left op right</c>; overflow-checked when <paramref name="state"/> has <c>{$Q+}</c> and it is integer <c>+ - *</c>.</summary>
    public static LBinary Binary(LBinaryOperator op, LExpression left, LExpression right, DirectiveState? state) =>
        new(op, left, right, state is { OverflowChecks: true } && left.Type is IntegerTypeSymbol
            && op is LBinaryOperator.Add or LBinaryOperator.Subtract or LBinaryOperator.Multiply);

    /// <summary><c>-operand</c>; overflow-checked for integers under <c>{$Q+}</c>.</summary>
    public static LUnary Negate(LExpression operand, DirectiveState? state) =>
        new(LUnaryOperator.Negate, operand, state is { OverflowChecks: true } && operand.Type is IntegerTypeSymbol);

    /// <summary>
    /// The implicit conversion of <paramref name="value"/> to <paramref name="type"/>, range-checked under <c>{$R+}</c> when the
    /// value's type can hold values outside <paramref name="type"/>. Constants are never checked (the binder rejects
    /// out-of-range constants with E2012).
    /// </summary>
    public static LExpression Convert(LoweringContext context, LExpression value, TypeSymbol type, DirectiveState? state)
    {
        if (state is { RangeChecks: true } && value is not LConst && Bounds(value.Type) is { } source && Bounds(type) is { } target
            && (source.Low < target.Low || source.High > target.High))
        {
            value = new LRangeCheck(value, BigInteger.Max(target.Low, source.Low), BigInteger.Min(target.High, source.High));
        }

        return context.Convert(value, type);
    }

    /// <summary>The value range of an integer, char or enumeration type (subranges: their bounds); null for other types.</summary>
    private static (BigInteger Low, BigInteger High)? Bounds(TypeSymbol type) => type is BooleanTypeSymbol ? null : Ordinals.Bounds(type);
}
