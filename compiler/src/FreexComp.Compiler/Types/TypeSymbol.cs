using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Types;

public enum TypeKind { Error, Integer, Boolean, Char, Real, Enum, Subrange, Set, String, ShortString, Pointer,
                       Array, DynamicArray, OpenArray, Record, Class, ClassRef, Interface, Procedural,
                       MethodPointer, Reference, TypeParameter, Text, Untyped, Nil }

/// <summary>A type (api.md §5). Sizes follow rtl-abi.md §2; -1 where a type has no fixed size.</summary>
public abstract class TypeSymbol : Symbol
{
    private protected TypeSymbol(
        TypeKind typeKind,
        string name,
        long size,
        int alignment,
        Symbol? containingSymbol = null,
        ImmutableArray<Location> locations = default,
        Visibility visibility = Visibility.Public)
        : base(SymbolKind.Type, name, containingSymbol, locations, visibility)
    {
        TypeKind = typeKind;
        Size = size;
        Alignment = alignment;
    }

    public TypeKind TypeKind { get; }

    public long Size { get; }

    public int Alignment { get; }

    public virtual bool IsManaged => false;

    public virtual bool IsOrdinal => false;

    /// <summary>Class parent, subrange host, enum of an enum subrange, alias target of a distinct type.</summary>
    public virtual TypeSymbol? BaseType => null;

    /// <summary>Own members: fields, methods, properties, nested types, constants.</summary>
    public virtual ImmutableArray<Symbol> Members => [];

    public IEnumerable<Symbol> GetMembers(string name, bool includeInherited = true)
    {
        for (var type = this; type != null; type = includeInherited ? type.InheritedFrom : null)
        {
            foreach (var member in type.Members)
            {
                if (IdentifierComparer.Instance.Equals(member.Name, name))
                {
                    yield return member;
                }
            }
        }
    }

    public virtual bool IsGenericDefinition => false;

    public virtual ImmutableArray<TypeSymbol> TypeArguments => [];

    public virtual TypeSymbol? GenericDefinition => null;

