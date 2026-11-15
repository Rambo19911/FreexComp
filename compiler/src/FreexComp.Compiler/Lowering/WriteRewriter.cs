using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// CMP-410 / LANG-910…922: <c>Write</c>/<c>Writeln</c> → one <c>_Write*</c> helper call per argument (file, value, width),
/// then <c>_WriteLn</c> for <c>Writeln</c>, <c>_WriteEnd</c>, and <c>_IOCheck</c> in <c>{$I+}</c> state (ABI-132). The file is
/// the given <c>Text</c> variable or <c>System.Output</c>, passed by address. M2.1 argument types: integers (<c>_WriteInt</c>
/// as <c>Int64</c> / <c>_WriteUInt</c> as <c>UInt64</c>), the Boolean family (<c>_WriteBool</c>, normalised to <c>Boolean</c>),
/// <c>Char</c>/<c>AnsiChar</c> (<c>_WriteWChar</c>/<c>_WriteAChar</c>), string constants (<c>_WriteUStr</c>) and reals
/// (<c>_WriteFloat</c> with width and decimals, C-059).
/// </summary>
internal static class WriteRewriter
{
    public static LStatement Rewrite(LoweringContext context, BoundWriteStatement write)
    {
        var types = context.Types;
        // the default file is the bound System.Output (LANG-910, 511)
        var file = new LAddrOf(write.File is { } variable ? context.Lower(variable) : new LGlobalRef(SystemFile(context.Output, "Output")), types.Pointer);
        var statements = ImmutableArray.CreateBuilder<LStatement>();
        foreach (var argument in write.Arguments)
        {
            var value = context.Lower(argument.Value);
            if (argument.Value.Type is RealTypeSymbol)
            {
                // CMP-410: _WriteFloat(T, v as Double, w, d); Write(x) = w 23, d -1; Write(x:w) = d -1
                var realWidth = argument.Width is { } rw ? context.Lower(rw) : context.Constant(23, types.Integer);
                var decimals = argument.Decimals is { } d ? context.Lower(d) : context.Constant(BigInteger.MinusOne, types.Integer);
                statements.Add(Call("_WriteFloat", [types.Pointer, types.Double, types.Integer, types.Integer],
                    [file, context.Convert(value, types.Double), realWidth, decimals]));
                continue;
            }

            var width = argument.Width is { } w ? context.Lower(w) : context.Constant(BigInteger.Zero, types.Integer);
            if (Strings.IsCharPointer(argument.Value.Type, wide: false) || Strings.IsCharArray(argument.Value.Type))
            {
                // C-063: a PAnsiChar through an AnsiString, a char array through a UnicodeString (up to its first #0, LANG-911)
                var target = Strings.IsCharArray(argument.Value.Type) ? types.String : types.AnsiString;
                value = ManagedRewriter.Value(context, new BoundConversionExpression(argument.Value.Syntax, argument.Value, target, ConversionKind.ImplicitString,
                    isExplicit: false, constantValue: null));
            }

            var (name, parameterType) = value.Type switch
            {
                IntegerTypeSymbol { IsSigned: true } => ("_WriteInt", types.Int64),
                IntegerTypeSymbol => ("_WriteUInt", types.UInt64),
                BooleanTypeSymbol => ("_WriteBool", types.Boolean),
                CharTypeSymbol { IsWide: true } => ("_WriteWChar", types.Char),
                CharTypeSymbol => ("_WriteAChar", types.AnsiChar),
                StringTypeSymbol { Variant: StringVariant.Unicode } => ("_WriteUStr", types.Pointer),
                StringTypeSymbol { Variant: StringVariant.Ansi } => ("_WriteAStr", types.Pointer),
                StringTypeSymbol { Variant: StringVariant.Short } => ("_WriteSStr", types.Pointer),
                PointerTypeSymbol => ("_WritePWChar", types.Pointer),
                var other => throw new NotSupportedException($"Write of '{other.Name}' is not lowered yet."),
            };
            var converted = Strings.IsShort(value.Type) ? new LAddrOf(value, types.Pointer)
                : parameterType == types.Pointer ? value : context.Convert(value, parameterType);
            statements.Add(Call(name, [types.Pointer, parameterType, types.Integer], [file, converted, width]));
        }

        if (write.IsWriteln)
        {
            statements.Add(Call("_WriteLn", [types.Pointer], [file]));
        }

        statements.Add(Call("_WriteEnd", [types.Pointer], [file]));
        if (write.Syntax.FirstToken().DirectiveState.IoChecks)
        {
            statements.Add(Call("_IOCheck", [], []));
        }

        return new LBlock(statements.ToImmutable());
    }

    private static LCallStatement Call(string name, ImmutableArray<TypeSymbol> parameterTypes, ImmutableArray<LExpression> arguments) =>
        new(new LHelperCallee(new RuntimeHelper(name, parameterTypes, ReturnType: null, IsNoReturn: false)), arguments);

    private static LCall Function(LoweringContext context, string name, TypeSymbol result, LExpression file) =>
        new(new LHelperCallee(new RuntimeHelper(name, [context.Types.Pointer], result, IsNoReturn: false)), [file]);

