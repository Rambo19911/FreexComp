using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// One inline declaration statement of a statement list with the scope it opens — the rest of that list (inline-variables.md
/// LANG-560). The binder notes for every anonymous method the innermost one it is written in; once all bodies of the unit are
/// bound, <see cref="ClosureInfo.ResolveMixedCaptures"/> gives the lists that declare captured variables their block frames
/// (LANG-566, CMP-1183; C-308).
/// </summary>
internal sealed class InlineScope
{
    public InlineScope(InlineScope? parent, bool continuesList, ImmutableArray<VariableSymbol> locals)
    {
        Parent = parent;
        ContinuesList = continuesList;
        Locals = locals;
    }

    /// <summary>The innermost enclosing declaration: an earlier one of the same list, or the innermost of the enclosing lists.</summary>
    public InlineScope? Parent { get; }

    /// <summary>Whether <see cref="Parent"/> is a declaration of the same statement list.</summary>
    public bool ContinuesList { get; }

    public ImmutableArray<VariableSymbol> Locals { get; }

    public bool IsResolved { get; set; }

    /// <summary>The block frame of this statement list, once one of its declarations up to this one declared a captured variable.</summary>
    public ClosureInfo? ListFrame { get; set; }

    /// <summary>The innermost block frame code in this scope runs on (<see cref="ListFrame"/>, or an enclosing list's); null: the body's closure.</summary>
    public ClosureInfo? Frame { get; set; }

    /// <summary>The block frame this declaration creates when executed: it declares the first captured variable of its list.</summary>
    public ClosureInfo? Creates { get; set; }
}