    /// <summary>True for the type itself and its ancestors (classes and interfaces).</summary>
    public bool InheritsFrom(TypeSymbol other)
    {
        for (var type = this; type != null; type = type.InheritedFrom)
        {
            if (ReferenceEquals(type, other))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The ancestor whose members are inherited (class/interface parent); null for other types.</summary>
    private protected virtual TypeSymbol? InheritedFrom => null;

    /// <summary><c>type Name</c>; declared types show their definition (CMP-731) once they exist.</summary>
    public override string ToDisplayString() => "type " + Name;
}

/// <summary>The type of an erroneous expression; compatible with everything so errors do not cascade.</summary>
public sealed class ErrorTypeSymbol : TypeSymbol
{
    internal ErrorTypeSymbol() : base(TypeKind.Error, "<error>", size: -1, alignment: 1)
    {
    }
}

/// <summary>
/// Predefined integer types (LANG-200; ranges from <see cref="Bits"/> and <see cref="IsSigned"/>) and integer subranges
/// (LANG-211/212: <see cref="TypeSymbol.TypeKind"/> <see cref="TypeKind.Subrange"/>, <see cref="Min"/>/<see cref="Max"/> = the
/// bounds, <see cref="Bits"/>/<see cref="IsSigned"/> and <see cref="BaseType"/> = the representation type).
/// </summary>
public sealed class IntegerTypeSymbol : TypeSymbol
{
    private readonly IntegerTypeSymbol? _representation;

    internal IntegerTypeSymbol(string name, int bits, bool isSigned)
        : base(TypeKind.Integer, name, size: bits / 8, alignment: bits / 8)
    {
        Bits = bits;
        IsSigned = isSigned;
        Min = isSigned ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
        Max = isSigned ? (BigInteger.One << (bits - 1)) - 1 : (BigInteger.One << bits) - 1;
    }

    /// <summary>A subrange <c>min..max</c> stored as <paramref name="representation"/> (LANG-212).</summary>
    internal IntegerTypeSymbol(string name, IntegerTypeSymbol representation, BigInteger min, BigInteger max, Symbol? containingSymbol,
        ImmutableArray<Location> locations)
        : base(TypeKind.Subrange, name, representation.Size, representation.Alignment, containingSymbol, locations)
    {
        _representation = representation;
        Bits = representation.Bits;
        IsSigned = representation.IsSigned;
        Min = min;
        Max = max;
    }

    public int Bits { get; }

    public bool IsSigned { get; }

    public BigInteger Min { get; }

    public BigInteger Max { get; }

    public override bool IsOrdinal => true;

    public override TypeSymbol? BaseType => _representation;
}

/// <summary><c>Boolean</c> (enumeration False, True) and the C-style <c>ByteBool</c>/<c>WordBool</c>/<c>LongBool</c> (LANG-215).</summary>
public sealed class BooleanTypeSymbol : TypeSymbol
{
    internal BooleanTypeSymbol(string name, int size, bool isCStyle)
        : base(TypeKind.Boolean, name, size, alignment: size)
    {
        IsCStyle = isCStyle;
    }

    public bool IsCStyle { get; }

    public override bool IsOrdinal => true;
}

/// <summary><c>Char</c> (UTF-16 code unit), <c>AnsiChar</c> (byte) and their subranges (LANG-211, <see cref="TypeKind.Subrange"/>).</summary>
public sealed class CharTypeSymbol : TypeSymbol
{
    private readonly CharTypeSymbol? _host;

    internal CharTypeSymbol(string name, bool isWide)
        : base(TypeKind.Char, name, size: isWide ? 2 : 1, alignment: isWide ? 2 : 1)
    {
        IsWide = isWide;
        MaxValue = isWide ? 0xFFFF : 0xFF;
    }

    /// <summary>A subrange <c>min..max</c> of <paramref name="host"/>, stored like it (LANG-212).</summary>
    internal CharTypeSymbol(string name, CharTypeSymbol host, int min, int max, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.Subrange, name, host.Size, host.Alignment, containingSymbol, locations)
    {
        _host = host;
        IsWide = host.IsWide;
        MinValue = min;
        MaxValue = max;
    }

    public bool IsWide { get; }

    public int MinValue { get; }

    public int MaxValue { get; }

    public override bool IsOrdinal => true;

    public override TypeSymbol? BaseType => _host;
}

/// <summary>
/// An enumeration (LANG-210) or a subrange of one (<see cref="TypeKind.Subrange"/>; <see cref="BaseType"/> = the enumeration).
/// Size per rtl-abi.md §2: the smallest of 1, 2, 4 bytes holding <c>0..High</c> of the base enumeration, at least
/// <c>{$MINENUMSIZE}</c>.
/// </summary>
public sealed class EnumTypeSymbol : TypeSymbol
{
    private readonly EnumTypeSymbol? _base;

    internal EnumTypeSymbol(string name, int size, bool isScoped, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.Enum, name, size, size, containingSymbol, locations)
    {
        IsScoped = isScoped;
    }

    internal EnumTypeSymbol(string name, EnumTypeSymbol baseEnum, BigInteger low, BigInteger high, Symbol? containingSymbol,
        ImmutableArray<Location> locations)
        : base(TypeKind.Subrange, name, baseEnum.Size, baseEnum.Alignment, containingSymbol, locations)
    {
        _base = baseEnum;
        IsScoped = baseEnum.IsScoped;
        Low = low;
        High = high;
    }

    /// <summary>The elements in declaration order (of the base enumeration for a subrange).</summary>
    public ImmutableArray<EnumMemberSymbol> EnumMembers => _base?.EnumMembers ?? _members;

    private ImmutableArray<EnumMemberSymbol> _members = [];

    /// <summary>Set once by the declaration binder: the elements and the ordinal range of a base enumeration.</summary>
    internal void SetMembers(ImmutableArray<EnumMemberSymbol> members)
    {
        _members = members;
        Low = members.IsEmpty ? 0 : members[0].Ordinal;
        High = members.IsEmpty ? 0 : members[^1].Ordinal;
    }

    public BigInteger Low { get; private set; }

    public BigInteger High { get; private set; }

    /// <summary>Declared under <c>{$SCOPEDENUMS ON}</c>: the elements are reachable only as <c>TEnum.Element</c>.</summary>
    public bool IsScoped { get; }

    public override ImmutableArray<Symbol> Members => [.. EnumMembers];

    public override bool IsOrdinal => true;

    public override TypeSymbol? BaseType => _base;

    /// <summary>rtl-abi.md §2: 1, 2 or 4 bytes for <c>0..high</c>, at least <paramref name="minimum"/>.</summary>
    internal static int SizeFor(BigInteger high, int minimum) => Math.Max(minimum, high <= 0xFF ? 1 : high <= 0xFFFF ? 2 : 4);
}

/// <summary>
/// <c>set of T</c> (LANG-250, rtl-abi.md §5): N = Ord(High(T)) + 1 bits (the low bound is not subtracted); 1..64 bits are a
/// small set stored as i8/i16/i32/i64, more a big set of 32 bytes (align 1). <see cref="ElementType"/> is null only for the
/// type of a context-free <c>[]</c> (<see cref="BuiltInTypes.EmptySet"/>), which is compatible with every set type.
/// </summary>
public sealed class SetTypeSymbol : TypeSymbol
{
    internal SetTypeSymbol(string name, TypeSymbol? elementType, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.Set, name, SizeFor(elementType), SizeFor(elementType) <= 8 ? (int)SizeFor(elementType) : 1, containingSymbol, locations)
    {
        ElementType = elementType;
    }

    public TypeSymbol? ElementType { get; }

    /// <summary>Small sets: 8, 16, 32 or 64 bits held in one integer.</summary>
    public bool IsSmall => Size <= 8;

    /// <summary>Number of element bits the representation holds (8 × size; 256 for a big set).</summary>
    public int Capacity => (int)Size * 8;

    private static long SizeFor(TypeSymbol? elementType)
    {
        var bits = elementType is not null && Ordinals.Bounds(elementType) is { } bounds ? bounds.High + 1 : 1;
        return bits <= 8 ? 1 : bits <= 16 ? 2 : bits <= 32 ? 4 : bits <= 64 ? 8 : 32;
    }
}

/// <summary>
/// A static array <c>array[IndexType] of ElementType</c> (LANG-255, ABI-062): <see cref="Length"/> = High − Low + 1 elements,
/// size <c>Length × SizeOf(ElementType)</c>, alignment of the element type. <c>array[I1, I2] of T</c> is an array of
/// <c>array[I2] of T</c>.
/// </summary>
public sealed class ArrayTypeSymbol : TypeSymbol
{
    internal ArrayTypeSymbol(string name, TypeSymbol indexType, TypeSymbol elementType, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.Array, name, LengthOf(indexType) * elementType.Size, elementType.Alignment, containingSymbol, locations)
    {
        IndexType = indexType;
        ElementType = elementType;
        (Low, High) = Ordinals.Bounds(indexType)!.Value;
    }

    public TypeSymbol IndexType { get; }

    public TypeSymbol ElementType { get; }

    public BigInteger Low { get; }

    public BigInteger High { get; }

    public long Length => (long)(High - Low + 1);

    /// <summary>An array of a managed element type is managed (CMP-325, C-065).</summary>
    public override bool IsManaged => ElementType.IsManaged;

    /// <summary>Number of values of an ordinal index type (may exceed the LANG-255 limit; the binder checks it first).</summary>
    internal static long LengthOf(TypeSymbol indexType)
    {
        var (low, high) = Ordinals.Bounds(indexType)!.Value;
        var count = high - low + 1;
        return count > long.MaxValue / 1024 ? long.MaxValue / 1024 : (long)count;
    }
}

/// <summary>
/// A record without variant part (LANG-260, ABI-060): <see cref="Fields"/> in declaration order with their offsets; size and
/// alignment from ABI-060 with the packing <see cref="Packing"/> (1 for <c>packed record</c>, else the <c>{$ALIGN}</c> value at
/// the <c>record</c> keyword). <see cref="IsAnonymous"/> records (no type declaration) are named by their source text.
/// </summary>
public sealed class RecordTypeSymbol : TypeSymbol
{
    internal RecordTypeSymbol(string name, long size, int alignment, bool isPacked, int packing, bool isAnonymous, Symbol? containingSymbol,
        ImmutableArray<Location> locations)
        : base(TypeKind.Record, name, size, alignment, containingSymbol, locations)
    {
        IsPacked = isPacked;
        Packing = packing;
        IsAnonymous = isAnonymous;
    }

    public ImmutableArray<FieldSymbol> Fields { get; internal set; } = [];

    public bool IsPacked { get; }

    /// <summary>The ABI-060 cap <c>A</c> on field alignment: 1, 2, 4 or 8.</summary>
    public int Packing { get; }

    public bool IsAnonymous { get; }

    public override ImmutableArray<Symbol> Members => [.. Fields];

    /// <summary>A record with any managed field is managed (CMP-325, C-065; its <c>tkRecord</c> block lists those fields).</summary>
    public override bool IsManaged => Fields.Any(f => f.Type.IsManaged);

    /// <summary>
    /// ABI-060: each field at the next multiple of <c>min(alignment, packing)</c>; record alignment = the largest of those (1
    /// when empty), size rounded up to it.
    /// </summary>
    internal static (ImmutableArray<long> Offsets, long Size, int Alignment) Layout(IEnumerable<TypeSymbol> fieldTypes, int packing)
    {
        var offsets = ImmutableArray.CreateBuilder<long>();
        long end = 0;
        var alignment = 1;
        foreach (var type in fieldTypes)
        {
            var fieldAlignment = Math.Min(type.Alignment, packing);
            alignment = Math.Max(alignment, fieldAlignment);
            var offset = (end + fieldAlignment - 1) / fieldAlignment * fieldAlignment;
            offsets.Add(offset);
            end = offset + type.Size;
        }

        return (offsets.ToImmutable(), (end + alignment - 1) / alignment * alignment, alignment);
    }
}

/// <summary>Set compatibility (LANG-250, 280 rule 8).</summary>
internal static class Sets
{
    /// <summary>An element of type <paramref name="element"/> may be in a set of <paramref name="set"/>: integers with integers, else one host.</summary>
    public static bool AcceptsElement(SetTypeSymbol set, TypeSymbol element) =>
        set.ElementType is null
        || (set.ElementType is IntegerTypeSymbol && element is IntegerTypeSymbol)
        || (set.ElementType is BooleanTypeSymbol && element is BooleanTypeSymbol)
        || (set.ElementType is CharTypeSymbol && element is CharTypeSymbol && (Ordinals.SameHost(set.ElementType, element) || element is CharTypeSymbol { IsWide: false }))
        || (set.ElementType is EnumTypeSymbol && element is EnumTypeSymbol && Ordinals.SameHost(set.ElementType, element));

    /// <summary>Base types with the same host (LANG-250); <c>[]</c> goes with every set.</summary>
    public static bool Compatible(SetTypeSymbol a, SetTypeSymbol b) =>
        a.ElementType is null || b.ElementType is null || AcceptsElement(a, b.ElementType) && AcceptsElement(b, a.ElementType);

    /// <summary>The type a binary set operation is done in: the other operand's for <c>[]</c>, else the larger one (left on a tie).</summary>
    public static SetTypeSymbol Combine(SetTypeSymbol left, SetTypeSymbol right) =>
        left.ElementType is null ? right
        : right.ElementType is null ? left
        : right.Size > left.Size ? right : left;
}

/// <summary>Pointer identity and compatibility (LANG-206, 266, 267).</summary>
internal static class Pointers
{
    /// <summary>The same type: identical, or two typed pointer types to the same type (LANG-206).</summary>
    public static bool SameType(TypeSymbol a, TypeSymbol b) =>
        ReferenceEquals(a, b) || a is PointerTypeSymbol { PointedType: { } x } && b is PointerTypeSymbol { PointedType: { } y } && ReferenceEquals(x, y);

    /// <summary>
    /// LANG-266/267: <c>nil</c> and the untyped <c>Pointer</c> go with every pointer type in both directions; two typed pointers
    /// only when they point to the same type.
    /// </summary>
    public static bool Compatible(PointerTypeSymbol source, PointerTypeSymbol destination) =>
        source.PointedType is null || destination.PointedType is null || ReferenceEquals(source.PointedType, destination.PointedType);

    /// <summary>The element type of pointer arithmetic: the pointed type, a byte for the untyped <c>Pointer</c> (CMP-342).</summary>
    public static long ElementSize(PointerTypeSymbol pointer) => pointer.PointedType?.Size ?? 1;
}

/// <summary>The ordinal view of the ordinal types (LANG-210…213, 280 rules 2, 4, 5, 6).</summary>
internal static class Ordinals
{
    /// <summary>The value range of an integer, char, enumeration or Boolean type (subranges: their bounds); null otherwise.</summary>
    public static (BigInteger Low, BigInteger High)? Bounds(TypeSymbol type) => type switch
    {
        IntegerTypeSymbol integer => (integer.Min, integer.Max),
        CharTypeSymbol c => (c.MinValue, c.MaxValue),
        EnumTypeSymbol e => (e.Low, e.High),
        BooleanTypeSymbol => (BigInteger.Zero, BigInteger.One),
        _ => null,
    };

    /// <summary>The type a subrange belongs to (an enum subrange's enumeration, a char subrange's char type); else the type.</summary>
    public static TypeSymbol Host(TypeSymbol type) => type switch
    {
        EnumTypeSymbol { BaseType: { } baseEnum } => baseEnum,
        CharTypeSymbol { BaseType: { } host } => host,
        _ => type,
    };

    /// <summary>Same host enumeration or char type (LANG-280 rules 4 and 5, LANG-303).</summary>
    public static bool SameHost(TypeSymbol a, TypeSymbol b) => ReferenceEquals(Host(a), Host(b));
}

public enum RealKind { Single, Double, Currency }

/// <summary><c>Single</c>, <c>Double</c> and <c>Currency</c> (Phase 8+, E2900 on use).</summary>
public sealed class RealTypeSymbol : TypeSymbol
{
    internal RealTypeSymbol(string name, RealKind realKind)
        : base(TypeKind.Real, name, size: realKind == RealKind.Single ? 4 : 8, alignment: realKind == RealKind.Single ? 4 : 8)
    {
        RealKind = realKind;
    }

    public RealKind RealKind { get; }
}

public enum StringVariant { Unicode, Ansi, Short }

/// <summary>Long strings (UTF-16 or byte strings with a code page, ABI-027) and short strings.</summary>
public sealed class StringTypeSymbol : TypeSymbol
{
    internal StringTypeSymbol(string name, StringVariant variant, int codePage, int maxLength = 0)
        : base(
            variant == StringVariant.Short ? TypeKind.ShortString : TypeKind.String,
            name,
            size: variant == StringVariant.Short ? maxLength + 1 : 8,
            alignment: variant == StringVariant.Short ? 1 : 8)
    {
        Variant = variant;
        CodePage = codePage;
        MaxLength = maxLength;
    }

    public StringVariant Variant { get; }

    /// <summary>1200 for UTF-16, 65001 for <c>AnsiString</c>/<c>UTF8String</c>/short strings, $FFFF for <c>RawByteString</c>.</summary>
    public int CodePage { get; }

    /// <summary>Capacity of a short string (1..255); 0 for long strings.</summary>
    public int MaxLength { get; }

    public override bool IsManaged => Variant != StringVariant.Short;
}

/// <summary>
/// A dynamic array <c>array of ElementType</c> (LANG-257, rtl-abi.md §4): a managed reference (8 bytes, <c>nil</c> when empty) to
/// element 0 of a counted block; 0-based. <c>array of array of T</c> is an array of arrays (ABI-043).
/// </summary>
public sealed class DynamicArrayTypeSymbol : TypeSymbol
{
    internal DynamicArrayTypeSymbol(string name, TypeSymbol elementType, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.DynamicArray, name, size: 8, alignment: 8, containingSymbol, locations)
    {
        ElementType = elementType;
    }

    public TypeSymbol ElementType { get; }

    public override bool IsManaged => true;

    /// <summary>The number of nested dynamic-array levels (1 for <c>array of Integer</c>, 2 for <c>array of array of Integer</c>).</summary>
    public int Depth => ElementType is DynamicArrayTypeSymbol inner ? inner.Depth + 1 : 1;
}

/// <summary>String kinds and their compile-time text (LANG-245, ABI-027, CMP-320; C-063).</summary>
internal static class Strings
{
    public static bool IsUnicode(TypeSymbol type) => type is StringTypeSymbol { Variant: StringVariant.Unicode };

    public static bool IsAnsi(TypeSymbol type) => type is StringTypeSymbol { Variant: StringVariant.Ansi };

    public static bool IsShort(TypeSymbol type) => type is StringTypeSymbol { Variant: StringVariant.Short };

    /// <summary><c>PChar</c> (wide) or <c>PAnsiChar</c>: a pointer to a character type with pointer arithmetic.</summary>
    public static bool IsCharPointer(TypeSymbol type, bool wide) =>
        type is PointerTypeSymbol { AllowsPointerMath: true, PointedType: CharTypeSymbol { TypeKind: TypeKind.Char } c } && c.IsWide == wide;

    /// <summary>LANG-245 last row: <c>array[0..N] of Char</c>.</summary>
    public static bool IsCharArray(TypeSymbol type) =>
        type is ArrayTypeSymbol { ElementType: CharTypeSymbol { IsWide: true, TypeKind: TypeKind.Char } } array && array.Low.IsZero;

    /// <summary>The literal's bytes (ABI-027: UTF-8; a lone surrogate becomes U+FFFD).</summary>
    public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    /// <summary>The UTF-8 bytes of <paramref name="text"/>, cut to at most <paramref name="max"/> at a sequence boundary (ABI §9.4 <c>_SStrFromUStr</c>).</summary>
    public static byte[] ShortBytes(string text, int max)
    {
        var bytes = Utf8(text);
        if (bytes.Length <= max)
        {
            return bytes;
        }

        var end = max;
        while (end > 0 && (bytes[end] & 0xC0) == 0x80)
        {
            end--;
        }

        return bytes[..end];
    }

    /// <summary>The length of a constant of string type <paramref name="type"/>: UTF-16 units, or bytes for the byte strings.</summary>
    public static int ConstantLength(string text, TypeSymbol type) => type switch
    {
        StringTypeSymbol { Variant: StringVariant.Ansi } => Utf8(text).Length,
        StringTypeSymbol { Variant: StringVariant.Short } s => ShortBytes(text, s.MaxLength).Length,
        _ => text.Length,
    };
}

/// <summary>Untyped <c>Pointer</c> (<see cref="PointedType"/> null) and typed pointers.</summary>
public sealed class PointerTypeSymbol : TypeSymbol
{
    internal PointerTypeSymbol(string name, TypeSymbol? pointedType, bool allowsPointerMath)
        : base(TypeKind.Pointer, name, size: 8, alignment: 8)
    {
        PointedType = pointedType;
        AllowsPointerMath = allowsPointerMath;
    }

    /// <summary>Set after construction for a forward reference <c>^T</c> to a type declared later in the same section (LANG-208).</summary>
    public TypeSymbol? PointedType { get; internal set; }

    /// <summary>Pointer arithmetic without <c>{$POINTERMATH ON}</c> (<c>PChar</c>, <c>PAnsiChar</c>).</summary>
    public bool AllowsPointerMath { get; }
}

/// <summary>
/// A plain procedural type <c>procedure(…)</c> / <c>function(…): T</c> (LANG-480): the code address of a global routine, 8 bytes,
/// <c>nil</c> allowed. <see cref="Signature"/> is a routine symbol declared nowhere that carries the parameters and result type,
/// so calls through the value bind and lower like direct calls (CMP-378). Calling-convention directives are ignored (LANG-462).
/// </summary>
public sealed class ProceduralTypeSymbol : TypeSymbol
{
    internal ProceduralTypeSymbol(string name, Symbol? containingSymbol, ImmutableArray<Location> locations)
        : base(TypeKind.Procedural, name, size: 8, alignment: 8, containingSymbol, locations)
    {
        Signature = new RoutineSymbol(name, this);
    }

    /// <summary>Parameters and result type (set by the declaration binder).</summary>
    public RoutineSymbol Signature { get; }
}

/// <summary>LANG-483 signature compatibility of routines and procedural types.</summary>
internal static class Signatures
{
    /// <summary>
    /// Same kind (procedure/function), parameter count, identical parameter types and modes, identical result type; names,
    /// defaults and calling conventions do not matter.
    /// </summary>
    public static bool Compatible(RoutineSymbol a, RoutineSymbol b) =>
        (a.ReturnType is null) == (b.ReturnType is null)
        && (a.ReturnType is null || Pointers.SameType(a.ReturnType, b.ReturnType!))
        && a.Parameters.Length == b.Parameters.Length
        && a.Parameters.Zip(b.Parameters).All(p => p.First.Mode == p.Second.Mode && Pointers.SameType(p.First.Type, p.Second.Type));

    /// <summary>A routine as an E2010 operand: <c>procedure(var Integer)</c>, <c>function(Integer): Integer</c>.</summary>
    public static string Display(RoutineSymbol routine)
    {
        var parameters = routine.Parameters.IsEmpty ? "" : "(" + string.Join(", ", routine.Parameters.Select(p => ParameterSymbol.Prefix(p.Mode) + p.Type.Name)) + ")";
        return routine.ReturnType is null ? "procedure" + parameters : $"function{parameters}: {routine.ReturnType.Name}";
    }
}

/// <summary><c>Text</c>: an RTL-owned record the compiler treats as opaque bytes (rtl-abi.md §2).</summary>
public sealed class TextTypeSymbol : TypeSymbol
{
    internal TextTypeSymbol(string name) : base(TypeKind.Text, name, size: 624, alignment: 8)
    {
    }
}
