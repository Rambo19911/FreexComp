using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Generic types (LANG-700, 703, 704, 710…713, 717; C-116): definitions, instantiation, method implementations.
internal sealed partial class DeclarationBinder
{
    /// <summary>LANG-715: instantiations nested deeper than this are E2712.</summary>
    private const int MaxInstantiationDepth = 64;

    [ThreadStatic]
    private static int t_instantiationDepth;

    /// <summary>The name a type bound by <see cref="BindType"/> gets instead of its declared one (a generic instance), used once.</summary>
    private string? _instanceName;

    /// <summary>Called with a record with members as soon as its symbol exists (a generic instance registers itself before its members).</summary>
    private Action<TypeSymbol>? _onTypeCreated;

    /// <summary>
    /// <c>TList&lt;T&gt; = …</c> (LANG-700) at unit level: a <see cref="GenericTypeSymbol"/> declared under its name and arity
    /// (LANG-703). Nothing of the body is bound until it is instantiated (LANG-711).
    /// </summary>
    private void BindGenericDeclaration(TypeDeclarationSyntax declaration, TypeParameterListSyntax typeParameters, ImmutableArray<Symbol>.Builder declared)
    {
        var identifier = declaration.Identifier;
        if (!IsGlobalLevel)
        {
            Unsupported(typeParameters, "local generic type");
            return;
        }

        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var parameter in typeParameters.Parameters.SelectMany(p => p.Identifiers).Where(i => !i.Identifier.IsMissing))
        {
            var name = NameOf(parameter.Identifier);
            if (names.Contains(name, IdentifierComparer.Instance))
            {
                _diagnostics.Report(parameter, "E2004", name);
                return;
            }

            names.Add(name);
        }

        var generic = new GenericTypeSymbol(NameOf(identifier), names.ToImmutable(), declaration, _scope, 0, _container, [DiagnosticBag.LocationOf(identifier)]);
        var previous = _sharedScope?.LookupLocal(generic.ScopeName);
        if (previous is not null || !_scope.TryDeclare(generic.ScopeName, generic, out previous))
        {
            _diagnostics.ReportRedeclared(identifier, generic.Name, previous!);
            return;
        }

