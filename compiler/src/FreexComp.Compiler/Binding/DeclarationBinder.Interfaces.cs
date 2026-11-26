using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Interface declarations (LANG-660), the interfaces a class lists and their implementation check (LANG-661, ABI §7.1/§7.3; C-098).
internal sealed partial class DeclarationBinder
{
    /// <summary>
    /// <c>I = interface …</c> in a type section; a forward declaration (<c>I = interface;</c>) is completed later in the same
    /// section (E2015 otherwise). Only at unit/program level (E2602). <c>System.IInterface</c> becomes the default ancestor.
    /// </summary>
    private void BindInterfaceDeclaration(SyntaxToken identifier, InterfaceTypeSyntax syntax, ImmutableArray<Symbol>.Builder declared,
        List<(InterfaceTypeSymbol Type, SyntaxToken Identifier)> forwardInterfaces) =>
        BindInterfaceDeclaration(identifier, syntax.InterfaceKeyword, syntax.EndKeyword is null && syntax.Heritage is null, isDispatch: false,
            type => BindInterfaceBody(type, syntax), declared, forwardInterfaces);

    /// <summary>
    /// The declaration of an interface or (<paramref name="isDispatch"/>, com-interfaces.md LANG-693) a dispatch interface; a
    /// forward declaration is completed by a declaration of the same kind.
    /// </summary>
    private void BindInterfaceDeclaration(SyntaxToken identifier, SyntaxToken keyword, bool isForward, bool isDispatch, Action<InterfaceTypeSymbol> bindBody,
        ImmutableArray<Symbol>.Builder declared, List<(InterfaceTypeSymbol Type, SyntaxToken Identifier)> forwardInterfaces)
    {
        var name = NameOf(identifier);
        if (!IsTypeLevel)
        {
            _diagnostics.Report(keyword, "E2602");                              // LANG-603, 771 (C-344): nested in a type is fine
            _scope.TryDeclare(name, _types.Error, out _);
            return;
        }

        var previous = _sharedScope?.LookupLocal(name) ?? _scope.LookupLocal(name);
        InterfaceTypeSymbol type;
        if (previous is InterfaceTypeSymbol { IsForward: true } pending && !isForward && pending.IsDispatchInterface == isDispatch
            && forwardInterfaces.Any(f => ReferenceEquals(f.Type, pending)))
        {
            type = pending;
            type.AddLocation(DiagnosticBag.LocationOf(identifier));
        }
        else if (previous is not null)
        {
            _diagnostics.ReportRedeclared(identifier, name, previous);
            return;
        }
        else
        {
            type = new InterfaceTypeSymbol(_types.Target, name, _container, [DiagnosticBag.LocationOf(identifier)]) { IsDispatchInterface = isDispatch };
            _scope.TryDeclare(name, type, out _);
            declared.Add(type);
            if (!isDispatch && _container is UnitSymbol { IsProgram: false } unit && IdentifierComparer.Instance.Equals(unit.Name, "System")
                && IdentifierComparer.Instance.Equals(name, "IInterface"))
            {
                _types.IInterface = type;                                       // the root interface (ABI-094)
            }
            else if (!isDispatch && _container is UnitSymbol { IsProgram: false } system && IdentifierComparer.Instance.Equals(system.Name, "System")
                && IdentifierComparer.Instance.Equals(name, "IDispatch"))
            {
                _types.IDispatch = type;                                        // RTL-1001: what dispatch-interface values refer to
            }

            if (isForward)
            {
                forwardInterfaces.Add((type, identifier));
                return;
            }
        }

        bindBody(type);
    }

