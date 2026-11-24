using FreexComp.Compiler.Symbols;

namespace FreexComp.Compiler.Types;

/// <summary>
/// The Automation types of dispatch-interface members (com-interfaces.md LANG-695) and their call-descriptor type codes
/// (rtl-abi.md ABI-205): a variant type number, or <see cref="StringCode"/>; <see cref="BooleanFlag"/> marks a 1-byte
/// <c>Boolean</c> variable, <see cref="ByRefFlag"/> a <c>var</c>/<c>out</c> argument.
/// </summary>
internal static class AutomationTypes
{
    /// <summary><c>UnicodeString</c>, marshalled as an OLE string by the helper.</summary>
    public const int StringCode = 0x0100;

    /// <summary>With code 11: the Pascal variable is a 1-byte <c>Boolean</c>.</summary>
    public const int BooleanFlag = 0x0200;

    /// <summary>A <c>var</c> or <c>out</c> argument (never on the result type).</summary>
    public const int ByRefFlag = 0x4000;

    /// <summary>The variant type number of a 32-bit integer: what an enumeration travels as.</summary>
    public const int Int32Code = 3;

    /// <summary>
    /// The descriptor code of <paramref name="type"/> (with <see cref="BooleanFlag"/> for <c>Boolean</c>); null when it is not an
    /// Automation type (E2658). Aliases are the type itself; a subrange counts as its representation type.
    /// </summary>
    public static int? CodeOf(TypeSymbol type) => type switch
    {
        IntegerTypeSymbol integer => (integer.Bits, integer.IsSigned) switch
        {
            (16, true) => 2,
            (32, true) => Int32Code,
            (8, true) => 16,
            (8, false) => 17,
            (16, false) => 18,
            (32, false) => 19,
            (64, true) => 20,
            (64, false) => 21,
            _ => null,
        },
        EnumTypeSymbol => Int32Code,
        RealTypeSymbol { RealKind: RealKind.Single } => 4,
        RealTypeSymbol { RealKind: RealKind.Currency } => 6,
        RealTypeSymbol real => IsSystemDateTime(real) ? 7 : 5,
        BooleanTypeSymbol { IsCStyle: false, Size: 1 } => 11 | BooleanFlag,
        BooleanTypeSymbol { IsCStyle: true, Size: 2 } => 11,
        InterfaceTypeSymbol { IsMethodReference: false, IsAnonymousMethod: false } intf => intf.IsDispatchReference ? 9 : 13,
        StringTypeSymbol { Variant: StringVariant.Unicode } => StringCode,
        _ => null,
    };

    /// <summary><c>System.TDateTime</c> (a distinct type of <c>Double</c>).</summary>
    private static bool IsSystemDateTime(RealTypeSymbol type) =>
        string.Equals(type.Name, "TDateTime", StringComparison.OrdinalIgnoreCase)
        && type.ContainingSymbol is UnitSymbol { IsProgram: false } unit && string.Equals(unit.Name, "System", StringComparison.OrdinalIgnoreCase);
}
