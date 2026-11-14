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

/// <summary>Untyped <c>Pointer</c> (<see cref="PointedType"/> null) and typed pointers.</summary>
public sealed class PointerTypeSymbol : TypeSymbol
{
    internal PointerTypeSymbol(string name, TypeSymbol? pointedType, bool allowsPointerMath)
        : base(TypeKind.Pointer, name, size: 8, alignment: 8)
    {
        PointedType = pointedType;
        AllowsPointerMath = allowsPointerMath;
    }

    public TypeSymbol? PointedType { get; }

    /// <summary>Pointer arithmetic without <c>{$POINTERMATH ON}</c> (<c>PChar</c>, <c>PAnsiChar</c>).</summary>
    public bool AllowsPointerMath { get; }
}

/// <summary><c>Text</c>: an RTL-owned record the compiler treats as opaque bytes (rtl-abi.md §2).</summary>
public sealed class TextTypeSymbol : TypeSymbol
{
    internal TextTypeSymbol(string name) : base(TypeKind.Text, name, size: 624, alignment: 8)
    {
    }
}
