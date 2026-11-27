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
            case IntrinsicKind.SetLength when arguments[0].Type is DynamicArrayTypeSymbol array:
                return DynArraySetLength(context, array, arguments);
            case IntrinsicKind.Insert when arguments[1].Type is DynamicArrayTypeSymbol array:
                return ManagedRewriter.DynArrayInsert(context, array, arguments);              // CMP-1418 (C-391)
            case IntrinsicKind.Delete when arguments[0].Type is DynamicArrayTypeSymbol array:
                return ManagedRewriter.DynArrayDelete(context, array, arguments);
            case IntrinsicKind.SetLength when arguments[0].Type is StringTypeSymbol { Variant: StringVariant.Ansi } ansi:
                return new LCallStatement(new LHelperCallee(Helper("_AStrSetLen", null, types.Pointer, types.Integer, types.Word)),
                    [AddressOf(context, context.Lower(arguments[0])), context.Lower(arguments[1]), context.Constant(ansi.CodePage, types.Word)]);
            case IntrinsicKind.SetLength when arguments[0].Type is StringTypeSymbol { Variant: StringVariant.Short } shortString:
                return ShortSetLength(context, shortString, context.Lower(arguments[0]), context.Lower(arguments[1]));
            case IntrinsicKind.SetLength:
                return new LCallStatement(new LHelperCallee(Helper("_UStrSetLen", null, types.Pointer, types.Integer)),
                    [AddressOf(context, context.Lower(arguments[0])), context.Lower(arguments[1])]);
            case IntrinsicKind.Delete or IntrinsicKind.Insert or IntrinsicKind.Str
                when arguments[call.Intrinsic.Intrinsic == IntrinsicKind.Delete ? 0 : 1].Type is StringTypeSymbol { Variant: not StringVariant.Unicode }:
                return ThroughUnicode(context, call);
            case IntrinsicKind.Delete:
                return Delete(context, context.Lower(arguments[0]), call);
            case IntrinsicKind.Insert:
            {
                var source = context.Lower(arguments[0]);
                return Insert(context, source, context.Lower(arguments[1]), call);
            }

            case IntrinsicKind.Str:
                return Str(context, call, context.Lower(call.Arguments[1]));
            case IntrinsicKind.Val:
                return Val(context, call);
            default:
                throw new InvalidOperationException($"{call.Intrinsic.Name} is no string procedure.");
        }
    }

    private static LStatement Delete(LoweringContext context, LExpression target, BoundIntrinsicCallStatement call) =>
        new LCallStatement(new LHelperCallee(Helper("_UStrDelete", null, context.Types.Pointer, context.Types.Integer, context.Types.Integer)),
            [AddressOf(context, target), context.Lower(call.Arguments[1]), context.Lower(call.Arguments[2])]);

    private static LStatement Insert(LoweringContext context, LExpression source, LExpression target, BoundIntrinsicCallStatement call) =>
        new LCallStatement(new LHelperCallee(Helper("_UStrInsert", null, context.Types.Pointer, context.Types.Pointer, context.Types.Integer)),
            [source, AddressOf(context, target), context.Lower(call.Arguments[2])]);

    /// <summary>
    /// C-067: <c>Delete</c> / <c>Insert</c> / <c>Str</c> on a short string or an AnsiString-family variable edit a UnicodeString copy
    /// (UTF-16 indexes, like <c>Pos</c>/<c>Copy</c> on these kinds): <c>$p := @s</c> (the variable evaluated once),
    /// <c>$u := string($p^)</c> (not for <c>Str</c>, which overwrites), the UnicodeString operation on <c>$u</c>, then
    /// <c>$p^ := $u</c> converted back (<c>_SStrFromUStr</c>, truncating at a UTF-8 boundary; <c>_AStrFromUStr</c> with the
    /// type's code page).
    /// </summary>
    private static LStatement ThroughUnicode(LoweringContext context, BoundIntrinsicCallStatement call)
    {
        var types = context.Types;
        var p = types.Pointer;
        var kind = call.Intrinsic.Intrinsic;
        var source = kind == IntrinsicKind.Insert ? context.Lower(call.Arguments[0]) : null;
        var targetType = (StringTypeSymbol)call.Arguments[kind == IntrinsicKind.Delete ? 0 : 1].Type;
        var address = new LLocalRef(context.NewTemp(p));
        var variable = new LDeref(address, targetType);
        var unicode = new LLocalRef(context.NewTemp(types.String));
        var statements = new List<LStatement> { new LAssign(address, AddressOf(context, context.Lower(call.Arguments[kind == IntrinsicKind.Delete ? 0 : 1]))) };
        if (kind != IntrinsicKind.Str)
        {
            statements.Add(targetType.Variant == StringVariant.Short
                ? new LCallStatement(new LHelperCallee(Helper("_UStrFromSStr", null, p, p)), [AddressOf(context, unicode), address])
                : new LCallStatement(new LHelperCallee(Helper("_UStrFromAStr", null, p, p)), [AddressOf(context, unicode), variable]));
        }

        statements.Add(kind switch
        {
            IntrinsicKind.Delete => Delete(context, unicode, call),
            IntrinsicKind.Insert => Insert(context, source!, unicode, call),
            _ => Str(context, call, unicode),
        });
        statements.Add(targetType.Variant == StringVariant.Short
            ? new LCallStatement(new LHelperCallee(Helper("_SStrFromUStr", null, p, types.Integer, p)),
                [address, context.Constant(targetType.MaxLength, types.Integer), unicode])
            : new LCallStatement(new LHelperCallee(Helper("_AStrFromUStr", null, p, p, types.Word)),
                [address, unicode, context.Constant(targetType.CodePage, types.Word)]));
        return new LBlock([.. statements]);
    }

    /// <summary>
    /// C-067: <c>SetLength(ss, n)</c> of a short string stores <c>n</c> clamped to <c>0..capacity</c> in the length byte; the
    /// characters are left as they are.
    /// </summary>
    private static LStatement ShortSetLength(LoweringContext context, StringTypeSymbol type, LExpression target, LExpression length)
    {
        var types = context.Types;
        var value = context.NewValue(types.Integer);
        LExpression N() => new LTempRef(value);
        var capacity = context.Constant(type.MaxLength, types.Integer);
        var clamped = new LSelect(context.Compare(LCompareOperator.Less, N(), context.Constant(BigInteger.Zero, types.Integer)),
            context.Constant(BigInteger.Zero, types.Integer),
            new LSelect(context.Compare(LCompareOperator.Greater, N(), capacity), capacity, N()));
        return new LAssign(new LView(target, types.Byte), context.Convert(new LTemp(value, context.Convert(length, types.Integer), clamped), types.Byte));
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

    /// <summary>
    /// CMP-330 / intrinsics.md: <c>%dims = [k x i64]</c> holding the lengths (outermost first, converted to <c>NativeInt</c>), then
    /// <c>_DynArraySetLen(@a, TypeInfo(a), k, @dims)</c>.
    /// </summary>
    private static LStatement DynArraySetLength(LoweringContext context, DynamicArrayTypeSymbol array, ImmutableArray<BoundExpression> arguments)
    {
        var types = context.Types;
        var count = arguments.Length - 1;
        var index = new IntegerTypeSymbol($"0..{count - 1}", (IntegerTypeSymbol)types.Integer, BigInteger.Zero, count - 1, containingSymbol: null, []);
        var dims = new LLocalRef(context.NewTemp(new ArrayTypeSymbol($"array[0..{count - 1}] of NativeInt", index, types.NativeInt, containingSymbol: null, [])));
        var statements = new List<LStatement>();
        var target = AddressOf(context, context.Lower(arguments[0]));
        for (var k = 0; k < count; k++)
        {
            statements.Add(new LAssign(new LIndex(dims, context.Constant(k, types.Int64), types.NativeInt), context.Convert(context.Lower(arguments[k + 1]), types.NativeInt)));
        }

        statements.Add(new LCallStatement(new LHelperCallee(Helper("_DynArraySetLen", null, types.Pointer, types.Pointer, types.NativeInt, types.Pointer)),
            [target, new LTypeInfo(array, types.Pointer), context.Constant(count, types.NativeInt), AddressOf(context, dims)]));
        return new LBlock([.. statements]);
    }

    /// <summary><c>_UStrCopy(destination, s, index, count)</c>; an omitted <c>count</c> is <c>MaxInt</c> (to the end).</summary>
    public static LStatement CopyInto(LoweringContext context, LExpression destination, BoundIntrinsicCallExpression copy)
    {
        var types = context.Types;
        if (copy.Type is DynamicArrayTypeSymbol array)
        {
            // CMP-330: _DynArrayCopy(@dst, a, TI, index, count); Copy(a) = index 0, count High(NativeInt) (rtl-abi.md §9.5)
            var start = copy.Arguments.Length > 1 ? context.Lower(copy.Arguments[1]) : context.Constant(BigInteger.Zero, types.NativeInt);
            var length = copy.Arguments.Length > 2 ? context.Lower(copy.Arguments[2]) : context.Constant(long.MaxValue, types.NativeInt);
            return new LCallStatement(new LHelperCallee(Helper("_DynArrayCopy", null, types.Pointer, types.Pointer, types.Pointer, types.NativeInt, types.NativeInt)),
                [destination, context.Lower(copy.Arguments[0]), new LTypeInfo(array, types.Pointer), start, length]);
        }

        var count = copy.Arguments.Length == 3 ? context.Lower(copy.Arguments[2]) : context.Constant(int.MaxValue, types.Integer);
        var name = Strings.IsAnsi(copy.Type) ? "_AStrCopy" : "_UStrCopy";                    // C-063
        return new LCallStatement(new LHelperCallee(Helper(name, null, types.Pointer, types.Pointer, types.Integer, types.Integer)),
            [destination, context.Lower(copy.Arguments[0]), context.Lower(copy.Arguments[1]), count]);
    }

    /// <summary>
    /// LANG-951 / CMP-412: <c>_StrInt(@s, Int64, w)</c> for signed integers, <c>_StrUInt(@s, UInt64, w)</c> for unsigned ones,
    /// <c>_StrFloat(@s, Double, w, d)</c> for reals with the <c>Write</c> defaults (CMP-410: width 23 and decimals −1 without
    /// width, decimals −1 without <c>:d</c>); an omitted integer width is 0.
    /// </summary>
    private static LStatement Str(LoweringContext context, BoundIntrinsicCallStatement call, LExpression destination)
    {
        var types = context.Types;
        var arguments = call.Arguments;
        var target = AddressOf(context, destination);
        var value = context.Lower(arguments[0]);
        var width = arguments.Length > 2 ? context.Lower(arguments[2]) : null;
        if (arguments[0].Type is BooleanTypeSymbol or StringTypeSymbol)
        {
            // LANG-951 / C-063: the text Write would produce (TRUE/FALSE, the string) padded by _StrUStr(@s, text, w)
            var text = arguments[0].Type is BooleanTypeSymbol
                ? new LSelect(value, new LStringLiteral("TRUE", types.String), new LStringLiteral("FALSE", types.String))
                : value;
            return new LCallStatement(new LHelperCallee(Helper("_StrUStr", null, types.Pointer, types.Pointer, types.Integer)),
                [target, text, width ?? context.Constant(BigInteger.Zero, types.Integer)]);
        }

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
