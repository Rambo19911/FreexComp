using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>Names of nested types and constants in expressions: <c>TOuter.TInner</c>, <c>TOuter.Limit</c> (LANG-770, C-122).</summary>
internal static class NestedNames
{
    /// <summary>
    /// Binds a generic segment <c>G&lt;…&gt;</c> of a qualified name: looked up in the scope (a unit qualifier's, or the current one when
    /// null) or among the nested types of the outer type; reports its own errors and returns the error type then.
    /// </summary>
    public delegate TypeSymbol GenericSegment(GenericNameSyntax name, Scope? scope, TypeSymbol? outer);

    /// <summary>
    /// The type an expression names: a type identifier (unit-qualified too) or a nested type; null otherwise. With
    /// <paramref name="generic"/> also names with generic segments (LANG-780, C-345: <c>TList&lt;Integer&gt;.TNode</c>,
    /// <c>U.TBox&lt;Integer&gt;</c>, <c>TOuter.TBox&lt;Integer&gt;</c>); the error type when such a segment was reported.
    /// </summary>
    public static TypeSymbol? TypeNamedBy(Scope scope, ExpressionSyntax expression, GenericSegment? generic = null)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        switch (scope.Unqualified(expression))
        {
            case IdentifierNameSyntax { Identifier: { IsMissing: false } token } name:
                return scope.For(name).Lookup(token.Value as string ?? token.Text) as TypeSymbol;
            case GenericNameSyntax name when generic is not null:
                return generic(name, null, null);
            case MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier: { IsMissing: false } member } name } access when generic is not null:
                if (UnitQualifiers.Qualifier(scope, access.Expression) is { } unit)
                {
                    return generic(name, unit.Qualified, null);
                }

                return TypeNamedBy(scope, access.Expression, generic) switch
                {
                    ErrorTypeSymbol error => error,
                    { } outer when TypeMembers.NestedGenerics(outer, member.Value as string ?? member.Text).Any() => generic(name, null, outer),
                    _ => null,                                          // a generic method TUtil.Max<Integer>
                };
            case MemberAccessExpressionSyntax { Name.Identifier: { IsMissing: false } member } access
                when TypeNamedBy(scope, access.Expression, generic) is { } outer:
                return outer is ErrorTypeSymbol ? outer : TypeMembers.NestedType(outer, member.Value as string ?? member.Text);
            default:
                return null;
        }
    }

    /// <summary>
    /// LANG-774 (C-344): the visibility word of a nested type named from code at <paramref name="scope"/> that may not access it
    /// (<c>private</c>: other units; <c>strict private</c> / <c>strict protected</c>: code outside its enclosing type), else null.
    /// </summary>
    public static string? InaccessibleNestedType(Scope scope, TypeSymbol nested)
    {
        if (nested.NestedVisibility is Visibility.Public or Visibility.Published || TypeMembers.EnclosingTypes(nested).FirstOrDefault() is not { } outer)
        {
            return null;
        }

        UnitSymbol? unit = null;
        var selves = new List<TypeSymbol>();
        for (var s = scope; s is not null; s = s.Parent)
        {
            switch (s.Owner)
            {
                case UnitSymbol u when s.Kind == ScopeKind.Unit:
                    unit ??= u;
                    break;
                case TypeSymbol type when s.Kind == ScopeKind.TypeMember:
                    selves.Add(type);
                    break;
                case MethodSymbol method:
                    selves.Add(method.Owner);
                    break;
            }
        }

        selves = [.. selves.SelectMany(t => new[] { t }.Concat(TypeMembers.EnclosingTypes(t)))];
        var sameUnit = TypeMembers.Outermost(outer).ContainingSymbol is UnitSymbol own && ReferenceEquals(own, unit);
        var allowed = nested.NestedVisibility switch
        {
            Visibility.Private => sameUnit,
            Visibility.Protected => sameUnit || selves.Any(t => TypeMembers.SelfAndAncestors(t).Contains(outer)),
            Visibility.StrictPrivate => selves.Contains(outer),
            _ => selves.Any(t => TypeMembers.SelfAndAncestors(t).Contains(outer)),
        };
        return allowed ? null : nested.NestedVisibility switch
        {
            Visibility.Private => "private",
            Visibility.StrictPrivate => "strict private",
            Visibility.Protected => "protected",
            _ => "strict protected",
        };
    }

    /// <summary>The nested member <c>T.Name</c> of a class or record named by the qualifier; null when the qualifier names none.</summary>
    public static Symbol? NestedMember(Scope scope, MemberAccessExpressionSyntax access, GenericSegment? generic = null)
    {
        var member = access.Name.Identifier;
        var outer = TypeNamedBy(scope, access.Expression, generic);
        if (member.IsMissing || outer is not (ClassTypeSymbol or RecordTypeSymbol))
        {
            return null;
        }

        var text = member.Value as string ?? member.Text;
        return TypeMembers.NestedMembers(outer).FirstOrDefault(m => IdentifierComparer.Instance.Equals(m.Name, text));
    }
}