        generic.VisibleCount = _scope.Count;                                    // the declaration sees itself and what precedes it
        generic.Constraints = BindConstraints(typeParameters);
        declared.Add(generic);
        if (declaration.Type is ClassTypeSyntax { Heritage.Types: [NamedTypeSyntax { Name: IdentifierNameSyntax { Identifier: var parentToken } parentName }, ..] }
            && names.Contains(NameOf(parentToken), IdentifierComparer.Instance))
        {
            _diagnostics.Report(parentName, "E2702", NameOf(parentToken));       // LANG-717 (C-118)
        }
    }

    /// <summary>
    /// LANG-702 (C-118): the constraints of each type parameter, bound in this scope; <c>class</c> with <c>record</c>, or two class
    /// types, are E2701 (at the later one).
    /// </summary>
    private ImmutableArray<TypeParameterConstraints> BindConstraints(TypeParameterListSyntax typeParameters)
    {
        var result = ImmutableArray.CreateBuilder<TypeParameterConstraints>();
        foreach (var parameter in typeParameters.Parameters)
        {
            bool isClass = false, isRecord = false, isConstructor = false;
            var types = ImmutableArray.CreateBuilder<TypeSymbol>();
            var texts = ImmutableArray.CreateBuilder<string>();
            var name = parameter.Identifiers.Count > 0 ? NameOf(parameter.Identifiers[0].Identifier) : "";
            foreach (var constraint in parameter.Constraints)
            {
                var conflict = false;
                switch (constraint)
                {
                    case KeywordConstraintSyntax { Keyword.Kind: SyntaxKind.ClassKeyword }:
                        conflict = isRecord;
                        isClass = true;
                        break;
                    case KeywordConstraintSyntax { Keyword.Kind: SyntaxKind.RecordKeyword }:
                        conflict = isClass || types.Any(t => t is ClassTypeSymbol);
                        isRecord = true;
                        break;
                    case KeywordConstraintSyntax:
                        isConstructor = true;
                        break;
                    case TypeSyntax typeSyntax:
                        var type = BindType(typeSyntax);
                        if (type.TypeKind == TypeKind.Error)
                        {
                            continue;
                        }

                        if (type is not (ClassTypeSymbol or InterfaceTypeSymbol))
                        {
                            _diagnostics.Report(typeSyntax, "E2603");
                            continue;
                        }

                        conflict = type is ClassTypeSymbol && (isRecord || types.Any(t => t is ClassTypeSymbol));
                        types.Add(type);
                        texts.Add(type.Name);
                        break;
                }

                if (conflict)
                {
                    _diagnostics.Report(constraint, "E2701", name);
                }
            }

            var constraints = new TypeParameterConstraints(isClass, isRecord, isConstructor, types.ToImmutable(), texts.ToImmutable());
            foreach (var _ in parameter.Identifiers)
            {
                result.Add(constraints);
            }
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// LANG-702 (C-118): E2704 at the argument (or <paramref name="at"/>) for every type argument that misses a constraint of its type
    /// parameter; false when one did.
    /// </summary>
    private bool CheckConstraints(ImmutableArray<string> names, ImmutableArray<TypeParameterConstraints> constraints, ImmutableArray<TypeSymbol> arguments,
        SeparatedSyntaxList<TypeSyntax>? argumentSyntax, SyntaxNode at)
    {
        var ok = true;
        for (var i = 0; i < arguments.Length && i < constraints.Length; i++)
        {
            var argument = arguments[i];
            var c = constraints[i];
            SyntaxNode where = argumentSyntax is { } list && i < list.Count ? list[i] : at;
            void Fail(string constraint)
            {
                _diagnostics.Report(where, "E2704", argument.Name, constraint, names[i]);
                ok = false;
            }

            if (c.Class && argument is not ClassTypeSymbol)
            {
                Fail("class");
            }

            if (c.Record && !(argument.IsOrdinal || argument is RealTypeSymbol or RecordTypeSymbol or ArrayTypeSymbol or SetTypeSymbol))
            {
                Fail("record");
            }

            if (c.Constructor && !(argument is ClassTypeSymbol @class && @class.SelfAndAncestors().SelectMany(a => a.Methods)
                    .Any(m => m is { MethodKind: MethodKind.Constructor, Parameters.IsEmpty: true, Visibility: Visibility.Public or Visibility.Published }
                        && IdentifierComparer.Instance.Equals(m.Name, "Create"))))
            {
                Fail("constructor");
            }

            for (var k = 0; k < c.Types.Length; k++)
            {
                var satisfied = (c.Types[k], argument) switch
                {
                    (ClassTypeSymbol baseClass, ClassTypeSymbol actual) => actual.InheritsFrom(baseClass),
                    (InterfaceTypeSymbol intf, InterfaceTypeSymbol actual) => actual.InheritsFrom(intf),
                    (InterfaceTypeSymbol intf, ClassTypeSymbol actual) => actual.SelfAndAncestors().Any(a => a.FindInterface(intf) is not null),
                    _ => false,
                };
                if (!satisfied)
                {
                    Fail(c.TypeTexts[k]);
                }
            }
        }

        return ok;
    }

    /// <summary>
    /// <c>TList&lt;Integer&gt;</c> in a type or expression (LANG-710): the instance of the generic of that name and arity for the bound
    /// arguments. E2703 when only another arity (or a non-generic type) has the name, E2003 when nothing has.
    /// </summary>
    internal TypeSymbol BindGenericName(GenericNameSyntax name)
    {
        if (name.Identifier.IsMissing || name.ContainsDiagnostics)
        {
            return _types.Error;
        }

        var text = NameOf(name.Identifier);
        var arguments = name.TypeArgumentList.Arguments;
        if (_scope.Lookup(GenericTypeSymbol.KeyOf(text, arguments.Count)) is not GenericTypeSymbol generic)
        {
            var other = Enumerable.Range(0, 16).Select(n => _scope.Lookup(GenericTypeSymbol.KeyOf(text, n))).OfType<GenericTypeSymbol>().FirstOrDefault();
            if (other is not null || _scope.Lookup(text) is TypeSymbol)
            {
                _diagnostics.Report(name, "E2703", text, other?.ParameterNames.Length ?? 0);
            }
            else
            {
                _diagnostics.ReportUndeclared(name, text);
            }

            return _types.Error;
        }

        if (_types.HasTypeHints && _scope.ImportedTypeHints(generic.ScopeName) is { } hints)
        {
            _diagnostics.ReportHints(name, generic.Name, hints);                                   // LANG-472 (C-232)
        }

        var bound = ImmutableArray.CreateBuilder<TypeSymbol>(arguments.Count);
        foreach (var argument in arguments)
        {
            bound.Add(BindType(argument));
        }

        var typeArguments = bound.MoveToImmutable();
        return typeArguments.Any(t => t.TypeKind == TypeKind.Error) || !CheckConstraints(generic.ParameterNames, generic.Constraints, typeArguments, arguments, name)
            ? _types.Error
            : Instantiate(generic, typeArguments, name);
    }

    /// <summary>
    /// LANG-710/711: the instance of <paramref name="generic"/> for <paramref name="arguments"/> — made once per argument list (under
    /// the compilation's generic lock, units bind in parallel) by binding the declaration below the declaring scope with each type
    /// parameter declared as its argument. The using unit records it (CMP-385).
    /// </summary>
    internal TypeSymbol Instantiate(GenericTypeSymbol generic, ImmutableArray<TypeSymbol> arguments, SyntaxNode at)
    {
        var unit = GenericUses.Current ?? GenericUses.UnitOf(_scope);
        lock (_types.GenericGate)
        {
            if (!generic.Instances.TryGetValue(arguments, out var instance))
            {
                if (generic.Declaration is null || generic.DeclaringScope is null)
                {
                    _diagnostics.Report(at, "E2900", "generic type of a precompiled unit");
                    return _types.Error;
                }

                if (t_instantiationDepth >= MaxInstantiationDepth)
                {
                    _diagnostics.Report(at, "E2712");
                    return _types.Error;
                }

                t_instantiationDepth++;
                try
                {
                    var bag = new DiagnosticBag();
                    using (GenericUses.For(unit))
                    {
                        instance = new DeclarationBinder(_types, _scope, _container, bag).CreateInstance(generic, arguments);
                    }

                    var site = DiagnosticBag.LocationOf(at);
                    foreach (var diagnostic in bag.ToImmutable())
                    {
                        if (DiagnosticBag.InInstance(diagnostic, instance.Name, site, _types.ReportedInstanceWarnings) is { } wrapped)
                        {
                            _diagnostics.Add(wrapped);                                    // LANG-711 (C-118, C-222)
                        }
                    }
                }
                finally
                {
                    t_instantiationDepth--;
                }
            }

            GenericUses.Note(instance, unit, at);
            return instance;
        }
    }

    private TypeSymbol CreateInstance(GenericTypeSymbol generic, ImmutableArray<TypeSymbol> arguments)
    {
        var declaration = generic.Declaration!;
        var display = generic.Name + "<" + string.Join(", ", arguments.Select(a => a.Name)) + ">";
        var canonical = (generic.ContainingSymbol is { } owner ? owner.QualifiedName + "." : "") + generic.Name
            + "<" + string.Join(",", arguments.Select(Emit.Mangler.TypeName)) + ">";
        var scope = new Scope(ScopeKind.Unit, generic.DeclaringScope, generic.ContainingSymbol, generic.VisibleCount) { DeclaresTypeParameters = true };
        for (var i = 0; i < arguments.Length; i++)
        {
            scope.TryDeclare(generic.ParameterNames[i], arguments[i], out _);   // T is another name of its argument
        }

        var binder = new DeclarationBinder(_types, scope, generic.ContainingSymbol!, _diagnostics);
        void Register(TypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Error && type.GenericDefinition is null)
            {
                type.GenericDefinition = generic;
                type.TypeArguments = arguments;
                type.QualifiedNameOverride = canonical;
            }

            generic.Instances[arguments] = type;
        }

        TypeSymbol instance;
        switch (declaration.Type)
        {
            case ClassTypeSyntax { EndKeyword: not null } classSyntax when declaration.TypeKeyword is null:
            {
                var type = new ClassTypeSymbol(_types.Target, display, generic.ContainingSymbol, generic.Locations);
                Register(type);                                                 // before the body: members may name the instance
                binder.BindClassBody(type, classSyntax);
                instance = type;
                break;
            }

            case InterfaceTypeSyntax { EndKeyword: not null } interfaceSyntax when declaration.TypeKeyword is null:
            {
                // C-222: a generic interface (LANG-700); every instance has the declared GUID, if any
                var type = new InterfaceTypeSymbol(_types.Target, display, generic.ContainingSymbol, generic.Locations);
                Register(type);
                binder.BindInterfaceBody(type, interfaceSyntax);
                instance = type;
                break;
            }

            case ClassTypeSyntax or InterfaceTypeSyntax or HelperTypeSyntax or DispInterfaceTypeSyntax:
                instance = binder.Unsupported(declaration.Type, "generic " + FeatureName(declaration.Type.Kind));
                break;
            default:
                binder._instanceName = display;
                binder._onTypeCreated = Register;
                instance = declaration.TypeKeyword is null
                    ? binder.BindType(declaration.Type, declaration.Identifier, ImmutableArray.CreateBuilder<Symbol>())
                    : binder.Unsupported(declaration.Type, "generic distinct type");
                break;
        }

        Register(instance);
        var first = true;
        foreach (var implementation in generic.Implementations)
        {
            DefineInstanceMethod(instance, implementation, first);
            first = false;
        }

        return instance;
    }

    /// <summary>
    /// <c>procedure TList&lt;T&gt;.Add(…)</c> (LANG-704): recorded with the generic of this unit and applied to every instance made so
    /// far and later (its heading is bound per instance, with its type parameter names declared as the instance's arguments).
    /// Diagnostics of the matching are reported for the first instance only.
    /// </summary>
    private void BindGenericMethodImplementation(RoutineDeclarationSyntax declaration, GenericNameSyntax qualifier, SyntaxToken identifier,
        ImmutableArray<SyntaxToken> nestedPath = default)
    {
        var text = NameOf(qualifier.Identifier);
        var arguments = qualifier.TypeArgumentList.Arguments;
        if (_scope.Lookup(GenericTypeSymbol.KeyOf(text, arguments.Count)) is not GenericTypeSymbol generic)
        {
            _diagnostics.ReportUndeclared(qualifier, text);
            return;
        }

        if (!ReferenceEquals(generic.ContainingSymbol, _container))
        {
            _diagnostics.Report(identifier, "E2606", NameOf(identifier), generic.Name);
            return;
        }

        var names = arguments.Select(a => a is NamedTypeSyntax { Name: IdentifierNameSyntax { Identifier: var token } } ? NameOf(token) : "").ToImmutableArray();
        var implementation = new GenericMethodImplementation(declaration, identifier, names, _scope, _scope.Count, _container)
        {
            NestedPath = nestedPath.IsDefault ? [] : nestedPath,
        };
        lock (_types.GenericGate)
        {
            generic.Implementations.Add(implementation);
            var first = true;
            foreach (var instance in generic.Instances.Values.Where(i => i.TypeKind != TypeKind.Error).ToList())
            {
                DefineInstanceMethod(instance, implementation, first);
                first = false;
            }
        }
    }

    private void DefineInstanceMethod(TypeSymbol instance, GenericMethodImplementation implementation, bool report)
    {
        if (instance.TypeKind == TypeKind.Error || instance is not (ClassTypeSymbol or RecordTypeSymbol))
        {
            return;
        }

        var scope = new Scope(ScopeKind.Unit, implementation.Scope, implementation.Container, implementation.VisibleCount) { DeclaresTypeParameters = true };
        for (var i = 0; i < implementation.ParameterNames.Length && i < instance.TypeArguments.Length; i++)
        {
            scope.TryDeclare(implementation.ParameterNames[i], instance.TypeArguments[i], out _);
        }

        var diagnostics = report ? _diagnostics : new DiagnosticBag();
        var owner = instance;
        foreach (var segment in implementation.NestedPath)
        {
            // LANG-776 (C-344): the instance's own nested type (each instance has its nested types, LANG-775)
            if (TypeMembers.NestedType(owner, NameOf(segment)) is not ({ } nested and (ClassTypeSymbol or RecordTypeSymbol))
                || !ReferenceEquals(nested.ContainingSymbol, owner))
            {
                diagnostics.Report(segment, "E2606", NameOf(segment), TypeMembers.DisplayName(owner));
                return;
            }

            owner = nested;
        }

        new DeclarationBinder(_types, scope, implementation.Container, diagnostics).DefineMethod(owner, implementation.Declaration, implementation.Identifier);
    }

    /// <summary>The binder binds the heading of a generic method instance (its type parameter list is expected then).</summary>
    private bool _genericMethodInstance;

    /// <summary>LANG-701 (C-117): a generic method of a class or record; nothing of it is bound until it is instantiated.</summary>
    private GenericMethodSymbol BindGenericMethodDeclaration(TypeSymbol owner, MethodDeclarationSyntax syntax, Visibility visibility)
    {
        var heading = syntax.Heading;
        var identifier = heading.Name is IdentifierNameSyntax { Identifier: var token } ? token : heading.Name.FirstToken();
        var names = heading.TypeParameterList!.Parameters.SelectMany(p => p.Identifiers).Where(i => !i.Identifier.IsMissing)
            .Select(i => NameOf(i.Identifier)).ToImmutableArray();
        return new GenericMethodSymbol(NameOf(identifier), owner, syntax, visibility, names, _scope, _container, [DiagnosticBag.LocationOf(identifier)])
        {
            Constraints = BindConstraints(heading.TypeParameterList),
        };
    }

    /// <summary><c>function TUtil.Max&lt;T&gt;(…)</c> (LANG-704): the implementation of the generic method of that name and arity.</summary>
    private void DefineGenericMethod(TypeSymbol type, RoutineDeclarationSyntax declaration, SyntaxToken identifier, TypeParameterListSyntax typeParameters)
    {
        var name = NameOf(identifier);
        var names = typeParameters.Parameters.SelectMany(p => p.Identifiers).Select(i => NameOf(i.Identifier)).ToImmutableArray();
        // C-226: overloads of the name and arity are told apart by the written parameter list (type parameters by position)
        var fitting = TypeMembers.SelfAndAncestors(type).Take(1).SelectMany(t => (IEnumerable<GenericMethodSymbol>)GenericMethodsOf(t))
            .Where(g => IdentifierComparer.Instance.Equals(g.Name, name) && g.ParameterNames.Length == names.Length).ToList();
        var key = HeadingKey(declaration.Heading, names);
        var generic = fitting.Count <= 1 ? fitting.FirstOrDefault()
            : fitting.FirstOrDefault(g => g.Implementation is null && HeadingKey(g.Declaration.Heading, g.ParameterNames) == key)
              ?? fitting.FirstOrDefault(g => HeadingKey(g.Declaration.Heading, g.ParameterNames) == key) ?? fitting[0];
        if (generic is null)
        {
            _diagnostics.Report(identifier, "E2606", name, type.Name);
            return;
        }

        lock (_types.GenericGate)
        {
            if (generic.Implementation is not null)
            {
                _diagnostics.ReportRedeclared(identifier, name, generic);
                return;
            }

            generic.Implementation = new GenericMethodImplementation(declaration, identifier, names, _scope, _scope.Count, _container);
            foreach (var pair in generic.Instances)
            {
                DefineGenericMethodInstance(pair.Value, generic.Implementation, pair.Key);
            }
        }
    }

    /// <summary>
    /// C-226: the parameter list of a heading as text — modes, one type per parameter (whitespace dropped, ASCII case folded, the
    /// method's type parameters replaced by their position) and the result type, so a declaration and its implementation compare equal.
    /// </summary>
    private static string HeadingKey(RoutineHeadingSyntax heading, ImmutableArray<string> typeParameters)
    {
        string Text(SyntaxNode? node) => node is null ? "" : string.Join(" ", node.DescendantTokens().Select(t =>
        {
            var text = t.Value as string ?? t.Text;
            var index = typeParameters.IndexOf(text, IdentifierComparer.Instance);
            return index >= 0 ? "$" + index : text.ToUpperInvariant();
        }));

        var parts = new List<string>();
        foreach (var parameter in heading.ParameterList?.Parameters ?? default)
        {
            foreach (var _ in parameter.Identifiers)
            {
                parts.Add((parameter.Modifier?.Text.ToUpperInvariant() ?? "") + ":" + Text(parameter.Type));
            }
        }

        return string.Join(";", parts) + "->" + Text(heading.ResultType);
    }

    internal static ImmutableArray<GenericMethodSymbol> GenericMethodsOf(TypeSymbol type) => type switch
    {
        ClassTypeSymbol c => c.GenericMethods,
        RecordTypeSymbol r => r.GenericMethods,
        _ => [],
    };

    /// <summary>
    /// LANG-701/711 (C-117): the method instance of <paramref name="generic"/> for <paramref name="arguments"/> — its heading bound with the
    /// type parameters declared as the arguments, once per argument list; the using unit records it (CMP-385). Null after an error.
    /// </summary>
    internal MethodSymbol? InstantiateMethod(GenericMethodSymbol generic, ImmutableArray<TypeSymbol> arguments, SyntaxNode at)
    {
        var written = at is MemberAccessExpressionSyntax { Name: GenericNameSyntax name } ? name.TypeArgumentList.Arguments : (SeparatedSyntaxList<TypeSyntax>?)null;
        if (!CheckConstraints(generic.ParameterNames, generic.Constraints, arguments, written, at is MemberAccessExpressionSyntax access ? access.Name : at))
        {
            return null;
        }

        var unit = GenericUses.Current ?? GenericUses.UnitOf(_scope);
        lock (_types.GenericGate)
        {
            if (!generic.Instances.TryGetValue(arguments, out var instance))
            {
                var scope = new Scope(ScopeKind.Unit, generic.DeclaringScope, generic.DeclaringContainer) { DeclaresTypeParameters = true };
                for (var i = 0; i < arguments.Length; i++)
                {
                    scope.TryDeclare(generic.ParameterNames[i], arguments[i], out _);
                }

                var binder = new DeclarationBinder(_types, scope, generic.DeclaringContainer, _diagnostics) { _genericMethodInstance = true };
                using (GenericUses.For(unit))
                {
                    instance = generic.Owner switch
                    {
                        ClassTypeSymbol c => binder.BindMethodDeclaration(c, generic.Declaration, generic.Visibility),
                        RecordTypeSymbol r => binder.BindRecordMethodDeclaration(r, generic.Declaration, generic.Visibility),
                        _ => null,
                    };
                }

                if (instance is null)
                {
                    return null;
                }

                if (instance.IsVirtual || instance.IsOverride || instance.IsAbstract)
                {
                    _diagnostics.Report(at, "E2900", "virtual generic method");
                    return null;
                }

                instance.GenericMethodDefinition = generic;
                instance.GenericSuffix = "<" + string.Join(",", arguments.Select(Emit.Mangler.TypeName)) + ">";
                generic.Instances[arguments] = instance;
                if (generic.Implementation is { } implementation)
                {
                    DefineGenericMethodInstance(instance, implementation, arguments);
                }
            }

            GenericUses.Note(instance, unit, at);
            return instance;
        }
    }

    private static void DefineGenericMethodInstance(MethodSymbol instance, GenericMethodImplementation implementation, ImmutableArray<TypeSymbol> arguments)
    {
        var scope = new Scope(ScopeKind.Unit, implementation.Scope, implementation.Container, implementation.VisibleCount) { DeclaresTypeParameters = true };
        for (var i = 0; i < implementation.ParameterNames.Length && i < arguments.Length; i++)
        {
            scope.TryDeclare(implementation.ParameterNames[i], arguments[i], out _);
        }

        instance.Definition = implementation.Declaration;
        instance.DefinitionScope = scope;
        instance.DefinitionScopeVisibleCount = scope.Count;
        instance.AddLocation(DiagnosticBag.LocationOf(implementation.Identifier));
    }

    /// <summary>C-119: the constraints of a generic re-parsed from a <c>.pcu</c>, bound in its unit's interface scope.</summary>
    internal static ImmutableArray<TypeParameterConstraints> ImportConstraints(BuiltInTypes types, Scope scope, Symbol unit, TypeParameterListSyntax typeParameters) =>
        new DeclarationBinder(types, scope, unit, new DiagnosticBag()).BindConstraints(typeParameters);

    /// <summary>C-119: the method implementations of a generic re-parsed from a <c>.pcu</c> (bound in its support scope).</summary>
    internal static void ImportGenericImplementations(BuiltInTypes types, Scope scope, Symbol unit, IEnumerable<RoutineDeclarationSyntax> implementations)
    {
        var binder = new DeclarationBinder(types, scope, unit, new DiagnosticBag());
        foreach (var implementation in implementations)
        {
            binder.BindMethodImplementation(implementation);
        }
    }

    /// <summary>C-119: an instance a <c>.pcu</c> interface names (made, or found, as for a use in source).</summary>
    internal static TypeSymbol ImportInstance(BuiltInTypes types, Scope scope, Symbol unit, GenericTypeSymbol generic, ImmutableArray<TypeSymbol> arguments) =>
        generic.Declaration is null
            ? throw new Units.PcuFormatException($"generic '{generic.Name}' has no body")
            : new DeclarationBinder(types, scope, unit, new DiagnosticBag()).Instantiate(generic, arguments, generic.Declaration);

    /// <summary>A type argument written in an expression (<c>TUtil.Max&lt;Integer&gt;</c>), bound in this binder's scope.</summary>
    internal TypeSymbol BindTypeArgument(TypeSyntax syntax) => BindType(syntax);

    /// <summary>
    /// The CMP-361 structural name of an anonymous type: its source text, where a type parameter of an enclosing generic instance is
    /// replaced by its argument's name (CMP-362 RTTI blocks are shared by name: <c>array of T</c> must differ per instance).
    /// </summary>
    private string AnonymousName(TypeSyntax type)
    {
        var text = type.ToString().Trim();
        var substituted = new System.Text.StringBuilder();
        var changed = false;
        foreach (var token in type.DescendantTokens())
        {
            var part = token.Text;
            if (token.Kind == SyntaxKind.IdentifierToken && _scope.DeclaringScope(NameOf(token)) is { DeclaresTypeParameters: true } declaring
                && declaring.LookupLocal(NameOf(token)) is TypeSymbol argument)
            {
                part = argument.Name;
                changed = true;
            }

            substituted.Append(part);
            if (!token.TrailingTrivia.IsEmpty)
            {
                substituted.Append(' ');
            }
        }

        return changed ? substituted.ToString().Trim() : text;
    }

    /// <summary>The name for the type <see cref="BindType"/> is binding: a pending instance name (used once), else the declared one.</summary>
    private string? TakeInstanceName()
    {
        var name = _instanceName;
        _instanceName = null;
        return name;
    }
}

