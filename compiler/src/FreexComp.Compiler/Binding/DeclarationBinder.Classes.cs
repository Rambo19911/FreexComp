using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Class declarations (LANG-600…604, 615, 620…625; ABI-070, 073, 074) and method implementations (C-080, C-081).
internal sealed partial class DeclarationBinder
{
    /// <summary>
    /// <c>T = class …</c> in a type section. The symbol is declared before its body is bound so that members may refer to the
    /// class itself. A forward declaration (<c>T = class;</c>) is completed by a later full declaration in the same section
    /// (LANG-601; E2015 at the end of the section otherwise). Only at unit/program level (E2602, LANG-603).
    /// </summary>
    private void BindClassDeclaration(SyntaxToken identifier, ClassTypeSyntax syntax, ImmutableArray<Symbol>.Builder declared,
        List<(ClassTypeSymbol Type, SyntaxToken Identifier)> forwardClasses)
    {
        var name = NameOf(identifier);
        if (!IsGlobalLevel)
        {
            _diagnostics.Report(syntax.ClassKeyword, "E2602");
            _scope.TryDeclare(name, _types.Error, out _);
            return;
        }

        var isForward = syntax.EndKeyword is null && syntax.Heritage is null && syntax.Modifier is null;
        var previous = _sharedScope?.LookupLocal(name) ?? _scope.LookupLocal(name);
        ClassTypeSymbol type;
        if (previous is ClassTypeSymbol { IsForward: true } pending && !isForward && forwardClasses.Any(f => ReferenceEquals(f.Type, pending)))
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
            type = new ClassTypeSymbol(name, _container, [DiagnosticBag.LocationOf(identifier)]);
            _scope.TryDeclare(name, type, out _);
            declared.Add(type);
            if (_container is UnitSymbol { IsProgram: false } unit && IdentifierComparer.Instance.Equals(unit.Name, "System")
                && IdentifierComparer.Instance.Equals(name, "TObject"))
            {
                _types.TObject = type;                                          // the root class (LANG-600)
            }

            if (isForward)
            {
                forwardClasses.Add((type, identifier));
                return;
            }
        }