    /// <summary>Parent (default <c>IInterface</c>), GUID (E2650), and the method headings; fields are E2654 (LANG-660).</summary>
    private void BindInterfaceBody(InterfaceTypeSymbol type, InterfaceTypeSyntax syntax)
    {
        type.IsForward = false;
        type.Parent = BindInterfaceParent(type, syntax.Heritage);
        if (syntax.Guid is { } guid)
        {
            type.Guid = BindGuid(guid.Value);
        }

        var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
        var names = new Dictionary<string, Symbol>(IdentifierComparer.Instance);
        var properties = new List<PropertyDeclarationSyntax>();
        var slot = type.Parent?.AllMethods.Length ?? 0;
        foreach (var (member, _) in Members(syntax.Members, Visibility.Public))
        {
            switch (member)
            {
                case PropertyDeclarationSyntax property:
                    properties.Add(property);                                   // bound after the methods (their accessors)
                    break;
                case MethodDeclarationSyntax method when BindInterfaceMethod(type, method) is { } symbol:
                    if (names.TryGetValue(symbol.Name, out var existing)
                        && !(existing is MethodSymbol other && other.IsOverload && symbol.IsOverload && !SameParameterTypes(other, symbol)))
                    {
                        _diagnostics.ReportRedeclared(((IdentifierNameSyntax)method.Heading.Name).Identifier, symbol.Name, existing);
                        break;
                    }

                    names.TryAdd(symbol.Name, symbol);
                    symbol.VmtSlot = slot++;                                    // ABI-091: the IMT slot
                    methods.Add(symbol);
                    break;
                case MethodDeclarationSyntax:
                    break;
                case FieldDeclarationSyntax or ClassVarSectionSyntax:
                    _diagnostics.Report(member, "E2654");
                    break;
                default:
                    Unsupported(member, "interface " + FeatureName(member.Kind));
                    break;
            }
        }

        type.Methods = methods.ToImmutable();
        type.Properties = BindInterfaceProperties(type, properties, names);
    }

    /// <summary>
    /// LANG-660/664 (C-101): interface properties; accessors are methods of the interface or an ancestor (a name that is no such
    /// method is E2003; a field-style accessor cannot exist). Array, default and <c>index</c> properties as for classes (LANG-642/643);
    /// <c>class property</c> and <c>implements</c> are E2900.
    /// </summary>
    private ImmutableArray<PropertySymbol> BindInterfaceProperties(InterfaceTypeSymbol type, List<PropertyDeclarationSyntax> properties, Dictionary<string, Symbol> names)
    {
        var bound = ImmutableArray.CreateBuilder<PropertySymbol>();
        foreach (var syntax in properties)
        {
            if (syntax.Identifier.IsMissing)
            {
                continue;
            }

            if (syntax.ClassKeyword is { } classKeyword)
            {
                _diagnostics.Report(classKeyword, "E2900", "interface class property");
                continue;
            }

            var name = NameOf(syntax.Identifier);
            var property = new PropertySymbol(name, type, [DiagnosticBag.LocationOf(syntax.Identifier)], Visibility.Public);
            if (!names.TryAdd(name, property))
            {
                _diagnostics.ReportRedeclared(syntax.Identifier, name, names[name]);
                continue;
            }

            if (syntax.Type is null)
            {
                _diagnostics.Report(syntax.Identifier, "E2900", "interface property redeclaration");
                continue;
            }

            property.Type = BindType(syntax.Type);
            property.Parameters = BindParameters(syntax.Parameters, property, new Scope(ScopeKind.Routine, parent: null, property), report: true);
            if (property.Type.TypeKind == TypeKind.Error || property.Parameters.Any(p => p.Type.TypeKind == TypeKind.Error))
            {
                continue;
            }

            if (syntax.DefaultKeyword is { } defaultKeyword)
            {
                if (!property.IsArrayProperty)
                {
                    _diagnostics.Report(defaultKeyword, "E2900", "default on a non-array property");
                    continue;
                }

                if (bound.Any(p => p.IsDefault))
                {
                    _diagnostics.Report(defaultKeyword, "E2635", type.Name);
                }
                else
                {
                    property.IsDefault = true;
                }
            }

            var ok = true;
            if (syntax.Specifiers.FirstOrDefault(s => s.Keyword.ContextualKind == SyntaxKind.IndexKeyword)?.Value is { } indexValue)
            {
                ok &= BindPropertyIndex(property, indexValue);
            }

            foreach (var specifier in syntax.Specifiers)
            {
                switch (specifier.Keyword.ContextualKind)
                {
                    case SyntaxKind.ReadKeyword or SyntaxKind.WriteKeyword when specifier.Value is { } value:
                        ok &= BindInterfaceAccessor(type, property, value, write: specifier.Keyword.ContextualKind == SyntaxKind.WriteKeyword);
                        break;
                    case SyntaxKind.ImplementsKeyword:
                        Unsupported(specifier.Keyword, "implements");
                        ok = false;
                        break;
                    case SyntaxKind.DispIdKeyword when specifier.Value is { } dispId:
                        property.DispId = BindDispId(dispId);                   // LANG-694: information only
                        break;
                    case SyntaxKind.ReadOnlyKeyword or SyntaxKind.WriteOnlyKeyword:
                        Unsupported(specifier.Keyword, specifier.Keyword.Text.ToLowerInvariant() + " outside a dispatch interface");
                        ok = false;
                        break;
                }
            }

            if (ok && !property.IsReadable && !property.IsWritable)
            {
                _diagnostics.Report(syntax.Identifier, "E2631", property.Name);                                // LANG-641
                ok = false;
            }

            if (ok)
            {
                bound.Add(property);
            }
        }

        return bound.ToImmutable();
    }