/// <summary>
/// CMP-385 bookkeeping: which unit uses which generic instance. A unit is the one whose scope chain a binder works in, except while
/// an instance is made or an instance's method bodies are bound for a unit (<see cref="For"/>): then everything they use counts for
/// that unit, so it emits the whole closure.
/// </summary>
internal static class GenericUses
{
    [ThreadStatic]
    private static UnitSymbol? t_current;

    public static UnitSymbol? Current => t_current;

    public static UnitSymbol? UnitOf(Scope scope)
    {
        for (var s = scope; s != null; s = s.Parent)
        {
            if (s.Kind == ScopeKind.Unit && s.Owner is UnitSymbol unit)
            {
                return unit;
            }
        }

        return null;
    }

    /// <summary>
    /// Records <paramref name="type"/> (when it is a generic instance) as used by <paramref name="unit"/>, and <paramref name="at"/> as
    /// its first instantiation site in that unit (C-222).
    /// </summary>
    public static void Note(Symbol? type, UnitSymbol? unit, SyntaxNode? at = null)
    {
        if (type is not (TypeSymbol { IsGenericInstance: true } or MethodSymbol { GenericMethodDefinition: not null }) || unit is null
            || !ArgumentsVisibleIn(type, unit))
        {
            return;
        }

        lock (unit.UsedInstances)
        {
            if (!unit.UsedInstances.Contains(type))
            {
                unit.UsedInstances.Add(type);
            }

            if (at is not null && !unit.InstanceSites.ContainsKey(type))
            {
                unit.InstanceSites[type] = DiagnosticBag.LocationOf(at);
            }
        }
    }

