using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// CMP-410 / LANG-910…922: <c>Write</c>/<c>Writeln</c> → one <c>_Write*</c> helper call per argument (file, value, width),
/// then <c>_WriteLn</c> for <c>Writeln</c>, <c>_WriteEnd</c>, and <c>_IOCheck</c> in <c>{$I+}</c> state (ABI-132). The file is
/// the given <c>Text</c> variable or <c>System.Output</c>, passed by address. M2.1 argument types: integers (<c>_WriteInt</c>
/// as <c>Int64</c> / <c>_WriteUInt</c> as <c>UInt64</c>), the Boolean family (<c>_WriteBool</c>, normalised to <c>Boolean</c>),
/// <c>Char</c>/<c>AnsiChar</c> (<c>_WriteWChar</c>/<c>_WriteAChar</c>) and string constants (<c>_WriteUStr</c>).
/// </summary>
internal static class WriteRewriter
{
    public static LStatement Rewrite(LoweringContext context, BoundWriteStatement write)
    {
        var types = context.Types;
        var file = new LAddrOf(write.File is { } variable ? context.Lower(variable) : new LRuntimeVariableRef(new RuntimeVariable("Output", types.Text)), types.Pointer);
        var statements = ImmutableArray.CreateBuilder<LStatement>();
        foreach (var argument in write.Arguments)
        {
            var value = context.Lower(argument.Value);
            var width = argument.Width is { } w ? context.Lower(w) : context.Constant(BigInteger.Zero, types.Integer);
            var (name, parameterType) = argument.Value.Type switch
            {
                IntegerTypeSymbol { IsSigned: true } => ("_WriteInt", types.Int64),
                IntegerTypeSymbol => ("_WriteUInt", types.UInt64),
                BooleanTypeSymbol => ("_WriteBool", types.Boolean),
                CharTypeSymbol { IsWide: true } => ("_WriteWChar", types.Char),
                CharTypeSymbol => ("_WriteAChar", types.AnsiChar),
                StringTypeSymbol { Variant: StringVariant.Unicode } when value is LStringLiteral => ("_WriteUStr", types.Pointer),
                var other => throw new NotSupportedException($"Write of '{other.Name}' is not lowered yet."),
            };
            var converted = parameterType == types.Pointer ? value : context.Convert(value, parameterType);
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
}
