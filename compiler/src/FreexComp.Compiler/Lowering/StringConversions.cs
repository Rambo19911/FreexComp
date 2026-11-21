using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// LANG-245 / LANG-290 string conversions (C-063) as calls of the rtl-abi.md §9.3/§9.4 helpers, written into a target variable:
/// <list type="bullet">
/// <item>to <c>UnicodeString</c>: <c>_UStrFromWChar</c> (characters), <c>_UStrFromAStr</c>, <c>_UStrFromSStr(@d, @s)</c>,
/// <c>_UStrFromPWChar</c>, a <c>PAnsiChar</c> through an AnsiString temporary, <c>array[0..N] of Char</c> by
/// <c>_UStrFromPWCharLen(@d, @a[0], n)</c> after an inline scan for the first <c>#0</c> (at most N+1); another UnicodeString type
/// (a distinct type, LANG-205) shares the block;</item>
/// <item>to the AnsiString family (code page of the target type, ABI-027): <c>_AStrFromUStr(@d, s, cp)</c> (a <c>Char</c> through a
/// UnicodeString temporary), <c>_AStrFromPCharLen(@d, p, n, cp)</c> for an <c>AnsiChar</c> (n = 1), a short string (its bytes 1..len)
/// and a <c>PAnsiChar</c> (n from an inline scan; <c>nil</c> gives 0); another AnsiString type shares the block (<c>_AStrAsg</c>);</item>
/// <item>to a short string: <c>_SStrAsg(@d, max, @s)</c> from a short string of another type, <c>_SStrFromUStr(@d, max, s)</c> from a
/// UnicodeString, anything else through a UnicodeString temporary.</item>
/// </list>
/// </summary>
internal static class StringConversions
{
    private static RuntimeHelper Helper(string name, params TypeSymbol[] parameters) => new(name, [.. parameters], ReturnType: null, IsNoReturn: false);

    private static LCallStatement Call(string name, TypeSymbol[] parameters, params LExpression[] arguments) =>
        new(new LHelperCallee(Helper(name, parameters)), [.. arguments]);

    private static LAddrOf AddressOf(LoweringContext context, LExpression variable) => new(variable, context.Types.Pointer);