    /// <summary>A <c>read</c>/<c>write</c> accessor of an interface property: a method of the interface chain (E2636 when none fits).</summary>
    private bool BindInterfaceAccessor(InterfaceTypeSymbol type, PropertySymbol property, ExpressionSyntax value, bool write)
    {
        if (value is not IdentifierNameSyntax { Identifier: { IsMissing: false } token } name)
        {
            _diagnostics.Report(value, "E2636", value.ToString().Trim(), property.Type.Name);
            return false;
        }

        var text = NameOf(token);
        var methods = type.SelfAndAncestors().SelectMany(i => i.Methods).Where(m => IdentifierComparer.Instance.Equals(m.Name, text)).ToList();
        if (methods.Count == 0)
        {
            _diagnostics.ReportUndeclared(name, text);
            return false;
        }

        return MatchAccessor(property, methods, value, write);
    }

    /// <summary>The ancestor interface: the heritage entry (E2652 for a non-interface, E2007 while forward), else <c>IInterface</c>.</summary>
    private InterfaceTypeSymbol? BindInterfaceParent(InterfaceTypeSymbol type, HeritageSyntax? heritage)
    {
        var root = _types.IInterface as InterfaceTypeSymbol;
        if (heritage is null || heritage.Types.Count == 0)
        {
            return ReferenceEquals(root, type) ? null : root;
        }

        for (var i = 1; i < heritage.Types.Count; i++)
        {
            Unsupported(heritage.Types[i], "second interface ancestor");
        }

        var first = heritage.Types[0];
        switch (BindType(first))
        {
            case InterfaceTypeSymbol parent when ReferenceEquals(parent, type) || parent.IsForward:
                _diagnostics.Report(first, "E2007", parent.Name);
                return root;
            case InterfaceTypeSymbol { IsDispatchInterface: true }:
                _diagnostics.Report(first, "E2652");                            // LANG-693: a dispatch interface is no ancestor
                return root;
            case InterfaceTypeSymbol parent:
                return parent;
            case ErrorTypeSymbol:
                return root;
            default:
                _diagnostics.Report(first, "E2652");
                return root;
        }
    }

    /// <summary>LANG-660: <c>'{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}'</c> (hex, any case) in <c>TGUID</c> memory order (ABI-096); E2650 otherwise.</summary>
    private ImmutableArray<byte>? BindGuid(ExpressionSyntax value)
    {
        if (_folder.Fold(value) is not { Value: var folded })
        {
            return null;
        }

        if (folded is string text && GuidValues.Parse(text) is { } bytes)
        {
            return bytes;                                                       // D1, D2, D3 little-endian, then D4: the TGUID layout
        }

        _diagnostics.Report(value, "E2650");
        return null;
    }

