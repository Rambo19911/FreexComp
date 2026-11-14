using System.Numerics;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Assignment compatibility (types.md LANG-280) for ordinal and real types: rule 1 (identity), 2 (integers), 3 (reals),
/// 4 (characters, LANG-245/262), 6 (Boolean family, LANG-215). The other rules arrive with their milestones.
/// </summary>
internal sealed class Conversions
{
    private static readonly Conversion Identity = new(ConversionKind.Identity, IsImplicit: true, Exists: true);
    private static readonly Conversion Numeric = new(ConversionKind.ImplicitNumeric, IsImplicit: true, Exists: true);
    private static readonly Conversion NoConversion = new(ConversionKind.None, IsImplicit: false, Exists: false);

    /// <summary>The implicit conversion from <paramref name="source"/> to <paramref name="destination"/> (type level, no constants).</summary>
    public Conversion Classify(TypeSymbol source, TypeSymbol destination)
    {
        if (ReferenceEquals(source, destination) || source.TypeKind == TypeKind.Error || destination.TypeKind == TypeKind.Error)
        {
            return Identity;                                            // the error type converts silently (CMP-135)
        }

        return (source, destination) switch
        {
            (IntegerTypeSymbol, IntegerTypeSymbol) => Numeric,
            (IntegerTypeSymbol or RealTypeSymbol, RealTypeSymbol) when IsReal(destination) && (source is IntegerTypeSymbol || IsReal(source))
                => Numeric,
            (CharTypeSymbol { IsWide: false }, CharTypeSymbol { IsWide: true }) => Numeric,    // zero-extended (LANG-245)
            (CharTypeSymbol, CharTypeSymbol) when Ordinals.SameHost(source, destination) => Numeric,  // char subranges (rule 4)
            (EnumTypeSymbol, EnumTypeSymbol) when Ordinals.SameHost(source, destination) => Numeric,  // rule 5
            (BooleanTypeSymbol, BooleanTypeSymbol) => Numeric,
            (SetTypeSymbol s, SetTypeSymbol d) when d.ElementType != null && Sets.Compatible(s, d) => Numeric,     // rule 8
            _ => NoConversion,
        };
    }

    /// <summary>
    /// Converts a constant for assignment to <paramref name="destination"/>: E2012 for an integer out of range, a
    /// character constant to <c>AnsiChar</c> per LANG-262 (W1021 / E2010), E2010 when no conversion exists. Null after
    /// an error.
    /// </summary>
    public ConstantValue? ConvertConstant(ConstantValue value, TypeSymbol destination, SyntaxNode at, DiagnosticBag diagnostics)
    {
        var source = value.Type;
        if (source is CharTypeSymbol { IsWide: true } && destination is CharTypeSymbol { IsWide: false } && value.Value is BigInteger code)
        {
            if (code > 255)
            {
                ReportIncompatible(source, destination, at, diagnostics);
                return null;
            }

            if (code > 127)
            {
                diagnostics.Report(at, "W1021");
            }

            return new ConstantValue(code, destination);
        }

        var conversion = Classify(source, destination);
        if (!conversion.Exists)
        {
            ReportIncompatible(source, destination, at, diagnostics);
            return null;
        }

        if (destination is not BooleanTypeSymbol && value.Value is BigInteger number && Ordinals.Bounds(destination) is { } bounds
            && (number < bounds.Low || number > bounds.High))
        {
            diagnostics.Report(at, "E2012", value.ToDisplayString(), destination.Name);
            return null;
        }

        if (conversion.Kind == ConversionKind.Identity)
        {
            return value;
        }

        return destination switch
        {
            // a set keeps the elements its capacity holds (like the run-time truncation)
            SetTypeSymbol set => new ConstantValue((BigInteger)value.Value! & ((BigInteger.One << set.Capacity) - 1), destination),
            RealTypeSymbol { RealKind: RealKind.Single } => new ConstantValue((double)(float)RealValue(value), destination),
            RealTypeSymbol => new ConstantValue(RealValue(value), destination),
            _ => new ConstantValue(value.Value, destination),
        };
    }

    /// <summary>E2010; E2900 for string and pointer conversions, which arrive with M2.2 (LANG-245, 266, 267).</summary>
    public void ReportIncompatible(TypeSymbol source, TypeSymbol destination, SyntaxNode at, DiagnosticBag diagnostics)
    {
        var later = source is PointerTypeSymbol || destination is PointerTypeSymbol
            || (destination is StringTypeSymbol && source is StringTypeSymbol or CharTypeSymbol)
            || (source is StringTypeSymbol && destination is StringTypeSymbol or CharTypeSymbol);
        if (later)
        {
            diagnostics.Report(at, "E2900", $"conversion from '{source.Name}' to '{destination.Name}'");
        }
        else
        {
            diagnostics.Report(at, "E2010", source.Name, destination.Name);
        }
    }

    private static bool IsReal(TypeSymbol type) => type is RealTypeSymbol { RealKind: not RealKind.Currency };

    private static double RealValue(ConstantValue value) => value.Value is BigInteger i ? (double)i : (double)value.Value!;
}
