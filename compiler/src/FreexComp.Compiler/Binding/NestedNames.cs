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
    public static TypeSymbol? TypeNamedBy(Scope scope, ExpressionSyntax expression, GenericSegment? generic = null) =>
        Named(scope, expression, generic, access: null);

    /// <summary>
    /// <see cref="TypeNamedBy"/> with LANG-774 visibility for every segment of a qualified name <c>O.N.M</c> (LANG-780 item 2):
    /// E2610 at the first segment this code may not access, a nested type or a nested alias (C-359, C-360), naming the type that
    /// declares it (C-354), and the error type then (C-344; C-356, C-357: the arguments of type-taking intrinsics and typecast
    /// callees too). Generic segments report their own visibility.
    /// </summary>
    public static TypeSymbol? AccessibleTypeNamedBy(Scope scope, ExpressionSyntax expression, GenericSegment? generic, DiagnosticBag diagnostics, BuiltInTypes types) =>
        Named(scope, expression, generic, new Access(diagnostics, types));

    /// <summary>Where <see cref="AccessibleTypeNamedBy"/> reports an inaccessible segment, and the error type it returns then.</summary>
    private sealed record Access(DiagnosticBag Diagnostics, BuiltInTypes Types);

    private static TypeSymbol? Named(Scope scope, ExpressionSyntax expression, GenericSegment? generic, Access? access)
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
            case MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier: { IsMissing: false } member } name } qualified when generic is not null:
                if (UnitQualifiers.Qualifier(scope, qualified.Expression) is { } unit)
                {
                    return generic(name, unit.Qualified, null);
                }

                return Named(scope, qualified.Expression, generic, access) switch
                {
                    ErrorTypeSymbol error => error,
                    { } outer when TypeMembers.MemberNestedGenerics(outer, member.Value as string ?? member.Text).Any() => generic(name, null, outer),   // C-354: inherited too
                    _ => null,                                          // a generic method TUtil.Max<Integer>
                };
            case MemberAccessExpressionSyntax { Name: { Identifier: { IsMissing: false } member } nestedName } qualified
                when Named(scope, qualified.Expression, generic, access) is { } outer:
                if (outer is ErrorTypeSymbol)
                {
                    return outer;
                }

                var text = member.Value as string ?? member.Text;
                if (access is not null && InaccessibleNestedType(scope, outer, text) is { } denied)
                {
                    access.Diagnostics.Report(nestedName, "E2610", denied.Visibility, denied.Name, TypeMembers.DisplayName(denied.Declaring));   // LANG-774
                    return access.Types.Error;
                }

                return TypeMembers.MemberNestedType(outer, text);                // C-354
            default:
                return null;
        }
    }

    /// <summary>
    /// LANG-774 (C-344, C-359): the visibility word, declared name and declaring type of the nested type or alias <paramref name="name"/>
    /// in the type member scope of <paramref name="outer"/> when code at <paramref name="scope"/> may not access it; null when it may or
    /// there is none.
    /// </summary>
    public static (string Visibility, string Name, TypeSymbol Declaring)? InaccessibleNestedType(Scope scope, TypeSymbol outer, string name) =>
        TypeMembers.MemberNestedTypeEntry(outer, name) is var (_, declared, declaring, visibility) && Inaccessible(scope, visibility, declaring) is { } word
            ? (word, declared, declaring)
            : null;

    /// <summary>
    /// LANG-774 (C-344): the visibility word of a nested type named from code at <paramref name="scope"/> that may not access it
    /// (<c>private</c>: other units; <c>strict private</c> / <c>strict protected</c>: code outside its enclosing type), else null.
    /// </summary>
    public static string? InaccessibleNestedType(Scope scope, TypeSymbol nested) =>
        TypeMembers.EnclosingTypes(nested).FirstOrDefault() is { } outer ? Inaccessible(scope, nested.NestedVisibility, outer) : null;

    /// <summary>
    /// LANG-774 (C-361): E2610 at <paramref name="at"/> when <paramref name="member"/> is a constant nested in a class, record or helper
    /// whose section code at <paramref name="scope"/> may not access (naming the type that declares it); true when reported.
    /// </summary>
    public static bool ReportInaccessibleConstant(Scope scope, SyntaxNode at, Symbol member, DiagnosticBag diagnostics)
    {
        if (member is not ConstantSymbol { ContainingSymbol: TypeSymbol owner } || Inaccessible(scope, member.Visibility, owner) is not { } word)
        {
            return false;
        }

        diagnostics.Report(at, "E2610", word, member.Name, TypeMembers.DisplayName(owner));
        return true;
    }

    /// <summary>LANG-774: the visibility word of a member of <paramref name="outer"/> declared in a <paramref name="visibility"/> section when code at <paramref name="scope"/> may not access it, else null.</summary>
    private static string? Inaccessible(Scope scope, Visibility visibility, TypeSymbol outer)
    {
        if (visibility is Visibility.Public or Visibility.Published)
        {
            return null;
        }

        UnitSymbol? unit = null;
        var selves = new List<TypeSymbol>();
        var inMethod = false;
        for (var s = scope; s is not null; s = s.Parent)
        {
            switch (s.Owner)
            {
                case UnitSymbol u when s.Kind == ScopeKind.Unit:
                    unit ??= u;
                    break;
                case TypeSymbol type when s.Kind == ScopeKind.TypeMember && !inMethod:
                    selves.Add(type);
                    break;
                case MethodSymbol method when !inMethod:
                    // C-361: a method body's member scopes include its type's ancestors (LANG-621); the code is its own type's only
                    selves.Add(method.Owner);
                    inMethod = true;
                    break;
            }
        }

        selves = [.. selves.SelectMany(t => new[] { t }.Concat(TypeMembers.EnclosingTypes(t)))];
        var sameUnit = TypeMembers.Outermost(outer).ContainingSymbol is UnitSymbol own && ReferenceEquals(own, unit);
        var allowed = visibility switch
        {
            Visibility.Private => sameUnit,
            Visibility.Protected => sameUnit || selves.Any(t => TypeMembers.SelfAndAncestors(t).Contains(outer)),
            Visibility.StrictPrivate => selves.Contains(outer),
            _ => selves.Any(t => TypeMembers.SelfAndAncestors(t).Contains(outer)),
        };
        return allowed ? null : visibility switch
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
        return TypeMembers.MemberNestedMember(outer, text);                               // C-354: inherited too
    }
}
