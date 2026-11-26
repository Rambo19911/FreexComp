using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// `implements` delegation (com-interfaces.md §5: LANG-690…692, CMP-1207; C-302).
internal sealed partial class DeclarationBinder
{
    /// <summary>One checked item of an <c>implements</c> list: <see cref="Property"/> implements <see cref="Interface"/>.</summary>
    private sealed record Delegation(InterfaceTypeSymbol Interface, PropertySymbol Property);

    /// <summary>
    /// LANG-692: the checked <c>implements</c> items of a class, in declaration order. Each listed type is an interface (E2652)
    /// that the class lists itself or an ancestor of one (E2662), delegated by one property only (E2664); the property is a
    /// readable instance property without index parameters whose type is a class, or the interface or a descendant of it (E2663).
    /// </summary>
    private List<Delegation> BindDelegations(ClassTypeSymbol type, IReadOnlyList<(InterfaceTypeSymbol Interface, TypeSyntax Syntax)> listed,
        List<(PropertySymbol Property, PropertySpecifierSyntax Specifier)> items)
    {
        var delegations = new List<Delegation>();
        foreach (var (property, specifier) in items)
        {
            foreach (var value in specifier.Values)
            {
                if (value.IsMissing)
                {
                    continue;
                }

                var named = BindImplementsType(value);
                if (named is ErrorTypeSymbol)
                {
                    continue;
                }

                if (named is not InterfaceTypeSymbol intf)
                {
                    _diagnostics.Report(value, "E2652");
                    continue;
                }

                if (!listed.Any(l => l.Interface.InheritsFrom(intf)))
                {
                    _diagnostics.Report(value, "E2662", intf.Name, type.Name);
                    continue;
                }

                var reason = property switch
                {
                    { IsClassProperty: true } => "it is a class property",
                    { IsArrayProperty: true } => "it is an array property",
                    { IsReadable: false } => "it has no read specifier",
                    { Type: ClassTypeSymbol } => null,
                    { Type: InterfaceTypeSymbol propertyType } => propertyType.InheritsFrom(intf) ? null : "its type does not descend from the interface",
                    _ => "its type is neither a class nor an interface",
                };
                if (reason is not null)
                {
                    _diagnostics.Report(value, "E2663", property.Name, intf.Name, reason);
                    continue;
                }

                if (delegations.FirstOrDefault(d => ReferenceEquals(d.Interface, intf)) is { } previous)
                {
                    _diagnostics.Report(value, "E2664", intf.Name, previous.Property.Name);
                    continue;
                }

                delegations.Add(new Delegation(intf, property));
            }
        }

        return delegations;
    }

    /// <summary>A type named in an <c>implements</c> list (the parser gives a name expression).</summary>
    private TypeSymbol BindImplementsType(ExpressionSyntax value)
    {
        switch (value)
        {
            case IdentifierNameSyntax or GenericNameSyntax:
            case QualifiedNameSyntax qualified when UnitQualifiers.Qualifier(_scope, qualified.Left) is not null || LeftmostNamesType(qualified)
                                                   || qualified.Left is GenericNameSyntax:
                return ResolveTypeName((NameSyntax)value);                                  // LANG-780 (C-345)
            default:
                _diagnostics.Report(value, "E2652");
                return _types.Error;
        }
    }

    /// <summary>
    /// LANG-690: the delegation that covers <paramref name="method"/> — the first item whose interface is the method's declaring
    /// interface or a descendant of it. The three methods of the root interface are never delegated (LANG-691).
    /// </summary>
    private Delegation? DelegationFor(List<Delegation> delegations, MethodSymbol method)
    {
        if (delegations.Count == 0 || method.ContainingInterface is not { } declaring || ReferenceEquals(declaring, _types.IInterface))
        {
            return null;
        }

        return delegations.FirstOrDefault(d => d.Interface.InheritsFrom(declaring));
    }

    /// <summary>The name of the synthesised method that delegates <paramref name="method"/> (CMP-1207).</summary>
    internal static string DelegatingName(MethodSymbol method) => "$dlg." + method.ContainingInterface!.QualifiedName + "." + method.Name;