    /// <summary>The address of the given <c>Text</c> variable, or of <c>System.Input</c>.</summary>
    private static LAddrOf InputFile(LoweringContext context, BoundExpression? file) =>
        new(file is not null ? context.Lower(file) : new LGlobalRef(SystemFile(context.Input, "Input")), context.Types.Pointer);

    /// <summary><c>System.Output</c>/<c>System.Input</c> as bound; a unit bound without a <c>System</c> that declares it cannot use the default file.</summary>
    private static VariableSymbol SystemFile(VariableSymbol? bound, string name) =>
        bound ?? throw new InvalidOperationException($"The default file needs System.{name} (LANG-910, 940): bind the unit against a System.");

    // ------------------------------------------------------------------ Read, Readln, Eof, Eoln (C-066)

    /// <summary>
    /// CMP-411 / LANG-940…945: per variable, left to right, <c>_ReadInt</c> (Int64; a variable of a narrower range is checked
    /// against its type's bounds — outside, <c>SetInOutRes(106)</c> records I/O error 106 (C-233) — then stored truncated), <c>_ReadUInt</c> for a range beyond
    /// Int64 (<c>UInt64</c>), <c>_ReadFloat</c> (converted to the variable's real type), <c>_ReadWChar</c>,
    /// <c>_ReadUStr(T, @s)</c>; then <c>_ReadLn(T)</c> for <c>Readln</c>, <c>_ReadEnd(T)</c> and, in <c>{$I+}</c> state,
    /// <c>_IOCheck</c> (ABI-132). The file is the leading <c>Text</c> argument or <c>System.Input</c>.
    /// </summary>
    public static LStatement Read(LoweringContext context, BoundIntrinsicCallStatement read)
    {
        var types = context.Types;
        var hasFile = read.Arguments.Length > 0 && read.Arguments[0].Type is TextTypeSymbol;
        var file = InputFile(context, hasFile ? read.Arguments[0] : null);
        var statements = ImmutableArray.CreateBuilder<LStatement>();
        foreach (var variable in read.Arguments.Skip(hasFile ? 1 : 0))
        {
            switch (variable.Type)
            {
                case IntegerTypeSymbol when Ordinals.Bounds(variable.Type)!.Value.High > long.MaxValue:
                    statements.Add(new LAssign(context.Lower(variable), context.Convert(Function(context, "_ReadUInt", types.UInt64, file), variable.Type)));
                    break;
                case IntegerTypeSymbol:
                {
                    var (low, high) = Ordinals.Bounds(variable.Type)!.Value;
                    var value = Function(context, "_ReadInt", types.Int64, file);
                    if (low == long.MinValue && high == long.MaxValue)
                    {
                        statements.Add(new LAssign(context.Lower(variable), context.Convert(value, variable.Type)));
                        break;
                    }

                    // LANG-942: a value outside the variable's range is I/O error 106, recorded in InOutRes (C-233, ABI-132):
                    // {$I-} code sees it through IOResult, under {$I+} the _IOCheck below raises it
                    var read64 = new LLocalRef(context.NewTemp(types.Int64));
                    var outside = new LBinary(LBinaryOperator.ConditionalOr,
                        context.Compare(LCompareOperator.Less, read64, context.Constant(low, types.Int64)),
                        context.Compare(LCompareOperator.Greater, read64, context.Constant(high, types.Int64)));
                    statements.Add(new LAssign(read64, value));
                    statements.Add(new LIf(outside, Call("SetInOutRes", [types.Integer], [context.Constant(106, types.Integer)]), @else: null));
                    statements.Add(new LAssign(context.Lower(variable), context.Convert(read64, variable.Type)));
                    break;
                }

                case RealTypeSymbol:
                    statements.Add(new LAssign(context.Lower(variable), context.Convert(Function(context, "_ReadFloat", types.Double, file), variable.Type)));
                    break;
                case CharTypeSymbol:
                    statements.Add(new LAssign(context.Lower(variable), context.Convert(Function(context, "_ReadWChar", types.Char, file), variable.Type)));
                    break;
                default:                                                                    // string (LANG-945)
                    statements.Add(Call("_ReadUStr", [types.Pointer, types.Pointer], [file, new LAddrOf(context.Lower(variable), types.Pointer)]));
                    break;
            }
        }

        if (read.Intrinsic.Intrinsic == Symbols.IntrinsicKind.Readln)
        {
            statements.Add(Call("_ReadLn", [types.Pointer], [file]));
        }

        statements.Add(Call("_ReadEnd", [types.Pointer], [file]));
        if (read.Syntax.FirstToken().DirectiveState.IoChecks)
        {
            statements.Add(Call("_IOCheck", [], []));
        }

        return new LBlock(statements.ToImmutable());
    }

    /// <summary>LANG-946: <c>Eof</c>/<c>Eoln</c> = <c>_Eof(@F)</c>/<c>_Eoln(@F)</c> (rtl-abi.md §9.9), <c>F</c> = <c>Input</c> by default.</summary>
    public static LExpression TextFunction(LoweringContext context, BoundIntrinsicCallExpression call) =>
        Function(context, call.Intrinsic.Intrinsic == Symbols.IntrinsicKind.Eof ? "_Eof" : "_Eoln", context.Types.Boolean,
            InputFile(context, call.Arguments.IsEmpty ? null : call.Arguments[0]));
}
