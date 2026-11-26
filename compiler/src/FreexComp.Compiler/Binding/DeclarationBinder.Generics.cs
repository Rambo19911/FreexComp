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
    /// <c>TList&lt;T&gt; = …</c> (LANG-700) at unit level or in a type section of a class or record (LANG-778): a
    /// <see cref="GenericTypeSymbol"/> declared under its name and arity (LANG-703). Nothing of the body is bound until it is
    /// instantiated (LANG-711). Inside a routine (at any depth) E2705 and the declaration is skipped (LANG-779).
    /// </summary>
    private void BindGenericDeclaration(TypeDeclarationSyntax declaration, TypeParameterListSyntax typeParameters, ImmutableArray<Symbol>.Builder declared)
    {
        var identifier = declaration.Identifier;
        if (!IsTypeLevel || IsInRoutine)
        {
            _diagnostics.Report(typeParameters, "E2705");                       // LANG-779 (C-345)
            return;
        }

        var inInstance = IsInGenericInstance;
        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var parameter in typeParameters.Parameters.SelectMany(p => p.Identifiers).Where(i => !i.Identifier.IsMissing))
        {
            var name = NameOf(parameter.Identifier);
            if (names.Contains(name, IdentifierComparer.Instance))
            {
                _diagnostics.Report(parameter, "E2004", name);
                return;
            }

            if (inInstance && EnclosingTypeParameter(name))
            {
                return;                                                         // LANG-778: E2004 reported with the enclosing generic
            }

            names.Add(name);
        }

        if (!inInstance)
        {
            ReportNestedTypeParameterClashes(declaration, names.ToImmutable());
        }

        var isForward = declaration.TypeKeyword is null
            && declaration.Type is ClassTypeSyntax { EndKeyword: null, Heritage: null, Modifier: null } or InterfaceTypeSyntax { EndKeyword: null, Heritage: null };
        var key = GenericTypeSymbol.KeyOf(NameOf(identifier), names.Count);
        if (!isForward && (_sharedScope?.LookupLocal(key) ?? _scope.LookupLocal(key)) is GenericTypeSymbol { IsForward: true } pending
            && _forwardGenerics?.FindIndex(f => ReferenceEquals(f.Generic, pending)) is >= 0 and var index)
        {
            _forwardGenerics.RemoveAt(index);
            if (pending.Declaration!.Type.Kind != declaration.Type.Kind || declaration.TypeKeyword is not null)
            {
                _diagnostics.Report(identifier, "E2001", NameOf(identifier));     // LANG-782 (C-346): completed by another kind
                return;
            }

            CompleteForwardGeneric(pending, declaration, typeParameters, names.ToImmutable(), identifier);
            return;
        }

        var generic = new GenericTypeSymbol(NameOf(identifier), names.ToImmutable(), declaration, _scope, 0, _container, [DiagnosticBag.LocationOf(identifier)])
        {
            IsForward = isForward,
        };
        var previous = _sharedScope?.LookupLocal(generic.ScopeName);
        if (previous is not null || !_scope.TryDeclare(generic.ScopeName, generic, out previous))
        {
            _diagnostics.ReportRedeclared(identifier, generic.Name, previous!);
            return;
        }

        generic.VisibleCount = _scope.Count;                                    // the declaration sees itself and what precedes it
        generic.Constraints = BindConstraints(typeParameters);
        declared.Add(generic);
        if (isForward)
        {
            _forwardGenerics?.Add((generic, identifier));                       // LANG-782 (C-346)
            return;
        }

        CheckGenericAncestor(declaration, names);
    }

    /// <summary>
    /// LANG-782 (C-346): the completion of a forward generic — the same type parameter names in the same order with the same
    /// constraints (E2706 otherwise, at the completion's list). It becomes the generic's declaration, seeing everything before it,
    /// and the instances made since the forward get their bodies now.
    /// </summary>
    private void CompleteForwardGeneric(GenericTypeSymbol generic, TypeDeclarationSyntax declaration, TypeParameterListSyntax typeParameters,
        ImmutableArray<string> names, SyntaxToken identifier)
    {
        var constraints = BindConstraints(typeParameters);
        if (!names.SequenceEqual(generic.ParameterNames, IdentifierComparer.Instance)
            || !constraints.Zip(generic.Constraints).All(p => SameConstraints(p.First, p.Second)))
        {
            _diagnostics.Report(typeParameters, "E2706", GenericDisplayName(generic));
        }

        generic.Declaration = declaration;
        generic.Constraints = constraints;
        generic.VisibleCount = _scope.Count;
        generic.IsForward = false;
        generic.AddLocation(DiagnosticBag.LocationOf(identifier));
        CheckGenericAncestor(declaration, names);
        var pending = generic.PendingInstances.ToList();
        generic.PendingInstances.Clear();
        foreach (var (instance, arguments, site, unit) in pending)
        {
            lock (_types.GenericGate)
            {
                var bag = new DiagnosticBag();
                using (GenericUses.For(unit))
                {
                    new DeclarationBinder(_types, _scope, _container, bag).CreateInstance(generic, arguments, instance);
                }

                var at = DiagnosticBag.LocationOf(site);
                foreach (var diagnostic in bag.ToImmutable())
                {
                    if (DiagnosticBag.InInstance(diagnostic, instance.Name, at, _types.ReportedInstanceWarnings) is { } wrapped)
                    {
                        _diagnostics.Add(wrapped);
                    }
                }
            }
        }
    }

    private static bool SameConstraints(TypeParameterConstraints a, TypeParameterConstraints b) =>
        a.Class == b.Class && a.Record == b.Record && a.Constructor == b.Constructor
        && a.Types.Length == b.Types.Length && a.Types.All(t => b.Types.Contains(t, ReferenceEqualityComparer.Instance));

    /// <summary><c>TNode&lt;T&gt;</c>: a generic's name with its type parameter list (E2015, E2706).</summary>
    private static string GenericDisplayName(GenericTypeSymbol generic) => generic.Name + "<" + string.Join(", ", generic.ParameterNames) + ">";

    private void CheckGenericAncestor(TypeDeclarationSyntax declaration, IReadOnlyCollection<string> names)
    {
        if (declaration.Type is ClassTypeSyntax { Heritage.Types: [NamedTypeSyntax { Name: IdentifierNameSyntax { Identifier: var parentToken } parentName }, ..] }
            && names.Contains(NameOf(parentToken), IdentifierComparer.Instance))
        {
            _diagnostics.Report(parentName, "E2702", NameOf(parentToken));       // LANG-717 (C-118)
        }
    }

    /// <summary>A scope of the chain lies in a routine (LANG-779: no generic types there, also not in a routine's nested record).</summary>
    private bool IsInRoutine
    {
        get
        {
            for (var scope = _scope; scope is not null; scope = scope.Parent)
            {
                if (scope.Kind == ScopeKind.Routine)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The binder works below the type parameter scope of a generic instance.</summary>
    private bool IsInGenericInstance => EnclosingTypeParameterScopes().Any();

    private IEnumerable<Scope> EnclosingTypeParameterScopes()
    {
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (scope.DeclaresTypeParameters)
            {
                yield return scope;
            }
        }
    }

    /// <summary>LANG-778: <paramref name="name"/> is a type parameter of an enclosing generic instance.</summary>
    private bool EnclosingTypeParameter(string name) => EnclosingTypeParameterScopes().Any(s => s.LookupLocal(name) is not null);

    /// <summary>
    /// LANG-778 (C-345): E2004 at every type parameter of a generic nested (at any depth) in <paramref name="declaration"/> whose name
    /// is a type parameter name of an enclosing generic — checked once with the definition (the nested generics of a generic are
    /// bound per instance only).
    /// </summary>
    private void ReportNestedTypeParameterClashes(TypeDeclarationSyntax declaration, ImmutableArray<string> names)
    {
        foreach (var nested in declaration.Type.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(d => d.TypeParameterList is not null))
        {
            var outer = new List<string>(names);
            for (var node = nested.Parent; node is not null && !ReferenceEquals(node, declaration); node = node.Parent)
            {
                if (node is TypeDeclarationSyntax { TypeParameterList: { } list })
                {
                    outer.AddRange(list.Parameters.SelectMany(p => p.Identifiers).Select(i => NameOf(i.Identifier)));
                }
            }

            foreach (var parameter in nested.TypeParameterList!.Parameters.SelectMany(p => p.Identifiers).Where(i => !i.Identifier.IsMissing))
            {
                if (outer.Contains(NameOf(parameter.Identifier), IdentifierComparer.Instance))
                {
                    _diagnostics.Report(parameter, "E2004", NameOf(parameter.Identifier));
                }
            }
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
            if (argument is TypeParameterTypeSymbol)
            {
                continue;                                                       // C-355: an open instance's type parameter
            }

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
    /// arguments. E2703 when only another arity (or a non-generic type) has the name, E2003 when nothing has. The generic is looked
    /// up in <paramref name="scope"/> (a unit qualifier's scope, LANG-780; default this binder's scope) or among the nested types of
    /// <paramref name="outer"/> (<c>TOuter.TBox&lt;X&gt;</c>, <c>TA&lt;X&gt;.TB&lt;Y&gt;</c>, LANG-778: E2005 when it has none of the
    /// name); the type arguments are bound in this binder's scope.
    /// </summary>
    internal TypeSymbol BindGenericName(GenericNameSyntax name, Scope? scope = null, TypeSymbol? outer = null)
    {
        if (name.Identifier.IsMissing || name.ContainsDiagnostics)
        {
            return _types.Error;
        }

        var text = NameOf(name.Identifier);
        var arguments = name.TypeArgumentList.Arguments;
        GenericTypeSymbol? generic;
        if (outer is not null)
        {
            var all = TypeMembers.MemberNestedGenerics(outer, text).ToList();             // C-354: inherited too
            generic = all.FirstOrDefault(g => g.ParameterNames.Length == arguments.Count);
            if (generic is null)
            {
                if (all.Count > 0 || TypeMembers.MemberNestedType(outer, text) is not null)
                {
                    _diagnostics.Report(name, "E2703", text, all.Count > 0 ? all.Min(g => g.ParameterNames.Length) : 0);
                }
                else
                {
                    _diagnostics.Report(name, "E2005", TypeMembers.DisplayName(outer), text);
                }

                return _types.Error;
            }

            if (NestedNames.InaccessibleNestedType(_scope, generic) is { } visibility)
            {
                _diagnostics.Report(name, "E2610", visibility, generic.Name, TypeMembers.DisplayName(TypeMembers.EnclosingTypes(generic).FirstOrDefault() ?? outer));   // LANG-774 (C-354: the declaring type)
                return _types.Error;
            }
        }
        else
        {
            scope ??= _scope;
            generic = scope.Lookup(GenericTypeSymbol.KeyOf(text, arguments.Count)) as GenericTypeSymbol;
            if (generic is null)
            {
                var other = Enumerable.Range(0, 16).Select(n => scope.Lookup(GenericTypeSymbol.KeyOf(text, n))).OfType<GenericTypeSymbol>().FirstOrDefault();
                if (other is not null || scope.Lookup(text) is TypeSymbol)
                {
                    _diagnostics.Report(name, "E2703", text, other?.ParameterNames.Length ?? 0);
                }
                else
                {
                    _diagnostics.ReportUndeclared(name, text);
                }

                return _types.Error;
            }

            if (_types.HasTypeHints && scope.ImportedTypeHints(generic.ScopeName) is { } hints)
            {
                _diagnostics.ReportHints(name, generic.Name, hints);                                   // LANG-472 (C-232)
            }
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

    /// <summary>C-355: this thread binds for the language service only (<see cref="BindOpen"/>).</summary>
    [ThreadStatic]
    private static bool t_open;

    /// <summary>
    /// C-355: while the result is not disposed, this thread binds an open instance or its method bodies: the instances it makes go to
    /// <see cref="GenericTypeSymbol.OpenInstances"/> (generic method instances to <see cref="GenericMethodSymbol.OpenInstances"/>, C-358), no unit
    /// notes a use and no forward generic records a pending instance; nothing of it reaches code generation, a <c>.pcu</c>, debug information or a diagnostic.
    /// </summary>
    internal static OpenBinding BindOpen()
    {
        var previous = t_open;
        t_open = true;
        return new OpenBinding(previous, GenericUses.Suppress());
    }

    internal readonly struct OpenBinding(bool previous, GenericUses.Suppression suppression) : IDisposable
    {
        public void Dispose()
        {
            suppression.Dispose();
            t_open = previous;
        }
    }

    /// <summary>
    /// C-355: the open instance of <paramref name="generic"/>: its declaration bound once below the declaring scope with each type
    /// parameter declared as a <see cref="TypeParameterTypeSymbol"/> of its name (<see cref="BindOpen"/>; its diagnostics are dropped), with
    /// the generic's method implementations defined on it. Null when the generic has no declaration text or is a forward declaration.
    /// </summary>
    internal static TypeSymbol? OpenInstanceOf(BuiltInTypes types, GenericTypeSymbol generic)
    {
        lock (types.GenericGate)
        {
            if (!generic.OpenInstanceAttempted)
            {
                generic.OpenInstanceAttempted = true;
                if (generic is { Declaration: not null, DeclaringScope: { } scope, IsForward: false, ContainingSymbol: { } container })
                {
                    // C-358: each carries its constraints, for the members code insight offers
                    ImmutableArray<TypeSymbol> arguments = [.. generic.ParameterNames.Select((n, i) =>
                        new TypeParameterTypeSymbol(n, types.Target, i < generic.Constraints.Length ? generic.Constraints[i] : null))];
                    using (BindOpen())
                    {
                        try
                        {
                            var instance = new DeclarationBinder(types, scope, container, new DiagnosticBag()).CreateInstance(generic, arguments);
                            generic.OpenInstance = instance.TypeKind == TypeKind.Error ? null : instance;
                        }
                        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
                        {
                            generic.OpenInstance = null;                        // CMP-202: code insight never fails on a type parameter
                        }
                    }
                }
            }

            return generic.OpenInstance;
        }
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
            if (!generic.Instances.TryGetValue(arguments, out var instance) && !(t_open && generic.OpenInstances.TryGetValue(arguments, out instance)))
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

                    if (t_open)
                    {
                        return instance;                                                  // C-355: nothing of it reaches the compilation
                    }

                    if (generic.IsForward)
                    {
                        generic.PendingInstances.Add((instance, arguments, at, unit));     // LANG-783 (C-346): its body comes with the completion
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

    /// <param name="shell">C-346: the incomplete instance made while the generic was a forward declaration, completed now.</param>
    private TypeSymbol CreateInstance(GenericTypeSymbol generic, ImmutableArray<TypeSymbol> arguments, TypeSymbol? shell = null)
    {
        var declaration = generic.Declaration!;
        var display = generic.Name + "<" + string.Join(", ", arguments.Select(a => a.Name)) + ">";
        var canonical = (generic.ContainingSymbol is { } owner ? Emit.Mangler.Qualified(owner) + "." : "") + generic.Name
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

            (t_open ? generic.OpenInstances : generic.Instances)[arguments] = type;     // C-355: an open binding's instances stay apart
        }

        TypeSymbol instance;
        switch (declaration.Type)
        {
            case ClassTypeSyntax when generic.IsForward:
                instance = new ClassTypeSymbol(_types.Target, display, generic.ContainingSymbol, generic.Locations);     // LANG-783: incomplete
                Register(instance);
                return instance;
            case InterfaceTypeSyntax when generic.IsForward:
                instance = new InterfaceTypeSymbol(_types.Target, display, generic.ContainingSymbol, generic.Locations);
                Register(instance);
                return instance;
            case ClassTypeSyntax { EndKeyword: not null } classSyntax when declaration.TypeKeyword is null:
            {
                var type = shell as ClassTypeSymbol ?? new ClassTypeSymbol(_types.Target, display, generic.ContainingSymbol, generic.Locations);
                Register(type);                                                 // before the body: members may name the instance
                binder.BindClassBody(type, classSyntax);
                instance = type;
                break;
            }

            case InterfaceTypeSyntax { EndKeyword: not null } interfaceSyntax when declaration.TypeKeyword is null:
            {
                // C-222: a generic interface (LANG-700); every instance has the declared GUID, if any
                var type = shell as InterfaceTypeSymbol ?? new InterfaceTypeSymbol(_types.Target, display, generic.ContainingSymbol, generic.Locations);
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
    /// Diagnostics of the matching are reported for the first instance only. With <paramref name="outer"/> the generic is one
    /// nested in that type of this unit (<c>TOuter.TBox&lt;T&gt;.Run</c>, LANG-778); <paramref name="nestedPath"/> are the segments
    /// after the generic (<c>TList&lt;T&gt;.TEnumerator.MoveNext</c>, LANG-776; generic ones too, <c>TA&lt;T&gt;.TB&lt;U&gt;.M</c>).
    /// </summary>
    private void BindGenericMethodImplementation(RoutineDeclarationSyntax declaration, GenericNameSyntax qualifier, SyntaxToken identifier,
        ImmutableArray<SimpleNameSyntax> nestedPath, TypeSymbol? outer = null)
    {
        var text = NameOf(qualifier.Identifier);
        var arguments = qualifier.TypeArgumentList.Arguments;
        var generic = outer is null ? _scope.Lookup(GenericTypeSymbol.KeyOf(text, arguments.Count)) as GenericTypeSymbol
            : TypeMembers.NestedGenerics(outer, text).FirstOrDefault(g => g.ParameterNames.Length == arguments.Count);
        if (generic is null)
        {
            if (outer is null)
            {
                _diagnostics.ReportUndeclared(qualifier, text);
            }
            else
            {
                _diagnostics.Report(qualifier, "E2606", text, TypeMembers.DisplayName(outer));    // LANG-776
            }

            return;
        }

        if (!ReferenceEquals(outer is null ? generic.ContainingSymbol : TypeMembers.Outermost(outer).ContainingSymbol, _container))
        {
            _diagnostics.Report(identifier, "E2606", NameOf(identifier), generic.Name);
            return;
        }

        var implementation = new GenericMethodImplementation(declaration, identifier, ParameterNamesOf(qualifier), _scope, _scope.Count, _container)
        {
            NestedPath = nestedPath,
        };
        AddGenericImplementation(generic, implementation, report: true);
    }

    /// <summary>The type parameter names a heading's generic segment repeats (LANG-704).</summary>
    private static ImmutableArray<string> ParameterNamesOf(GenericNameSyntax segment) =>
        [.. segment.TypeArgumentList.Arguments.Select(a => a is NamedTypeSyntax { Name: IdentifierNameSyntax { Identifier: var token } } ? NameOf(token) : "")];

    private void AddGenericImplementation(GenericTypeSymbol generic, GenericMethodImplementation implementation, bool report)
    {
        lock (_types.GenericGate)
        {
            generic.Implementations.Add(implementation);
            var first = report;
            foreach (var instance in generic.Instances.Values.Concat(t_open ? generic.OpenInstances.Values : []).Where(i => i.TypeKind != TypeKind.Error).ToList())
            {
                // C-345 (C-228): an instance made earlier belongs to the units that use it ā€” they note what its headings name
                // (Binder.BindInstances); defining the method here does not make the declaring unit a user
                using (GenericUses.Suppress())
                {
                    DefineInstanceMethod(instance, implementation, first);
                }

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
        for (var k = 0; k < implementation.NestedPath.Length; k++)
        {
            var segment = implementation.NestedPath[k];
            var text = NameOf(segment.Identifier);
            if (segment is GenericNameSyntax nestedGeneric)
            {
                // LANG-778 (C-345): TA<T>.TB<U>.M — this instance's nested generic gets the rest of the path, bound in this scope
                if (TypeMembers.NestedGenerics(owner, text).FirstOrDefault(g => g.ParameterNames.Length == nestedGeneric.TypeArgumentList.Arguments.Count)
                    is not { } inner || !ReferenceEquals(inner.ContainingSymbol, owner))
                {
                    diagnostics.Report(segment, "E2606", text, TypeMembers.DisplayName(owner));
                    return;
                }

                var rest = new GenericMethodImplementation(implementation.Declaration, implementation.Identifier, ParameterNamesOf(nestedGeneric), scope,
                    scope.Count, implementation.Container)
                {
                    NestedPath = implementation.NestedPath[(k + 1)..],
                };
                new DeclarationBinder(_types, scope, implementation.Container, diagnostics).AddGenericImplementation(inner, rest, report);
                return;
            }

            // LANG-776 (C-344): the instance's own nested type (each instance has its nested types, LANG-775)
            if (TypeMembers.NestedType(owner, text) is not ({ } nested and (ClassTypeSymbol or RecordTypeSymbol))
                || !ReferenceEquals(nested.ContainingSymbol, owner))
            {
                diagnostics.Report(segment, "E2606", text, TypeMembers.DisplayName(owner));
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
            if (!generic.Instances.TryGetValue(arguments, out var instance) && !(t_open && generic.OpenInstances.TryGetValue(arguments, out instance)))
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
                if (!t_open)
                {
                    instance.GenericSuffix = "<" + string.Join(",", arguments.Select(Emit.Mangler.TypeName)) + ">";
                }

                (t_open ? generic.OpenInstances : generic.Instances)[arguments] = instance;   // C-358: an open binding's, for navigation only
                if (generic.Implementation is { } implementation)
                {
                    DefineGenericMethodInstance(instance, implementation, arguments);
                }
            }

            if (!t_open)
            {
                GenericUses.Note(instance, unit, at);
            }

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
        if (t_suppressed || type is not (TypeSymbol { IsGenericInstance: true } or MethodSymbol { GenericMethodDefinition: not null }) || unit is null
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

    [ThreadStatic]
    private static bool t_suppressed;

    /// <summary>C-345 (C-228): while the result is not disposed, nothing is noted as used on this thread.</summary>
    public static Suppression Suppress()
    {
        var previous = t_suppressed;
        t_suppressed = true;
        return new Suppression(previous);
    }

    internal readonly struct Suppression(bool previous) : IDisposable
    {
        public void Dispose() => t_suppressed = previous;
    }
}