    /// <summary>
    /// CMP-1207: the private method <c>$dlg.&lt;IntfUnit&gt;.&lt;Interface&gt;.&lt;Method&gt;</c> of <paramref name="type"/> with the exact
    /// signature of <paramref name="method"/> whose body is <c>[Result :=] Self.&lt;Property&gt;.&lt;Method&gt;(&lt;parameters&gt;)</c>, bound
    /// with the other bodies of the unit. For a class-typed property the method must exist in the property's class with an identical
    /// signature and be accessible from <paramref name="type"/>: null otherwise (the caller reports E2651).
    /// </summary>
    private MethodSymbol? DelegatingMethod(ClassTypeSymbol type, Delegation delegation, MethodSymbol method, List<MethodSymbol> synthesised)
    {
        var name = DelegatingName(method);
        if (synthesised.FirstOrDefault(m => IdentifierComparer.Instance.Equals(m.Name, name) && SameParameterTypes(m, method)) is { } existing)
        {
            return existing;
        }

        var property = delegation.Property;
        if (property.Type is ClassTypeSymbol delegateClass
            && (FindImplementation(delegateClass, method.Name, method) is not { } target || !IsAccessibleFrom(type, target, delegateClass)))
        {
            return null;
        }

        property.IsReferenced = true;
        var delegating = new MethodSymbol(name, type, [], declaration: null!, MethodKind.Ordinary, Visibility.Private)
        {
            IsOverload = method.IsOverload,
            CallingConvention = method.CallingConvention,
            ReturnType = method.ReturnType,
            IsReferenced = true,
            DelegateProperty = property,
        };
        delegating.Parameters = [.. method.Parameters.Select(p => new ParameterSymbol(p.Name, p.Type, p.Mode, p.Ordinal, delegating, [], _types.Int64)
        {
            IsRef = p.IsRef,
        })];
        delegating.SelfParameter = new ParameterSymbol("Self", type, ParameterMode.Value, -1, delegating, []);
        if (method.ReturnType is { } returnType)
        {
            delegating.ResultVariable = new VariableSymbol("Result", returnType, isGlobal: false, delegating, []);
        }

        // the call of LANG-690 as source text, so that the normal pipeline binds it (member lookup, overloads, open arrays)
        var call = "Self.&" + property.Name + ".&" + method.Name
            + (method.Parameters.IsEmpty ? "" : "(" + string.Join(", ", method.Parameters.Select(p => "&" + p.Name)) + ")");
        var text = "unit Delegation; interface implementation procedure Delegate; begin " + (method.ReturnType is null ? "" : "Result := ") + call + " end; end.";
        var path = property.Locations.IsEmpty ? "" : property.Locations[0].Path;
        delegating.Definition = SyntaxTree.Parse(SourceText.From(path, text)).Root.DescendantNodes().OfType<RoutineDeclarationSyntax>().First();
        delegating.DefinitionScope = _scope;
        delegating.DefinitionScopeVisibleCount = _scope.Count;
        synthesised.Add(delegating);
        return delegating;
    }

    /// <summary>LANG-610/612 for the call a delegating method of <paramref name="type"/> makes on a value of class <paramref name="receiver"/>.</summary>
    private static bool IsAccessibleFrom(ClassTypeSymbol type, MethodSymbol member, ClassTypeSymbol receiver)
    {
        var owner = member.Owner;
        var sameUnit = ReferenceEquals(TypeMembers.Outermost(owner).ContainingSymbol, TypeMembers.Outermost(type).ContainingSymbol);
        var own = ReferenceEquals(type, owner);
        var descendant = !own && type.SelfAndAncestors().Contains(owner) && receiver.SelfAndAncestors().Contains(type);
        return member.Visibility switch
        {
            Visibility.Public or Visibility.Published => true,
            Visibility.Private => sameUnit,
            Visibility.StrictPrivate => own,
            Visibility.Protected => sameUnit || own || descendant,
            _ => own || descendant,
        };
    }
}
