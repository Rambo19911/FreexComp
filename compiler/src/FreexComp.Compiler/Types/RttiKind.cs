namespace FreexComp.Compiler.Types;

/// <summary>
/// The <c>Kind</c> byte of a type's RTTI block (rtl-abi.md §10; the ordinals of <c>System.TTypeKind</c>). The one mapping from a type
/// symbol to its kind (CMP-1474, C-416): the RTTI emitter writes it into the block and <c>GetTypeKind(T)</c> (LANG-974) folds to
/// it, so the two cannot differ.
/// </summary>
internal static class RttiKind
{
    private const int Unknown = 0;
    private const int Integer = 1;
    private const int Char = 2;
    private const int Enumeration = 3;
    private const int Float = 4;
    private const int Set = 5;
    private const int Class = 6;
    private const int Method = 7;
    private const int WChar = 8;
    private const int UString = 9;
    private const int AString = 10;
    private const int SString = 11;
    private const int Record = 12;
    private const int Array = 13;
    private const int DynArray = 14;
    private const int Interface = 15;
    private const int Int64 = 16;
    private const int Pointer = 17;
    private const int ClassRef = 18;
    private const int Procedure = 19;

    /// <summary>
    /// The kind of <paramref name="type"/>, whether or not a block is emitted for it. Integer types of 64 bits (also the
    /// pointer-sized ones of a 64-bit target and subranges hosted by them) are <see cref="Int64"/>; the Boolean types are
    /// enumerations; a method reference is an interface (ABI-097); anything without a kind of its own (<c>Text</c>) is
    /// <see cref="Unknown"/>.
    /// </summary>
    public static int Of(TypeSymbol type) => type switch
    {
        StringTypeSymbol { Variant: StringVariant.Short } => SString,
        StringTypeSymbol { Variant: StringVariant.Unicode } => UString,
        StringTypeSymbol => AString,
        IntegerTypeSymbol { Bits: 64 } => Int64,
        IntegerTypeSymbol => Integer,
        CharTypeSymbol { IsWide: true } => WChar,
        CharTypeSymbol => Char,
        BooleanTypeSymbol or EnumTypeSymbol => Enumeration,
        RealTypeSymbol => Float,
        SetTypeSymbol => Set,
        ClassTypeSymbol => Class,
        DynamicArrayTypeSymbol => DynArray,
        RecordTypeSymbol => Record,
        InterfaceTypeSymbol => Interface,
        ArrayTypeSymbol => Array,
        PointerTypeSymbol => Pointer,
        ClassRefTypeSymbol => ClassRef,
        MethodPointerTypeSymbol => Method,
        ProceduralTypeSymbol => Procedure,
        _ => Unknown,
    };
}
