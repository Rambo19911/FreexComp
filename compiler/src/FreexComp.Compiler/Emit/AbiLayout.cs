using System.Collections.Immutable;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Emit;

/// <summary>
/// The run-time layouts of rtl-abi.md that contain addresses or <c>NativeInt</c> fields, computed from their field types for a
/// target (CMP-1007): no offset of these structures is written as a literal anywhere else.
/// </summary>
internal static class AbiLayout
{
    /// <summary><c>NativeInt</c> as the run-time blocks store it: <c>i&lt;PointerSize*8&gt;</c>.</summary>
    public static IntegerIrType NativeInt(TargetInfo target) => IrType.Int(target.PointerSize * 8);

    /// <summary>rtl-abi.md §7.2: the fixed part of a VMT — Parent, InstanceSize, ClassName, TypeInfo, IntfTable, MsgTable, InitTable, Reserved.</summary>
    public static StructIrType VmtHeader(TargetInfo target) =>
        new([IrType.Ptr, NativeInt(target), IrType.Ptr, IrType.Ptr, IrType.Ptr, IrType.Ptr, IrType.Ptr, IrType.Ptr]);

    /// <summary>The byte offset of field <paramref name="field"/> of the VMT header (0 = Parent, 2 = ClassName).</summary>
    public static long VmtFieldOffset(TargetInfo target, int field) => TypeLayout.NaturalLayout(target, VmtHeader(target)).Offsets[field];

    /// <summary>The byte offset of virtual slot <paramref name="slot"/> in a VMT: the header's size plus one address per slot.</summary>
    public static long VmtSlotOffset(TargetInfo target, int slot) => TypeLayout.LlvmSize(target, VmtHeader(target)) + (long)target.PointerSize * slot;

    /// <summary>The byte offset of slot <paramref name="slot"/> in an IMT (ABI-091): one address per slot.</summary>
    public static long ImtSlotOffset(TargetInfo target, int slot) => (long)target.PointerSize * slot;

    /// <summary>rtl-abi.md §4: the dynamic-array header in front of element 0 — RefCount, Reserved, Length.</summary>
    public static StructIrType DynArrayHeader(TargetInfo target) => new([IrType.I32, IrType.I32, NativeInt(target)]);

    /// <summary>The (negative) byte offset of a dynamic array's <c>Length</c> field from element 0 (ABI-041).</summary>
    public static long DynArrayLengthOffset(TargetInfo target)
    {
        var (offsets, size) = TypeLayout.NaturalLayout(target, DynArrayHeader(target));
        return offsets[2] - size;
    }

    /// <summary>CMP-355: a unit table entry <c>{ $init, $final }</c>.</summary>
    public static StructIrType UnitEntry { get; } = new([IrType.Ptr, IrType.Ptr]);

    /// <summary>ABI-180: a unit's threadvar descriptor <c>{ NativeInt Size, NativeInt Offset }</c>.</summary>
    public static StructIrType TlsDescriptor(TargetInfo target) => new([NativeInt(target), NativeInt(target)]);
}
