using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>Unit-level symbols of one compilation unit (CMP-113).</summary>
/// <param name="InterfaceScope">The interface scope of a unit; null for a program.</param>
/// <param name="Scope">The innermost unit-level scope: a program's scope or a unit's implementation scope.</param>
internal sealed record DeclarationBindingResult(
    UnitSymbol Unit,
    Scope? InterfaceScope,
    Scope Scope,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>
/// Binds declaration sections into a scope (CMP-113): creates the declared symbols in source order and reports
/// declaration errors. One instance per scope being filled.
/// </summary>
internal sealed partial class DeclarationBinder
{
    private readonly BuiltInTypes _types;
    private readonly Scope _scope;
    private readonly Symbol _container;
    private readonly DiagnosticBag _diagnostics;
    private readonly Scope? _sharedScope;
    private readonly ConstantFolder _folder;
    private readonly Conversions _conversions = new();
    private readonly List<RoutineSymbol> _awaitingDefinition = [];

    /// <param name="sharedScope">
    /// A scope whose names also count as declared in <paramref name="scope"/> for E2004: a unit's interface scope when
    /// binding its implementation (LANG-518).
    /// </param>
    public DeclarationBinder(BuiltInTypes types, Scope scope, Symbol container, DiagnosticBag diagnostics, Scope? sharedScope = null)
    {
        _types = types;
        _scope = scope;
        _container = container;
        _diagnostics = diagnostics;
        _sharedScope = sharedScope;
        _folder = new ConstantFolder(types, scope, diagnostics) { Generic = BindGenericName };     // C-353: SizeOf(G<X>), G<X>.C
    }

    /// <summary>This binder's constant folder (generic instance names included, C-353).</summary>
    internal ConstantFolder Folder => _folder;

    /// <summary>A unit's interface section: its routines are defined in the implementation (E2100 is checked there).</summary>
    public bool IsInterfaceSection { get; init; }

    /// <summary>Unit and program level declarations are global; routine-level ones are locals.</summary>
    private bool IsGlobalLevel => _scope.Kind == ScopeKind.Unit;

    /// <summary>
    /// LANG-777 (C-344): <paramref name="type"/> is a record whose member list is being bound around this declaration (a nested
    /// record's field of the enclosing record's own type: E2007). Method bodies, below a routine scope, see complete records.
    /// </summary>
    private bool IsEnclosingRecordUnderConstruction(TypeSymbol type)
    {
        for (var scope = _scope; scope is { Kind: not ScopeKind.Routine }; scope = scope.Parent)
        {
            if (scope.Kind == ScopeKind.TypeMember && type is RecordTypeSymbol && ReferenceEquals(scope.Owner, type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>C-344: the scope that receives the nested names of a class's ancestors once its parent is bound (LANG-772).</summary>
    private Scope? _inheritedNested;

    /// <summary>Unit level or a type section of a class or record (LANG-771: nested class and interface types; not of helpers, F10-027).</summary>
    private bool IsTypeLevel => IsGlobalLevel || _scope is { Kind: ScopeKind.TypeMember, Owner: ClassTypeSymbol or RecordTypeSymbol };

    /// <summary>
    /// The unit-level scopes of a program (one scope) or unit (interface scope, then implementation scope inside it;
    /// LANG-310), chained to the built-in scope. Uses clauses and System are bound by later tasks.
    /// </summary>
    public static DeclarationBindingResult BindCompilationUnit(SyntaxTree tree, BuiltInTypes types) =>
        BindCompilationUnit(tree, types, new DiagnosticBag());

    /// <param name="diagnostics">Shared with the bodies of the unit level (one E2003 set, CMP-507).</param>
    /// <param name="usesScope">
    /// The scope between the built-ins and the unit's own scope (LANG-310): the interface scope of the implicitly used
    /// <c>System</c> (LANG-511); null = the built-ins directly (unit <c>System</c> itself, white-box tests).
    /// </param>
    public static DeclarationBindingResult BindCompilationUnit(SyntaxTree tree, BuiltInTypes types, DiagnosticBag diagnostics, Scope? usesScope = null)
    {
        var outer = usesScope ?? types.Scope;
        return BindImplementation(tree, BindInterface(tree, types, diagnostics, outer), types, diagnostics, outer);
    }

    /// <summary>
    /// The first phase (C-069): a unit's symbol and interface section in an interface scope whose parent is
    /// <paramref name="interfaceUses"/> (the interface-uses chain, LANG-515); a program's whole declaration part (its uses
    /// chain). Importers may use the interface scope as soon as this returns (implementation-uses cycles, LANG-513).
    /// </summary>
    /// <returns>For a unit, <see cref="DeclarationBindingResult.Scope"/> is the interface scope until the second phase.</returns>
    public static DeclarationBindingResult BindInterface(SyntaxTree tree, BuiltInTypes types, DiagnosticBag diagnostics, Scope interfaceUses)
    {
        switch (tree.Root)
        {
            case UnitSyntax unit:
            {
                var symbol = new UnitSymbol(NameOf(unit.Name), isProgram: false, [DiagnosticBag.LocationOf(unit.Name)]);
                var interfaceScope = new Scope(ScopeKind.Unit, interfaceUses, symbol);
                symbol.InterfaceMembers = new DeclarationBinder(types, interfaceScope, symbol, diagnostics) { IsInterfaceSection = true }
                    .BindDeclarations(unit.InterfaceSection.Declarations);
                return new DeclarationBindingResult(symbol, interfaceScope, interfaceScope, diagnostics.ToImmutable());
            }

            case ProgramSyntax program:
            {
                var symbol = program.Name is { IsMissing: false } name
                    ? new UnitSymbol(NameOf(name), isProgram: true, [DiagnosticBag.LocationOf(name)])
                    : new UnitSymbol(Path.GetFileNameWithoutExtension(tree.Text.Path), isProgram: true, []);
                var scope = new Scope(ScopeKind.Unit, interfaceUses, symbol);
                symbol.ImplementationMembers = new DeclarationBinder(types, scope, symbol, diagnostics)
                    .BindDeclarations(program.Block.Declarations);
                ReportUnimplementedMethods(symbol, diagnostics);
                return new DeclarationBindingResult(symbol, null, scope, diagnostics.ToImmutable());
            }

            case LibrarySyntax library:
            {
                // LANG-541: a library file follows the rules of a program file; its exports clauses are bound last (LANG-545)
                var symbol = library.Name is { IsMissing: false } name
                    ? new UnitSymbol(NameOf(name), isProgram: true, [DiagnosticBag.LocationOf(name)], isLibrary: true)
                    : new UnitSymbol(Path.GetFileNameWithoutExtension(tree.Text.Path), isProgram: true, [], isLibrary: true);
                var scope = new Scope(ScopeKind.Unit, interfaceUses, symbol);
                var binder = new DeclarationBinder(types, scope, symbol, diagnostics);
                symbol.ImplementationMembers = binder.BindDeclarations(library.Block.Declarations);
                binder.FinishExports(symbol);
                ReportUnimplementedMethods(symbol, diagnostics);
                return new DeclarationBindingResult(symbol, null, scope, diagnostics.ToImmutable());
            }

            default:
            {
                var symbol = new UnitSymbol(Path.GetFileNameWithoutExtension(tree.Text.Path), isProgram: false, []);
                return new DeclarationBindingResult(symbol, null, new Scope(ScopeKind.Unit, interfaceUses, symbol), diagnostics.ToImmutable());
            }
        }
    }

    /// <summary>
    /// The second phase: a unit's implementation section in an implementation scope inside its interface scope, which is
    /// chained to <paramref name="implementationUses"/> (the implementation-uses chain on top of the interface-uses chain:
    /// LANG-310 searches implementation-uses before interface-uses). Nothing to do for a program.
    /// </summary>
    public static DeclarationBindingResult BindImplementation(SyntaxTree tree, DeclarationBindingResult interfacePart, BuiltInTypes types,
        DiagnosticBag diagnostics, Scope implementationUses)
    {
        if (tree.Root is not UnitSyntax unit || interfacePart.InterfaceScope is not { } interfaceScope)
        {
            return interfacePart with { Diagnostics = diagnostics.ToImmutable() };
        }

        var symbol = interfacePart.Unit;
        var ownInterface = ReferenceEquals(interfaceScope.Parent, implementationUses) ? interfaceScope : Scope.Reparented(interfaceScope, implementationUses);
        var implementationScope = new Scope(ScopeKind.Unit, ownInterface, symbol);
        symbol.ImplementationMembers = new DeclarationBinder(types, implementationScope, symbol, diagnostics, interfaceScope)
            .BindDeclarations(unit.ImplementationSection.Declarations);
        foreach (var routine in symbol.InterfaceMembers.OfType<RoutineSymbol>().Where(r => r.AwaitsDefinition))
        {
            diagnostics.Report(routine.Declaration.Heading.Name, "E2100", routine.Name);     // LANG-400
        }

        ReportUnimplementedMethods(symbol, diagnostics);

        return new DeclarationBindingResult(symbol, interfaceScope, implementationScope, diagnostics.ToImmutable());
    }

    /// <summary>Binds the sections in order; returns the declared symbols in declaration order.</summary>
    public ImmutableArray<Symbol> BindDeclarations(SyntaxList<SyntaxNode> declarations)
    {
        var declared = ImmutableArray.CreateBuilder<Symbol>();
        foreach (var declaration in declarations)
        {
            switch (declaration)
            {
                case ConstSectionSyntax constSection:
                    BindConstSection(constSection, declared);
                    break;
                case TypeSectionSyntax typeSection:
                    BindTypeSection(typeSection, declared);
                    break;
                case VarSectionSyntax varSection:
                    BindVarSection(varSection, declared);
                    break;
                case RoutineDeclarationSyntax routine:
                    BindRoutine(routine, declared);
                    break;
                case ExportsClauseSyntax exports:
                    BindExportsClause(exports);                             // LANG-544, 545 (C-286)
                    break;
            }
        }

        ReportUnsatisfied();
        return declared.ToImmutable();
    }

    private void BindConstSection(ConstSectionSyntax section, ImmutableArray<Symbol>.Builder declared)
    {
        var isResourceString = section.Keyword.Kind == SyntaxKind.ResourceStringKeyword;
        foreach (var declaration in section.Declarations)
        {
            var identifier = declaration.Identifier;
            if (identifier.IsMissing)
            {
                continue;
            }

            ConstantSymbol symbol;
            if (declaration.Type != null)
            {
                if (isResourceString)
                {
                    // LANG-346: true string constants only; the typed constant is still declared (no follow-up errors)
                    _diagnostics.Report(declaration.Type, "E2010", "typed constant", _types.String.Name);
                }

                // LANG-344: a read-only variable with static storage, initialised statically (LANG-522)
                var type = BindType(declaration.Type, declared: declared);
                var value = type.TypeKind == TypeKind.Error || !IsStaticallyInitializable(type, declaration.Type) ? null
                    : BindTypedConstant(declaration.Value, type);
                var locations = ImmutableArray.Create(DiagnosticBag.LocationOf(identifier));
                symbol = new ConstantSymbol(NameOf(identifier), type, value, isTyped: true, _container, locations)
                {
                    Storage = new VariableSymbol(NameOf(identifier), type, isGlobal: true, _container, locations, value) { IsReadOnly = true },
                };
            }
            else
            {
                var value = declaration.Value is ExpressionSyntax expression ? _folder.Fold(expression) : NotConstant(declaration.Value);
                if (isResourceString && value != null)
                {
                    value = AsResourceString(value, declaration.Value);
                }

                symbol = Constant(identifier, value?.Type ?? _types.Error, value, isTyped: false);
            }

            symbol.Hints = BindHints(declaration.HintDirectives);                                 // LANG-472 (C-232)
            Declare(identifier, symbol, declared);
        }
    }

    /// <summary>
    /// LANG-205: <c>type T = U</c> declares <c>T</c> as another name of <c>U</c>; a type constructor (enumeration, subrange;
    /// later milestones add the rest) creates a new type named <c>T</c>. Distinct types (<c>= type U</c>, C-057) and
    /// generic types are E2900.
    /// </summary>
    private void BindTypeSection(TypeSectionSyntax section, ImmutableArray<Symbol>.Builder declared)
    {
        // LANG-208: ^T may name a type declared later in this section; such pointers are completed after the section
        _sectionTypeNames = new(section.Declarations.Where(d => !d.Identifier.IsMissing).Select(d => NameOf(d.Identifier)), IdentifierComparer.Instance);
        _forwardPointers = [];
        var forwardClasses = new List<(ClassTypeSymbol Type, SyntaxToken Identifier)>();
        var forwardInterfaces = new List<(InterfaceTypeSymbol Type, SyntaxToken Identifier)>();
        var outerForwardGenerics = _forwardGenerics;
        _forwardGenerics = [];
        foreach (var declaration in section.Declarations)
        {
            var identifier = declaration.Identifier;
            if (identifier.IsMissing)
            {
                continue;
            }

            if (BindHints(declaration.HintDirectives) is { } hints)
            {
                // LANG-472 (C-232): on the declared name — an alias has no symbol of its own; a generic is declared under its arity key
                _scope.SetHints(declaration.TypeParameterList is { } list
                    ? GenericTypeSymbol.KeyOf(NameOf(identifier), list.Parameters.SelectMany(p => p.Identifiers).Count()) : NameOf(identifier), hints);
                _types.HasTypeHints = true;
            }

            if (declaration.TypeParameterList is { } genericParameters)
            {
                BindGenericDeclaration(declaration, genericParameters, declared);                               // C-116 (LANG-700)
                continue;
            }

            if (declaration.Type is ClassTypeSyntax classSyntax && declaration.TypeParameterList is null && declaration.TypeKeyword is null)
            {
                BindClassDeclaration(identifier, classSyntax, declared, forwardClasses);
                continue;
            }

            if (declaration.Type is HelperTypeSyntax helperSyntax && declaration.TypeParameterList is null && declaration.TypeKeyword is null)
            {
                BindHelperDeclaration(identifier, helperSyntax, declared);                                       // C-124
                continue;
            }

            if (declaration.Type is InterfaceTypeSyntax interfaceSyntax && declaration.TypeParameterList is null && declaration.TypeKeyword is null)
            {
                BindInterfaceDeclaration(identifier, interfaceSyntax, declared, forwardInterfaces);                // C-098
                continue;
            }

            if (declaration.Type is DispInterfaceTypeSyntax dispSyntax && declaration.TypeParameterList is null && declaration.TypeKeyword is null)
            {
                // com-interfaces.md LANG-693 (C-303)
                BindInterfaceDeclaration(identifier, dispSyntax.DispInterfaceKeyword, dispSyntax.EndKeyword is null && dispSyntax.Heritage is null,
                    isDispatch: true, type => BindDispInterfaceBody(type, dispSyntax), declared, forwardInterfaces);
                continue;
            }

            if (forwardClasses.FindIndex(f => f.Type.IsForward && IdentifierComparer.Instance.Equals(f.Type.Name, NameOf(identifier))) is var forward and >= 0)
            {
                // a forward class completed by another kind of type (C-096): E2001 once, instead of E2004 + E2015
                _diagnostics.Report(identifier, "E2001", NameOf(identifier));
                forwardClasses.RemoveAt(forward);
                continue;
            }

            TypeSymbol type;
            _declaringType = (NameOf(identifier), declaration.Type);
            if (declaration.TypeKeyword is not null)
            {
                type = BindDistinctType(declaration.Type, identifier, declared);
            }
            else
            {
                type = BindType(declaration.Type, identifier, declared);
            }

            _declaringType = null;
            var name = NameOf(identifier);
            var previous = _sharedScope?.LookupLocal(name);
            if (previous is null && _scope.TryDeclare(name, type, out previous))
            {
                declared.Add(type);
                if (declaration.TypeKeyword is null)
                {
                    RecordAlias(identifier, declaration.Type, type);
                }
            }
            else
            {
                _diagnostics.ReportRedeclared(identifier, name, previous!);
            }
        }

        foreach (var (pointer, pointedName) in _forwardPointers)
        {
            if (_scope.LookupLocal(NameOf(pointedName.Identifier)) is TypeSymbol target)
            {
                pointer.PointedType = target;
            }
            else
            {
                _diagnostics.Report(pointedName, "E2015", NameOf(pointedName.Identifier));
                pointer.PointedType = _types.Error;
            }
        }

        foreach (var (type, identifier) in forwardClasses.Where(f => f.Type.IsForward))
        {
            _diagnostics.Report(identifier, "E2015", type.Name);                    // LANG-601
        }

        foreach (var (type, identifier) in forwardInterfaces.Where(f => f.Type.IsForward))
        {
            _diagnostics.Report(identifier, "E2015", type.Name);
        }

        foreach (var (generic, identifier) in _forwardGenerics.Where(f => f.Generic.IsForward))
        {
            _diagnostics.Report(identifier, "E2015", GenericDisplayName(generic));     // LANG-782 (C-346)
        }

        _forwardGenerics = outerForwardGenerics;

        _sectionTypeNames = null;
        _forwardPointers = null;
    }

    /// <summary>
    /// C-268 (debug-info-sidecar.md §4): a unit-level <c>type T = U</c> naming an existing type (or <c>string</c>) is recorded for
    /// the sidecar; the scope already maps <c>T</c> to <c>U</c>'s symbol, so type identity is untouched (LANG-205). An alias of an
    /// earlier alias of the same unit keeps that alias as its target.
    /// </summary>
    private void RecordAlias(SyntaxToken identifier, TypeSyntax syntax, TypeSymbol type)
    {
        if (_container is not UnitSymbol unit || type.TypeKind == TypeKind.Error || t_open                 // C-355: no alias of an open binding
            || syntax is not (NamedTypeSyntax or StringTypeSyntax { Length: null }))
        {
            return;
        }

        var of = syntax is NamedTypeSyntax { Name: IdentifierNameSyntax target }
            ? unit.TypeAliases.LastOrDefault(a => ReferenceEquals(a.Target, type) && IdentifierComparer.Instance.Equals(a.Name, NameOf(target.Identifier)))
            : null;
        unit.TypeAliases.Add(new TypeAlias(NameOf(identifier), type, of, DiagnosticBag.LocationOf(identifier)));
    }

    /// <summary>
    /// LANG-205 <c>type T = type U</c>: a new type with the representation of <c>U</c>, assignment-compatible with it. Ordinal
    /// types become a full-range subrange of <c>U</c>'s host (so char and enumeration rules still match); reals, Booleans,
    /// long strings (C-262) and pointers a copy. A type constructor after <c>type</c> is already a new type (LANG-206).
    /// Distinct copies of other structured and managed named types are E2900 (C-057 decision).
    /// </summary>
    private TypeSymbol BindDistinctType(TypeSyntax syntax, SyntaxToken identifier, ImmutableArray<Symbol>.Builder declared)
    {
        var source = BindType(syntax, identifier, declared);
        if (syntax is not (NamedTypeSyntax or StringTypeSyntax { Length: null }) || source.TypeKind == TypeKind.Error)
        {
            return source;
        }

        var name = NameOf(identifier);
        ImmutableArray<Text.Location> locations = [DiagnosticBag.LocationOf(identifier)];
        switch (source)
        {
            case StringTypeSymbol { Variant: not StringVariant.Short } s:                                        // C-262
                return new StringTypeSymbol(_types.Target, name, s.Variant, s.CodePage, containingSymbol: _container, locations: locations);
            case IntegerTypeSymbol integer:
                return new IntegerTypeSymbol(name, integer.BaseType as IntegerTypeSymbol ?? integer, integer.Min, integer.Max, _container, locations);
            case CharTypeSymbol c:
                return new CharTypeSymbol(name, (CharTypeSymbol)Ordinals.Host(c), c.MinValue, c.MaxValue, _container, locations);
            case EnumTypeSymbol e:
                return new EnumTypeSymbol(name, (EnumTypeSymbol)Ordinals.Host(e), e.Low, e.High, _container, locations);
            case BooleanTypeSymbol boolean:
                return new BooleanTypeSymbol(name, (int)boolean.Size, boolean.IsCStyle, _container, locations);
            case RealTypeSymbol real:
                return new RealTypeSymbol(name, real.RealKind, real.Alignment, _container, locations);                          // C-262: RTTI name
            case PointerTypeSymbol { PointedType: var pointed } pointer when !ReferenceEquals(pointer, _types.Nil):
                return new PointerTypeSymbol(_types.Target, name, pointed, pointer.AllowsPointerMath);
            default:
                return Unsupported(syntax, "distinct type of " + KindName(source));
        }

        static string KindName(TypeSymbol type) => type switch
        {
            RecordTypeSymbol => "a record",
            ArrayTypeSymbol => "an array",
            SetTypeSymbol => "a set",
            StringTypeSymbol => "a string type",
            _ => $"'{type.Name}'",
        };
    }

    private HashSet<string>? _sectionTypeNames;

    /// <summary>The type declaration being bound (its name and type syntax), for E2007/E2022 on self-references.</summary>
    private (string Name, TypeSyntax Type)? _declaringType;
    private List<(PointerTypeSymbol Pointer, IdentifierNameSyntax Name)>? _forwardPointers;

    /// <summary>LANG-782 (C-346): the forward generic declarations of the type section being bound, completed in it or E2015.</summary>
    private List<(GenericTypeSymbol Generic, SyntaxToken Identifier)>? _forwardGenerics;

    /// <summary>
    /// LANG-265 <c>^T</c>: a typed pointer named after its declaration (or <c>^T</c>). A <c>T</c> declared later in the current type
    /// section — or not declared at all yet — is resolved at the section's end (LANG-208, E2015 when the section does not declare it).
    /// </summary>
    private TypeSymbol BindPointer(PointerTypeSyntax syntax, string name, ImmutableArray<Symbol>.Builder? declared)
    {
        if (syntax.PointedType is NamedTypeSyntax { Name: IdentifierNameSyntax { Identifier.IsMissing: false } pointedName }
            && _forwardPointers is not null
            && (_sectionTypeNames!.Contains(NameOf(pointedName.Identifier)) && _scope.LookupLocal(NameOf(pointedName.Identifier)) is null
                || _scope.Lookup(NameOf(pointedName.Identifier)) is null))
        {
            var forward = new PointerTypeSymbol(_types.Target, name, pointedType: null, allowsPointerMath: false);
            _forwardPointers.Add((forward, pointedName));
            return forward;
        }

        var target = BindType(syntax.PointedType, declared: declared);
        return target.TypeKind == TypeKind.Error ? _types.Error : new PointerTypeSymbol(_types.Target, name, target, allowsPointerMath: false);
    }

    // ------------------------------------------------------------------ enumerations and subranges (LANG-210…212)

    /// <summary>
    /// LANG-210: ordinals 0, 1, 2… or explicit strictly ascending constants in 0..2^31−1 (E2021 at the value); the
    /// elements are declared in this scope unless <c>{$SCOPEDENUMS ON}</c> held at the <c>(</c>.
    /// </summary>
    private EnumTypeSymbol BindEnum(EnumTypeSyntax syntax, string name, SyntaxToken? declaredAt, ImmutableArray<Symbol>.Builder declared)
    {
        var ordinals = new List<(EnumMemberSyntax Syntax, long Ordinal)>();
        long next = 0;
        foreach (var member in syntax.Members)
        {
            var ordinal = next;
            if (member.Value is { } valueSyntax && _folder.Fold(valueSyntax) is { } value)
            {
                if (value is { Type: IntegerTypeSymbol, Value: System.Numerics.BigInteger explicitValue } && explicitValue >= next && explicitValue <= int.MaxValue)
                {
                    ordinal = (long)explicitValue;
                }
                else
                {
                    _diagnostics.Report(valueSyntax, "E2021");
                }
            }

            ordinals.Add((member, ordinal));
            next = ordinal + 1;
        }

        var state = syntax.OpenParenToken.DirectiveState;
        var high = ordinals.Count == 0 ? 0 : ordinals[^1].Ordinal;
        var type = new EnumTypeSymbol(name, EnumTypeSymbol.SizeFor(high, state.MinEnumSize), state.ScopedEnums, _container,
            declaredAt is { } at ? [DiagnosticBag.LocationOf(at)] : []);
        var members = ImmutableArray.CreateBuilder<EnumMemberSymbol>(ordinals.Count);
        foreach (var (member, ordinal) in ordinals)
        {
            if (member.Identifier.IsMissing)
            {
                continue;
            }

            var symbol = new EnumMemberSymbol(NameOf(member.Identifier), type, ordinal, [DiagnosticBag.LocationOf(member.Identifier)]);
            members.Add(symbol);
            if (!type.IsScoped)
            {
                Declare(member.Identifier, symbol, declared);
            }
        }

        type.SetMembers(members.ToImmutable());
        return type;
    }

    /// <summary>
    /// LANG-211/212: two constants of one ordinal type (E2010 otherwise; E2013 for non-ordinals), low ≤ high (E2020 at the
    /// subrange). Integer subranges take the LANG-212 representation; enum and char subranges that of their host.
    /// </summary>
    private TypeSymbol BindSubrange(SubrangeTypeSyntax syntax, string name, SyntaxToken? declaredAt)
    {
        var low = _folder.Fold(syntax.Low);
        var high = _folder.Fold(syntax.High);
        if (low is null || high is null)
        {
            return _types.Error;
        }

        if (!low.Type.IsOrdinal || !high.Type.IsOrdinal)
        {
            _diagnostics.Report(low.Type.IsOrdinal ? syntax.High : syntax.Low, "E2013");
            return _types.Error;
        }

        var compatible = (low.Type, high.Type) switch
        {
            (IntegerTypeSymbol, IntegerTypeSymbol) => true,
            (CharTypeSymbol, CharTypeSymbol) or (EnumTypeSymbol, EnumTypeSymbol) => Ordinals.SameHost(low.Type, high.Type),
            (BooleanTypeSymbol, BooleanTypeSymbol) => true,
            _ => false,
        };
        if (!compatible)
        {
            _diagnostics.Report(syntax.High, "E2010", high.Type.Name, low.Type.Name);
            return _types.Error;
        }

        var (lo, hi) = (OrdinalOf(low), OrdinalOf(high));
        if (lo > hi)
        {
            _diagnostics.Report(syntax, "E2020");
            return _types.Error;
        }

        ImmutableArray<Text.Location> locations = declaredAt is { } at ? [DiagnosticBag.LocationOf(at)] : [];
        switch (Ordinals.Host(low.Type))
        {
            case IntegerTypeSymbol:
                return new IntegerTypeSymbol(name, SubrangeRepresentation(lo, hi), lo, hi, _container, locations);
            case CharTypeSymbol host:
                return new CharTypeSymbol(name, host, (int)lo, (int)hi, _container, locations);
            case EnumTypeSymbol baseEnum:
                return new EnumTypeSymbol(name, baseEnum, lo, hi, _container, locations);
            default:
                return Unsupported(syntax, "Boolean subrange");
        }

        static System.Numerics.BigInteger OrdinalOf(ConstantValue value) =>
            value.Value is bool b ? (b ? System.Numerics.BigInteger.One : System.Numerics.BigInteger.Zero) : (System.Numerics.BigInteger)value.Value!;
    }

    /// <summary>
    /// LANG-250: an ordinal base type (E2013) with ordinals in 0..255 (E2030), both at the base type; Char types of any range
    /// are accepted (types.md lead note 2026-11-14).
    /// </summary>
    private TypeSymbol BindSet(SetTypeSyntax syntax, string name, SyntaxToken? declaredAt, ImmutableArray<Symbol>.Builder? declared)
    {
        var element = BindType(syntax.ElementType, declared: declared);
        if (element.TypeKind == TypeKind.Error)
        {
            return _types.Error;
        }

        if (Ordinals.Bounds(element) is not { } bounds)
        {
            _diagnostics.Report(syntax.ElementType, "E2013");
            return _types.Error;
        }

        // types.md lead note 2026-11-14: set of Char (or a wider Char subrange) is the 256-bit set over Ord 0..255
        if ((bounds.Low < 0 || bounds.High > 255) && element is not CharTypeSymbol)
        {
            _diagnostics.Report(syntax.ElementType, "E2030");
            return _types.Error;
        }

        return new SetTypeSymbol(name, element, _container, declaredAt is { } at ? [DiagnosticBag.LocationOf(at)] : []);
    }

    /// <summary>
    /// LANG-255 static array: ordinal index types (E2013 at the index type) of at most 2^31−1 elements and a total size of at
    /// most 2^31−1 bytes (E2031 at the array type). <c>array[I1, I2] of T</c> is <c>array[I1] of array[I2] of T</c>; the inner
    /// types are named by their constructor (<c>array[I2] of T</c>). <c>packed</c> changes nothing (elements are not padded).
    /// A managed element type makes the array managed (C-065).
    /// </summary>
    /// <summary>
    /// LANG-257 <c>array of T</c> (C-064): a new dynamic array type (LANG-206); any element type with a size, managed ones included.
    /// </summary>
    private TypeSymbol BindDynamicArray(ArrayTypeSyntax syntax, string name, SyntaxToken? declaredAt, ImmutableArray<Symbol>.Builder? declared)
    {
        var element = BindType(syntax.ElementType!, declared: declared);
        if (element.TypeKind == TypeKind.Error)
        {
            return _types.Error;
        }

        if (element.Size < 0 || element is TextTypeSymbol)
        {
            return Unsupported(syntax.ElementType!, "array of " + element.Name);
        }


        ImmutableArray<Text.Location> locations = declaredAt is { } at ? [DiagnosticBag.LocationOf(at)] : [];
        return new DynamicArrayTypeSymbol(_types.Target, name, element, declaredAt is null ? null : _container, locations);
    }

    private TypeSymbol BindArray(ArrayTypeSyntax syntax, string name, SyntaxToken? declaredAt, ImmutableArray<Symbol>.Builder? declared)
    {
        var indexTypes = new List<TypeSymbol>();
        foreach (var indexSyntax in syntax.IndexTypes)
        {
            var index = BindType(indexSyntax, declared: declared);
            if (index.TypeKind == TypeKind.Error)
            {
                return _types.Error;
            }

            if (Ordinals.Bounds(index) is not { } bounds)
            {
                _diagnostics.Report(indexSyntax, "E2013");
                return _types.Error;
            }

            if (bounds.High - bounds.Low + 1 > int.MaxValue)
            {
                _diagnostics.Report(syntax, "E2031");
                return _types.Error;
            }

            indexTypes.Add(index);
        }

        var element = BindType(syntax.ElementType!, declared: declared);
        if (element.TypeKind == TypeKind.Error)
        {
            return _types.Error;
        }


        if (element.Size < 0 || element is TextTypeSymbol)
        {
            return Unsupported(syntax.ElementType!, "array of " + element.Name);
        }

        // innermost first: array[I1, I2] of T = array[I1] of (array[I2] of T)
        ImmutableArray<Text.Location> locations = declaredAt is { } at ? [DiagnosticBag.LocationOf(at)] : [];
        var result = element;
        for (var i = indexTypes.Count - 1; i >= 0; i--)
        {
            var size = (System.Numerics.BigInteger)ArrayTypeSymbol.LengthOf(indexTypes[i]) * result.Size;
            if (size > int.MaxValue)
            {
                _diagnostics.Report(syntax, "E2031");
                return _types.Error;
            }

            var innerName = i == 0 ? name
                : $"array[{string.Join(", ", syntax.IndexTypes.Skip(i).Select(t => t.ToString().Trim()))}] of {syntax.ElementType!.ToString().Trim()}";
            result = new ArrayTypeSymbol(innerName, indexTypes[i], result, i == 0 ? _container : null, i == 0 ? locations : []);
        }

        return result;
    }

    /// <summary>
    /// LANG-260/263 record without variant part: fields in declaration order (E2004 at a repeated name), laid out per ABI-060
    /// with packing 1 for <c>packed record</c>, else the <c>{$ALIGN}</c> value at the <c>record</c> keyword. Managed fields
    /// make the record managed (C-065); variant parts (C-108) and non-field members (M2.6) are E2900.
    /// </summary>
    private TypeSymbol BindRecord(RecordTypeSyntax syntax, string name, SyntaxToken? declaredAt, ImmutableArray<Symbol>.Builder? declared)
    {
        if (syntax.Members.Any(m => m is not FieldDeclarationSyntax))
        {
            return BindRecordWithMembers(syntax, name, declaredAt, declared);              // LANG-730, 770 (C-122)
        }

        var fields = new List<(SyntaxToken Identifier, TypeSymbol Type)>();
        var names = new HashSet<string>(IdentifierComparer.Instance);
        var failed = false;
        foreach (var member in syntax.Members)
        {
            if (member is not FieldDeclarationSyntax field)
            {
                Unsupported(member, "record " + FeatureName(member.Kind));                   // records with methods, M2.6
                failed = true;
                continue;
            }

            var type = BindType(field.Type, declared: declared);
            if (type.TypeKind == TypeKind.Error)
            {
                failed = true;
                continue;
            }

            if (IsEnclosingRecordUnderConstruction(type))
            {
                _diagnostics.Report(field.Type, "E2007", type.Name);                // LANG-777 (C-344): the enclosing record's own type
                failed = true;
                continue;
            }

            if (type.Size < 0 || type is TextTypeSymbol)
            {
                Unsupported(field.Type, "record field of type " + type.Name);
                failed = true;
                continue;
            }

            foreach (var identifier in field.Identifiers.Select(i => i.Identifier).Where(i => !i.IsMissing))
            {
                if (!names.Add(NameOf(identifier)))
                {
                    _diagnostics.Report(identifier, "E2004", NameOf(identifier));
                    failed = true;
                    continue;
                }

                fields.Add((identifier, type));
            }
        }

        var packing = syntax.PackedKeyword is not null ? 1 : syntax.RecordKeyword.DirectiveState.Align;
        var (offsets, size, alignment) = RecordTypeSymbol.Layout(fields.Select(f => f.Type), packing);
        var placed = fields.Select((f, i) => (f.Identifier, f.Type, Offset: offsets[i])).ToList();
        if (syntax.VariantPart is { } variantPart && !failed)
        {
            // ABI-061 (C-108): the variant part continues after the fixed part's last field
            var end = fields.Count == 0 ? 0 : offsets[^1] + fields[^1].Type.Size;
            var types = new Dictionary<FieldDeclarationSyntax, TypeSymbol>();
            failed = !BindVariantFields(variantPart, types, declared)
                || !PlaceVariantPart(variantPart, types, names, packing, placed, ref end, ref alignment);
            size = (end + alignment - 1) / alignment * alignment;
        }

        if (failed)
        {
            return _types.Error;
        }

        if (size > int.MaxValue)
        {
            _diagnostics.Report(syntax, "E2031");
            return _types.Error;
        }

        var record = new RecordTypeSymbol(_types.Target, name, size, alignment, syntax.PackedKeyword is not null, packing, isAnonymous: declaredAt is null,
            _container, declaredAt is { } at ? [DiagnosticBag.LocationOf(at)] : [])
        {
            HasVariantPart = syntax.VariantPart is not null,
        };
        record.Fields = [.. placed.Select(f => new FieldSymbol(NameOf(f.Identifier), f.Type, f.Offset, record, [DiagnosticBag.LocationOf(f.Identifier)]))];
        return record;
    }

    /// <summary>
    /// LANG-261 / ABI-061 (C-108): binds the types of every field in <paramref name="part"/> (nested parts too) into
    /// <paramref name="types"/>; E2160 for a managed type, E2900 for a type without fixed size. False after any error.
    /// </summary>
    private bool BindVariantFields(VariantPartSyntax part, Dictionary<FieldDeclarationSyntax, TypeSymbol> types, ImmutableArray<Symbol>.Builder? declared)
    {
        var ok = true;
        foreach (var variant in part.Variants)
        {
            foreach (var field in variant.Fields)
            {
                var type = BindType(field.Type, declared: declared);
                if (type.TypeKind == TypeKind.Error)
                {
                    ok = false;
                }
                else if (type.IsManaged)
                {
                    _diagnostics.Report(field.Type, "E2160", type.Name);
                    ok = false;
                }
                else if (type.Size < 0 || type is TextTypeSymbol)
                {
                    Unsupported(field.Type, "record field of type " + type.Name);
                    ok = false;
                }
                else
                {
                    types[field] = type;
                }
            }

            if (variant.VariantPart is { } nested)
            {
                ok &= BindVariantFields(nested, types, declared);
            }
        }

        return ok;
    }

    /// <summary>The largest field alignment (capped by <paramref name="packing"/>) in <paramref name="part"/>, nested parts included.</summary>
    private static int VariantAlignment(VariantPartSyntax part, Dictionary<FieldDeclarationSyntax, TypeSymbol> types, int packing) =>
        part.Variants.Select(v => Math.Max(
                v.Fields.Select(f => Math.Min(types[f].Alignment, packing)).DefaultIfEmpty(1).Max(),
                v.VariantPart is { } nested ? VariantAlignment(nested, types, packing) : 1))
            .DefaultIfEmpty(1).Max();

    /// <summary>
    /// ABI-061 (C-108): the tag field (when named) at the next aligned offset after <paramref name="end"/>; every variant then
    /// starts at one offset aligned to the largest alignment of all variant fields, its fields (and nested part) following in
    /// order; <paramref name="end"/> becomes the end of the largest variant. Labels are constants of the tag type (E2010,
    /// E2012); the tag type is ordinal (E2013); all names share the record's name space (E2004).
    /// </summary>
    private bool PlaceVariantPart(VariantPartSyntax part, Dictionary<FieldDeclarationSyntax, TypeSymbol> types, HashSet<string> names, int packing,
        List<(SyntaxToken Identifier, TypeSymbol Type, long Offset)> placed, ref long end, ref int alignment)
    {
        var ok = true;
        var tagType = BindType(part.TagType);
        if (tagType.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (!tagType.IsOrdinal)
        {
            _diagnostics.Report(part.TagType, "E2013");
            return false;
        }

        if (part.TagIdentifier is { IsMissing: false } tag)
        {
            if (!names.Add(NameOf(tag)))
            {
                _diagnostics.Report(tag, "E2004", NameOf(tag));
                ok = false;
            }

            var tagAlignment = Math.Min(tagType.Alignment, packing);
            var offset = (end + tagAlignment - 1) / tagAlignment * tagAlignment;
            placed.Add((tag, tagType, offset));
            end = offset + tagType.Size;
            alignment = Math.Max(alignment, tagAlignment);
        }

        var partAlignment = VariantAlignment(part, types, packing);
        alignment = Math.Max(alignment, partAlignment);
        var start = (end + partAlignment - 1) / partAlignment * partAlignment;
        var partEnd = start;
        var conversions = new Conversions();
        var labels = new HashSet<object>();
        foreach (var variant in part.Variants)
        {
            foreach (var label in variant.Labels)
            {
                if (_folder.Fold(label) is not { } value || conversions.ConvertConstant(value, tagType, label, _diagnostics) is not { } converted)
                {
                    ok = false;
                }
                else if (converted.Value is { } ordinal && !labels.Add(ordinal))
                {
                    _diagnostics.Report(label, "E2004", converted.ToDisplayString());                   // C-198: a repeated variant label
                    ok = false;
                }
            }

            var cursor = start;
            foreach (var field in variant.Fields)
            {
                var type = types[field];
                var fieldAlignment = Math.Min(type.Alignment, packing);
                foreach (var identifier in field.Identifiers.Select(i => i.Identifier).Where(i => !i.IsMissing))
                {
                    if (!names.Add(NameOf(identifier)))
                    {
                        _diagnostics.Report(identifier, "E2004", NameOf(identifier));
                        ok = false;
                        continue;
                    }

                    var offset = (cursor + fieldAlignment - 1) / fieldAlignment * fieldAlignment;
                    placed.Add((identifier, type, offset));
                    cursor = offset + type.Size;
                }
            }

            if (variant.VariantPart is { } nested)
            {
                ok &= PlaceVariantPart(nested, types, names, packing, placed, ref cursor, ref alignment);
            }

            partEnd = Math.Max(partEnd, cursor);
        }

        end = partEnd;
        return ok;
    }

    /// <summary>
    /// LANG-212: the first of ShortInt, Byte, SmallInt, Word, Integer, Cardinal, Int64, UInt64 holding both bounds; with a
    /// non-negative low bound the signed type of each size is skipped.
    /// </summary>
    private IntegerTypeSymbol SubrangeRepresentation(System.Numerics.BigInteger low, System.Numerics.BigInteger high)
    {
        foreach (var candidate in (ReadOnlySpan<TypeSymbol>)[_types.ShortInt, _types.Byte, _types.SmallInt, _types.Word,
                     _types.Integer, _types.Cardinal, _types.Int64, _types.UInt64])
        {
            var integer = (IntegerTypeSymbol)candidate;
            if ((low < 0 || !integer.IsSigned) && low >= integer.Min && high <= integer.Max)
            {
                return integer;
            }
        }

        return (IntegerTypeSymbol)_types.UInt64;                  // unreachable: constants are within the 64-bit ranges
    }

    private void BindVarSection(VarSectionSyntax section, ImmutableArray<Symbol>.Builder declared)
    {
        var isThreadVar = section.Keyword.Kind == SyntaxKind.ThreadVarKeyword;
        if (isThreadVar && !IsGlobalLevel)
        {
            _diagnostics.Report(section.Keyword, "E1100", section.Keyword.Text);   // LANG-345: unit or program level only
            isThreadVar = false;
        }

        foreach (var declaration in section.Declarations)
        {
            var type = BindType(declaration.Type, declared: declared);
            if (isThreadVar && declaration.Initializer is { } threadVarInitializer)
            {
                _diagnostics.Report(threadVarInitializer, "E2207");               // LANG-345: no initialiser
                continue;
            }

            if (isThreadVar && type.IsManaged)
            {
                _diagnostics.Report(declaration.Type, "E2900", "managed threadvar");   // LANG-345
                continue;
            }

            if (isThreadVar && declaration.PublicDirective is { } threadVarPublic)
            {
                _diagnostics.Report(threadVarPublic.Keyword, "E2900", "public name on a threadvar");
                continue;
            }

            var initializer = declaration.Initializer is { } node ? BindInitializer(declaration, node, type) : null;
            var linkName = declaration.PublicDirective is { } publicDirective ? BindVariablePublicName(declaration, publicDirective) : null;
            foreach (var name in declaration.Identifiers)
            {
                var identifier = name.Identifier;
                if (!identifier.IsMissing)
                {
                    Declare(identifier, new VariableSymbol(NameOf(identifier), type, IsGlobalLevel, _container,
                        [DiagnosticBag.LocationOf(identifier)], initializer)
                    {
                        ExplicitLinkName = linkName,
                        ThreadVarOffset = isThreadVar ? NextThreadVarOffset(type) : null,
                        Hints = BindHints(declaration.HintDirectives),                            // LANG-472 (C-232)
                    }, declared);
                }
            }
        }
    }

    /// <summary>ABI-180: the next field of the unit's threadvar record (ABI-060 natural alignment, declaration order).</summary>
    private long NextThreadVarOffset(TypeSymbol type)
    {
        var unit = (UnitSymbol)_container!;
        var alignment = Math.Max(1, type.Alignment);
        var offset = (unit.ThreadVarSize + alignment - 1) / alignment * alignment;
        unit.ThreadVarSize = offset + type.Size;
        return offset;
    }

    /// <summary>LANG-470 <c>var x: T; public name 'sym';</c>: one global variable only (else E2900 / E1101).</summary>
    private string? BindVariablePublicName(VarDeclarationSyntax declaration, DirectiveSyntax directive)
    {
        if (!IsGlobalLevel)
        {
            _diagnostics.Report(directive.Keyword, "E2900", "public name on a local variable");
            return null;
        }

        if (declaration.Identifiers.Count > 1)
        {
            _diagnostics.Report(directive.Keyword, "E1101", ";", directive.Keyword.Text);
            return null;
        }

        return directive.NameValue is { } value ? FoldString(value) : null;
    }

    /// <summary>grammar.md varDecl: an initialiser only for one global variable; a constant expression (LANG-522).</summary>
    private ConstantValue? BindInitializer(VarDeclarationSyntax declaration, SyntaxNode initializer, TypeSymbol type)
    {
        if (!IsGlobalLevel)
        {
            _diagnostics.Report(initializer, "E2207");
            return null;
        }

        if (declaration.Identifiers.Count > 1)
        {
            _diagnostics.Report(declaration.EqualsToken!.Value, "E1101", ";", "=");
            return null;
        }

        return type.TypeKind == TypeKind.Error || !IsStaticallyInitializable(type, declaration.Type) ? null : BindTypedConstant(initializer, type);
    }

    /// <summary>
    /// Managed types are statically initialised only when their managed parts are long strings (C-203: they hold constant
    /// literals, ABI-020); dynamic arrays, interfaces and <c>Text</c> have no static initialiser yet: E2900 at the type.
    /// </summary>
    private bool IsStaticallyInitializable(TypeSymbol type, TypeSyntax syntax)
    {
        static bool StringsOnly(TypeSymbol t) => !t.IsManaged || t switch
        {
            StringTypeSymbol => true,
            ArrayTypeSymbol array => StringsOnly(array.ElementType),
            RecordTypeSymbol record => record.Fields.All(f => StringsOnly(f.Type)),
            _ => false,
        };

        if (!StringsOnly(type) || type is TextTypeSymbol)
        {
            _diagnostics.Report(syntax, "E2900", "initialised " + type.Name);
            return false;
        }

        return true;
    }

    /// <summary>
    /// grammar.md <c>typedConst</c> of type <paramref name="type"/> (LANG-344): a constant expression converted to the type
    /// (LANG-280); for a static array <c>(e1, e2, …)</c> with exactly one value per element (E2035 / E2036 at the
    /// aggregate; a parenthesised expression is a one-element aggregate); for a record <c>(F1: v1; F2: v2)</c> with the
    /// fields in declaration order, trailing fields omitted = zero (E2037 at a name that is unknown or out of order). An
    /// array aggregate for another type is E2017, a record constant for a non-record E2016. The value of an array or record
    /// is an <see cref="ImmutableArray{T}"/> of element / field values (null = zero).
    /// </summary>
    private ConstantValue? BindTypedConstant(SyntaxNode node, TypeSymbol type)
    {
        switch (node)
        {
            case TypedConstantAggregateSyntax aggregate when aggregate.Elements.Any(e => e is RecordConstantFieldSyntax)
                || (aggregate.Elements.Count == 0 && type is not ArrayTypeSymbol):
                return BindRecordConstant(aggregate, type);
            case TypedConstantAggregateSyntax aggregate:
                return BindArrayConstant(aggregate, [.. aggregate.Elements], type);
            case ParenthesizedExpressionSyntax parenthesized when type is ArrayTypeSymbol:
                return BindArrayConstant(parenthesized, [parenthesized.Expression], type);
            case AddressOfExpressionSyntax addressOf:
                return BindAddressConstant(addressOf, type);
            case ExpressionSyntax expression when type is RecordTypeSymbol guidType && GuidValues.IsGuidType(type):
                return BindGuidConstant(expression, guidType);                      // LANG-680, 681 (C-299)
            case ExpressionSyntax expression:
            {
                // converted to the declared type (LANG-280), so the value carries that type
                var value = _folder.Fold(expression);
                if (type is StringTypeSymbol { Variant: StringVariant.Short } shortString && value?.Value is string text
                    && Strings.Utf8(text).Length > shortString.MaxLength)
                {
                    _diagnostics.Report(expression, "E2054", type.Name);            // a constant is not truncated (C-063)
                    return null;
                }

                return value is null ? null : _conversions.ConvertConstant(value, type, expression, _diagnostics);
            }

            default:
                return NotConstant(node);
        }
    }

    /// <summary>
    /// The value of a typed <c>TGUID</c> constant or initialised variable written as an expression: an interface type identifier
    /// (its GUID, LANG-680; E2655 without one) or a constant string in the format of LANG-660 (LANG-681; E2650 when malformed).
    /// </summary>
    private ConstantValue? BindGuidConstant(ExpressionSyntax expression, RecordTypeSymbol guidType)
    {
        ImmutableArray<byte>? bytes;
        if (NestedNames.TypeNamedBy(_scope, expression) is InterfaceTypeSymbol { IsMethodReference: false } intf)
        {
            bytes = intf.Guid;
            if (bytes is null)
            {
                _diagnostics.Report(expression, "E2655", intf.Name);
                return null;
            }
        }
        else if (_folder.Fold(expression) is not { } value)
        {
            return null;
        }
        else if (value.Value is string text)
        {
            bytes = GuidValues.Parse(text);
            if (bytes is null)
            {
                _diagnostics.Report(expression, "E2650");
                return null;
            }
        }
        else
        {
            return _conversions.ConvertConstant(value, guidType, expression, _diagnostics);     // E2010
        }

        if (GuidValues.Constant(guidType, bytes.Value) is { } constant)
        {
            return constant;
        }

        _diagnostics.Report(expression, "E2010", "GUID", guidType.Name);
        return null;
    }

    /// <summary>
    /// LANG-265/344 <c>@X</c> in a typed constant or initialiser: the address of a global variable, a typed constant or a
    /// top-level routine (E2033 for anything else), converted to the pointer type of the constant (E2010 otherwise).
    /// </summary>
    private ConstantValue? BindAddressConstant(AddressOfExpressionSyntax syntax, TypeSymbol type)
    {
        Symbol? target = syntax.Operand is IdentifierNameSyntax { Identifier.IsMissing: false } name
            ? _scope.Lookup(NameOf(name.Identifier)) switch
            {
                VariableSymbol { IsGlobal: true } global => global,
                ConstantSymbol { IsTyped: true, Storage: { } storage } => storage,
                RoutineSymbol { IsNested: false } routine => routine,
                _ => null,
            }
            : null;
        if (target is null)
        {
            _diagnostics.Report(syntax.Operand, "E2033");
            return null;
        }

        return _conversions.ConvertConstant(new ConstantValue(new AddressConstant(target), _types.Pointer), type, syntax, _diagnostics);
    }

    private ConstantValue? BindArrayConstant(SyntaxNode syntax, IReadOnlyList<SyntaxNode> elements, TypeSymbol type)
    {
        if (type is not ArrayTypeSymbol array)
        {
            _diagnostics.Report(syntax, "E2017");
            return null;
        }

        if (elements.Count != array.Length)
        {
            _diagnostics.Report(syntax, elements.Count < array.Length ? "E2035" : "E2036", array.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return null;
        }

        var values = elements.Select(e => BindTypedConstant(e, array.ElementType)).ToImmutableArray();
        return values.Any(v => v is null) ? null : new ConstantValue(values, type);
    }

    private ConstantValue? BindRecordConstant(TypedConstantAggregateSyntax syntax, TypeSymbol type)
    {
        if (type is not RecordTypeSymbol record)
        {
            _diagnostics.Report(syntax, "E2016");
            return null;
        }

        var values = new ConstantValue?[record.Fields.Length];
        var next = 0;
        var failed = false;
        foreach (var element in syntax.Elements)
        {
            if (element is not RecordConstantFieldSyntax field)
            {
                _diagnostics.Report(element, "E2016");                     // a positional value inside a record constant
                return null;
            }

            var name = NameOf(field.Name.Identifier);
            if (record.HasVariantPart)
            {
                // C-198: in declaration order, skipping fields (the other variants); never overlapping a field given before
                var found = next;
                while (found < record.Fields.Length && !IdentifierComparer.Instance.Equals(record.Fields[found].Name, name))
                {
                    found++;
                }

                if (found < record.Fields.Length && Enumerable.Range(0, found).Any(i => values[i] is not null && Overlap(record.Fields[i], record.Fields[found])))
                {
                    found = record.Fields.Length;
                }

                next = found;
            }

            if (next >= record.Fields.Length || !IdentifierComparer.Instance.Equals(record.Fields[next].Name, name))
            {
                _diagnostics.Report(field.Name, "E2037", name);
                return null;
            }

            values[next] = BindTypedConstant(field.Value, record.Fields[next].Type);
            failed |= values[next] is null;
            next++;
        }

        if (failed)
        {
            return null;
        }

        var constant = new ConstantValue(values.ToImmutableArray(), type);
        if (record.HasVariantPart && Emit.ConstantBytes.Unencodable(constant, record) is { } unencodable)
        {
            Unsupported(syntax, "typed constant of a variant record with a field of type " + unencodable.Name);   // C-198: byte storage
            return null;
        }

        return constant;

        static bool Overlap(FieldSymbol a, FieldSymbol b) => a.Offset < b.Offset + b.Type.Size && b.Offset < a.Offset + a.Type.Size;
    }

    // ------------------------------------------------------------------ routines (LANG-400…403, 440, 460…473)

    /// <summary>
    /// A routine declaration: either the definition of an earlier <c>forward</c>/interface declaration of this scope
    /// (LANG-401; completes that symbol) or a new routine, possibly joining an overload set (LANG-440).
    /// </summary>
    private void BindRoutine(RoutineDeclarationSyntax declaration, ImmutableArray<Symbol>.Builder declared)
    {
        var heading = declaration.Heading;
        if (heading.Name is QualifiedNameSyntax)
        {
            BindMethodImplementation(declaration);                              // procedure TFoo.Bar (LANG-620)
            return;
        }

        if (!IsSupportedHeading(heading, out var identifier))
        {
            return;
        }

        var name = NameOf(identifier);
        var directives = BindDirectives(declaration.Directives);
        var existing = (_sharedScope?.LookupRoutinesLocal(name) ?? []).AddRange(_scope.LookupRoutinesLocal(name));
        if (declaration.Block != null && FindDeclarationToDefine(heading, identifier, existing) is { } target)
        {
            if (DisagreesOnSafecall(target, declaration))
            {
                _diagnostics.Report(identifier, "E2101", name);                 // LANG-689 (C-301)
            }

            target.Definition = declaration;
            target.DefinitionScope = _scope;
            target.DefinitionScopeVisibleCount = _scope.Count;
            target.AddLocation(DiagnosticBag.LocationOf(identifier));
            return;
        }

        if (declaration.Block != null && existing.Any(r => r.IsExternal))
        {
            _diagnostics.Report(identifier, "E2104", name);                    // LANG-463: an external routine has no body
            return;
        }

        var routine = new RoutineSymbol(name, _container, [DiagnosticBag.LocationOf(identifier)], declaration)
        {
            IsOverload = directives.IsOverload,
            IsExternal = directives.IsExternal,
            IsInline = directives.IsInline,
            IsNoReturn = directives.IsNoReturn,
            IsDeprecated = directives.IsDeprecated,
            DeprecationMessage = directives.DeprecationMessage,
            IsPlatform = directives.IsPlatform,
            IsExperimental = directives.IsExperimental,
            LibraryName = directives.LibraryName,
            ExplicitLinkName = directives.LinkName ?? (directives.IsExternal ? name : null),
            CallingConvention = CallConvOf(declaration.Directives.Select(d => d.Keyword)),
        };
        if (routine.IsSafecall && (routine.IsNested || declaration.Directives.Any(d => d.Keyword.ContextualKind == SyntaxKind.VarArgsKeyword)))
        {
            // LANG-685 (C-301)
            _diagnostics.Report(declaration.Directives.First(d => d.Keyword.ContextualKind == SyntaxKind.SafeCallKeyword).Keyword, "E2656",
                routine.IsNested ? "a nested routine" : "a varargs routine");
            routine.CallingConvention = CallConvDirective.Default;
        }

        BindSignature(routine, heading, identifier, report: true);
        if (declaration.Block != null)
        {
            routine.Definition = declaration;
            routine.DefinitionScope = _scope;
        }

        if (DeclareRoutine(identifier, routine, existing))
        {
            routine.DefinitionScopeVisibleCount = _scope.Count;
            declared.Add(routine);
            if (routine.AwaitsDefinition && !IsInterfaceSection)
            {
                _awaitingDefinition.Add(routine);
            }
        }
    }

    /// <summary>Plain procedures and functions; methods, operators, class routines and generic routines come later.</summary>
    private bool IsSupportedHeading(RoutineHeadingSyntax heading, out SyntaxToken identifier)
    {
        identifier = default;
        if (heading.Name is not IdentifierNameSyntax { Identifier: var token } || token.IsMissing)
        {
            if (heading.Name is not IdentifierNameSyntax)
            {
                _diagnostics.Report(heading.Name, "E2900", "method implementation");          // M2.4
            }

            return false;
        }

        identifier = token;
        var unsupported = heading.ClassKeyword != null ? "class routine"
            : heading.Keyword.Kind is not (SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword) ? heading.Keyword.Text.ToLowerInvariant()
            : null;
        if (unsupported != null)
        {
            _diagnostics.Report(heading, "E2900", unsupported);
            return false;
        }

        if (heading.TypeParameterList != null)
        {
            _diagnostics.Report(identifier, "E2700");                          // LANG-701 (C-117): only methods may be generic
            return false;
        }

        return true;
    }

    private sealed record RoutineDirectives(bool IsForward, bool IsOverload, bool IsExternal, bool IsInline, bool IsNoReturn,
        bool IsDeprecated, string? LibraryName, string? LinkName)
    {
        /// <summary>LANG-472 (C-127): <c>deprecated 'text'</c>, <c>platform</c>, <c>experimental</c>.</summary>
        public string? DeprecationMessage { get; init; }

        public bool IsPlatform { get; init; }

        public bool IsExperimental { get; init; }
    }

    /// <summary>LANG-460…473: calling conventions, <c>assembler</c> and hints are accepted; method directives are E1114.</summary>
    /// <summary>CMP-1011: the calling-convention directive among <paramref name="keywords"/> (the last one written), else <see cref="CallConvDirective.Default"/>.</summary>
    internal static CallConvDirective CallConvOf(IEnumerable<SyntaxToken> keywords)
    {
        var result = CallConvDirective.Default;
        foreach (var keyword in keywords)
        {
            result = keyword.ContextualKind switch
            {
                SyntaxKind.RegisterKeyword => CallConvDirective.Register,
                SyntaxKind.PascalKeyword => CallConvDirective.Pascal,
                SyntaxKind.CdeclKeyword => CallConvDirective.Cdecl,
                SyntaxKind.StdCallKeyword => CallConvDirective.StdCall,
                SyntaxKind.SafeCallKeyword => CallConvDirective.SafeCall,
                SyntaxKind.WinApiKeyword => CallConvDirective.WinApi,
                _ => result,
            };
        }

        return result;
    }

    private RoutineDirectives BindDirectives(SyntaxList<DirectiveSyntax> directives)
    {
        bool forward = false, overload = false, external = false, inline = false, noReturn = false, deprecated = false, platform = false, experimental = false;
        string? deprecation = null;
        string? library = null, linkName = null;
        foreach (var directive in directives)
        {
            switch (directive.Keyword.ContextualKind)
            {
                case SyntaxKind.ForwardKeyword:
                    forward = true;
                    break;
                case SyntaxKind.OverloadKeyword:
                    overload = true;
                    break;
                case SyntaxKind.InlineKeyword:
                    inline = true;
                    break;
                case SyntaxKind.NoReturnKeyword:
                    noReturn = true;
                    break;
                case SyntaxKind.DeprecatedKeyword:
                    deprecated = true;
                    deprecation = directive.Value is { } text ? FoldString(text) : null;
                    break;
                case SyntaxKind.PlatformKeyword:
                    platform = true;
                    break;
                case SyntaxKind.ExperimentalKeyword:
                    experimental = true;
                    break;
                case SyntaxKind.ExternalKeyword:
                    external = true;
                    if (directive.DelayedKeyword is { } delayed)
                    {
                        _diagnostics.Report(delayed, "E2900", "delayed");
                    }

                    library = directive.Value is { } lib ? FoldString(lib) : null;
                    linkName = directive.NameValue is { } import ? FoldString(import) : null;
                    break;
                case SyntaxKind.PublicKeyword:
                    linkName = directive.NameValue is { } exported ? FoldString(exported) : null;
                    break;
                case SyntaxKind.VirtualKeyword or SyntaxKind.DynamicKeyword or SyntaxKind.OverrideKeyword or SyntaxKind.AbstractKeyword
                    or SyntaxKind.FinalKeyword or SyntaxKind.ReintroduceKeyword or SyntaxKind.StaticKeyword or SyntaxKind.MessageKeyword:
                    _diagnostics.Report(directive.Keyword, "E1114", directive.Keyword.Text);
                    break;
            }
        }

        return new RoutineDirectives(forward, overload, external, inline, noReturn, deprecated, library, linkName)
        {
            DeprecationMessage = deprecation,
            IsPlatform = platform,
            IsExperimental = experimental,
        };
    }

    /// <summary>LANG-472: the hint directives after a constant, type or variable declaration; null when there are none.</summary>
    private static SymbolHints? BindHints(SyntaxList<HintDirectiveSyntax> directives)
    {
        if (directives.Count == 0)
        {
            return null;
        }

        var hints = new SymbolHints(false, null, false, false);
        foreach (var directive in directives)
        {
            hints = directive.Keyword.ContextualKind switch
            {
                SyntaxKind.DeprecatedKeyword => hints with { IsDeprecated = true, DeprecationMessage = directive.Message?.Value as string },
                SyntaxKind.PlatformKeyword => hints with { IsPlatform = true },
                SyntaxKind.ExperimentalKeyword => hints with { IsExperimental = true },
                _ => hints,
            };
        }

        return hints;
    }

    /// <summary>LANG-472 (C-232): the hint directives among a method's directives; null when there are none.</summary>
    private SymbolHints? BindHints(SyntaxList<DirectiveSyntax> directives)
    {
        SymbolHints? hints = null;
        foreach (var directive in directives)
        {
            var current = hints ?? new SymbolHints(false, null, false, false);
            hints = directive.Keyword.ContextualKind switch
            {
                SyntaxKind.DeprecatedKeyword => current with { IsDeprecated = true, DeprecationMessage = directive.Value is { } text ? FoldString(text) : null },
                SyntaxKind.PlatformKeyword => current with { IsPlatform = true },
                SyntaxKind.ExperimentalKeyword => current with { IsExperimental = true },
                _ => hints,
            };
        }

        return hints;
    }

    /// <summary>A constant string expression (library and link names); E2010 for another constant.</summary>
    private string? FoldString(ExpressionSyntax expression)
    {
        switch (_folder.Fold(expression))
        {
            case { Value: string text }:
                return text;
            case { Value: System.Numerics.BigInteger code, Type: CharTypeSymbol }:
                return ((char)(int)code).ToString();
            case { } other:
                _diagnostics.Report(expression, "E2010", other.Type.Name, _types.String.Name);
                return null;
            default:
                return null;
        }
    }

    /// <summary>Parameters (LANG-420 table) and result type of a new routine; also creates <c>Result</c> (LANG-410).</summary>
    private void BindSignature(RoutineSymbol routine, RoutineHeadingSyntax heading, SyntaxToken identifier, bool report)
    {
        if (heading.Keyword.Kind == SyntaxKind.FunctionKeyword || heading.Keyword.ContextualKind == SyntaxKind.OperatorKeyword)       // C-123: operators are functions
        {
            if (heading.ResultType is { } resultType)
            {
                routine.ReturnType = BindType(resultType);
                if (routine.ReturnType is TextTypeSymbol)
                {
                    if (report)
                    {
                        _diagnostics.Report(resultType, "E2023", routine.ReturnType.Name);     // LANG-412 (C-067)
                    }

                    routine.ReturnType = _types.Error;
                }
            }
            else
            {
                _diagnostics.Report(identifier, "E2102");
                routine.ReturnType = _types.Error;
            }

            routine.ResultVariable = new VariableSymbol("Result", routine.ReturnType, isGlobal: false, routine, []);
        }
        else if (heading.ResultType is { } procedureResult && report)
        {
            _diagnostics.Report(procedureResult, "E2103");
        }

        var names = new Scope(ScopeKind.Routine, parent: null, routine);
        if (routine.ResultVariable is { } result)
        {
            names.TryDeclare(result, out _);
        }

        routine.Parameters = BindParameters(heading.ParameterList, routine, names, report);
    }

    /// <summary>
    /// LANG-480: <c>procedure(…)</c> / <c>function(…): T</c> with any calling-convention directive (ignored, LANG-462); method
    /// pointers (<c>of object</c>, M2.5) and anonymous-method types (<c>reference to</c>, M2.6) are E2900.
    /// </summary>
    private TypeSymbol BindProcedural(ProceduralTypeSyntax syntax, string typeName, SyntaxToken? declaredAt)
    {
        ImmutableArray<Text.Location> locations = declaredAt is { } at ? [DiagnosticBag.LocationOf(at)] : [];
        if (syntax.ReferenceKeyword is not null)
        {
            // LANG-720 / ABI-097 (C-120): a compiler-made interface with the one method Invoke (IMT slot 3)
            var reference = NewMethodReference(typeName, _container, locations);
            BindAnonymousSignature(reference.Invoke, syntax.Keyword, syntax.ParameterList, syntax.ResultType, declaresResult: false);
            return reference;
        }

        TypeSymbol type;
        RoutineSymbol signature;
        if (syntax.ObjectKeyword is not null)
        {
            var method = new MethodPointerTypeSymbol(_types.Target, typeName, _container, locations);         // LANG-481 (C-103)
            (type, signature) = (method, method.Signature);
        }
        else
        {
            var plain = new ProceduralTypeSymbol(_types.Target, typeName, _container, locations);
            (type, signature) = (plain, plain.Signature);
        }

        signature.CallingConvention = CallConvOf(syntax.CallingConventions.Select(c => c.Keyword));        // CMP-1011

        if (syntax.Keyword.Kind == SyntaxKind.FunctionKeyword)
        {
            var result = syntax.ResultType is { IsMissing: false } resultType ? BindType(resultType) : _types.Error;
            if (result is TextTypeSymbol)
            {
                _diagnostics.Report(syntax.ResultType!, "E2023", result.Name);          // LANG-412
                result = _types.Error;
            }

            signature.ReturnType = result;
        }

        signature.Parameters = BindParameters(syntax.ParameterList, signature, new Scope(ScopeKind.Routine, parent: null, signature), report: true);
        return type;
    }

    /// <summary>
    /// A method reference interface (LANG-720, ABI-097): parent <c>IInterface</c>, one method <c>Invoke</c> without parameters yet.
    /// </summary>
    internal InterfaceTypeSymbol NewMethodReference(string name, Symbol? container, ImmutableArray<Text.Location> locations)
    {
        var type = new InterfaceTypeSymbol(_types.Target, name, container, locations) { IsForward = false, IsMethodReference = true };
        type.Parent = _types.IInterface as InterfaceTypeSymbol;
        var invoke = new MethodSymbol("Invoke", type, locations, declaration: null!, MethodKind.Ordinary, Visibility.Public)
        {
            VmtSlot = type.Parent?.AllMethods.Length ?? 0,
        };
        invoke.SelfParameter = new ParameterSymbol("Self", type, ParameterMode.Value, -1, invoke, []);
        type.Methods = [invoke];
        return type;
    }

    /// <summary>
    /// The parameters and result of a <c>reference to</c> type's <c>Invoke</c> or of an anonymous method (LANG-720); with
    /// <paramref name="declaresResult"/> the routine gets its <c>Result</c> variable.
    /// </summary>
    internal void BindAnonymousSignature(RoutineSymbol routine, SyntaxToken keyword, ParameterListSyntax? parameters, TypeSyntax? resultType, bool declaresResult)
    {
        if (keyword.Kind == SyntaxKind.FunctionKeyword)
        {
            var result = resultType is { IsMissing: false } ? BindType(resultType) : _types.Error;
            if (result is TextTypeSymbol)
            {
                _diagnostics.Report(resultType!, "E2023", result.Name);                  // LANG-412
                result = _types.Error;
            }

            routine.ReturnType = result;
            if (declaresResult)
            {
                routine.ResultVariable = new VariableSymbol("Result", result, isGlobal: false, routine, []);
            }
        }

        routine.Parameters = BindParameters(parameters, routine, new Scope(ScopeKind.Routine, parent: null, routine), report: true);
    }

    /// <summary>
    /// LANG-422 (C-105): the default value of a value or <c>const</c> parameter of a simple type, folded in the declaring scope
    /// and converted to the parameter type. E2115 for a parameter group, E2117 for <c>var</c>/<c>out</c> or another type;
    /// procedural types and properties keep E2900 (LANG-484: defaults are not available through procedural types).
    /// </summary>
    private ConstantValue? BindDefaultValue(ParameterSyntax parameter, ExpressionSyntax value, ParameterMode mode, TypeSymbol type, Symbol owner, bool report)
    {
        var diagnostics = report ? _diagnostics : new DiagnosticBag();
        if (owner is not RoutineSymbol || owner.ContainingSymbol is ProceduralTypeSymbol or MethodPointerTypeSymbol or InterfaceTypeSymbol { IsMethodReference: true })
        {
            diagnostics.Report(value, "E2900", "default parameter value outside a routine declaration");
            return null;
        }

        if (parameter.Identifiers.Count > 1)
        {
            diagnostics.Report(value, "E2115");
            return null;
        }

        if (mode is ParameterMode.Var or ParameterMode.Out
            || type is not (IntegerTypeSymbol or CharTypeSymbol or EnumTypeSymbol or BooleanTypeSymbol or RealTypeSymbol or StringTypeSymbol
                or PointerTypeSymbol or ClassTypeSymbol or ClassRefTypeSymbol or SetTypeSymbol or ProceduralTypeSymbol or InterfaceTypeSymbol or ErrorTypeSymbol))     // C-120: nil for an interface (IComparer<T> = nil)
        {
            diagnostics.Report(value, "E2117");
            return null;
        }

        var folder = report ? _folder : new DeclarationBinder(_types, _scope, _container, diagnostics)._folder;
        return folder.Fold(value) is { } constant
            ? _conversions.ConvertConstant(constant, type, value, diagnostics)
            : null;
    }

    /// <summary>The formal parameters of <paramref name="owner"/> (LANG-420 table); E2004 for duplicate names in <paramref name="names"/>.</summary>
    private ImmutableArray<ParameterSymbol> BindParameters(ParameterListSyntax? list, Symbol owner, Scope names, bool report) =>
        BindParameters(list?.Parameters ?? default, owner, names, report);

    /// <summary>A formal parameter list (also the index parameters of an array property, LANG-642).</summary>
    private ImmutableArray<ParameterSymbol> BindParameters(SeparatedSyntaxList<ParameterSyntax> list, Symbol owner, Scope names, bool report)
    {
        var routine = owner;
        var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>();
        var defaulted = false;
        foreach (var parameter in list)
        {
            var mode = parameter.Modifier?.ContextualKind switch
            {
                SyntaxKind.ConstKeyword => ParameterMode.Const,
                SyntaxKind.VarKeyword => ParameterMode.Var,
                SyntaxKind.OutKeyword => ParameterMode.Out,
                _ => ParameterMode.Value,
            };
            // routines.md §3 (C-202): no type = untyped, only with var/const/out (E2118 below)
            var type = parameter.Type is { } typeSyntax ? BindType(typeSyntax, parameter: true) : mode == ParameterMode.Value ? _types.Error : _types.Untyped;

            var defaultConstant = parameter.DefaultValue is { } defaultValue ? BindDefaultValue(parameter, defaultValue, mode, type, owner, report) : null;
            if (parameter.DefaultValue is not null)
            {
                defaulted = true;
            }

            foreach (var name in parameter.Identifiers)
            {
                var token = name.Identifier;
                if (token.IsMissing)
                {
                    continue;
                }

                if (parameter.Type is null && mode == ParameterMode.Value && report)
                {
                    _diagnostics.Report(token, "E2118", NameOf(token));
                }

                var symbol = new ParameterSymbol(NameOf(token), type, mode, parameters.Count, routine, [DiagnosticBag.LocationOf(token)], _types.Int64)
                {
                    DefaultValue = defaultConstant,
                    IsRef = mode == ParameterMode.Const && type is not (OpenArrayTypeSymbol or UntypedTypeSymbol) && HasRefAttribute(parameter),  // C-231
                };
                if (defaulted && parameter.DefaultValue is null && report)
                {
                    _diagnostics.Report(token, "E2114", NameOf(token));                        // LANG-422
                }

                if (!names.TryDeclare(symbol, out var previous) && report)
                {
                    _diagnostics.ReportRedeclared(token, NameOf(token), previous!);
                }

                parameters.Add(symbol);
            }
        }

        return parameters.ToImmutable();
    }

    /// <summary>LANG-763: <c>[Ref]</c> among the parameter's attributes (checked by <see cref="AttributeBinder"/>).</summary>
    private static bool HasRefAttribute(ParameterSyntax parameter) =>
        parameter.Attributes.Concat(parameter.ModifierAttributes).SelectMany(l => l.Attributes)               // C-232: also const [Ref] X
            .Any(a => a.Name is IdentifierNameSyntax { Identifier: var token } && IdentifierComparer.Instance.Equals(token.Value as string ?? token.Text, "Ref"));

    /// <summary>
    /// LANG-401: the undefined declaration a definition completes. With its parameter list omitted it completes the
    /// only undefined routine of the name; otherwise the one with the same parameter modes and types (names and result
    /// type must match too, E2101). A mismatch against a single non-overloaded declaration is E2101 and still completes
    /// it (no E2100 cascade); otherwise the definition declares a new routine.
    /// </summary>
    private RoutineSymbol? FindDeclarationToDefine(RoutineHeadingSyntax heading, SyntaxToken identifier, ImmutableArray<RoutineSymbol> existing)
    {
        var undefined = existing.Where(r => r.AwaitsDefinition).ToList();
        if (undefined.Count == 0)
        {
            return null;
        }

        if (heading.ParameterList is null && undefined.Count == 1)
        {
            var only = undefined[0];
            var omitted = heading.ResultType is null || ReferenceEquals(BindType(heading.ResultType), only.ReturnType);
            if (!omitted || (heading.Keyword.Kind == SyntaxKind.FunctionKeyword) != (only.ReturnType != null))
            {
                _diagnostics.Report(identifier, "E2101", NameOf(identifier));
            }

            return only;
        }

        var probe = new RoutineSymbol(NameOf(identifier), _container, [], undefined[0].Declaration);
        BindSignature(probe, heading, identifier, report: false);
        var match = undefined.FirstOrDefault(r => SameParameterTypes(r, probe));
        if (match is null)
        {
            if (undefined.Count == 1 && !undefined[0].IsOverload)
            {
                _diagnostics.Report(identifier, "E2101", NameOf(identifier));
                return undefined[0];
            }

            return null;
        }

        var sameNames = match.Parameters.Select(p => p.Name).SequenceEqual(probe.Parameters.Select(p => p.Name), IdentifierComparer.Instance);
        if (!sameNames || !ReferenceEquals(match.ReturnType, probe.ReturnType) || !SameRefParameters(match, probe))
        {
            _diagnostics.Report(identifier, "E2101", NameOf(identifier));
        }

        return match;
    }

    /// <summary>
    /// LANG-689 (C-301): the heading of a definition writes a calling convention that disagrees with its declaration on
    /// <c>safecall</c>. A definition without a convention directive takes the declaration's.
    /// </summary>
    private static bool DisagreesOnSafecall(RoutineSymbol declared, RoutineDeclarationSyntax definition) =>
        !ReferenceEquals(declared.Declaration, definition)
        && definition.Directives.Select(d => d.Keyword).Where(k => CallConvOf([k]) != CallConvDirective.Default).ToList() is { Count: > 0 } written
        && (CallConvOf(written) == CallConvDirective.SafeCall) != declared.IsSafecall;

    /// <summary>LANG-440: both have defaults and the same parameters before their first defaulted one (a call could match both).</summary>
    private static bool SameRequiredParameters(RoutineSymbol a, RoutineSymbol b)
    {
        if (!a.Parameters.Any(p => p.DefaultValue is not null) && !b.Parameters.Any(p => p.DefaultValue is not null))
        {
            return false;
        }

        var (ra, rb) = (a.Parameters.TakeWhile(p => p.DefaultValue is null).ToList(), b.Parameters.TakeWhile(p => p.DefaultValue is null).ToList());
        return ra.Count == rb.Count && ra.Zip(rb).All(p => p.First.Mode == p.Second.Mode && ReferenceEquals(p.First.Type, p.Second.Type));
    }

    private static bool SameParameterTypes(RoutineSymbol a, RoutineSymbol b) =>
        a.Parameters.Length == b.Parameters.Length
        && a.Parameters.Zip(b.Parameters).All(p => p.First.Mode == p.Second.Mode && ReferenceEquals(p.First.Type, p.Second.Type));

    /// <summary>
    /// LANG-763 (C-232): the parameters agree on <c>[Ref]</c> — a <c>[Ref] const</c> parameter is passed as an address, so a declaration
    /// and its implementation, a method and its override or interface method must not differ in it. Overloads are not told apart by it.
    /// </summary>
    internal static bool SameRefParameters(RoutineSymbol a, RoutineSymbol b) =>
        a.Parameters.Length == b.Parameters.Length && a.Parameters.Zip(b.Parameters).All(p => p.First.IsRef == p.Second.IsRef);

    /// <summary>LANG-440: a name shared only by routines that all carry <c>overload</c> and differ in their parameters.</summary>
    private bool DeclareRoutine(SyntaxToken identifier, RoutineSymbol routine, ImmutableArray<RoutineSymbol> existing)
    {
        var name = NameOf(identifier);
        if (existing.IsEmpty)
        {
            var previous = _sharedScope?.LookupLocal(name);
            if (previous is null && _scope.TryDeclare(routine, out previous))
            {
                return true;
            }

            _diagnostics.ReportRedeclared(identifier, name, previous!);
            return false;
        }

        if (!routine.IsOverload || existing.Any(r => !r.IsOverload))
        {
            _diagnostics.Report(identifier, "E2120", name);
            return false;
        }

        if (existing.Any(r => SameParameterTypes(r, routine) || SameRequiredParameters(r, routine)))
        {
            _diagnostics.Report(identifier, "E2121", name);
            return false;
        }

        if (_scope.LookupLocal(name) is null)
        {
            _scope.TryDeclare(routine, out _);                                  // overloads an interface routine
        }
        else
        {
            _scope.AddOverload(routine);
        }

        return true;
    }

    /// <summary>LANG-400/460: E2100 for each forward routine of this declaration part left without a definition.</summary>
    private void ReportUnsatisfied()
    {
        foreach (var routine in _awaitingDefinition.Where(r => r.AwaitsDefinition))
        {
            _diagnostics.Report(routine.Declaration.Heading.Name, "E2100", routine.Name);
        }
    }

    /// <summary>
    /// The type a type syntax denotes; the error type (after a diagnostic) when it cannot be bound. A type constructor
    /// creates a new type named after <paramref name="declaredAt"/> (a type declaration) or after its own text (an
    /// anonymous type); enumeration elements go into <paramref name="declared"/>.
    /// </summary>
    private TypeSymbol BindType(TypeSyntax type, SyntaxToken? declaredAt = null, ImmutableArray<Symbol>.Builder? declared = null, bool parameter = false)
    {
        if (type.IsMissing)
        {
            return _types.Error;
        }

        var typeName = TakeInstanceName() ?? (declaredAt is { } identifier ? NameOf(identifier) : AnonymousName(type));
        switch (type)
        {
            case EnumTypeSyntax enumType:
                return BindEnum(enumType, typeName, declaredAt, declared ?? ImmutableArray.CreateBuilder<Symbol>());
            case SubrangeTypeSyntax subrange:
                return BindSubrange(subrange, typeName, declaredAt);
            case SetTypeSyntax set:
                return BindSet(set, typeName, declaredAt, declared);
            case ArrayTypeSyntax { IndexTypes.Count: > 0, ElementType: not null } array:
                return BindArray(array, typeName, declaredAt, declared);
            case RecordTypeSyntax record:
                return BindRecord(record, typeName, declaredAt, declared);
            case PointerTypeSyntax pointer:
                return BindPointer(pointer, typeName, declared);
            case ProceduralTypeSyntax procedural:
                return BindProcedural(procedural, typeName, declaredAt);
            case ClassRefTypeSyntax classRef:
                // LANG-652: class of C, C a class (possibly still forward-declared in this section)
                switch (BindType(classRef.ClassType))
                {
                    case ClassTypeSymbol referenced when declaredAt is { } at:
                        return new ClassRefTypeSymbol(_types.Target, typeName, referenced, _container, [DiagnosticBag.LocationOf(at)]);
                    case ClassTypeSymbol referenced:
                        return referenced.Reference;
                    case ErrorTypeSymbol error:
                        return error;
                    default:
                        _diagnostics.Report(classRef.ClassType, "E2603");
                        return _types.Error;
                }
            case ArrayTypeSyntax { ElementType: null } when parameter:
                // LANG-433 (C-107): an open array of the TVarRec record in scope (System's)
                if (_scope.Lookup("TVarRec") is RecordTypeSymbol varRec)
                {
                    return _types.OpenArrayOf(varRec, isArrayOfConst: true);
                }

                _diagnostics.ReportUndeclared(type, "TVarRec");
                return _types.Error;
            case ArrayTypeSyntax { ElementType: null }:
                return Unsupported(type, "array of const");                     // only as a parameter type
            case ArrayTypeSyntax { ElementType: { } element } when parameter:
                return BindType(element) is { TypeKind: not TypeKind.Error } elementType ? _types.OpenArrayOf(elementType) : _types.Error;   // LANG-430 (C-106)
            case ArrayTypeSyntax array:
                return BindDynamicArray(array, typeName, declaredAt, declared);
            case NamedTypeSyntax { Name: var name }:
                return ResolveTypeName(name);                           // U.TName, TOuter.TInner, U.G<X>.TNode (LANG-311, 538, 770, 780)
            case StringTypeSyntax { Length: null }:
                return _types.String;
            case StringTypeSyntax { Length: { } length }:
                return BindShortString(length, typeName);
            default:
                return Unsupported(type, FeatureName(type.Kind));        // type constructors arrive with M2.2+
        }
    }

    /// <summary>
    /// LANG-200 <c>string[N]</c>: a short string of capacity N, an integer constant in 1..255 (E2012 otherwise, C-063 decision:
    /// the range is shown as the type <c>1..255</c>). Every occurrence is a new type (LANG-206).
    /// </summary>
    private TypeSymbol BindShortString(ExpressionSyntax length, string typeName)
    {
        switch (_folder.Fold(length))
        {
            case null:
                return _types.Error;
            case { Value: BigInteger n } when n >= 1 && n <= 255:
                return new StringTypeSymbol(_types.Target, typeName, StringVariant.Short, codePage: 65001, maxLength: (int)n);
            case { Value: BigInteger } value:
                _diagnostics.Report(length, "E2012", value.ToDisplayString(), "1..255");
                return _types.Error;
            case var value:
                _diagnostics.Report(length, "E2010", value.Type.Name, _types.Integer.Name);
                return _types.Error;
        }
    }

    private TypeSymbol BindTypeName(IdentifierNameSyntax name)
    {
        var identifier = name.Identifier;
        if (identifier.IsMissing)
        {
            return _types.Error;
        }

        var text = NameOf(identifier);
        var lookup = _scope.For(name);
        switch (lookup.Lookup(text))
        {
            case null when SmallestGenericArity(lookup, text) is { } arity:
                _diagnostics.Report(name, "E2703", text, arity);                    // LANG-781 (C-345): TBox where only TBox<T> exists
                return _types.Error;
            case null when _declaringType is { } declaring && IdentifierComparer.Instance.Equals(declaring.Name, text):
                // C-067: a type naming itself (not through ^T, LANG-208): an alias of itself is circular, a use inside its own
                // structure (record field, array element, set base…) needs the incomplete type
                var alias = declaring.Type is NamedTypeSyntax { Name: var aliased } && ReferenceEquals(aliased, name);
                _diagnostics.Report(name, alias ? "E2022" : "E2007", text);
                return _types.Error;
            case null:
                _diagnostics.ReportUndeclared(name, text);
                return _types.Error;
            case HelperTypeSymbol helper:
                _diagnostics.Report(name, "E2751", helper.Name);                // LANG-754 (C-124)
                return _types.Error;
            case TypeSymbol type:
                HintChecks.CheckName(_diagnostics, _types, _scope, name, text, type);              // LANG-472 (C-232)
                return type;
            default:
                _diagnostics.Report(name, "E2002", text);
                return _types.Error;
        }
    }

    /// <summary>LANG-781 (C-345): the smallest arity of the generic types named <paramref name="text"/> visible in <paramref name="scope"/>, or null.</summary>
    internal static int? SmallestGenericArity(Scope scope, string text) =>
        Enumerable.Range(1, 16).Select(n => scope.Lookup(GenericTypeSymbol.KeyOf(text, n))).OfType<GenericTypeSymbol>().FirstOrDefault()?.ParameterNames.Length;

    /// <summary>The type of an inline variable or constant (LANG-385, C-125), bound in a body's scope.</summary>
    internal TypeSymbol BindLocalType(TypeSyntax type) => BindType(type);

    private TypeSymbol Unsupported(SyntaxNode node, string feature)
    {
        _diagnostics.Report(node, "E2900", feature);
        return _types.Error;
    }

    /// <summary>"ArrayType" gives "array type".</summary>
    internal static string FeatureName(SyntaxKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    /// <summary>LANG-346: string true constants only (a character constant becomes a one-character string).</summary>
    private ConstantValue? AsResourceString(ConstantValue value, SyntaxNode node)
    {
        switch (value.Value)
        {
            case string:
                return value;
            case System.Numerics.BigInteger code when value.Type is CharTypeSymbol:
                return new ConstantValue(((char)(int)code).ToString(), _types.String);
            default:
                _diagnostics.Report(node, "E2010", value.Type.Name, _types.String.Name);
                return null;
        }
    }

    private ConstantValue? NotConstant(SyntaxNode node)
    {
        if (!node.ContainsDiagnostics)
        {
            _diagnostics.Report(node, "E2050");
        }

        return null;
    }

    private ConstantSymbol Constant(SyntaxToken identifier, TypeSymbol type, ConstantValue? value, bool isTyped) =>
        new(NameOf(identifier), type, value, isTyped, _container, [DiagnosticBag.LocationOf(identifier)]);

    /// <summary>Adds the symbol to the scope; E2004 when the scope already declares the name (LANG-402, LANG-518).</summary>
    private void Declare(SyntaxToken identifier, Symbol symbol, ImmutableArray<Symbol>.Builder declared)
    {
        var previous = _sharedScope?.LookupLocal(symbol.Name);
        if (previous is null && _scope.TryDeclare(symbol, out previous))
        {
            declared.Add(symbol);
        }
        else
        {
            _diagnostics.ReportRedeclared(identifier, NameOf(identifier), previous!);
        }
    }

    private static string NameOf(SyntaxToken identifier) => identifier.Value as string ?? identifier.Text;

    private static string NameOf(NameSyntax name) =>
        string.Join(".", name.DescendantTokens().Where(t => t.Kind == SyntaxKind.IdentifierToken).Select(NameOf));
}
