using System.Collections.Immutable;
using System.Numerics;

namespace FreexComp.Compiler.Emit;

/// <summary>An operand: a local value, a global, or a constant (model only; the writer owns the syntax).</summary>
internal abstract record IrValue(IrType Type);

/// <summary>A named local value <c>%name</c> (parameter, alloca, temporary; CMP-303).</summary>
internal sealed record IrLocal(IrType Type, string Name) : IrValue(Type);

/// <summary>A constant operand, also usable in global initialisers.</summary>
internal abstract record IrConstant(IrType Type) : IrValue(Type);

/// <summary>The address of a global variable or function, <c>ptr @name</c>.</summary>
internal sealed record IrGlobal(LinkName Name) : IrConstant(IrType.Ptr);

/// <summary>
/// An integer constant. Any value is accepted and written in the signed range of the type (two's complement), so
/// <c>UInt64</c> maximum becomes <c>-1</c>; <c>i1</c> is written <c>true</c>/<c>false</c>.
/// </summary>
internal sealed record IrIntConstant(IntegerIrType IntegerType, BigInteger Value) : IrConstant(IntegerType)
{
    public static IrIntConstant I1(bool value) => new(IrType.I1, value ? 1 : 0);

    public static IrIntConstant I32(long value) => new(IrType.I32, value);

    public static IrIntConstant I64(long value) => new(IrType.I64, value);
}

/// <summary>A <c>float</c>/<c>double</c> constant (a <c>float</c> value is rounded to single precision).</summary>
internal sealed record IrFloatConstant(FloatingIrType FloatingType, double Value) : IrConstant(FloatingType);

/// <summary><c>ptr null</c>.</summary>
internal sealed record IrNullConstant() : IrConstant(IrType.Ptr);

/// <summary><c>zeroinitializer</c> of any type.</summary>
internal sealed record IrZeroConstant(IrType ZeroType) : IrConstant(ZeroType);

/// <summary><c>[T a, T b]</c>.</summary>
internal sealed record IrArrayConstant(ArrayIrType ArrayType, ImmutableArray<IrConstant> Elements) : IrConstant(ArrayType);

/// <summary><c>{ T1 a, T2 b }</c>; typed as <paramref name="AsType"/> (a named struct with that body) when given.</summary>
internal sealed record IrStructConstant(StructIrType StructType, ImmutableArray<IrConstant> Fields, IrType? AsType = null) : IrConstant(AsType ?? StructType);

/// <summary>
/// The constant expression <c>getelementptr inbounds (SourceType, ptr @Base, indices…)</c> (CMP-320 string references).
/// </summary>
internal sealed record IrGepConstant(IrType SourceType, IrGlobal Base, ImmutableArray<IrIntConstant> Indices) : IrConstant(IrType.Ptr);

/// <summary>A constant address from an integer: <c>inttoptr (i64 N to ptr)</c> (C-185, LANG-340 pointer constants).</summary>
internal sealed record IrIntToPtrConstant(System.Numerics.BigInteger Address) : IrConstant(IrType.Ptr);

/// <summary>A code address as an integer: <c>ptrtoint (ptr @f to i64)</c> (CMP-406 property accessors, C-112).</summary>
internal sealed record IrPtrToIntConstant(IrConstant Pointer) : IrConstant(IrType.I64);

/// <summary>A pointer call argument with the <c>align N</c> parameter attribute: <c>ptr align N %p</c> (CMP-326 memcpy operands).</summary>
internal sealed record IrAlignedPointer(IrValue Pointer, int Alignment) : IrValue(IrType.Ptr);

/// <summary>A basic block label (CMP-303: <c>&lt;kind&gt;.&lt;N&gt;</c>, numbered by the emitter).</summary>
internal sealed record IrBlock(string Name);
