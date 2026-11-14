using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// CMP-412 / intrinsics.md §1, §4 (C-062): the UnicodeString intrinsics as calls of the rtl-abi.md §9.3/§9.10 helpers —
/// <c>SetLength</c> → <c>_UStrSetLen</c>, <c>Delete</c> → <c>_UStrDelete</c>, <c>Insert</c> → <c>_UStrInsert</c>, <c>Copy</c> →
/// <c>_UStrCopy</c> into its destination (<see cref="ManagedRewriter"/>), <c>Pos</c> → <c>_UStrPos</c>, <c>Str</c> →
/// <c>_StrInt</c>/<c>_StrUInt</c>/<c>_StrFloat</c>, <c>Val</c> → <c>_ValInt</c>/<c>_ValUInt</c>/<c>_ValFloat</c> plus the range test; <c>UpCase</c>
/// is inline. <c>Concat</c> was bound as <c>+</c>.
/// </summary>
internal static class StringRewriter
{
    private static RuntimeHelper Helper(string name, TypeSymbol? result, params TypeSymbol[] parameters) =>
        new(name, [.. parameters], result, IsNoReturn: false);

    private static LAddrOf AddressOf(LoweringContext context, LExpression variable) => new(variable, context.Types.Pointer);

    public static LStatement Statement(LoweringContext context, BoundIntrinsicCallStatement call)
    {
        var types = context.Types;
        var arguments = call.Arguments;
        switch (call.Intrinsic.Intrinsic)
        {
            case IntrinsicKind.SetLength:
                return new LCallStatement(new LHelperCallee(Helper("_UStrSetLen", null, types.Pointer, types.Integer)),
                    [AddressOf(context, context.Lower(arguments[0])), context.Lower(arguments[1])]);
            case IntrinsicKind.Delete:
                return new LCallStatement(new LHelperCallee(Helper("_UStrDelete", null, types.Pointer, types.Integer, types.Integer)),
                    [AddressOf(context, context.Lower(arguments[0])), context.Lower(arguments[1]), context.Lower(arguments[2])]);
            case IntrinsicKind.Insert:
                return new LCallStatement(new LHelperCallee(Helper("_UStrInsert", null, types.Pointer, types.Pointer, types.Integer)),
                    [context.Lower(arguments[0]), AddressOf(context, context.Lower(arguments[1])), context.Lower(arguments[2])]);
            case IntrinsicKind.Str:
                return Str(context, call);
            case IntrinsicKind.Val:
                return Val(context, call);
            default:
                throw new InvalidOperationException($"{call.Intrinsic.Name} is no string procedure.");
        }
    }

    /// <summary><c>Pos</c> (<c>Offset</c> defaults to 1) and <c>UpCase</c> (<c>'a'..'z'</c> → minus 32, everything else unchanged).</summary>
    public static LExpression Function(LoweringContext context, BoundIntrinsicCallExpression call)
    {
        var types = context.Types;
        if (call.Intrinsic.Intrinsic == IntrinsicKind.Pos)
        {
            var offset = call.Arguments.Length == 3 ? context.Lower(call.Arguments[2]) : context.Constant(BigInteger.One, types.Integer);
            return new LCall(new LHelperCallee(Helper("_UStrPos", types.Integer, types.Pointer, types.Pointer, types.Integer)),
                [context.Lower(call.Arguments[0]), context.Lower(call.Arguments[1]), offset]);
        }

        var character = context.Lower(call.Arguments[0]);
        var value = context.NewValue(character.Type);
        LExpression V() => new LTempRef(value);
        var isLower = new LBinary(LBinaryOperator.And,
            context.Compare(LCompareOperator.GreaterOrEqual, V(), context.Constant('a', types.Char)),
            context.Compare(LCompareOperator.LessOrEqual, V(), context.Constant('z', types.Char)));
        var upper = context.Convert(new LBinary(LBinaryOperator.Subtract, context.Convert(V(), types.Integer), context.Constant(32, types.Integer)), types.Char);
        return new LTemp(value, character, new LSelect(isLower, upper, V()));
    }

    /// <summary><c>_UStrCopy(destination, s, index, count)</c>; an omitted <c>count</c> is <c>MaxInt</c> (to the end).</summary>
    public static LStatement CopyInto(LoweringContext context, LExpression destination, BoundIntrinsicCallExpression copy)
    {
        var types = context.Types;
        var count = copy.Arguments.Length == 3 ? context.Lower(copy.Arguments[2]) : context.Constant(int.MaxValue, types.Integer);
        return new LCallStatement(new LHelperCallee(Helper("_UStrCopy", null, types.Pointer, types.Pointer, types.Integer, types.Integer)),
            [destination, context.Lower(copy.Arguments[0]), context.Lower(copy.Arguments[1]), count]);
    }

