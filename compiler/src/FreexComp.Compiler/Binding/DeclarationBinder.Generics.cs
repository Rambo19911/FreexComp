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
    /// LANG-702 (C-118), LANG-982 (C-421): the constraints of each type parameter, bound in this scope. <c>record</c> with
    /// <c>class</c>, with <c>constructor</c>, with a class type or with an interface type, and two class types, are E2701 — one per
    /// item that cannot be combined with an earlier one, at that item (C-422: not one per conflicting pair). A constraint type that
    /// is no class or interface type, or that names a type parameter (of this list or of an enclosing generic), is E2603. A keyword
    /// or type written twice has no further effect.
    /// </summary>
    private ImmutableArray<TypeParameterConstraints> BindConstraints(TypeParameterListSyntax typeParameters)
    {
        var listed = typeParameters.Parameters.SelectMany(p => p.Identifiers).Where(i => !i.Identifier.IsMissing).Select(i => NameOf(i.Identifier)).ToList();
        var result = ImmutableArray.CreateBuilder<TypeParameterConstraints>();
        foreach (var parameter in typeParameters.Parameters)
        {
            bool isClass = false, isRecord = false, isConstructor = false;
            var types = ImmutableArray.CreateBuilder<TypeSymbol>();
            var texts = ImmutableArray.CreateBuilder<string>();
            var name = parameter.Identifiers.Count > 0 ? NameOf(parameter.Identifiers[0].Identifier) : "";
            foreach (var constraint in parameter.Constraints)
            {
                var conflicts = false;                                          // an earlier item this one cannot be combined with
                switch (constraint)
                {
                    case KeywordConstraintSyntax { Keyword.Kind: SyntaxKind.ClassKeyword }:
                        conflicts = isRecord && !isClass;
                        isClass = true;
                        break;
                    case KeywordConstraintSyntax { Keyword.Kind: SyntaxKind.RecordKeyword }:
                        conflicts = !isRecord && (isClass || isConstructor || types.Count > 0);
                        isRecord = true;
                        break;
                    case KeywordConstraintSyntax:
                        conflicts = isRecord && !isConstructor;
                        isConstructor = true;
                        break;
                    case TypeSyntax typeSyntax:
                        if (typeSyntax is NamedTypeSyntax { Name: IdentifierNameSyntax { Identifier: { IsMissing: false } token } }
                            && (listed.Contains(NameOf(token), IdentifierComparer.Instance) || _scope.DeclaringScope(NameOf(token)) is { DeclaresTypeParameters: true }))
                        {
                            _diagnostics.Report(typeSyntax, "E2603");           // a type parameter, whatever its argument is
                            continue;
                        }

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

                        if (types.Contains(type, ReferenceEqualityComparer.Instance))
                        {
                            continue;                                           // written twice
                        }

                        conflicts = isRecord || (type is ClassTypeSymbol && types.Any(t => t is ClassTypeSymbol));
                        types.Add(type);
                        texts.Add(type.Name);
                        break;
                }

                if (conflicts)
                {
                    _diagnostics.Report(constraint, "E2701", name);             // once per conflicting item, however many pairs it makes
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
    /// LANG-702 (C-118), LANG-983, 984 (C-421): E2704 at the argument (or <paramref name="at"/>) for every constraint a type argument
    /// misses — the arguments in order, for each <c>class</c>, <c>record</c>, <c>constructor</c>, then the constraint types as
    /// written; false when one did.
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

            if (c.Constructor && CreatedBy(argument) is null)
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
    /// LANG-983 (C-421): what satisfies the <c>constructor</c> constraint for <paramref name="argument"/> — the constructor the call
    /// <c>X.Create</c> without arguments resolves to (the methods of that name the class sees, LANG-441; the overload chosen for no
    /// arguments, LANG-442) when it is <c>public</c> or <c>published</c>: the call as code outside the class hierarchy writes it,
    /// which is what <c>T.Create</c> in the generic's body binds per instance. Null for a type that is no class, a <c>Create</c>
    /// that needs arguments or is ambiguous, one hidden by a field or property, one that is no constructor, a private or protected one.
    /// </summary>
    private MethodSymbol? CreatedBy(TypeSymbol argument)
    {
        if (argument is not ClassTypeSymbol type)
        {
            return null;
        }

        var resolved = new OverloadResolver(_conversions).Resolve(ExpressionBinder.MemberCandidates(type, "Create"), ImmutableArray<BoundExpression>.Empty, _ => false);
        return resolved.Routine is MethodSymbol { MethodKind: MethodKind.Constructor, Visibility: Visibility.Public or Visibility.Published } constructor
            ? constructor
            : null;
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

    /// <summary>C-417 (LANG-976): this thread binds an open instance or its method bodies — nothing is pruned there.</summary>
    internal static bool IsOpenBinding => t_open;

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
    /// CMP-729 (C-392): the open instance of the generic method <paramref name="generic"/> — its heading bound once with each type parameter
    /// declared as a <see cref="TypeParameterTypeSymbol"/> of its name (<see cref="BindOpen"/>; its diagnostics are dropped), for signature
    /// help before the arguments infer the type arguments. Null when the heading does not bind.
    /// </summary>
    internal static MethodSymbol? OpenInstanceOf(BuiltInTypes types, GenericMethodSymbol generic)
    {
        lock (types.GenericGate)
        {
            if (!generic.OpenInstanceAttempted)
            {
                generic.OpenInstanceAttempted = true;
                generic.OpenInstance = OpenInstanceOf(types, generic, [.. generic.ParameterNames.Select((n, i) =>
                    new TypeParameterTypeSymbol(n, types.Target, i < generic.Constraints.Length ? generic.Constraints[i] : null))]);
            }

            return generic.OpenInstance;
        }
    }

    /// <summary>
    /// CMP-729 (C-392): the instance of the generic method for <paramref name="arguments"/> as an open binding makes it (no use recorded,
    /// diagnostics dropped) — for signature help with written or inferred type arguments. Null when its heading does not bind.
    /// </summary>
    internal static MethodSymbol? OpenInstanceOf(BuiltInTypes types, GenericMethodSymbol generic, ImmutableArray<TypeSymbol> arguments)
    {
        lock (types.GenericGate)
        {
            using (BindOpen())
            {
                try
                {
                    return new DeclarationBinder(types, generic.DeclaringScope, generic.DeclaringContainer, new DiagnosticBag())
                        .InstantiateMethod(generic, arguments, generic.Declaration);
                }
                catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
                {
                    return null;                                                // CMP-202
                }
            }
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

    /// <summary>
    /// LANG-701 (C-117): a generic method of a class or record; nothing of it is bound until it is instantiated. LANG-978 (C-420):
    /// what a generic method cannot be is reported here, at its name, whether or not it is ever called — E2713 once, for the first
    /// row of the table that applies: a constructor, destructor or operator; a section the source marks <c>published</c> (the
    /// default section of a class with type information is silently <c>public</c>); the first of the directives <c>virtual</c>,
    /// <c>dynamic</c>, <c>override</c>, <c>abstract</c>, <c>message</c>. The declaration is kept without the offending property (it
    /// is <c>public</c>; its instances ignore those directives, <see cref="IsRestrictedDirective"/>), so calls of it bind.
    /// </summary>
    private GenericMethodSymbol BindGenericMethodDeclaration(TypeSymbol owner, MethodDeclarationSyntax syntax, Visibility visibility)
    {
        var heading = syntax.Heading;
        var identifier = heading.Name is IdentifierNameSyntax { Identifier: var token } ? token : heading.Name.FirstToken();
        var names = heading.TypeParameterList!.Parameters.SelectMany(p => p.Identifiers).Where(i => !i.Identifier.IsMissing)
            .Select(i => NameOf(i.Identifier)).ToImmutableArray();
        var kind = heading.Keyword.Kind switch
        {
            SyntaxKind.ConstructorKeyword => "a constructor",
            SyntaxKind.DestructorKeyword => "a destructor",
            _ => heading.Keyword.ContextualKind == SyntaxKind.OperatorKeyword ? "an operator" : null,
        };
        var phrase = kind
            ?? (visibility == Visibility.Published && syntax.Parent is VisibilitySectionSyntax ? "published" : null)
            ?? syntax.Directives.Select(d => RestrictedDirective(d.Keyword.ContextualKind)).FirstOrDefault(p => p is not null);
        if (phrase is not null && !identifier.IsMissing)
        {
            _diagnostics.Report(identifier, "E2713", NameOf(identifier), phrase);
        }
        else if (phrase is null && owner is RecordTypeSymbol && syntax.Directives.FirstOrDefault(d => IsRecordOnlyRestriction(d.Keyword.ContextualKind)) is { } directive)
        {
            // LANG-730 (C-431): as on a non-generic record method — at the directive; the instances ignore it (IsRestrictedDirective)
            _diagnostics.Report(directive.Keyword, "E2730", directive.Keyword.Text.ToLowerInvariant());
        }

        return new GenericMethodSymbol(NameOf(identifier), owner, syntax, visibility == Visibility.Published ? Visibility.Public : visibility, names, _scope,
            _container, [DiagnosticBag.LocationOf(identifier)])
        {
            Constraints = BindConstraints(heading.TypeParameterList),
            HasRestrictedKind = kind is not null,
        };
    }

    /// <summary>LANG-978: the phrase of E2713 for a directive a generic method cannot have; null for every other directive.</summary>
    private static string? RestrictedDirective(SyntaxKind directive) => directive switch
    {
        SyntaxKind.VirtualKeyword => "virtual",
        SyntaxKind.DynamicKeyword => "dynamic",
        SyntaxKind.OverrideKeyword => "an override",
        SyntaxKind.AbstractKeyword => "abstract",
        SyntaxKind.MessageKeyword => "a message handler",
        _ => null,
    };

    /// <summary>LANG-730: a directive a record method cannot have that the LANG-978 table does not list (E2730 at the declaration, C-431).</summary>
    private static bool IsRecordOnlyRestriction(SyntaxKind directive) => directive is SyntaxKind.FinalKeyword or SyntaxKind.ReintroduceKeyword;

    /// <summary>
    /// LANG-978 (C-420): the heading being bound is that of a generic method instance of <paramref name="owner"/> and
    /// <paramref name="directive"/> is one a generic method cannot have — reported at the declaration (E2713) and without effect on
    /// the instance; so are <c>final</c> and <c>reintroduce</c> in a record (E2730 at the declaration, C-431).
    /// </summary>
    private bool IsRestrictedDirective(DirectiveSyntax directive, TypeSymbol owner) =>
        _genericMethodInstance && (RestrictedDirective(directive.Keyword.ContextualKind) is not null
            || owner is RecordTypeSymbol && IsRecordOnlyRestriction(directive.Keyword.ContextualKind));

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
    /// LANG-978 "Overloads" (LANG-440; C-419): the methods of one name in one type, generic and non-generic together, are one overload
    /// set — each must carry <c>overload</c> (E2120 at the later declaration; both are kept, so calls give no second diagnostic). Two
    /// generic methods with the same number of type parameters and the same parameter list after the type parameters are replaced by
    /// their positions are one declaration (E2121; the later one is dropped). A generic and a non-generic method never clash.
    /// Non-generic methods among themselves are checked where they are declared (E2004). The result: the generic methods kept.
    /// </summary>
    private ImmutableArray<GenericMethodSymbol> CheckMixedOverloads(IEnumerable<MethodSymbol> methods, IEnumerable<GenericMethodSymbol> generics)
    {
        static string ParameterKey(GenericMethodSymbol generic)
        {
            var key = HeadingKey(generic.Declaration.Heading, generic.ParameterNames);
            return key[..key.LastIndexOf("->", StringComparison.Ordinal)];        // the result type alone does not distinguish (LANG-440)
        }

        var kept = ImmutableArray.CreateBuilder<GenericMethodSymbol>();
        var earlier = new List<(string Name, bool IsOverload, GenericMethodSymbol? Generic)>();
        var declarations = methods.Select(m => (m.MethodDeclaration.Heading, m.Name, m.IsOverload, Generic: (GenericMethodSymbol?)null))
            .Concat(generics.Select(g => (g.Declaration.Heading, g.Name, g.IsOverload, Generic: (GenericMethodSymbol?)g)))
            .OrderBy(d => d.Heading.Name.Span.Start);
        foreach (var (heading, name, isOverload, generic) in declarations)
        {
            var same = earlier.Where(e => IdentifierComparer.Instance.Equals(e.Name, name)).ToList();
            if (same.Count > 0 && (generic is not null || same.Any(e => e.Generic is not null)))
            {
                var identifier = heading.Name is IdentifierNameSyntax { Identifier: var token } ? token : heading.Name.FirstToken();
                if (!isOverload || same.Any(e => !e.IsOverload))
                {
                    _diagnostics.Report(identifier, "E2120", name);
                }
                else if (generic is not null && same.Any(e => e.Generic is { } other && other.ParameterNames.Length == generic.ParameterNames.Length
                             && ParameterKey(other) == ParameterKey(generic)))
                {
                    _diagnostics.Report(identifier, "E2121", name);
                    continue;
                }
            }

            earlier.Add((name, isOverload, generic));
            if (generic is not null)
            {
                kept.Add(generic);
            }
        }

        return kept.ToImmutable();
    }

    /// <summary>
    /// LANG-701/711 (C-117): the method instance of <paramref name="generic"/> for <paramref name="arguments"/> — its heading bound with the
    /// type parameters declared as the arguments, once per argument list; the using unit records it (CMP-385). Null after an error.
    /// </summary>
    internal MethodSymbol? InstantiateMethod(GenericMethodSymbol generic, ImmutableArray<TypeSymbol> arguments, SyntaxNode at) =>
        CandidateMethod(generic, arguments, at) is { } candidate ? UseMethod(candidate, at) : null;

    /// <summary>
    /// A generic method instance as a candidate of a call (<see cref="CandidateMethod"/>), with the generic uses the binding of its
    /// heading made (the instances its parameters name): they are noted only when the candidate is chosen (<see cref="UseMethod"/>).
    /// </summary>
    internal sealed record MethodCandidate(MethodSymbol Instance, List<GenericUses.Use> Uses);

    /// <summary>
    /// LANG-980 rule 6 (C-419): the instance of <paramref name="generic"/> for <paramref name="arguments"/> as a candidate of a call —
    /// the instance already made for that argument list, else its heading bound with the type parameters declared as the arguments.
    /// Nothing records a new one: it is not in the generic method's instance list, no unit notes a use and no body is bound, until
    /// <see cref="UseMethod"/> makes it the instance because the call chose it. Null after an error (a constraint, E2704; the
    /// heading), reported to this binder's diagnostics.
    /// </summary>
    internal MethodCandidate? CandidateMethod(GenericMethodSymbol generic, ImmutableArray<TypeSymbol> arguments, SyntaxNode at)
    {
        // LANG-984 (C-421): at the written type argument (TUtil.Keep<Integer>; Keep<Integer> inside the type), else at the method name
        var methodName = at is MemberAccessExpressionSyntax access ? access.Name : at;
        var written = methodName is GenericNameSyntax name ? name.TypeArgumentList.Arguments : (SeparatedSyntaxList<TypeSyntax>?)null;
        if (!CheckConstraints(generic.ParameterNames, generic.Constraints, arguments, written, methodName))
        {
            return null;
        }

        var unit = GenericUses.Current ?? GenericUses.UnitOf(_scope);
        lock (_types.GenericGate)
        {
            if (generic.Instances.TryGetValue(arguments, out var instance) || t_open && generic.OpenInstances.TryGetValue(arguments, out instance))
            {
                return new MethodCandidate(instance, []);
            }

            var scope = new Scope(ScopeKind.Unit, generic.DeclaringScope, generic.DeclaringContainer) { DeclaresTypeParameters = true };
            for (var i = 0; i < arguments.Length; i++)
            {
                scope.TryDeclare(generic.ParameterNames[i], arguments[i], out _);
            }

            // LANG-978 (C-420): a generic constructor, destructor or operator was reported where it is declared (E2713); where its
            // heading does not bind as a method of that kind (an operator of a class, a destructor of a record) a call adds nothing
            var reported = generic.HasRestrictedKind ? new DiagnosticBag() : _diagnostics;
            var binder = new DeclarationBinder(_types, scope, generic.DeclaringContainer, reported) { _genericMethodInstance = true };
            var uses = new List<GenericUses.Use>();
            using (GenericUses.For(unit))
            using (GenericUses.Capture(uses))
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

            if (!ReferenceEquals(reported, _diagnostics))
            {
                foreach (var diagnostic in reported.ToImmutable())
                {
                    _diagnostics.Add(diagnostic);
                }
            }

            instance.GenericMethodDefinition = generic;
            instance.GenericMethodArguments = arguments;
            if (!t_open)
            {
                instance.GenericSuffix = "<" + string.Join(",", arguments.Select(Emit.Mangler.TypeName)) + ">";
            }

            return new MethodCandidate(instance, uses);
        }
    }

    /// <summary>
    /// LANG-980 rule 6, CMP-1477 (3) (C-419): the chosen <paramref name="candidate"/> becomes the instance of its generic method for
    /// its argument list (the one made for that list meanwhile is taken instead), and the using unit records it and what its heading
    /// uses (CMP-385) — it is emitted and written to the <c>.pcu</c> as before.
    /// </summary>
    internal MethodSymbol UseMethod(MethodCandidate candidate, SyntaxNode at)
    {
        var instance = candidate.Instance;
        var generic = instance.GenericMethodDefinition!;
        var arguments = instance.GenericMethodArguments;
        var unit = GenericUses.Current ?? GenericUses.UnitOf(_scope);
        lock (_types.GenericGate)
        {
            if (generic.Instances.TryGetValue(arguments, out var made) || t_open && generic.OpenInstances.TryGetValue(arguments, out made))
            {
                instance = made;
            }
            else
            {
                (t_open ? generic.OpenInstances : generic.Instances)[arguments] = instance;   // C-358: an open binding's, for navigation only
                if (generic.Implementation is { } implementation)
                {
                    DefineGenericMethodInstance(instance, implementation, arguments);
                }

                foreach (var use in candidate.Uses)
                {
                    GenericUses.Note(use.Instance, use.Unit, use.At);
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
        if (t_suppressed || type is not (TypeSymbol { IsGenericInstance: true } or MethodSymbol { GenericMethodDefinition: not null }) || unit is null)
        {
            return;
        }

        if (t_captured is { } captured)
        {
            captured.Add(new Use(type, unit, at));                              // C-419: noted when the candidate is chosen
            return;
        }

        if (!ArgumentsVisibleIn(type, unit))
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

    /// <summary>A use <see cref="Note"/> was asked to record while uses were captured (<see cref="Capture"/>).</summary>
    internal readonly record struct Use(Symbol Instance, UnitSymbol Unit, SyntaxNode? At);

    [ThreadStatic]
    private static List<Use>? t_captured;

    /// <summary>
    /// LANG-980 rule 6 (C-419): while the result is not disposed, the uses <see cref="Note"/> would record on this thread go to
    /// <paramref name="uses"/> instead — those of the heading of a generic method instance that is only a candidate of a call. They
    /// are noted when the call chooses it (<c>DeclarationBinder.UseMethod</c>) and dropped otherwise.
    /// </summary>
    public static Capturing Capture(List<Use> uses)
    {
        var previous = t_captured;
        t_captured = uses;
        return new Capturing(previous);
    }

    internal readonly struct Capturing(List<Use>? previous) : IDisposable
    {
        public void Dispose() => t_captured = previous;
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