namespace FreexComp.Compiler.Symbols;

/// <summary>Which compiler-magic routine an <see cref="IntrinsicSymbol"/> is (intrinsics.md; grows with C-029).</summary>
internal enum IntrinsicKind
{
    // control (LANG-363, 364)
    Break, Continue, Exit, Halt,

    // M2.1 procedures
    Inc, Dec, RunError, Write, Writeln,

    // M2.1 functions (constant-foldable, LANG-340)
    Succ, Pred, Ord, Chr, Odd, Abs, Sqr, Low, High, SizeOf, Lo, Hi, Swap,
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

    /// <summary>Signature-help form, e.g. <c>Halt[(ExitCode: Integer)]</c>.</summary>
    public string Signature { get; }

    public override string ToDisplayString() => (IsFunction ? "function " : "procedure ") + Signature;
}
