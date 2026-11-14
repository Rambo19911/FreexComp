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
    private static readonly Conversion Pointer = new(ConversionKind.ImplicitReference, IsImplicit: true, Exists: true);
    private static readonly Conversion String = new(ConversionKind.ImplicitString, IsImplicit: true, Exists: true);

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
            (PointerTypeSymbol s, PointerTypeSymbol d) when Pointers.Compatible(s, d) => Pointer,                  // rule 9
            (PointerTypeSymbol { Name: "nil" }, DynamicArrayTypeSymbol or ProceduralTypeSymbol) => Pointer,         // LANG-266 (C-064, C-161)
            (ProceduralTypeSymbol s, ProceduralTypeSymbol d) when Signatures.Compatible(s.Signature, d.Signature) => Pointer,    // LANG-483
            (PointerTypeSymbol { Name: "nil" }, ClassTypeSymbol) => Pointer,                                       // LANG-600: nil (C-080)
            (ClassTypeSymbol s, ClassTypeSymbol d) when s.InheritsFrom(d) => Pointer,                              // descendant → ancestor
            (PointerTypeSymbol { Name: "nil" }, ClassRefTypeSymbol) => Pointer,                                    // LANG-266 (C-086)
            (ClassRefTypeSymbol s, ClassRefTypeSymbol d) when s.Class.InheritsFrom(d.Class) => Pointer,            // LANG-280 rule 10
            (CharTypeSymbol, StringTypeSymbol) => String,                                                         // LANG-245: a 1-char string
            (StringTypeSymbol, StringTypeSymbol) => String,                                                       // LANG-245 (C-063)
            (PointerTypeSymbol, StringTypeSymbol) when Strings.IsCharPointer(source, wide: true) && Strings.IsUnicode(destination) => String,
            (PointerTypeSymbol, StringTypeSymbol { Variant: not StringVariant.Short }) when Strings.IsCharPointer(source, wide: false) => String,
            (ArrayTypeSymbol, StringTypeSymbol) when Strings.IsCharArray(source) && Strings.IsUnicode(destination) => String,
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
        if (destination is PointerTypeSymbol && (Strings.IsCharPointer(destination, wide: true) || Strings.IsCharPointer(destination, wide: false))
            && (value.Value is string || source is CharTypeSymbol && value.Value is BigInteger))
        {
            // LANG-245: a string (or character) literal → PChar / PAnsiChar: the static literal data (C-063)
            return new ConstantValue(value.Value as string ?? ((char)(int)(BigInteger)value.Value!).ToString(), destination);
        }

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

        // C-058 decision: a finite constant that overflows Single is E2012 rather than a silent infinity (types.md is silent)
        if (destination is RealTypeSymbol { RealKind: RealKind.Single } && RealValue(value) is var real && double.IsFinite(real) && float.IsInfinity((float)real))
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
            // LANG-245: a character constant becomes a 1-char string constant (AnsiChar zero-extended, ABI-027)
            StringTypeSymbol when value.Value is BigInteger unit => new ConstantValue(((char)(int)unit).ToString(), destination),
            // a set keeps the elements its capacity holds (like the run-time truncation)
            SetTypeSymbol set => new ConstantValue((BigInteger)value.Value! & ((BigInteger.One << set.Capacity) - 1), destination),
            RealTypeSymbol { RealKind: RealKind.Single } => new ConstantValue((double)(float)RealValue(value), destination),
            RealTypeSymbol => new ConstantValue(RealValue(value), destination),
            _ => new ConstantValue(value.Value, destination),
        };
    }

    /// <summary>E2010.</summary>
    public void ReportIncompatible(TypeSymbol source, TypeSymbol destination, SyntaxNode at, DiagnosticBag diagnostics) =>
        diagnostics.Report(at, "E2010", source.Name, destination.Name);

    /// <summary>LANG-245: <c>UnicodeString</c> → <c>AnsiString</c>/<c>UTF8String</c>/<c>RawByteString</c>/<c>ShortString</c> may lose data (W1020).</summary>
    public static bool IsNarrowing(TypeSymbol source, TypeSymbol destination) =>
        Strings.IsUnicode(source) && destination is StringTypeSymbol { Variant: not StringVariant.Unicode };

    private static bool IsReal(TypeSymbol type) => type is RealTypeSymbol { RealKind: not RealKind.Currency };

    private static double RealValue(ConstantValue value) => value.Value is BigInteger i ? (double)i : (double)value.Value!;
}
