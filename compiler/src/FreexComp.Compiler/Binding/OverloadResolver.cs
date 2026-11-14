using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// LANG-442/443: picks one routine of an overload candidate set (LANG-441) by conversion cost. A candidate is applicable
/// when the argument count fits and every argument converts to its parameter; each argument/parameter pair has a cost
/// (<see cref="Cost"/>). The routine that is better than every other (≤ everywhere, &lt; somewhere) wins, then the unique
/// smallest sum, then the unique candidate using fewest default parameters; otherwise the call is ambiguous.
/// </summary>
internal sealed class OverloadResolver(Conversions conversions)
{
    /// <summary>The outcome: the chosen routine, or null with the tied candidates (ambiguous) / none (nothing applicable).</summary>
    public readonly record struct Resolution(RoutineSymbol? Routine, ImmutableArray<RoutineSymbol> Tied);

    /// <param name="isVariable">Whether an argument is an assignable variable reference (for <c>var</c>/<c>out</c> parameters).</param>
    public Resolution Resolve(ImmutableArray<RoutineSymbol> candidates, ImmutableArray<BoundExpression> arguments, Func<BoundExpression, bool> isVariable)
    {
        var applicable = new List<(RoutineSymbol Routine, int[] Costs, int Defaults)>();
        foreach (var candidate in candidates)
        {
            if (Costs(candidate, arguments, isVariable) is { } costs)
            {
                applicable.Add((candidate, costs, candidate.Parameters.Length - arguments.Length));
            }
        }

        if (applicable.Count <= 1)
        {
            return new Resolution(applicable.Count == 1 ? applicable[0].Routine : null, []);
        }

        var best = applicable.Where(a => applicable.All(b => ReferenceEquals(a.Routine, b.Routine) || Better(a.Costs, b.Costs))).ToList();
        if (best.Count == 1)
        {
            return new Resolution(best[0].Routine, []);
        }

        var minimum = applicable.Min(a => a.Costs.Sum());
        var tied = applicable.Where(a => a.Costs.Sum() == minimum).ToList();
        if (tied.Count > 1)
        {
            var fewest = tied.Min(a => a.Defaults);
            if (tied.Count(a => a.Defaults == fewest) == 1)
            {
                tied = [.. tied.Where(a => a.Defaults == fewest)];
            }
        }

        return tied.Count == 1 ? new Resolution(tied[0].Routine, []) : new Resolution(null, [.. tied.Select(a => a.Routine)]);
    }

    /// <summary>Per-argument costs, or null when <paramref name="candidate"/> is not applicable.</summary>
    private int[]? Costs(RoutineSymbol candidate, ImmutableArray<BoundExpression> arguments, Func<BoundExpression, bool> isVariable)
    {
        var parameters = candidate.Parameters;
        if (arguments.Length > parameters.Length || parameters.Skip(arguments.Length).Any(p => p.DefaultValue is null))
        {
            return null;
        }

        var costs = new int[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            if (Cost(arguments[i], parameters[i], isVariable) is not { } cost)
            {
                return null;
            }

            costs[i] = cost;
        }

        return costs;
    }

    private static bool Better(int[] a, int[] b) => a.Zip(b).All(p => p.First <= p.Second) && a.Zip(b).Any(p => p.First < p.Second);

    /// <summary>The LANG-442 cost table entry for passing <paramref name="argument"/> to <paramref name="parameter"/>; null = not applicable.</summary>
    public int? Cost(BoundExpression argument, ParameterSymbol parameter, Func<BoundExpression, bool> isVariable)
    {
        var (source, destination) = (argument.Type, parameter.Type);
        if (parameter.IsByReference)
        {
            var matches = destination is OpenArrayTypeSymbol ? conversions.Classify(source, destination).Exists : Pointers.SameType(source, destination);
            return isVariable(argument) && matches ? 0 : null;
        }

        if (argument is BoundArrayConstructor && destination is OpenArrayTypeSymbol)
        {
            return conversions.Classify(source, destination).Exists ? 3 : null;    // LANG-442: array constructor → open array
        }

        var constant = argument.ConstantValue;
        if (constant is not null
            ? conversions.ConvertConstant(constant, destination, argument.Syntax, new DiagnosticBag()) is null
            : !ReferenceEquals(source, destination) && !conversions.Classify(source, destination).Exists)
        {
            return null;
        }

        if (ReferenceEquals(source, destination))
        {
            return constant?.Value is string ? 2 : 0;                  // a string literal → string costs 2
        }

        return (source, destination) switch
        {
            (BooleanTypeSymbol, BooleanTypeSymbol) => 2,
            (IntegerTypeSymbol or CharTypeSymbol or EnumTypeSymbol, IntegerTypeSymbol or CharTypeSymbol or EnumTypeSymbol)
                => Contains(Ordinals.Bounds(destination), Ordinals.Bounds(source)) ? 1 : 2,
            (RealTypeSymbol { RealKind: RealKind.Single }, RealTypeSymbol { RealKind: RealKind.Double }) => 1,
            (RealTypeSymbol, RealTypeSymbol) => 2,
            (IntegerTypeSymbol, RealTypeSymbol) => 3,
            (CharTypeSymbol, StringTypeSymbol) => 2,
            (StringTypeSymbol { Variant: StringVariant.Ansi }, StringTypeSymbol { CodePage: 0xFFFF }) when constant is null => 1,
            (StringTypeSymbol, StringTypeSymbol) => 3,
            (PointerTypeSymbol { Name: "nil" }, _) => 4,
            (_, PointerTypeSymbol d) when d.PointedType is null => 4,  // → untyped Pointer
            (ClassTypeSymbol s, ClassTypeSymbol d) => Steps(s, d),
            (ClassRefTypeSymbol s, ClassRefTypeSymbol d) => Steps(s.Class, d.Class),
            _ when Pointers.SameType(source, destination) => 0,
            _ => 3,
        };
    }

    private static bool Contains((BigInteger Low, BigInteger High)? outer, (BigInteger Low, BigInteger High)? inner) =>
        outer is { } o && inner is { } i && o.Low <= i.Low && i.High <= o.High;

    /// <summary>Descendant → ancestor: one per inheritance step, capped at 8.</summary>
    private static int Steps(ClassTypeSymbol source, ClassTypeSymbol destination)
    {
        var steps = 0;
        for (var type = source; type is not null && !ReferenceEquals(type, destination); type = type.Parent)
        {
            steps++;
        }

        return Math.Min(steps, 8);
    }
}
