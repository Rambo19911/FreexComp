namespace FreexComp.Compiler.Symbols;

/// <summary>Which compiler-magic routine an <see cref="IntrinsicSymbol"/> is (intrinsics.md; grows with C-029).</summary>
internal enum IntrinsicKind { Break, Continue, Exit, Halt }

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

    /// <summary>Signature-help form, e.g. <c>Halt[(ExitCode: Integer)]</c>.</summary>
    public string Signature { get; }

    public override string ToDisplayString() => "procedure " + Signature;
}