    /// <summary>
    /// C-228 (codegen.md decision): an instance is used (and so defined) only by units in which every type argument is visible — a
    /// type of the program or of a unit's implementation part is visible in that compilation unit only. Another unit reaching the
    /// instance (e.g. through a generic's signature bound for an instance made elsewhere) does not record it.
    /// </summary>
    private static bool ArgumentsVisibleIn(Symbol instance, UnitSymbol unit)
    {
        IEnumerable<TypeSymbol> ArgumentsOf(Symbol symbol) => symbol switch
        {
            MethodSymbol { GenericMethodDefinition: { } generic } method =>
                generic.Instances.Where(p => ReferenceEquals(p.Value, method)).SelectMany(p => p.Key).Concat(ArgumentsOf(method.Owner)),
            TypeSymbol type => type.TypeArguments.Concat(type.ContainingSymbol is TypeSymbol outer ? ArgumentsOf(outer) : []),
            _ => [],
        };

        bool Visible(TypeSymbol type)
        {
            var outermost = TypeMembers.Outermost(type);
            if (outermost.IsGenericInstance)
            {
                return ArgumentsOf(outermost).All(Visible);                     // an instance is as visible as its arguments
            }

            // a type declared in another program or unit implementation part (anonymous and built-in types are visible)
            return outermost.ContainingSymbol is not UnitSymbol owner || ReferenceEquals(owner, unit) || owner.InterfaceMembers.Contains(outermost)
                || !owner.ImplementationMembers.Contains(outermost);
        }

        return ArgumentsOf(instance).All(Visible);
    }

