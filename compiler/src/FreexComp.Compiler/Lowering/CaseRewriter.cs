using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Binding;

namespace FreexComp.Compiler.Lowering;

/// <summary>
/// CMP-322 / LANG-355…357: <c>case</c> → <see cref="LSwitch"/> on the selector widened to its computation type. Each
/// label becomes its ordinal value; ranges of at most <see cref="MaxExpandedRange"/> values are listed value by value,
/// larger ones stay ranges (tested before the switch). The <c>else</c> part is the default; without it nothing happens.
/// </summary>
internal static class CaseRewriter
{
    public const int MaxExpandedRange = 256;

    public static LStatement Rewrite(LoweringContext context, BoundCaseStatement statement)
    {
        var selector = context.Convert(context.Lower(statement.Selector), context.ComputationType(statement.Selector.Type));
        var sections = ImmutableArray.CreateBuilder<LSwitchSection>(statement.Arms.Length);
        foreach (var arm in statement.Arms)
        {
            var values = ImmutableArray.CreateBuilder<BigInteger>();
            var ranges = ImmutableArray.CreateBuilder<(BigInteger, BigInteger)>();
            foreach (var label in arm.Labels)
            {
                var low = Ordinal(label.Low);
                var high = label.High is null ? low : Ordinal(label.High);
                if (high - low + 1 <= MaxExpandedRange)
                {
                    for (var v = low; v <= high; v++)
                    {
                        values.Add(v);
                    }
                }
                else
                {
                    ranges.Add((low, high));
                }
            }

            sections.Add(new LSwitchSection(values.ToImmutable(), ranges.ToImmutable(), context.Lower(arm.Body)));
        }

        return new LSwitch(selector, sections.MoveToImmutable(), statement.Else is null ? null : context.Lower(statement.Else));
    }

    private static BigInteger Ordinal(BoundExpression label) => label.ConstantValue?.Value switch
    {
        BigInteger i => i,
        bool b => b ? BigInteger.One : BigInteger.Zero,
        _ => throw new InvalidOperationException("A case label must be an ordinal constant."),
    };
}
