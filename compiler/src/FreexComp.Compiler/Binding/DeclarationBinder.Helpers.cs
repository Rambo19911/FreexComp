using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Class and record helpers (LANG-750…754, C-124).
internal sealed partial class DeclarationBinder
{
    /// <summary>
    /// <c>T = class helper for TFoo …</c> / <c>record helper for X …</c> at unit level: methods (instance methods take <c>Self</c>, a
    /// <c>var</c> reference for a record helper, LANG-751), static class methods, properties, constants and class vars; instance
    /// fields and virtual methods are E2750. <c>helper(TOther)</c> inherits another helper's members (LANG-990 (e)).
    /// </summary>
    private void BindHelperDeclaration(SyntaxToken identifier, HelperTypeSyntax syntax, ImmutableArray<Symbol>.Builder declared)
    {
        var name = NameOf(identifier);
        var previous = _sharedScope?.LookupLocal(name) ?? _scope.LookupLocal(name);
        if (previous is not null)
        {
            _diagnostics.ReportRedeclared(identifier, name, previous);
            return;
        }

        if (!IsGlobalLevel)
        {
            Unsupported(syntax.Keyword, "local helper type");
            _scope.TryDeclare(name, _types.Error, out _);
            return;
        }

        var extended = BindType(syntax.ExtendedType);
        var isClassHelper = syntax.Keyword.Kind == SyntaxKind.ClassKeyword;
        if (extended.TypeKind != TypeKind.Error && isClassHelper != extended is ClassTypeSymbol)
        {
            if (isClassHelper)
            {
                _diagnostics.Report(syntax.ExtendedType, "E2603");
            }
            else
            {
                _diagnostics.Report(syntax.ExtendedType, "E2900", "record helper for a class");
            }
            extended = _types.Error;
        }
        else if (extended is InterfaceTypeSymbol { IsMethodReference: false })
        {
            _diagnostics.Report(syntax.ExtendedType, "E2900", "helper for an interface type");     // LANG-990 (b) (C-439), dispatch interfaces too
            extended = _types.Error;
        }

        if (extended.TypeKind == TypeKind.Error)
        {
            _scope.TryDeclare(name, _types.Error, out _);
            return;
        }

        var helper = new HelperTypeSymbol(name, isClassHelper, extended, _container, [DiagnosticBag.LocationOf(identifier)]);
        _scope.TryDeclare(name, helper, out _);
        declared.Add(helper);
        _types.HasHelpers = true;
        if (syntax.Heritage is { Types: [NamedTypeSyntax { Name: { IsMissing: false } ancestor }] })
        {
            helper.Parent = BindHelperAncestor(helper, ancestor);
        }
        else if (syntax.Heritage is { } heritage)
        {
            _diagnostics.Report(heritage, "E2900", "helper ancestor list");
        }

        var memberScope = new Scope(ScopeKind.TypeMember, _scope, helper);
        memberScope.TryDeclare(name, helper, out _);
        var inner = new DeclarationBinder(_types, memberScope, helper, _diagnostics);
        var names = new Dictionary<string, Symbol>(IdentifierComparer.Instance);
        var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
        var properties = new List<(PropertyDeclarationSyntax Syntax, Visibility Visibility)>();
        var classVariables = ImmutableArray.CreateBuilder<VariableSymbol>();
        var nested = ImmutableArray.CreateBuilder<Symbol>();
        var nestedAliases = ImmutableArray.CreateBuilder<(string Name, TypeSymbol Target, Visibility Visibility)>();
        foreach (var (member, visibility) in Members(syntax.Members, Visibility.Public))
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    _diagnostics.Report(field, "E2750");                                            // LANG-750
                    break;
                case ClassVarSectionSyntax { ClassKeyword: null } section:
                    _diagnostics.Report(section, "E2750");
                    break;
                case ClassVarSectionSyntax section:
                    inner.BindClassVariables(helper, section, visibility, classVariables, names);
                    break;
                case MethodDeclarationSyntax classConstructor when IsClassConstructorHeading(classConstructor.Heading):
                    inner.BindClassConstructorDeclaration(helper, classConstructor, visibility);  // LANG-995 (C-441)
                    break;
                case MethodDeclarationSyntax method when inner.BindHelperMethodDeclaration(helper, method, visibility) is { } symbol:
                    if (names.TryGetValue(symbol.Name, out var existing)
                        && !(existing is MethodSymbol other && other.IsOverload && symbol.IsOverload && !SameParameterTypes(other, symbol)))
                    {
                        _diagnostics.ReportRedeclared(((IdentifierNameSyntax)method.Heading.Name).Identifier, symbol.Name, existing);
                        break;
                    }

                    names.TryAdd(symbol.Name, symbol);
                    methods.Add(symbol);
                    break;
                case MethodDeclarationSyntax:
                    break;
                case PropertyDeclarationSyntax { DefaultKeyword: { } defaultKeyword }:
                    Unsupported(defaultKeyword, "default property in a helper");                 // LANG-990 (c) (C-439)
                    break;
                case PropertyDeclarationSyntax property:
                    properties.Add((property, visibility));
                    break;
                case ConstSectionSyntax or TypeSectionSyntax:
                    nested.AddRange(inner.BindNestedSection(member, nestedAliases, visibility));
                    break;
                default:
                    Unsupported(member, "helper " + FeatureName(member.Kind));
                    break;
            }
        }

        helper.ClassVariables = classVariables.ToImmutable();
        helper.Methods = methods.ToImmutable();
        helper.NestedMembers = nested.ToImmutable();
        helper.NestedAliases = nestedAliases.ToImmutable();
        helper.Properties = inner.BindProperties(helper, properties, names);
        ReportHiddenMembers(syntax, helper);
    }

    /// <summary>
    /// LANG-994 (C-440): H2012 at the name of each member of <paramref name="helper"/> that hides a member of the helped type — the first
    /// member of its name that LANG-992 steps 1…2 find on the helped type here without the helper's chain, among those the helper's
    /// code may use — unless the member is a method with <c>overload</c> whose parameter types differ from those of every method of
    /// the name at that place (it only adds an overload). An element of a helped enumeration is not hidden (it is found first).
    /// </summary>
    private void ReportHiddenMembers(HelperTypeSyntax syntax, HelperTypeSymbol helper)
    {
        var tokens = syntax.DescendantTokens().ToList();
        foreach (var member in helper.Members)
        {
            var index = member.Locations is [var at, ..] ? tokens.FindIndex(t => t.Span.Start == at.Span.Start) : -1;
            if (index < 0 || HiddenMember(helper, member) is not { ContainingSymbol: TypeSymbol owner } hidden)
            {
                continue;
            }

            _diagnostics.Report(tokens[index], "H2012", member.Name, TypeMembers.DisplayName(owner) + "." + hidden.Name);
        }
    }

    /// <summary>The member <paramref name="member"/> of <paramref name="helper"/> hides (LANG-994); null when it hides none.</summary>
    private Symbol? HiddenMember(HelperTypeSymbol helper, Symbol member)
    {
        foreach (var place in ExpressionBinder.LookupPlaces(_scope, helper.ExtendedType, helper))
        {
            // an element of a helped enumeration is not hidden: it comes before the helper's members (LANG-992 (b))
            var found = place.Members.Where(m => m is not EnumMemberSymbol && IdentifierComparer.Instance.Equals(m.Name, member.Name)
                && UsableByHelper(helper, m, place)).ToList();
            if (found.Count == 0)
            {
                continue;
            }

            return member is MethodSymbol { IsOverload: true } method && found[0] is MethodSymbol
                ? found.OfType<MethodSymbol>().FirstOrDefault(m => SameParameterTypes(m, method))
                : found[0];
        }

        return null;
    }

    /// <summary>
    /// LANG-991 (c): the code of <paramref name="helper"/> may use <paramref name="member"/> of <paramref name="owner"/> (the helped type,
    /// an ancestor of it, or a helper outside the helper's chain) — <c>private</c> in the same unit; (strict) <c>protected</c> of the
    /// helped type and its ancestors, of which the helper counts as a descendant; <c>protected</c> of another helper in the same unit.
    /// </summary>
    private static bool UsableByHelper(HelperTypeSymbol helper, Symbol member, TypeSymbol owner)
    {
        var sameUnit = ReferenceEquals(TypeMembers.Outermost(owner).ContainingSymbol, TypeMembers.Outermost(helper).ContainingSymbol);
        return member.Visibility switch
        {
            Visibility.Public or Visibility.Published => true,
            Visibility.Private => sameUnit,
            Visibility.StrictPrivate => false,
            Visibility.Protected => sameUnit || owner is not HelperTypeSymbol,
            _ => owner is not HelperTypeSymbol,
        };
    }

    /// <summary>
    /// LANG-990 (e) (C-438): the helper <c>helper(A)</c> names as its ancestor — a class helper for the helped class of
    /// <paramref name="helper"/> or for an ancestor class of it, so that an inherited method gets a <c>Self</c> of its own helped
    /// class. Everything else that <c>A</c> names (a record helper, a helper for another class, the helper itself, a symbol that is
    /// no helper), and every ancestor of a record helper, is E2753 at <c>A</c>; a name that is not declared reports itself (E2003).
    /// Null after an error: the helper is declared without an ancestor.
    /// </summary>
    private HelperTypeSymbol? BindHelperAncestor(HelperTypeSymbol helper, NameSyntax ancestor)
    {
        var last = ancestor is QualifiedNameSyntax { Right: var right } qualified && UnitQualifiers.Qualifier(_scope, qualified.Left) is not null ? right : ancestor;
        var named = last is IdentifierNameSyntax { Identifier: { IsMissing: false } identifier } name ? _scope.For(name).Lookup(NameOf(identifier)) : null;
        if (named is HelperTypeSymbol { IsClassHelper: true } parent && helper.IsClassHelper && !ReferenceEquals(parent, helper)
            && helper.ExtendedType.InheritsFrom(parent.ExtendedType))
        {
            return parent;
        }

        // a name that names nothing reports itself (E2003, E2703…); whatever else is written there cannot be the ancestor
        if (named is not null || ResolveTypeName(ancestor).TypeKind != TypeKind.Error)
        {
            _diagnostics.Report(ancestor, "E2753", helper.Name, string.Concat(ancestor.DescendantTokens().Select(t => t.Text)));
        }

        return null;
    }

    /// <summary>
    /// A helper method heading (LANG-750/751): instance methods take <c>Self</c> — the object for a class helper, a <c>var</c>
    /// reference to the helped value for a record helper; class methods of a class helper without <c>static</c> take the class
    /// reference (LANG-991 (a), C-439), the others are static. Virtual methods are E2750; constructors, destructors and operators
    /// in helpers are E2900.
    /// </summary>
    private MethodSymbol? BindHelperMethodDeclaration(HelperTypeSymbol helper, MethodDeclarationSyntax syntax, Visibility visibility)
    {
        var heading = syntax.Heading;
        if (heading.Name is not IdentifierNameSyntax { Identifier: { IsMissing: false } identifier })
        {
            return null;
        }

        if (heading.Keyword.Kind is not (SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword) || heading.TypeParameterList is not null)
        {
            _diagnostics.Report(heading, "E2900", heading.TypeParameterList is not null ? "generic method" : "helper " + heading.Keyword.Text.ToLowerInvariant());
            return null;
        }

        if (syntax.Directives.FirstOrDefault(d => d.Keyword.ContextualKind is SyntaxKind.VirtualKeyword or SyntaxKind.DynamicKeyword
                or SyntaxKind.OverrideKeyword or SyntaxKind.AbstractKeyword or SyntaxKind.MessageKeyword) is { } directive)
        {
            _diagnostics.Report(directive.Keyword, "E2750");                                                // LANG-750
            return null;
        }

        var isClass = heading.ClassKeyword is not null;
        var classSelf = isClass && helper.IsClassHelper && !syntax.Directives.Any(d => d.Keyword.ContextualKind == SyntaxKind.StaticKeyword);
        var method = new MethodSymbol(NameOf(identifier), helper, [DiagnosticBag.LocationOf(identifier)], syntax,
            classSelf ? MethodKind.ClassMethod : isClass ? MethodKind.ClassStatic : MethodKind.Ordinary, visibility);
        BindMethodDirectives(method, syntax.Directives, isClass);
        BindSignature(method, heading, identifier, report: true);
        if (classSelf)
        {
            // LANG-991 (a) (C-439): the class the method is called on, typed class of the helped class (ABI-084 as for LANG-620)
            method.SelfParameter = new ParameterSymbol("Self", ((ClassTypeSymbol)helper.ExtendedType).Reference, ParameterMode.Value, -1, method, []);
        }
        else if (!isClass)
        {
            // LANG-751: the helped object; a var reference to the helped value (ABI-084 record rule)
            method.SelfParameter = new ParameterSymbol("Self", helper.ExtendedType, helper.IsClassHelper ? ParameterMode.Value : ParameterMode.Var, -1, method, []);
        }

        return method;
    }

    /// <summary>The helpers a unit declares, in declaration order.</summary>
    internal static IEnumerable<HelperTypeSymbol> HelpersOf(UnitSymbol unit) => OwnTypesOf(unit).OfType<HelperTypeSymbol>();
}
