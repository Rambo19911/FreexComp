using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// LANG-311, 538: unit-qualified names <c>U.Name</c> in designators (<c>U.X</c>, <c>U.F(1)</c>, <c>U.TEnum.A</c>) and type
/// names (<c>U.TRec</c>). A designator <c>a.b.….n</c> is unit-qualified when <c>a.b.…</c> (identifiers only) equals a qualifier
/// of a used unit's scope in the chain — the name as written in the uses clause or the canonical name — unless <c>a</c> is
/// declared in a scope nearer than the used units' scopes (the unit's own scopes, routines, blocks: LANG-311 "the local
/// symbol wins"). Binding proceeds from the outermost designator inwards, so the longest qualifying prefix wins.
/// The member is then looked up in that unit's interface only (<see cref="Scope.Qualified"/>).
/// </summary>
internal static class UnitQualifiers
{
    /// <summary>The scope that binds <paramref name="name"/>: the qualifying unit's interface when it is the member of a unit-qualified name.</summary>
    public static Scope For(this Scope scope, IdentifierNameSyntax name) => name.Parent switch
    {
        MemberAccessExpressionSyntax access when ReferenceEquals(access.Name, name) && Qualifier(scope, access.Expression) is { } unit => unit.Qualified,
        QualifiedNameSyntax qualified when ReferenceEquals(qualified.Right, name) && Qualifier(scope, qualified.Left) is { } unit => unit.Qualified,
        _ => scope,
    };

    /// <summary>
    /// <paramref name="syntax"/> without its unit qualifier: the member name of a unit-qualified <c>U.Name</c> (to be looked up
    /// with <see cref="For"/>), else <paramref name="syntax"/> itself.
    /// </summary>
    public static ExpressionSyntax Unqualified(this Scope scope, ExpressionSyntax syntax) =>
        syntax is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax member } access && Qualifier(scope, access.Expression) is not null
            ? member
            : syntax;

    /// <summary>The used unit's scope (<see cref="ScopeKind.Uses"/>) that <paramref name="prefix"/> names, or null.</summary>
    public static Scope? Qualifier(Scope scope, SyntaxNode prefix)
    {
        var parts = new List<SyntaxToken>();
        if (!Flatten(prefix, parts))
        {
            return null;
        }

        var first = parts[0].Value as string ?? parts[0].Text;
        if (scope.DeclaringScope(first) is { Kind: not (ScopeKind.Uses or ScopeKind.BuiltIn) })
        {
            return null;                                                // LANG-311: a nearer declaration hides the unit
        }

        var name = string.Join(".", parts.Select(p => p.Value as string ?? p.Text));
        for (var s = scope; s != null; s = s.Parent)
        {
            if (s.Kind == ScopeKind.Uses && s.Qualifiers.Any(q => IdentifierComparer.Instance.Equals(q, name)))
            {
                return s;
            }
        }

        return null;
    }

    private static bool Flatten(SyntaxNode node, List<SyntaxToken> parts)
    {
        switch (node)
        {
            case IdentifierNameSyntax { Identifier: { IsMissing: false } identifier }:
                parts.Add(identifier);
                return true;
            case MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } access:
                return Flatten(access.Expression, parts) && Flatten(name, parts);
            case QualifiedNameSyntax { Right: IdentifierNameSyntax right } qualified:
                return Flatten(qualified.Left, parts) && Flatten(right, parts);
            default:
                return false;
        }
    }
}
