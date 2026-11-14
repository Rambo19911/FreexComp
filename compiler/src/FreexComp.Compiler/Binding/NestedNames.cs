using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>Names of nested types and constants in expressions: <c>TOuter.TInner</c>, <c>TOuter.Limit</c> (LANG-770, C-122).</summary>
internal static class NestedNames
{
    /// <summary>The type an expression names: a type identifier (unit-qualified too) or a nested type; null otherwise.</summary>
    public static TypeSymbol? TypeNamedBy(Scope scope, ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        switch (scope.Unqualified(expression))
        {
            case IdentifierNameSyntax { Identifier: { IsMissing: false } token } name:
                return scope.For(name).Lookup(token.Value as string ?? token.Text) as TypeSymbol;
            case MemberAccessExpressionSyntax { Name.Identifier: { IsMissing: false } member } access
                when TypeNamedBy(scope, access.Expression) is { } outer:
                var text = member.Value as string ?? member.Text;
                return TypeMembers.NestedMembers(outer).FirstOrDefault(m => IdentifierComparer.Instance.Equals(m.Name, text)) as TypeSymbol;
            default:
                return null;
        }
    }

    /// <summary>The nested member <c>T.Name</c> of a class or record named by the qualifier; null when the qualifier names none.</summary>
    public static Symbol? NestedMember(Scope scope, MemberAccessExpressionSyntax access)
    {
        var member = access.Name.Identifier;
        var outer = TypeNamedBy(scope, access.Expression);
        if (member.IsMissing || outer is not (ClassTypeSymbol or RecordTypeSymbol))
        {
            return null;
        }

        var text = member.Value as string ?? member.Text;
        return TypeMembers.NestedMembers(outer).FirstOrDefault(m => IdentifierComparer.Instance.Equals(m.Name, text));
    }
}
