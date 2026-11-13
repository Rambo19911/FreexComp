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

/// <summary>Predefined integer types (LANG-200; ranges from <see cref="Bits"/> and <see cref="IsSigned"/>).</summary>
public sealed class IntegerTypeSymbol : TypeSymbol
{
    internal IntegerTypeSymbol(string name, int bits, bool isSigned)
        : base(TypeKind.Integer, name, size: bits / 8, alignment: bits / 8)
    {
        Bits = bits;
        IsSigned = isSigned;
        Min = isSigned ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
        Max = isSigned ? (BigInteger.One << (bits - 1)) - 1 : (BigInteger.One << bits) - 1;
    }

    public int Bits { get; }

    public bool IsSigned { get; }

    public BigInteger Min { get; }

    public BigInteger Max { get; }

    public override bool IsOrdinal => true;
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

/// <summary><c>Char</c> (UTF-16 code unit) and <c>AnsiChar</c> (byte).</summary>
public sealed class CharTypeSymbol : TypeSymbol
{
    internal CharTypeSymbol(string name, bool isWide)
        : base(TypeKind.Char, name, size: isWide ? 2 : 1, alignment: isWide ? 2 : 1)
    {
        IsWide = isWide;
    }

    public bool IsWide { get; }

    public int MaxValue => IsWide ? 0xFFFF : 0xFF;

    public override bool IsOrdinal => true;
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