    /// <summary>A method heading of an interface (LANG-660): procedure or function, <c>Self</c> typed as the interface, <c>overload</c> allowed.</summary>
    private MethodSymbol? BindInterfaceMethod(InterfaceTypeSymbol type, MethodDeclarationSyntax syntax)
    {
        var heading = syntax.Heading;
        if (heading.Name is not IdentifierNameSyntax { Identifier: { IsMissing: false } identifier })
        {
            return null;
        }

        if (heading.ClassKeyword is not null || heading.Keyword.Kind is not (SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword) || heading.TypeParameterList is not null)
        {
            _diagnostics.Report(heading, "E2900", "interface " + heading.Keyword.Text.ToLowerInvariant());
            return null;
        }

        var method = new MethodSymbol(NameOf(identifier), type, [DiagnosticBag.LocationOf(identifier)], syntax, MethodKind.Ordinary, Visibility.Public)
        {
            IsOverload = syntax.Directives.Any(d => d.Keyword.ContextualKind == SyntaxKind.OverloadKeyword),
            CallingConvention = CallConvOf(syntax.Directives.Select(d => d.Keyword)),
            Hints = BindHints(syntax.Directives),                                                           // LANG-472 (C-232)
        };
        BindSignature(method, heading, identifier, report: true);
        method.SelfParameter = new ParameterSymbol("Self", type, ParameterMode.Value, -1, method, []);     // ABI-092: the interface reference
        if (syntax.Directives.OfType<DispIdDirectiveSyntax>().FirstOrDefault() is { Value: { } dispId })
        {
            method.DispId = BindDispId(dispId);                                 // LANG-694: range-checked, otherwise without effect
        }

        return method;
    }

    // ------------------------------------------------------------------ classes implementing interfaces (LANG-661)

    /// <summary>
    /// ABI §7.1: one hidden IMT pointer per listed interface after the fields (8-aligned, listing order), each with the class
    /// method implementing every method of the interface and its ancestors (LANG-661: resolution clause, else the nearest
    /// method of that name with an identical signature; any visibility). E2651 at the listed interface for a missing one.
    /// Returns the end of the instance.
    /// </summary>
    private long BindImplementedInterfaces(ClassTypeSymbol type, IReadOnlyList<(InterfaceTypeSymbol Interface, TypeSyntax Syntax)> listed,
        IReadOnlyList<MethodResolutionSyntax> resolutions, List<(PropertySymbol Property, PropertySpecifierSyntax Specifier)> implements, long end)
    {
        var offset = (end + 7) / 8 * 8;
        var interfaces = ImmutableArray.CreateBuilder<ImplementedInterface>();
        var mapped = BindResolutions(type, listed, resolutions);
        var delegations = BindDelegations(type, listed, implements);            // LANG-690…692 (C-302)
        var synthesised = new List<MethodSymbol>();
        foreach (var (intf, syntax) in listed)
        {
            if (interfaces.Any(i => ReferenceEquals(i.Interface, intf)))
            {
                _diagnostics.Report(syntax, "E2004", intf.Name);
                continue;
            }

            var implementations = ImmutableArray.CreateBuilder<MethodSymbol>();
            foreach (var method in intf.AllMethods)
            {
                // LANG-661/690/692: a resolution clause, else the class's own delegation, else the nearest method or inherited delegation
                var implementation = mapped.TryGetValue((intf, method), out var target) ? target
                    : DelegationFor(delegations, method) is { } delegation ? DelegatingMethod(type, delegation, method, synthesised)
                    : FindImplementationOrDelegation(type, method);
                if (implementation is null)
                {
                    _diagnostics.Report(syntax, "E2651", method.ContainingSymbol!.Name, method.Name);
                    implementation = method;                                    // placeholder: the unit has an error and is not emitted
                }
                else
                {
                    implementation.IsReferenced = true;                         // called through the interface (H2003)
                }

                implementations.Add(implementation);
            }

            interfaces.Add(new ImplementedInterface(intf, offset, implementations.ToImmutable()));
            offset += 8;
        }

        type.Interfaces = interfaces.ToImmutable();
        type.Methods = type.Methods.AddRange(synthesised);                      // CMP-1207: ordinary private methods of the class
        return offset;
    }