    /// <summary>
    /// LANG-951 / CMP-412: <c>_StrInt(@s, Int64, w)</c> for signed integers, <c>_StrUInt(@s, UInt64, w)</c> for unsigned ones,
    /// <c>_StrFloat(@s, Double, w, d)</c> for reals with the <c>Write</c> defaults (CMP-410: width 23 and decimals −1 without
    /// width, decimals −1 without <c>:d</c>); an omitted integer width is 0.
    /// </summary>
    private static LStatement Str(LoweringContext context, BoundIntrinsicCallStatement call)
    {
        var types = context.Types;
        var arguments = call.Arguments;
        var target = AddressOf(context, context.Lower(arguments[1]));
        var value = context.Lower(arguments[0]);
        var width = arguments.Length > 2 ? context.Lower(arguments[2]) : null;
        if (arguments[0].Type is RealTypeSymbol)
        {
            var decimals = arguments.Length > 3 ? context.Lower(arguments[3]) : context.Constant(BigInteger.MinusOne, types.Integer);
            return new LCallStatement(new LHelperCallee(Helper("_StrFloat", null, types.Pointer, types.Double, types.Integer, types.Integer)),
                [target, context.Convert(value, types.Double), width ?? context.Constant(23, types.Integer), decimals]);
        }

        var signed = ((IntegerTypeSymbol)arguments[0].Type).IsSigned;
        var wide = signed ? types.Int64 : types.UInt64;
        return new LCallStatement(new LHelperCallee(Helper(signed ? "_StrInt" : "_StrUInt", null, types.Pointer, wide, types.Integer)),
            [target, context.Convert(value, wide), width ?? context.Constant(BigInteger.Zero, types.Integer)]);
    }

    /// <summary>
    /// LANG-950 / CMP-412: <c>$r := _ValInt|_ValUInt|_ValFloat(s, @$code)</c> (the code in an <c>Integer</c> temporary); for a variable
    /// narrower than the helper's result, <c>if ($code = 0) and (($r &lt; Low) or ($r &gt; High)) then begin $code := Length(s); $r := 0
    /// end</c>; then <c>v := $r</c>, <c>code := $code</c>. A string source needing a temporary is evaluated once.
    /// </summary>
    private static LStatement Val(LoweringContext context, BoundIntrinsicCallStatement call)
    {
        var types = context.Types;
        var statements = new List<LStatement>();
        var source = context.Lower(call.Arguments[0]);
        if (source is LSequence sequence)
        {
            statements.Add(sequence.First);
            source = sequence.Value;
        }

        var variableType = call.Arguments[1].Type;
        var (name, result) = variableType switch
        {
            RealTypeSymbol => ("_ValFloat", types.Double),
            IntegerTypeSymbol { IsSigned: false, Size: 8 } => ("_ValUInt", types.UInt64),
            _ => ("_ValInt", types.Int64),
        };
        var code = new LLocalRef(context.NewTemp(types.Integer));
        var value = new LLocalRef(context.NewTemp(result));
        statements.Add(new LAssign(value, new LCall(new LHelperCallee(Helper(name, result, types.Pointer, types.Pointer)), [source, AddressOf(context, code)])));

        if (variableType is IntegerTypeSymbol && Ordinals.Bounds(variableType) is { } bounds && Ordinals.Bounds(result) is { } full
            && (bounds.Low > full.Low || bounds.High < full.High))
        {
            LExpression? outside = null;
            if (bounds.Low > full.Low)
            {
                outside = context.Compare(LCompareOperator.Less, value, context.Constant(bounds.Low, result));
            }

            if (bounds.High < full.High)
            {
                var above = context.Compare(LCompareOperator.Greater, value, context.Constant(bounds.High, result));
                outside = outside is null ? above : new LBinary(LBinaryOperator.Or, outside, above);
            }

            var failed = new LBinary(LBinaryOperator.And, context.Compare(LCompareOperator.Equal, code, context.Constant(BigInteger.Zero, types.Integer)), outside!);
            statements.Add(new LIf(failed, new LBlock([
                new LAssign(code, new LStringLength(source, types.Integer)),
                new LAssign(value, context.Constant(BigInteger.Zero, result))]), @else: null));
        }

        statements.Add(new LAssign(context.Lower(call.Arguments[1]), context.Convert(value, variableType)));
        statements.Add(new LAssign(context.Lower(call.Arguments[2]), context.Convert(code, call.Arguments[2].Type)));
        return new LBlock([.. statements]);
    }
}
