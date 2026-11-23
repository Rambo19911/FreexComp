using System.Numerics;
using FreexComp.Debugger.Values;
using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Expressions;

/// <summary>
/// What a sub-expression evaluates to (Pascal values §9.3): a location in debuggee memory, or an immediate value.
/// </summary>
internal abstract record Value
{
    /// <summary>The Pascal type name used in messages and for result presentation (§9.6).</summary>
    public abstract string TypeName { get; }
}

/// <summary>An address with a type of the type information; produced by designators. Read only when its value is needed.</summary>
/// <param name="Address">Address of the storage.</param>
/// <param name="Type">Type of the storage.</param>
/// <param name="OpenArrayHigh">For an open-array parameter: the value of its hidden upper-bound variable.</param>
internal sealed record LocationValue(ulong Address, PdiType Type, long? OpenArrayHigh = null) : Value
{
    public override string TypeName => ValueFormatter.TypeName(Type);
}

/// <summary>An integer held in 64 bits; <paramref name="Unsigned"/> says the bits are an unsigned 64-bit number.</summary>
internal sealed record IntValue(long Bits, bool Unsigned = false) : Value
{
    /// <summary>The mathematical value.</summary>
    public Int128 Number => Unsigned ? (ulong)Bits : Bits;

    public bool FitsInt32 => Number >= int.MinValue && Number <= int.MaxValue;

    public override string TypeName => FitsInt32 ? "Integer" : Unsigned ? "UInt64" : "Int64";
}

internal sealed record FloatValue(double Number) : Value
{
    public override string TypeName => "Double";
}

internal sealed record BoolValue(bool Flag) : Value
{
    public override string TypeName => "Boolean";
}

/// <summary>A UTF-16 code unit.</summary>
internal sealed record CharValue(char Unit) : Value
{
    public override string TypeName => "Char";
}

internal sealed record StrValue(string Text) : Value
{
    public override string TypeName => "string";
}

internal sealed record EnumValue(PdiType Type, long Ordinal) : Value
{
    public override string TypeName => ValueFormatter.TypeName(Type);
}

/// <summary>A set of ordinals 0 … 255: bit <c>n</c> of <paramref name="Members"/> is the ordinal <c>n</c>.</summary>
/// <param name="Element">The element type; null for the empty constructor, which fits every set.</param>
/// <param name="Members">The member bits.</param>
internal sealed record SetValue(PdiType? Element, BigInteger Members) : Value
{
    public override string TypeName => Element is null ? "set" : "set of " + ValueFormatter.TypeName(Element);
}

/// <summary>A pointer value; <paramref name="Target"/> is null for an untyped pointer.</summary>
internal sealed record PtrValue(ulong Address, PdiType? Target) : Value
{
    public override string TypeName => Target is null ? "Pointer" : "^" + ValueFormatter.TypeName(Target);
}

/// <summary>A reference to a class instance: its address and its dynamic class (or the known ancestor that supplies the fields).</summary>
internal sealed record InstanceValue(ulong Address, PdiType Type, string ClassName) : Value
{
    public override string TypeName => ClassName;
}

internal sealed record NilValue : Value
{
    public override string TypeName => "nil";
}

/// <summary>A type name: valid only as a typecast or as the argument of SizeOf, High and Low (§9.2 rule 5).</summary>
internal sealed record TypeValue(PdiType Type) : Value
{
    public override string TypeName => ValueFormatter.TypeName(Type);
}

/// <summary>Types of immediate values, which have no entry in the type information.</summary>
internal static class SyntheticTypes
{
    public static readonly PdiType Integer = new() { Id = 0, Name = "Integer", Kind = PdiTypeKind.Integer, Size = 4, Signed = true };
    public static readonly PdiType Int64 = new() { Id = 0, Name = "Int64", Kind = PdiTypeKind.Integer, Size = 8, Signed = true };
    public static readonly PdiType UInt64 = new() { Id = 0, Name = "UInt64", Kind = PdiTypeKind.Integer, Size = 8, Signed = false };
    public static readonly PdiType Double = new() { Id = 0, Name = "Double", Kind = PdiTypeKind.Float, Size = 8, FloatFormat = PdiFloatFormat.Double };
    public static readonly PdiType Boolean = new() { Id = 0, Name = "Boolean", Kind = PdiTypeKind.Boolean, Size = 1 };
    public static readonly PdiType Char = new() { Id = 0, Name = "Char", Kind = PdiTypeKind.Char, Size = 2 };
    public static readonly PdiType String = new() { Id = 0, Name = "string", Kind = PdiTypeKind.String, Size = 8, Flavor = PdiStringFlavor.Unicode };
    public static readonly PdiType Pointer = new() { Id = 0, Name = "Pointer", Kind = PdiTypeKind.Pointer, Size = 8 };

    public static PdiType PointerTo(PdiType target) => new() { Id = 0, Name = "", Kind = PdiTypeKind.Pointer, Size = 8, To = target };

    public static PdiType SetOf(PdiType? element) => new() { Id = 0, Name = element is null ? "set" : "", Kind = PdiTypeKind.Set, Size = 32, Base = element };
}
