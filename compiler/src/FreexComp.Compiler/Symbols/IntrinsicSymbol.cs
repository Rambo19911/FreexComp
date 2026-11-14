namespace FreexComp.Compiler.Symbols;

/// <summary>Which compiler-magic routine an <see cref="IntrinsicSymbol"/> is (intrinsics.md; grows with C-029).</summary>
internal enum IntrinsicKind
{
    // control (LANG-363, 364)
    Break, Continue, Exit, Halt,

    // M2.1 procedures
    Inc, Dec, RunError, Write, Writeln,

    // M2.2 procedures
    Include, Exclude,

    // M2.2 memory procedures (LANG-268, intrinsics.md)
    New, Dispose, GetMem, FreeMem, ReallocMem, FillChar, Move,

    // M2.2 checks (intrinsics.md, C-057)
    Assert,

    // M2.2 string procedures (intrinsics.md, C-062)
    SetLength, Delete, Insert, Str, Val,

    // M2.2 text input (intrinsics.md §3, C-066)
    Read, Readln,

    // M2.1 functions (constant-foldable, LANG-340)
    Succ, Pred, Ord, Chr, Odd, Abs, Sqr, Low, High, SizeOf, Lo, Hi, Swap,

    // M2.2 functions (Length: constant for static arrays and string constants)
    Length,

    // M2.2 pointer functions (intrinsics.md)
    Assigned, Addr, Ptr,

    // M2.2 real functions (intrinsics.md, C-059; Trunc/Round are constant-foldable, LANG-340)
    Trunc, Round, Int, Frac, Sqrt,

    // M2.2 atomic functions (intrinsics.md, ABI-172, C-168; also usable as statements)
    AtomicIncrement, AtomicDecrement, AtomicExchange, AtomicCmpExchange,

    // M2.2 string functions (intrinsics.md, C-062; not constant, LANG-340)
    Copy, Pos, Concat, UpCase,

    // M2.2 text-file functions (intrinsics.md, LANG-946, C-066; never constant)
    Eof, Eoln,

    // M2.5 open arrays (LANG-431, C-106; only as an open-array argument)
    Slice,
}

/// <summary>A compiler-magic routine predeclared in the outermost scope (LANG-900); user symbols may hide it.</summary>
public sealed class IntrinsicSymbol : Symbol
{
    internal IntrinsicSymbol(string name, IntrinsicKind intrinsic, string signature)
        : base(SymbolKind.Intrinsic, name, containingSymbol: null, locations: [])
    {
        Intrinsic = intrinsic;
        Signature = signature;
    }

    internal IntrinsicKind Intrinsic { get; }

    /// <summary>Has a value (a function-like intrinsic); the others are statements.</summary>
    internal bool IsFunction => Intrinsic >= IntrinsicKind.Succ;

    /// <summary>One of the atomic intrinsics (ABI-172): a function whose result may be discarded.</summary>
    internal bool IsAtomic => Intrinsic is >= IntrinsicKind.AtomicIncrement and <= IntrinsicKind.AtomicCmpExchange;

    /// <summary>A string function (<c>Copy</c>, <c>Pos</c>, <c>Concat</c>, <c>UpCase</c>; C-062).</summary>
    internal bool IsStringFunction => Intrinsic is >= IntrinsicKind.Copy and <= IntrinsicKind.UpCase;

    /// <summary>A function-like intrinsic that is never a constant expression (not in the LANG-340 list).</summary>
    internal bool IsNeverConstant => IsAtomic || IsStringFunction || Intrinsic is IntrinsicKind.Eof or IntrinsicKind.Eoln or IntrinsicKind.Slice;

    /// <summary>Signature-help form, e.g. <c>Halt[(ExitCode: Integer)]</c>.</summary>
    public string Signature { get; }

    public override string ToDisplayString() => (IsFunction ? "function " : "procedure ") + Signature;
}
