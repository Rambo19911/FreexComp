using System.Collections.Immutable;

namespace FreexComp.Compiler.Emit;

internal enum IrLinkage { External, Internal, Private, LinkOnceOdr }

internal enum IrBinaryOperator { Add, Sub, Mul, SDiv, UDiv, SRem, URem, And, Or, Xor, Shl, LShr, AShr, FAdd, FSub, FMul, FDiv }

internal enum IrIntPredicate { Eq, Ne, Slt, Sle, Sgt, Sge, Ult, Ule, Ugt, Uge }

internal enum IrFloatPredicate { Oeq, One, Olt, Ole, Ogt, Oge, Ueq, Une, Ult, Ule, Ugt, Uge, Ord, Uno }

internal enum IrCastOperator { Trunc, ZExt, SExt, FPTrunc, FPExt, FPToSI, FPToUI, SIToFP, UIToFP, PtrToInt, IntToPtr, BitCast }

/// <summary>
/// A function attribute: an enum attribute (<c>uwtable</c>, <c>noreturn</c>) when <see cref="Value"/> and
/// <see cref="IsString"/> are unset, else a string attribute (<c>"no-builtins"</c>, <c>"frame-pointer"="all"</c>).
/// </summary>
internal sealed record IrAttribute(string Key, string? Value = null, bool IsString = false)
{
    public static IrAttribute Enum(string key) => new(key);

    public static IrAttribute String(string key, string? value = null) => new(key, value, IsString: true);
}

internal sealed record IrParameter(IrType Type, string Name);

/// <summary><c>define [linkage] RetType @Name(params) [#group] {</c>.</summary>
internal sealed record IrFunctionDefinition(
    LinkName Name, IrLinkage Linkage, IrType ReturnType, ImmutableArray<IrParameter> Parameters, int? AttributeGroup = null);

/// <summary><c>declare RetType @Name(types) [#group]</c>.</summary>
internal sealed record IrFunctionDeclaration(LinkName Name, IrType ReturnType, ImmutableArray<IrType> ParameterTypes, int? AttributeGroup = null);

/// <summary><c>@Name = [linkage] [unnamed_addr] global|constant Type Init, align N</c>.</summary>
internal sealed record IrGlobalDefinition(
    LinkName Name, IrLinkage Linkage, bool IsConstant, IrConstant Initializer, int Alignment, bool UnnamedAddr = false);

/// <summary>
/// The seam between <c>IrEmitter</c> and an LLVM backend (CMP-125, ADR 001). "LLVM-shaped": one method per IR entity and
/// instruction kind of codegen.md §1–§4. The emitter chooses every local and block name (CMP-303); the writer only
/// formats. Top-level entities may be added in any order; the writer emits them in CMP-301 order (declarations sorted by
/// link name, ordinal). Misuse (an instruction outside a block, a block without terminator) throws
/// <see cref="InvalidOperationException"/>.
/// </summary>
internal interface IIrWriter
{
    void BeginModule(string moduleName, string sourceFileName);

    /// <summary><c>%"Name" = type { … }</c>; returns the type to use in references.</summary>
    NamedStructIrType DeclareStruct(LinkName name, StructIrType body);

    IrGlobal DefineGlobal(IrGlobalDefinition global);

    /// <summary><c>@Name = external global Type, align N</c>: a variable defined in another module (in the globals section, call order).</summary>
    IrGlobal DeclareGlobal(LinkName name, IrType type, int alignment);

    IrGlobal DeclareFunction(IrFunctionDeclaration declaration);

    /// <summary><c>attributes #id = { … }</c>.</summary>
    void DefineAttributeGroup(int id, ImmutableArray<IrAttribute> attributes);

    /// <summary>Starts a definition; returns the parameter values in order. The first block must follow.</summary>
    ImmutableArray<IrLocal> BeginFunction(IrFunctionDefinition function);

    void AppendBlock(IrBlock block);

    IrLocal Alloca(string name, IrType type, int alignment);

    IrLocal Load(string name, IrType type, IrValue address, int alignment);

    void Store(IrValue value, IrValue address, int alignment);

    IrLocal Binary(string name, IrBinaryOperator op, IrValue left, IrValue right);

    IrLocal FNeg(string name, IrValue operand);

    IrLocal ICmp(string name, IrIntPredicate predicate, IrValue left, IrValue right);

    IrLocal FCmp(string name, IrFloatPredicate predicate, IrValue left, IrValue right);

    IrLocal Cast(string name, IrCastOperator op, IrValue value, IrType targetType);

    IrLocal Select(string name, IrValue condition, IrValue whenTrue, IrValue whenFalse);

    IrLocal Phi(string name, IrType type, ImmutableArray<(IrValue Value, IrBlock Block)> incoming);

    /// <summary><c>getelementptr [inbounds] SourceType, ptr %base, idx…</c>.</summary>
    IrLocal GetElementPtr(string name, IrType sourceType, IrValue address, ImmutableArray<IrValue> indices, bool inBounds = true);

    IrLocal ExtractValue(string name, IrValue aggregate, int index);

    /// <summary>
    /// Direct or indirect call. <paramref name="name"/> must be null for <c>void</c>; may be null for a discarded result
    /// (then null is returned). <paramref name="attributeGroup"/> is the call-site group (<c>#1</c> for noreturn helpers).
    /// </summary>
    IrLocal? Call(string? name, IrType returnType, IrValue callee, ImmutableArray<IrValue> arguments, int? attributeGroup = null);

    void Br(IrBlock target);

    void CondBr(IrValue condition, IrBlock whenTrue, IrBlock whenFalse);

    void Switch(IrValue value, IrBlock defaultBlock, ImmutableArray<(IrIntConstant Value, IrBlock Block)> cases);

    /// <summary><c>ret T v</c>, or <c>ret void</c> for null.</summary>
    void Ret(IrValue? value);

    void Unreachable();

    void EndFunction();

    void EndModule();
}
