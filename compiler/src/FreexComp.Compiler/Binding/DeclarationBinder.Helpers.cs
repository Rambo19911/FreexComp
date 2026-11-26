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
    /// fields and virtual methods are E2750. <c>helper(TOther)</c> inherits another helper's members (LANG-753).
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

        if (extended.TypeKind == TypeKind.Error)
        {
            _scope.TryDeclare(name, _types.Error, out _);
            return;
        }

        var helper = new HelperTypeSymbol(name, isClassHelper, extended, _container, [DiagnosticBag.LocationOf(identifier)]);
        _scope.TryDeclare(name, helper, out _);
        declared.Add(helper);
        _types.HasHelpers = true;
        if (syntax.Heritage is { Types: [NamedTypeSyntax { Name: IdentifierNameSyntax { Identifier: { IsMissing: false } parentToken } parentName }] })
        {
            if (_scope.For(parentName).Lookup(NameOf(parentToken)) is HelperTypeSymbol parent)
            {
                helper.Parent = parent;
            }
            else
            {
                _diagnostics.Report(parentName, "E2900", "helper ancestor that is no helper");
            }
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
        var nestedAliases = ImmutableArray.CreateBuilder<(string Name, TypeSymbol Target)>();
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
    }

    /// <summary>
    /// A helper method heading (LANG-750/751): instance methods take <c>Self</c> — the object for a class helper, a <c>var</c>
    /// reference to the helped value for a record helper; class methods are static. Virtual methods are E2750; constructors,
    /// destructors and operators in helpers are E2900.
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
        var method = new MethodSymbol(NameOf(identifier), helper, [DiagnosticBag.LocationOf(identifier)], syntax,
            isClass ? MethodKind.ClassStatic : MethodKind.Ordinary, visibility);
        BindMethodDirectives(method, syntax.Directives, isClass);
        BindSignature(method, heading, identifier, report: true);
        if (!isClass)
        {
            // LANG-751: the helped object; a var reference to the helped value (ABI-084 record rule)
            method.SelfParameter = new ParameterSymbol("Self", helper.ExtendedType, helper.IsClassHelper ? ParameterMode.Value : ParameterMode.Var, -1, method, []);
        }

        return method;
    }

    /// <summary>The helpers a unit declares, in declaration order.</summary>
    internal static IEnumerable<HelperTypeSymbol> HelpersOf(UnitSymbol unit) => OwnTypesOf(unit).OfType<HelperTypeSymbol>();
}