    /// <summary>The first instantiation site of <paramref name="instance"/> in <paramref name="unit"/> (C-222), if known.</summary>
    public static Location? SiteOf(Symbol instance, UnitSymbol unit)
    {
        lock (unit.UsedInstances)
        {
            for (var symbol = (Symbol?)instance; symbol is not null; symbol = symbol.ContainingSymbol)    // a nested type: its outermost
            {
                if (unit.InstanceSites.TryGetValue(symbol, out var site))
                {
                    return site;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The generic instance a routine belongs to (its own generic method instance, or the generic instance type that contains it),
    /// or null (C-222).
    /// </summary>
    public static Symbol? InstanceOf(Symbol routine)
    {
        for (var symbol = (Symbol?)routine; symbol is not null; symbol = symbol.ContainingSymbol)
        {
            switch (symbol)
            {
                case MethodSymbol { GenericMethodDefinition: not null } method:
                    return method;
                case TypeSymbol { IsGenericInstance: true } type:
                    return type;
            }
        }

        return null;
    }

    public static Restore For(UnitSymbol? unit)
    {
        var previous = t_current;
        t_current = unit ?? previous;
        return new Restore(previous);
    }

    internal readonly struct Restore(UnitSymbol? previous) : IDisposable
    {
        public void Dispose() => t_current = previous;
    }
}