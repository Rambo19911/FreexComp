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
