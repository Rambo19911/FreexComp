using System.Collections.Immutable;

namespace FreexComp.Compiler.Emit;

internal enum IrLinkage { External, Internal, Private, LinkOnceOdr }

internal enum IrBinaryOperator { Add, Sub, Mul, SDiv, UDiv, SRem, URem, And, Or, Xor, Shl, LShr, AShr, FAdd, FSub, FMul, FDiv }

internal enum IrIntPredicate { Eq, Ne, Slt, Sle, Sgt, Sge, Ult, Ule, Ugt, Uge }

internal enum IrFloatPredicate { Oeq, One, Olt, Ole, Ogt, Oge, Ueq, Une, Ult, Ule, Ugt, Uge, Ord, Uno }

/// <summary>The <c>atomicrmw</c> operations the compiler emits (CMP-412, ABI-172).</summary>
internal enum IrAtomicOperator { Add, Sub, Xchg }

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
    LinkName Name, IrLinkage Linkage, IrType ReturnType, ImmutableArray<IrParameter> Parameters, int? AttributeGroup = null)
{
    /// <summary>CMP-421: the metadata node number of the function's <c>DISubprogram</c> (<c>define … !dbg !N</c>); null → none.</summary>
    public int? Subprogram { get; init; }
}

/// <summary><c>declare RetType @Name(types) [#group]</c>.</summary>
internal sealed record IrFunctionDeclaration(LinkName Name, IrType ReturnType, ImmutableArray<IrType> ParameterTypes, int? AttributeGroup = null)
{
    /// <summary><c>declare dllimport</c>: a routine imported from a DLL (CMP-430).</summary>
    public bool DllImport { get; init; }
}

/// <summary><c>@Name = [linkage] [unnamed_addr] global|constant Type Init, align N</c>.</summary>
internal sealed record IrGlobalDefinition(
    LinkName Name, IrLinkage Linkage, bool IsConstant, IrConstant Initializer, int Alignment, bool UnnamedAddr = false)
{
    /// <summary>CMP-422 / DBG-140: the node number of the global's <c>DIGlobalVariableExpression</c> (<c>, !dbg !N</c>); null → none.</summary>
    public int? DebugVariable { get; init; }
}

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

    /// <summary><c>module asm "line"</c>, in call order, written right after the module header (CMP-611).</summary>
    void AppendModuleAsm(string line);

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

    /// <summary><c>atomicrmw op ptr %address, T %value seq_cst, align N</c>: the previous value (CMP-412).</summary>
    IrLocal AtomicRmw(string name, IrAtomicOperator op, IrValue address, IrValue value, int alignment);

    /// <summary><c>cmpxchg ptr %address, T %comparand, T %newValue seq_cst seq_cst, align N</c>: the pair <c>{ T, i1 }</c> (CMP-412).</summary>
    IrLocal CmpXchg(string name, IrValue address, IrValue comparand, IrValue newValue, int alignment);

    /// <summary>
    /// Direct or indirect call. <paramref name="name"/> must be null for <c>void</c>; may be null for a discarded result
    /// (then null is returned). <paramref name="attributeGroup"/> is the call-site group (<c>#1</c> for noreturn helpers).
    /// </summary>
    /// <remarks>
    /// <paramref name="funclet"/>: the pad token of the funclet the call is in (<c>[ "funclet"(token %cl) ]</c>, CMP-392/393).
    /// <paramref name="mustTail"/>: <c>musttail call</c> (interface thunks, CMP-372); a <c>ret</c> of its result must follow.
    /// </remarks>
    IrLocal? Call(string? name, IrType returnType, IrValue callee, ImmutableArray<IrValue> arguments, int? attributeGroup = null, IrValue? funclet = null,
        bool mustTail = false);

    /// <summary>
    /// <c>invoke … to label %normal unwind label %unwind</c> (CMP-394): a terminator. A function with an invoke or a pad gets
    /// <c>personality ptr @__C_specific_handler</c>, which the writer declares (CMP-390).
    /// </summary>
    IrLocal? Invoke(string? name, IrType returnType, IrValue callee, ImmutableArray<IrValue> arguments, IrBlock normal, IrBlock unwind,
        int? attributeGroup = null, IrValue? funclet = null);

    /// <summary><c>%name = catchswitch within none [label %handler] unwind to caller|label %unwind</c> (CMP-391).</summary>
    IrLocal CatchSwitch(string name, IrBlock handler, IrBlock? unwind);

    /// <summary><c>%name = catchpad within %catchSwitch [args]</c>.</summary>
    IrLocal CatchPad(string name, IrValue catchSwitch, ImmutableArray<IrValue> arguments);

    /// <summary><c>catchret from %pad to label %target</c>.</summary>
    void CatchRet(IrValue pad, IrBlock target);

    /// <summary><c>%name = cleanuppad within none []</c> (CMP-391…393).</summary>
    IrLocal CleanupPad(string name);

    /// <summary><c>cleanupret from %pad unwind to caller|label %unwind</c>.</summary>
    void CleanupRet(IrValue pad, IrBlock? unwind);

    void Br(IrBlock target);

    void CondBr(IrValue condition, IrBlock whenTrue, IrBlock whenFalse);

    void Switch(IrValue value, IrBlock defaultBlock, ImmutableArray<(IrIntConstant Value, IrBlock Block)> cases);

    /// <summary><c>ret T v</c>, or <c>ret void</c> for null.</summary>
    void Ret(IrValue? value);

    void Unreachable();

    void EndFunction();

    /// <summary>
    /// CMP-421: every instruction written from now on carries <c>, !dbg !N</c> (node <paramref name="location"/>, a
    /// <c>DILocation</c>); null → none. <see cref="BeginFunction"/> resets it to null.
    /// </summary>
    void SetDebugLocation(int? location);

    /// <summary>CMP-422: the debug record <c>#dbg_declare(ptr %address, !variable, !DIExpression(expression), !location)</c> (DBG-135).</summary>
    void DebugDeclare(IrValue address, int variable, string expression, int location);

    /// <summary>CMP-420: a module-level metadata line (<c>!name = …</c> or <c>!N = …</c>), written last, in call order.</summary>
    void AppendMetadata(string line);

    void EndModule();
}