    /// <summary><paramref name="target"/> := <paramref name="conversion"/> (a string-typed conversion of another type's value).</summary>
    public static LStatement Convert(LoweringContext context, LExpression target, BoundConversionExpression conversion)
    {
        var types = context.Types;
        var source = conversion.Operand;
        var p = types.Pointer;
        switch (target.Type)
        {
            case StringTypeSymbol { Variant: StringVariant.Unicode }:
                switch (source.Type)
                {
                    case CharTypeSymbol:
                        return ManagedRewriter.FromChar(context, AddressOf(context, target), context.Lower(source));
                    case StringTypeSymbol { Variant: StringVariant.Unicode }:                       // a distinct UnicodeString type (C-262)
                        return ManagedRewriter.Assign(context, target, context.Lower(source));
                    case StringTypeSymbol { Variant: StringVariant.Ansi }:
                        return Call("_UStrFromAStr", [p, p], AddressOf(context, target), context.Lower(source));
                    case StringTypeSymbol { Variant: StringVariant.Short }:
                        return Call("_UStrFromSStr", [p, p], AddressOf(context, target), AddressOf(context, context.Lower(source)));
                    case var type when Strings.IsCharPointer(type, wide: true):
                        return Call("_UStrFromPWChar", [p, p], AddressOf(context, target), context.Lower(source));
                    case var type when Strings.IsCharPointer(type, wide: false):
                    {
                        var bytes = new LLocalRef(context.NewTemp(types.AnsiString));
                        return new LBlock([FromPAnsiChar(context, bytes, context.Lower(source)), Call("_UStrFromAStr", [p, p], AddressOf(context, target), bytes)]);
                    }

                    case var type when Strings.IsCharArray(type):
                        return FromCharArray(context, target, (ArrayTypeSymbol)type, context.Lower(source));
                }

                break;

            case StringTypeSymbol { Variant: StringVariant.Ansi } ansi:
            {
                var codePage = context.Constant(ansi.CodePage, types.Word);
                switch (source.Type)
                {
                    case StringTypeSymbol { Variant: StringVariant.Unicode }:
                        return Call("_AStrFromUStr", [p, p, types.Word], AddressOf(context, target), context.Lower(source), codePage);
                    case CharTypeSymbol { IsWide: true }:
                        return Call("_AStrFromUStr", [p, p, types.Word], AddressOf(context, target), Unicode(context, source), codePage);
                    case CharTypeSymbol:
                    {
                        var character = new LLocalRef(context.NewTemp(types.AnsiChar));
                        return new LBlock([
                            new LAssign(character, context.Convert(context.Lower(source), types.AnsiChar)),
                            Call("_AStrFromPCharLen", [p, p, types.Integer, types.Word], AddressOf(context, target), AddressOf(context, character),
                                context.Constant(BigInteger.One, types.Integer), codePage)]);
                    }

                    case StringTypeSymbol { Variant: StringVariant.Ansi }:
                        return ManagedRewriter.Assign(context, target, context.Lower(source));
                    case StringTypeSymbol { Variant: StringVariant.Short } shortString:
                    {
                        var (setup, value) = Once(context, context.Lower(source));
                        var bytes = ShortBytes(context, value, shortString);
                        return new LBlock([.. setup,
                            Call("_AStrFromPCharLen", [p, p, types.Integer, types.Word], AddressOf(context, target),
                                AddressOf(context, new LIndex(bytes, context.Constant(BigInteger.One, types.Int64), types.AnsiChar)), ShortLength(context, value), codePage)]);
                    }

                    case var type when Strings.IsCharPointer(type, wide: false):
                        return FromPAnsiChar(context, target, context.Lower(source));
                }

                break;
            }

            case StringTypeSymbol { Variant: StringVariant.Short } shortTarget:
                return source.Type switch
                {
                    StringTypeSymbol { Variant: StringVariant.Short } => ShortFromShort(context, target, context.Lower(source)),
                    StringTypeSymbol { Variant: StringVariant.Unicode } => Call("_SStrFromUStr", [p, types.Integer, p], AddressOf(context, target),
                        context.Constant(shortTarget.MaxLength, types.Integer), context.Lower(source)),
                    _ => Call("_SStrFromUStr", [p, types.Integer, p], AddressOf(context, target), context.Constant(shortTarget.MaxLength, types.Integer),
                        Unicode(context, source)),
                };
        }

        throw new InvalidOperationException($"No string conversion from '{source.Type.Name}' to '{target.Type.Name}'.");
    }

    /// <summary>LANG-246: <c>_SStrAsg(@target, capacity, @source)</c> (truncates).</summary>
    public static LStatement ShortFromShort(LoweringContext context, LExpression target, LExpression source) =>
        Call("_SStrAsg", [context.Types.Pointer, context.Types.Integer, context.Types.Pointer], AddressOf(context, target),
            context.Constant(((StringTypeSymbol)target.Type).MaxLength, context.Types.Integer), AddressOf(context, source));

    /// <summary>A value as a UnicodeString (itself, or converted into a managed temporary).</summary>
    private static LExpression Unicode(LoweringContext context, BoundExpression value) =>
        Strings.IsUnicode(value.Type)
            ? context.Lower(value)
            : ManagedRewriter.Value(context, new BoundConversionExpression(value.Syntax, value, context.Types.String, ConversionKind.ImplicitString, isExplicit: false, constantValue: null));

    /// <summary>The bytes of a short string as <c>array[0..capacity] of AnsiChar</c> (byte 0 = the length).</summary>
    internal static LExpression ShortBytes(LoweringContext context, LExpression value, StringTypeSymbol type)
    {
        var types = context.Types;
        var index = new IntegerTypeSymbol($"0..{type.MaxLength}", (IntegerTypeSymbol)types.Integer, BigInteger.Zero, type.MaxLength, containingSymbol: null, []);
        return new LView(value, new ArrayTypeSymbol($"array[0..{type.MaxLength}] of AnsiChar", index, types.AnsiChar, containingSymbol: null, []));
    }

    /// <summary>The length byte of a short string as an <c>Integer</c>.</summary>
    internal static LExpression ShortLength(LoweringContext context, LExpression value) =>
        context.Convert(new LView(value, context.Types.Byte), context.Types.Integer);