        BindClassBody(type, syntax);
    }

    /// <summary>Parent, modifiers, fields (ABI §7.1 layout), methods and the VMT slots of one class.</summary>
    private void BindClassBody(ClassTypeSymbol type, ClassTypeSyntax syntax)
    {
        type.IsForward = false;
        type.IsAbstract = syntax.Modifier?.ContextualKind == SyntaxKind.AbstractKeyword;
        type.IsSealed = syntax.Modifier?.ContextualKind == SyntaxKind.SealedKeyword;
        type.Parent = BindParent(type, syntax.Heritage);

        var fields = ImmutableArray.CreateBuilder<FieldSymbol>();
        var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
        var names = new Dictionary<string, Symbol>(IdentifierComparer.Instance);
        var end = type.Parent?.InstanceSize ?? 8;                               // offset 0: the VMT pointer (ABI §7.1)
        foreach (var (member, visibility) in Members(syntax.Members, Visibility.Public))
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    end = BindField(type, field, visibility, end, fields, names);
                    break;
                case ClassVarSectionSyntax { ClassKeyword: null } section:
                    foreach (var field in section.Fields)
                    {
                        end = BindField(type, field, visibility, end, fields, names);
                    }

                    break;
                case ClassVarSectionSyntax section:
                    Unsupported(section.ClassKeyword!.Value, "class var");                              // LANG-615, C-086
                    break;
                case MethodDeclarationSyntax method when BindMethodDeclaration(type, method, visibility) is { } symbol:
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
                    Unsupported(property.PropertyKeyword, "property");                                   // LANG-641, C-085
                    break;
                default:
                    Unsupported(member, "class " + FeatureName(member.Kind));
                    break;
            }
        }

        type.Fields = fields.ToImmutable();
        type.InstanceSize = (end + 7) / 8 * 8;                                   // ABI-070
        type.Methods = methods.ToImmutable();
        type.VirtualSlots = AssignSlots(type);
    }

    private void Unsupported(SyntaxToken token, string feature) => _diagnostics.Report(token, "E2900", feature);

    /// <summary>The members of a class body with their visibility (LANG-610/611: before the first keyword <c>public</c>).</summary>
    private static IEnumerable<(SyntaxNode Member, Visibility Visibility)> Members(SyntaxList<SyntaxNode> members, Visibility visibility)
    {
        foreach (var member in members)
        {
            if (member is VisibilitySectionSyntax section)
            {
                var strict = section.StrictKeyword is not null;
                var sectionVisibility = section.VisibilityKeyword.ContextualKind switch
                {
                    SyntaxKind.PrivateKeyword => strict ? Visibility.StrictPrivate : Visibility.Private,
                    SyntaxKind.ProtectedKeyword => strict ? Visibility.StrictProtected : Visibility.Protected,
                    SyntaxKind.PublishedKeyword => Visibility.Published,
                    _ => Visibility.Public,
                };
                foreach (var inner in Members(section.Members, sectionVisibility))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return (member, visibility);
            }
        }
    }

    /// <summary>
    /// LANG-600: the first heritage entry is the parent class (E2007 while it is only forward-declared, E2601 when sealed,
    /// E2603 for a non-class type); without heritage the parent is <c>TObject</c>. Implemented interfaces are M2.5 (E2900).
    /// </summary>
    private ClassTypeSymbol? BindParent(ClassTypeSymbol type, HeritageSyntax? heritage)
    {
        var root = _types.TObject as ClassTypeSymbol;
        if (heritage is null || heritage.Types.Count == 0)
        {
            return ReferenceEquals(root, type) ? null : root;
        }

        for (var i = 1; i < heritage.Types.Count; i++)
        {
            Unsupported(heritage.Types[i], "implemented interface");                                      // LANG-661, M2.5
        }

        var first = heritage.Types[0];
        switch (BindType(first))
        {
            case ClassTypeSymbol parent when ReferenceEquals(parent, type) || parent.IsForward:
                _diagnostics.Report(first, "E2007", parent.Name);
                return root;
            case ClassTypeSymbol { IsSealed: true } parent:
                _diagnostics.Report(first, "E2601", parent.Name);
                return parent;
            case ClassTypeSymbol parent:
                return parent;
            case ErrorTypeSymbol:
                return root;
            default:
                _diagnostics.Report(first, "E2603");
                return root;
        }
    }

    /// <summary>Instance fields at the next multiple of their alignment (cap 8, ABI §7.1); returns the new end offset.</summary>
    private long BindField(ClassTypeSymbol type, FieldDeclarationSyntax field, Visibility visibility, long end,
        ImmutableArray<FieldSymbol>.Builder fields, Dictionary<string, Symbol> names)
    {
        var fieldType = BindType(field.Type);
        if (fieldType.TypeKind == TypeKind.Error)
        {
            return end;
        }

        if (fieldType.Size < 0 || fieldType is TextTypeSymbol)
        {
            Unsupported(field.Type, "class field of type " + fieldType.Name);
            return end;
        }

        var alignment = Math.Max(1, Math.Min(fieldType.Alignment, 8));
        foreach (var identifier in field.Identifiers.Select(i => i.Identifier).Where(i => !i.IsMissing))
        {
            var name = NameOf(identifier);
            var offset = (end + alignment - 1) / alignment * alignment;
            var symbol = new FieldSymbol(name, fieldType, offset, type, [DiagnosticBag.LocationOf(identifier)], visibility);
            if (!names.TryAdd(name, symbol))
            {
                _diagnostics.ReportRedeclared(identifier, name, names[name]);
                continue;
            }

            fields.Add(symbol);
            end = offset + fieldType.Size;
        }

        return end;
    }

    /// <summary>
    /// A method heading in a class body (LANG-620): kind, directives (LANG-622/623), parameters and result; <c>Self</c> and the
    /// constructor/destructor flag as implicit parameters (ABI-075/076/084). Null after an error (reported).
    /// </summary>
    private MethodSymbol? BindMethodDeclaration(ClassTypeSymbol type, MethodDeclarationSyntax syntax, Visibility visibility)
    {
        var heading = syntax.Heading;
        if (heading.Name is not IdentifierNameSyntax { Identifier: { IsMissing: false } identifier })
        {
            return null;
        }

        var isClass = heading.ClassKeyword is not null;
        var unsupported = heading.Keyword.Kind switch
        {
            SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword when isClass => "class " + heading.Keyword.Text.ToLowerInvariant(),   // LANG-636
            SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword => null,
            _ => heading.Keyword.Text.ToLowerInvariant(),
        } ?? (heading.TypeParameterList is not null ? "generic method" : null);
        if (unsupported is not null)
        {
            _diagnostics.Report(heading, "E2900", unsupported);
            return null;
        }

        var isStatic = syntax.Directives.Any(d => d.Keyword.ContextualKind == SyntaxKind.StaticKeyword);
        var kind = heading.Keyword.Kind switch
        {
            SyntaxKind.ConstructorKeyword => MethodKind.Constructor,
            SyntaxKind.DestructorKeyword => MethodKind.Destructor,
            _ when isClass => isStatic ? MethodKind.ClassStatic : MethodKind.ClassMethod,
            _ => MethodKind.Ordinary,
        };
        var method = new MethodSymbol(NameOf(identifier), type, [DiagnosticBag.LocationOf(identifier)], syntax, kind, visibility);
        BindMethodDirectives(method, syntax.Directives, isClass);
        BindSignature(method, heading, identifier, report: true);
        if (kind == MethodKind.Constructor)
        {
            method.ReturnType = type;                                           // ABI-075: returns the instance
        }

        if (kind != MethodKind.ClassStatic)
        {
            // ABI-084: the instance; for class methods the VMT (typed Pointer until class references, C-086)
            method.SelfParameter = new ParameterSymbol("Self", isClass ? _types.Pointer : type, ParameterMode.Value, -1, method, []);
        }

        if (kind is MethodKind.Constructor or MethodKind.Destructor)
        {
            method.FlagParameter = new ParameterSymbol(kind == MethodKind.Constructor ? "Alloc" : "Dealloc", _types.Byte, ParameterMode.Value, -1, method, []);
        }

        return method;
    }

    /// <summary>LANG-622/623 binding directives; calling conventions and hints are accepted, <c>message</c> is M2.5.</summary>
    private void BindMethodDirectives(MethodSymbol method, SyntaxList<DirectiveSyntax> directives, bool isClass)
    {
        foreach (var directive in directives)
        {
            switch (directive.Keyword.ContextualKind)
            {
                case SyntaxKind.VirtualKeyword or SyntaxKind.DynamicKeyword:
                    method.IsVirtual = true;
                    break;
                case SyntaxKind.OverrideKeyword:
                    method.IsOverride = true;
                    break;
                case SyntaxKind.AbstractKeyword:
                    method.IsAbstract = true;
                    break;
                case SyntaxKind.FinalKeyword:
                    method.IsFinal = true;
                    break;
                case SyntaxKind.ReintroduceKeyword:
                    method.IsReintroduce = true;
                    break;
                case SyntaxKind.OverloadKeyword:
                    method.IsOverload = true;
                    break;
                case SyntaxKind.InlineKeyword:
                    method.IsInline = true;
                    break;
                case SyntaxKind.StaticKeyword when isClass:
                    break;
                case SyntaxKind.MessageKeyword:
                    _diagnostics.Report(directive.Keyword, "E2900", "message method");                       // LANG-622, M2.5
                    break;
                case SyntaxKind.ForwardKeyword or SyntaxKind.ExternalKeyword or SyntaxKind.PublicKeyword or SyntaxKind.StaticKeyword:
                    _diagnostics.Report(directive.Keyword, "E1114", directive.Keyword.Text);
                    break;
            }
        }

        if (method.IsVirtual && method.IsOverride)
        {
            var (first, second) = directives.First(d => d.Keyword.ContextualKind is SyntaxKind.VirtualKeyword or SyntaxKind.DynamicKeyword
                    or SyntaxKind.OverrideKeyword).Keyword.ContextualKind == SyntaxKind.OverrideKeyword
                ? ("override", "virtual") : ("virtual", "override");
            _diagnostics.Report(method.MethodDeclaration.Heading.Name, "E2625", first, second);            // LANG-623
            method.IsOverride = false;
        }

        if (method.IsAbstract && !method.IsVirtual)
        {
            _diagnostics.Report(method.MethodDeclaration.Heading.Name, "E2622");                          // LANG-622
            method.IsAbstract = false;
        }
    }

    /// <summary>
    /// ABI-073/074: the parent's slots; an <c>override</c> takes the slot of the nearest ancestor virtual method of that name
    /// (E2620 none, E2621 other signature, E2624 final); <c>virtual</c>/<c>dynamic</c> appends a slot. A new method hiding an
    /// inherited virtual one without <c>override</c>/<c>reintroduce</c> gives W1060 (LANG-604).
    /// </summary>
    private ImmutableArray<MethodSymbol> AssignSlots(ClassTypeSymbol type)
    {
        var slots = (type.Parent?.VirtualSlots ?? []).ToBuilder();
        foreach (var method in type.Methods)
        {
            var at = method.MethodDeclaration.Heading.Name;
            var inherited = slots.LastOrDefault(s => IdentifierComparer.Instance.Equals(s.Name, method.Name) && !ReferenceEquals(s.ContainingType, type));
            if (method.IsOverride)
            {
                if (inherited is null)
                {
                    _diagnostics.Report(at, "E2620", method.Name);
                    continue;
                }

                if (!SameSignature(inherited, method))
                {
                    _diagnostics.Report(at, "E2621", method.Name);
                    continue;
                }

                if (inherited.IsFinal)
                {
                    _diagnostics.Report(at, "E2624", inherited.Name);
                }

                method.VmtSlot = inherited.VmtSlot;
                method.OverriddenMethod = inherited;
                slots[inherited.VmtSlot] = method;
                continue;
            }

            if (inherited is not null && !method.IsReintroduce)
            {
                _diagnostics.Report(at, "W1060", method.Name, inherited.ContainingType.Name);
            }

            if (method.IsVirtual)
            {
                method.VmtSlot = slots.Count;
                slots.Add(method);
            }
        }

        return slots.ToImmutable();
    }

    /// <summary>LANG-622: identical parameter modes and types and the same result type (kind included).</summary>
    private static bool SameSignature(MethodSymbol a, MethodSymbol b) =>
        a.MethodKind == b.MethodKind && SameParameterTypes(a, b)
        && (a.MethodKind == MethodKind.Constructor || ReferenceEquals(a.ReturnType, b.ReturnType));

    // ------------------------------------------------------------------ method implementations (LANG-620, C-081)

    /// <summary>
    /// <c>procedure TFoo.Bar(…); begin … end;</c>: completes the method of a class declared in this unit (E2606 when the class has
    /// no such method; E2603 when the qualifier is no class); a heading without parameter list completes the only method of
    /// the name; E2623 for an abstract method; E2004 for a second implementation.
    /// </summary>
    private void BindMethodImplementation(RoutineDeclarationSyntax declaration)
    {
        var heading = declaration.Heading;
        if (heading.Name is not QualifiedNameSyntax { Left: IdentifierNameSyntax { Identifier.IsMissing: false } className, Right: IdentifierNameSyntax { Identifier: { IsMissing: false } identifier } }
            || declaration.Block is null || !IsGlobalLevel)
        {
            _diagnostics.Report(heading.Name, "E2900", "method implementation");
            return;
        }

        switch (_scope.For(className).Lookup(NameOf(className.Identifier)))
        {
            case null:
                _diagnostics.ReportUndeclared(className, NameOf(className.Identifier));
                return;
            case ClassTypeSymbol type when ReferenceEquals(type.ContainingSymbol, _container):
                DefineMethod(type, declaration, identifier);
                return;
            case ClassTypeSymbol type:
                _diagnostics.Report(identifier, "E2606", NameOf(identifier), type.Name);
                return;
            case ErrorTypeSymbol:
                return;
            default:
                _diagnostics.Report(className, "E2603");
                return;
        }
    }

    private void DefineMethod(ClassTypeSymbol type, RoutineDeclarationSyntax declaration, SyntaxToken identifier)
    {
        var heading = declaration.Heading;
        var name = NameOf(identifier);
        var candidates = type.Methods.Where(m => IdentifierComparer.Instance.Equals(m.Name, name)).ToList();
        MethodSymbol? method = null;
        if (heading.ParameterList is null && candidates.Count == 1)
        {
            method = candidates[0];
        }
        else if (candidates.Count > 0)
        {
            var probe = new RoutineSymbol(name, _container, [], declaration);
            BindSignature(probe, heading, identifier, report: false);
            method = candidates.FirstOrDefault(m => SameParameterTypes(m, probe));
            if (method is not null && !method.Parameters.Select(p => p.Name).SequenceEqual(probe.Parameters.Select(p => p.Name), IdentifierComparer.Instance))
            {
                _diagnostics.Report(identifier, "E2101", name);
            }
        }

        if (method is null)
        {
            _diagnostics.Report(identifier, "E2606", name, type.Name);
            return;
        }

        if (method.IsAbstract)
        {
            _diagnostics.Report(identifier, "E2623", name);
            return;
        }

        if (method.Definition is not null)
        {
            _diagnostics.ReportRedeclared(identifier, name, method);
            return;
        }

        method.Definition = declaration;
        method.DefinitionScope = _scope;
        method.DefinitionScopeVisibleCount = _scope.Count;
        method.AddLocation(DiagnosticBag.LocationOf(identifier));
    }

    /// <summary>The classes a unit declares (its own, each once), in declaration order.</summary>
    internal static IEnumerable<ClassTypeSymbol> ClassesOf(UnitSymbol unit) =>
        unit.InterfaceMembers.Concat(unit.ImplementationMembers).OfType<ClassTypeSymbol>()
            .Where(c => ReferenceEquals(c.ContainingSymbol, unit)).Distinct();

    /// <summary>E2605 at the declaration of every non-abstract method of the unit's classes without implementation.</summary>
    private static void ReportUnimplementedMethods(UnitSymbol unit, DiagnosticBag diagnostics)
    {
        foreach (var method in ClassesOf(unit).SelectMany(c => c.Methods).Where(m => m is { IsAbstract: false, Definition: null }))
        {
            diagnostics.Report(method.MethodDeclaration.Heading.Name, "E2605", method.ContainingType.Name + "." + method.Name);
        }
    }
}
