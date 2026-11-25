using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// LANG-472 (C-232): the checks shared by the binders for a name that resolves to a symbol declared <c>deprecated</c>,
/// <c>platform</c> or <c>experimental</c> — W1050 / W1051 / W1052 for a use outside the declaring unit.
/// </summary>
internal static class HintChecks
{
    /// <summary>
    /// <paramref name="name"/> (spelled <paramref name="text"/>) was looked up from <paramref name="scope"/> and binds
    /// <paramref name="symbol"/>: a type is checked by its declared name (an alias carries its own hints, LANG-205), a variable or
    /// constant by its symbol. Routines are checked where they are called or referenced (their overload is known only there).
    /// </summary>
    public static void CheckName(DiagnosticBag diagnostics, BuiltInTypes types, Scope scope, IdentifierNameSyntax name, string text, Symbol symbol)
    {
        switch (symbol)
        {
            case TypeSymbol when types.HasTypeHints:
                if (scope.For(name).ImportedTypeHints(text) is { } hints)
                {
                    diagnostics.ReportHints(name, IdentifierComparer.Instance.Equals(symbol.Name, text) ? symbol.Name : text, hints);
                }

                break;
            case VariableSymbol or ConstantSymbol when symbol.Hints is { } own && !ReferenceEquals(symbol.DeclaringUnit, UnitOf(scope)):
                diagnostics.ReportHints(name, symbol.Name, own);
                break;
        }
    }

    /// <summary>The unit whose code <paramref name="scope"/> belongs to (the owner of the nearest unit scope).</summary>
    private static UnitSymbol? UnitOf(Scope scope)
    {
        for (var current = scope; current != null; current = current.Parent)
        {
            if (current is { Kind: ScopeKind.Unit, Owner: UnitSymbol unit })
            {
                return unit;
            }
        }

        return null;
    }

    /// <summary>The name a call or reference <paramref name="node"/> ends in: <c>M</c> of <c>O.M(1)</c>, <c>R</c> of <c>U.R</c>; else the node.</summary>
    public static SyntaxNode NameIn(SyntaxNode node) => node switch
    {
        InvocationExpressionSyntax invocation => NameIn(invocation.Expression),
        MemberAccessExpressionSyntax access => access.Name,
        QualifiedNameSyntax qualified => qualified.Right,
        ParenthesizedExpressionSyntax parenthesized => NameIn(parenthesized.Expression),
        AddressOfExpressionSyntax addressOf => NameIn(addressOf.Operand),
        _ => node,
    };
}
