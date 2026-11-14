using System.Collections.Immutable;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// An LLVM first-class type as a value (codegen.md §2). Only the model lives here; <c>LlvmTextWriter</c> owns the textual
/// syntax (CMP-125). Instances compare structurally.
/// </summary>
internal abstract record IrType
{
    public static readonly IntegerIrType I1 = new(1);
    public static readonly IntegerIrType I8 = new(8);
    public static readonly IntegerIrType I16 = new(16);
    public static readonly IntegerIrType I32 = new(32);
    public static readonly IntegerIrType I64 = new(64);
    public static readonly FloatingIrType Float = new(FloatingKind.Float);
    public static readonly FloatingIrType Double = new(FloatingKind.Double);
    public static readonly PointerIrType Ptr = new();
    public static readonly VoidIrType Void = new();
    public static readonly TokenIrType Token = new();

    public static IntegerIrType Int(int bits) => bits switch
    {
        1 => I1,
        8 => I8,
        16 => I16,
        32 => I32,
        64 => I64,
        _ => new IntegerIrType(bits),
    };
}

/// <summary><c>iN</c>.</summary>
internal sealed record IntegerIrType(int Bits) : IrType;

internal enum FloatingKind { Float, Double }

/// <summary><c>float</c> / <c>double</c>.</summary>
internal sealed record FloatingIrType(FloatingKind Kind) : IrType;

/// <summary>The opaque <c>ptr</c> (address space 0).</summary>
internal sealed record PointerIrType : IrType;

/// <summary><c>[N x T]</c>.</summary>
internal sealed record ArrayIrType(long Length, IrType Element) : IrType;

/// <summary><c>void</c> (routine results only).</summary>
internal sealed record VoidIrType : IrType;

/// <summary><c>token</c>: the value of a <c>catchswitch</c>, <c>catchpad</c> or <c>cleanuppad</c> (CMP-391…393).</summary>
internal sealed record TokenIrType : IrType;

/// <summary>A literal struct type <c>{ T1, T2 }</c>, or <c>&lt;{ … }&gt;</c> when <see cref="IsPacked"/>.</summary>
internal sealed record StructIrType(ImmutableArray<IrType> Fields, bool IsPacked = false) : IrType
{
    public bool Equals(StructIrType? other) =>
        other is not null && IsPacked == other.IsPacked && Fields.SequenceEqual(other.Fields);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IsPacked);
        foreach (var field in Fields)
        {
            hash.Add(field);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// A reference to a named struct type <c>%"Name"</c>: declared with <see cref="IIrWriter.DeclareStruct"/>, or — when it
/// carries its <see cref="Body"/> (record types, CMP-306) — declared by the writer on first use.
/// </summary>
internal sealed record NamedStructIrType(LinkName Name, StructIrType? Body = null) : IrType;
