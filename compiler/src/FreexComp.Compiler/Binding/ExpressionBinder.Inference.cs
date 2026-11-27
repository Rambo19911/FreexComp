using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// The initialisers of var X := e whose type cannot be inferred (inline-variables.md LANG-574; C-384).
internal sealed partial class ExpressionBinder
{
    /// <summary>LANG-574 rule 2: <paramref name="syntax"/> is an anonymous-method expression (parentheses allowed).</summary>
    internal static bool IsAnonymousMethod(ExpressionSyntax syntax) => Unparenthesized(syntax) is AnonymousMethodExpressionSyntax;

    /// <summary>LANG-573 rule 1: <paramref name="syntax"/> is an integer literal (parentheses allowed), which takes the literal rule.</summary>
    internal static bool IsIntegerLiteral(ExpressionSyntax syntax) =>
        Unparenthesized(syntax) is LiteralExpressionSyntax { Token.Kind: SyntaxKind.IntegerLiteralToken };

    /// <summary>
    /// LANG-574 rule 3: <paramref name="syntax"/> names routines without calling them — <c>R</c>, <c>U.R</c>, <c>TClass.M</c>,
    /// <c>Obj.M</c>, <c>Rec.M</c>, <c>Intf.M</c> — none of which is a function (or constructor) callable without arguments
    /// (LANG-482: such a name is a call). Inside a <c>with</c> statement a bare name is left to the normal binding.
    /// </summary>
    internal bool IsUncalledRoutine(ExpressionSyntax syntax)
    {
        var inner = _scope.Unqualified(Unparenthesized(syntax));
        var candidates = inner switch
        {
            IdentifierNameSyntax { Identifier: { IsMissing: false } token } name when _withs.Count == 0
                => _scope.For(name).LookupCandidates(TextOf(token)),
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier: { IsMissing: false } member } } access
                => MemberRoutines(access.Expression, TextOf(member)),
            _ => [],
        };
        return !candidates.IsEmpty && !candidates.Any(c => (c.ReturnType is not null || c is MethodSymbol { MethodKind: MethodKind.Constructor })
            && c.Parameters.All(p => p.DefaultValue is not null));
    }

    /// <summary>
    /// The methods named <paramref name="name"/> of the class named by <paramref name="qualifier"/>, or of the class, record or
    /// interface type of the variable it designates (a chain of names only: nothing with side effects is bound).
    /// </summary>
    private ImmutableArray<RoutineSymbol> MemberRoutines(ExpressionSyntax qualifier, string name)
    {
        if (!IsNameChain(qualifier))
        {
            return [];
        }

        if (_scope.Unqualified(qualifier) is IdentifierNameSyntax { Identifier: { IsMissing: false } head } headName
            && _scope.For(headName).Lookup(TextOf(head)) is TypeSymbol named)
        {
            return named is ClassTypeSymbol @class ? MemberCandidates(@class, name) : [];
        }

        return ProbeBinder(new DiagnosticBag()).BindExpression(qualifier) switch
        {
            { HasErrors: true } => [],
            { Type: ClassTypeSymbol @class } => MemberCandidates(@class, name),
            { Type: InterfaceTypeSymbol intf } => InterfaceCandidates(intf, name),
            { Type: RecordTypeSymbol record } => [.. record.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name))],
            _ => [],
        };
    }

    private static bool IsNameChain(ExpressionSyntax syntax) => syntax switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax { Name: IdentifierNameSyntax } access => IsNameChain(access.Expression),
        _ => false,
    };

    private static string TextOf(SyntaxToken token) => token.Value as string ?? token.Text;
}