    /// <summary>A lowered value computed into a temporary first (so it can be used twice): <see cref="LSequence"/>s are split.</summary>
    private static (ImmutableArray<LStatement> Setup, LExpression Value) Once(LoweringContext context, LExpression value) =>
        value is LSequence sequence ? ([sequence.First], sequence.Value) : ([], value);

    /// <summary>
    /// <c>$p := p; $n := 0; if $p &lt;&gt; nil then while $p[$n] &lt;&gt; #0 do Inc($n); _AStrFromPCharLen(@target, $p, $n, cp)</c>.
    /// </summary>
    private static LStatement FromPAnsiChar(LoweringContext context, LExpression target, LExpression pointer)
    {
        var types = context.Types;
        var p = new LLocalRef(context.NewTemp(types.PAnsiChar));
        var n = new LLocalRef(context.NewTemp(types.Integer));
        var character = new LDeref(new LPointerOffset(p, context.Convert(n, types.Int64), types.AnsiChar, types.PAnsiChar), types.AnsiChar);
        var scan = Scan(context, n, context.Compare(LCompareOperator.NotEqual, character, context.Constant(BigInteger.Zero, types.AnsiChar)));
        var notNil = context.Compare(LCompareOperator.NotEqual, p, new LConst(new ConstantValue(null, types.Nil), types.Nil));
        return new LBlock([
            new LAssign(p, pointer),
            new LAssign(n, context.Constant(BigInteger.Zero, types.Integer)),
            new LIf(notNil, scan, @else: null),
            Call("_AStrFromPCharLen", [types.Pointer, types.Pointer, types.Integer, types.Word], AddressOf(context, target), p, n,
                context.Constant(((StringTypeSymbol)target.Type).CodePage, types.Word))]);
    }

    /// <summary>
    /// LANG-245 last row: <c>$n := 0; while ($n &lt; N+1) and (a[$n] &lt;&gt; #0) do Inc($n); _UStrFromPWCharLen(@target, @a[0], $n)</c>
    /// (the array evaluated once).
    /// </summary>
    private static LStatement FromCharArray(LoweringContext context, LExpression target, ArrayTypeSymbol type, LExpression array)
    {
        var types = context.Types;
        var (setup, value) = Once(context, array);
        var n = new LLocalRef(context.NewTemp(types.Integer));
        var element = new LIndex(value, context.Convert(n, types.Int64), types.Char);
        var inside = new LBinary(LBinaryOperator.ConditionalAnd,
            context.Compare(LCompareOperator.Less, n, context.Constant(type.Length, types.Integer)),
            context.Compare(LCompareOperator.NotEqual, element, context.Constant(BigInteger.Zero, types.Char)));
        return new LBlock([.. setup,
            new LAssign(n, context.Constant(BigInteger.Zero, types.Integer)),
            Scan(context, n, inside),
            Call("_UStrFromPWCharLen", [types.Pointer, types.Pointer, types.Integer], AddressOf(context, target),
                AddressOf(context, new LIndex(value, context.Constant(BigInteger.Zero, types.Int64), types.Char)), n)]);
    }

    /// <summary><c>while condition do n := n + 1</c>.</summary>
    private static LStatement Scan(LoweringContext context, LExpression n, LExpression condition)
    {
        var label = context.NewLoop("while");
        var step = new LAssign(n, new LBinary(LBinaryOperator.Add, n, context.Constant(BigInteger.One, context.Types.Integer)));
        return new LLoop(label, new LIf(condition, step, new LBreak(label)), step: null);
    }

    /// <summary>
    /// LANG-290: <c>PChar(s)</c> / <c>PAnsiChar(a)</c> = the data pointer, or a static empty terminator for <c>''</c> (CMP-324:
    /// <c>select (s == null), @.str.empty, s</c>).
    /// </summary>
    public static LExpression DataPointer(LoweringContext context, LExpression text, TypeSymbol pointerType)
    {
        var value = context.NewValue(text.Type);
        var isEmpty = context.Compare(LCompareOperator.Equal, new LTempRef(value), new LStringLiteral("", context.Types.String));
        return new LTemp(value, text, new LSelect(isEmpty, new LStringLiteral("", pointerType), context.Convert(new LTempRef(value), pointerType)));
    }
}