    /// <summary>The nearest instance method named <paramref name="name"/> of the class or an ancestor with the signature of <paramref name="method"/>.</summary>
    internal static MethodSymbol? FindImplementation(ClassTypeSymbol type, string name, MethodSymbol method) =>
        type.SelfAndAncestors().SelectMany(c => c.Methods)
            .FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary && IdentifierComparer.Instance.Equals(m.Name, name) && SameParameterTypes(m, method)
                && ReferenceEquals(m.ReturnType, method.ReturnType) && m.IsSafecall == method.IsSafecall         // LANG-689 (C-301)
                && SameRefParameters(m, method));                                                               // LANG-763 (C-232)

    /// <summary>
    /// As <see cref="FindImplementation"/> for the method's own name, where a class of the chain that delegates the method
    /// (<c>implements</c>, LANG-692: inherited delegation) supplies its synthesised method.
    /// </summary>
    internal static MethodSymbol? FindImplementationOrDelegation(ClassTypeSymbol type, MethodSymbol method)
    {
        var delegating = method.ContainingInterface is null ? null : DelegatingName(method);
        foreach (var owner in type.SelfAndAncestors())
        {
            var inherited = delegating is null ? null : owner.Methods.FirstOrDefault(m => string.Equals(m.Name, delegating, StringComparison.OrdinalIgnoreCase)
                && SameParameterTypes(m, method) && ReferenceEquals(m.ReturnType, method.ReturnType));
            var found = inherited ?? owner.Methods.FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary && IdentifierComparer.Instance.Equals(m.Name, method.Name)
                && SameParameterTypes(m, method) && ReferenceEquals(m.ReturnType, method.ReturnType) && m.IsSafecall == method.IsSafecall
                && SameRefParameters(m, method));                                                               // LANG-763 (C-232)
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// LANG-661 <c>procedure IFoo.Bar = MyBar;</c>: maps method <c>Bar</c> of a listed interface <c>IFoo</c> (or of one of its ancestors)
    /// to <c>MyBar</c>; E2653 when the class does not list <c>IFoo</c>, E2003 for an unknown method, E2651 when no <c>MyBar</c> fits.
    /// </summary>
    private Dictionary<(InterfaceTypeSymbol, MethodSymbol), MethodSymbol?> BindResolutions(ClassTypeSymbol type,
        IReadOnlyList<(InterfaceTypeSymbol Interface, TypeSyntax Syntax)> listed, IReadOnlyList<MethodResolutionSyntax> resolutions)
    {
        var mapped = new Dictionary<(InterfaceTypeSymbol, MethodSymbol), MethodSymbol?>();
        foreach (var resolution in resolutions)
        {
            if (resolution.InterfaceMethod is not QualifiedNameSyntax { Left: IdentifierNameSyntax { Identifier.IsMissing: false } qualifier, Right: IdentifierNameSyntax { Identifier.IsMissing: false } methodName }
                || resolution.Target.Identifier.IsMissing)
            {
                continue;
            }

            var named = _scope.For(qualifier).Lookup(NameOf(qualifier.Identifier));
            if (named is not InterfaceTypeSymbol intf)
            {
                if (named is null)
                {
                    _diagnostics.ReportUndeclared(qualifier, NameOf(qualifier.Identifier));
                }
                else if (named is not ErrorTypeSymbol)
                {
                    _diagnostics.Report(qualifier, "E2652");
                }

                continue;
            }

            var owners = listed.Where(l => l.Interface.InheritsFrom(intf)).Select(l => l.Interface).ToList();
            if (owners.Count == 0)
            {
                _diagnostics.Report(qualifier, "E2653", type.Name, intf.Name);
                continue;
            }

            var name = NameOf(methodName.Identifier);
            var methods = intf.AllMethods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name)
                && (m.ReturnType is null) == (resolution.Keyword.Kind == SyntaxKind.ProcedureKeyword)).ToList();
            if (methods.Count == 0)
            {
                _diagnostics.ReportUndeclared(methodName, name);
                continue;
            }

            foreach (var method in methods)
            {
                var target = FindImplementation(type, NameOf(resolution.Target.Identifier), method);
                foreach (var owner in owners)
                {
                    mapped[(owner, method)] = target;
                }
            }
        }

        return mapped;
    }
}
