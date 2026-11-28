using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
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
    /// (LANG-601; E2015 at the end of the section otherwise). At unit/program level or nested in a class or record (LANG-771);
    /// E2602 inside a routine (LANG-603).
    /// </summary>
    private void BindClassDeclaration(SyntaxToken identifier, ClassTypeSyntax syntax, ImmutableArray<Symbol>.Builder declared,
        List<(ClassTypeSymbol Type, SyntaxToken Identifier)> forwardClasses)
    {
        var name = NameOf(identifier);
        if (!IsTypeLevel)
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
            type = new ClassTypeSymbol(_types.Target, name, _container, [DiagnosticBag.LocationOf(identifier)]);
            _scope.TryDeclare(name, type, out _);
            declared.Add(type);
            if (_container is UnitSymbol { IsProgram: false } unit && IdentifierComparer.Instance.Equals(unit.Name, "System")
                && IdentifierComparer.Instance.Equals(name, "TObject"))
            {
                _types.TObject = type;                                          // the root class (LANG-600)
            }
            else if (_container is UnitSymbol { IsProgram: false } system && IdentifierComparer.Instance.Equals(system.Name, "System")
                && IdentifierComparer.Instance.Equals(name, "TInterfacedObject"))
            {
                _types.TInterfacedObject = type;                                // the closure ancestor (CMP-380)
            }

            if (isForward)
            {
                forwardClasses.Add((type, identifier));
                return;
            }
        }

        BindClassBody(type, syntax);
    }

    /// <summary>
    /// Parent, modifiers, fields (ABI §7.1 layout), methods and the VMT slots of one class. <paramref name="bound"/>: the parent and
    /// listed interfaces already bound by the binder that handed the body to this one (C-354).
    /// </summary>
    private void BindClassBody(ClassTypeSymbol type, ClassTypeSyntax syntax,
        (ClassTypeSymbol? Parent, List<(InterfaceTypeSymbol Interface, TypeSyntax Syntax)> Listed)? bound = null)
    {
        if (!(_scope.Kind == ScopeKind.TypeMember && ReferenceEquals(_scope.Owner, type))
            && Members(syntax.Members, Visibility.Public).Any(m => m.Member is ConstSectionSyntax or TypeSectionSyntax))
        {
            // LANG-770 (C-122): nested types and constants live in a member scope that the whole body is bound in (C-344: also
            // the body of a nested class, whose binder's scope is the enclosing type's member scope)
            // LANG-772 (C-344): the ancestors' nested types and constants are seen below the class's own (filled once the parent is known)
            var inherited = new Scope(ScopeKind.TypeMember, _scope, type);
            var inner = new DeclarationBinder(_types, new Scope(ScopeKind.TypeMember, inherited, type), type, _diagnostics)
            {
                _sectionTypeNames = _sectionTypeNames,
                _forwardPointers = _forwardPointers,
                _inheritedNested = inherited,
            };
            inner.BindClassBody(type, syntax);
            return;
        }

        type.IsForward = false;
        type.IsAbstract = syntax.Modifier?.ContextualKind == SyntaxKind.AbstractKeyword;
        type.IsSealed = syntax.Modifier?.ContextualKind == SyntaxKind.SealedKeyword;
        var listed = bound?.Listed ?? [];
        var resolutions = new List<MethodResolutionSyntax>();
        type.Parent = bound is { } given ? given.Parent : BindParent(type, syntax.Heritage, listed);
        if (_inheritedNested is { } inheritedNested)
        {
            _inheritedNested = null;
            DeclareInheritedNested(inheritedNested, type.Parent);
        }
        else if (bound is null && !(_scope.Kind == ScopeKind.TypeMember && ReferenceEquals(_scope.Owner, type))
            && (type.Parent?.SelfAndAncestors() ?? []).Any(a => !a.NestedMembers.IsEmpty || !a.NestedAliases.IsEmpty))
        {
            // LANG-772 (C-354): a class without type or const sections of its own still sees its ancestors' nested names in its
            // member list; its body is bound in a member scope that holds them (the container stays, no nested type is declared)
            var inherited = new Scope(ScopeKind.TypeMember, _scope, type);
            DeclareInheritedNested(inherited, type.Parent);
            new DeclarationBinder(_types, new Scope(ScopeKind.TypeMember, inherited, type), _container, _diagnostics)
            {
                _sectionTypeNames = _sectionTypeNames,
                _forwardPointers = _forwardPointers,
            }.BindClassBody(type, syntax, (type.Parent, listed));
            return;
        }

        var state = syntax.ClassKeyword.DirectiveState;
        type.IsTypeInfo = state.TypeInfo || type.Parent?.IsTypeInfo == true;                 // LANG-611, ABI-142
        type.RttiScope = state.Rtti switch                                                  // LANG-882 (C-169)
        {
            "broad" => RttiScope.Broad,
            "published" => RttiScope.Published,
            _ => _types.DefaultRttiScope,
        };

        var fields = ImmutableArray.CreateBuilder<FieldSymbol>();
        var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
        var names = new Dictionary<string, Symbol>(IdentifierComparer.Instance);
        var properties = new List<(PropertyDeclarationSyntax Syntax, Visibility Visibility)>();
        var classVariables = ImmutableArray.CreateBuilder<VariableSymbol>();
        var nested = ImmutableArray.CreateBuilder<Symbol>();
        var nestedAliases = ImmutableArray.CreateBuilder<(string Name, TypeSymbol Target, Visibility Visibility)>();
        var genericMethods = ImmutableArray.CreateBuilder<GenericMethodSymbol>();
        var end = type.Parent?.InstanceSize ?? 8;                              // offset 0: the VMT pointer (ABI §7.1)
        foreach (var (member, visibility) in Members(syntax.Members, type.IsTypeInfo ? Visibility.Published : Visibility.Public))
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
                    BindClassVariables(type, section, visibility, classVariables, names);              // LANG-615, CMP-370
                    break;
                case MethodDeclarationSyntax classConstructor when IsClassConstructorHeading(classConstructor.Heading):
                    BindClassConstructorDeclaration(type, classConstructor, visibility);               // LANG-786 (C-374)
                    break;
                case MethodDeclarationSyntax { Heading.TypeParameterList: not null } genericMethod:
                    genericMethods.Add(BindGenericMethodDeclaration(type, genericMethod, visibility));          // C-117 (LANG-701)
                    break;
                case MethodDeclarationSyntax method when BindMethodDeclaration(type, method, visibility) is { } symbol:
                    if (names.TryGetValue(symbol.Name, out var existing)
                        && !(existing is MethodSymbol other && other.IsOverload && symbol.IsOverload && !SameParameterTypes(other, symbol)))
                    {
                        _diagnostics.ReportRedeclared(((IdentifierNameSyntax)method.Heading.Name).Identifier, symbol.Name, existing);
                        break;
                    }

                    if (symbol.MessageId is { } id && methods.FirstOrDefault(m => m.MessageId == id) is { } handler)
                    {
                        // C-212 (LANG-622): one handler per message id and class
                        var value = method.Directives.First(d => d.Keyword.ContextualKind == SyntaxKind.MessageKeyword).Value!;
                        _diagnostics.Report(value, "E2004", [new RelatedLocation(handler.Locations[0], "previous declaration")], id.ToString(CultureInfo.InvariantCulture));
                        symbol.MessageId = null;
                    }

                    names.TryAdd(symbol.Name, symbol);
                    methods.Add(symbol);
                    break;
                case MethodDeclarationSyntax:
                    break;
                case PropertyDeclarationSyntax property:
                    properties.Add((property, visibility));                     // bound once all fields and methods are known
                    break;
                case MethodResolutionSyntax resolution:
                    resolutions.Add(resolution);                                // LANG-661, bound with the listed interfaces
                    break;
                case ConstSectionSyntax or TypeSectionSyntax:
                    nested.AddRange(BindNestedSection(member, nestedAliases, visibility));  // LANG-770 (C-122)
                    break;
                default:
                    Unsupported(member, "class " + FeatureName(member.Kind));
                    break;
            }
        }

        type.Fields = fields.ToImmutable();
        type.ClassVariables = classVariables.ToImmutable();
        type.NestedMembers = nested.ToImmutable();
        type.NestedAliases = nestedAliases.ToImmutable();
        type.Methods = methods.ToImmutable();
        type.GenericMethods = CheckMixedOverloads(methods, genericMethods);                // LANG-978 (C-419)
        type.VirtualSlots = AssignSlots(type);
        var implements = new List<(PropertySymbol Property, PropertySpecifierSyntax Specifier)>();
        type.Properties = BindProperties(type, properties, names, implements);  // before the interfaces: `implements` (LANG-690)
        end = BindImplementedInterfaces(type, listed, resolutions, implements, end);       // ABI §7.1: the hidden IMT pointers follow the fields
        type.InstanceSize = (end + 7) / 8 * 8;                                   // ABI-070
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

    /// <summary>LANG-772 (C-344, C-354): declares the nested types, constants and nested aliases of <paramref name="parent"/> and its ancestors, nearest first.</summary>
    internal static void DeclareInheritedNested(Scope scope, ClassTypeSymbol? parent)
    {
        foreach (var ancestor in parent?.SelfAndAncestors() ?? [])
        {
            foreach (var member in ancestor.NestedMembers)
            {
                scope.TryDeclare(member, out _);                                // the nearest ancestor's declaration wins
            }

            foreach (var (name, target, _) in ancestor.NestedAliases)
            {
                scope.TryDeclare(name, target, out _);
            }
        }
    }

    /// <summary>
    /// LANG-600: the first heritage entry is the parent class (E2007 while it is only forward-declared, E2601 when sealed,
    /// E2603 for a non-class type); without heritage the parent is <c>TObject</c>. The other entries — and the first when it is an
    /// interface (the parent is then <c>TObject</c>) — are the implemented interfaces, added to <paramref name="listed"/> (E2652
    /// for a non-interface, E2007 while forward; LANG-661, C-098).
    /// </summary>
    private ClassTypeSymbol? BindParent(ClassTypeSymbol type, HeritageSyntax? heritage, List<(InterfaceTypeSymbol Interface, TypeSyntax Syntax)> listed)
    {
        var root = _types.TObject as ClassTypeSymbol;
        if (heritage is null || heritage.Types.Count == 0)
        {
            return ReferenceEquals(root, type) ? null : root;
        }

        var firstType = BindType(heritage.Types[0]);
        for (var i = firstType is InterfaceTypeSymbol ? 0 : 1; i < heritage.Types.Count; i++)
        {
            switch (i == 0 ? firstType : BindType(heritage.Types[i]))
            {
                case InterfaceTypeSymbol { IsForward: true } forward:
                    _diagnostics.Report(heritage.Types[i], "E2007", forward.Name);
                    break;
                case InterfaceTypeSymbol { IsDispatchInterface: true } dispatch:
                    _diagnostics.Report(heritage.Types[i], "E2661", dispatch.Name);          // LANG-696 (C-303)
                    break;
                case InterfaceTypeSymbol intf:
                    listed.Add((intf, heritage.Types[i]));
                    break;
                case ErrorTypeSymbol:
                    break;
                default:
                    _diagnostics.Report(heritage.Types[i], "E2652");
                    break;
            }
        }

        var first = heritage.Types[0];
        switch (firstType)
        {
            case InterfaceTypeSymbol:
                return ReferenceEquals(root, type) ? null : root;
            case ClassTypeSymbol parent when ReferenceEquals(parent, type) || parent.IsForward
                || TypeMembers.EnclosingTypes(type).Contains(parent):                  // LANG-777 (C-344): an enclosing type is incomplete
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

    /// <summary><c>class var</c> fields (LANG-615): global variables owned by the class (link name <c>U.TClass.Name</c>, CMP-370).</summary>
    private void BindClassVariables(TypeSymbol type, ClassVarSectionSyntax section, Visibility visibility,
        ImmutableArray<VariableSymbol>.Builder classVariables, Dictionary<string, Symbol> names)
    {
        foreach (var field in section.Fields)
        {
            var fieldType = BindFieldType(field.Type);
            if (fieldType.TypeKind == TypeKind.Error)
            {
                continue;
            }

            if (fieldType is TextTypeSymbol)
            {
                Unsupported(field.Type, "class var of type " + fieldType.Name);
                continue;
            }

            foreach (var identifier in field.Identifiers.Select(i => i.Identifier).Where(i => !i.IsMissing))
            {
                var name = NameOf(identifier);
                var symbol = new VariableSymbol(name, fieldType, isGlobal: true, type, [DiagnosticBag.LocationOf(identifier)], visibility: visibility);
                if (!names.TryAdd(name, symbol))
                {
                    _diagnostics.ReportRedeclared(identifier, name, names[name]);
                    continue;
                }

                classVariables.Add(symbol);
            }
        }
    }

    /// <summary>Instance fields at the next multiple of their alignment (cap 8, ABI §7.1); returns the new end offset.</summary>
    private long BindField(ClassTypeSymbol type, FieldDeclarationSyntax field, Visibility visibility, long end,
        ImmutableArray<FieldSymbol>.Builder fields, Dictionary<string, Symbol> names)
    {
        var fieldType = BindFieldType(field.Type);
        if (fieldType.TypeKind == TypeKind.Error)
        {
            return end;
        }

        if (IsEnclosingRecordUnderConstruction(fieldType))
        {
            _diagnostics.Report(field.Type, "E2007", fieldType.Name);          // LANG-777 (C-344): its size is not known yet
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

            if (visibility == Visibility.Published && fieldType is not ClassTypeSymbol)
            {
                _diagnostics.Report(identifier, "E2611", name);                                           // LANG-616 (C-112)
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
            SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword => null,
            _ => heading.Keyword.Text.ToLowerInvariant(),
        } ?? (heading.TypeParameterList is not null && !_genericMethodInstance ? "generic method" : null);
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
            // ABI-084: the instance; for class methods the VMT, typed class of the class (LANG-620)
            method.SelfParameter = new ParameterSymbol("Self", isClass ? type.Reference : type, ParameterMode.Value, -1, method, []);
        }

        if (kind is MethodKind.Constructor or MethodKind.Destructor)
        {
            method.FlagParameter = new ParameterSymbol(kind == MethodKind.Constructor ? "Alloc" : "Dealloc", _types.Byte, ParameterMode.Value, -1, method, []);
        }

        if (method.MessageId is not null
            && (kind != MethodKind.Ordinary || method.ReturnType is not null || method.Parameters is not [{ Mode: ParameterMode.Var }]))
        {
            _diagnostics.Report(identifier, "E2627", method.Name);                                         // LANG-622
            method.MessageId = null;
        }

        return method;
    }

    /// <summary>LANG-622: the id of <c>message N</c>, an integer constant 1..65535 (E2014 not an integer, E2012 out of range).</summary>
    private int? BindMessageId(ExpressionSyntax value)
    {
        if (_folder.Fold(value) is not { } constant)
        {
            return null;
        }

        if (constant.Value is not BigInteger number)
        {
            _diagnostics.Report(value, "E2014", "Integer");
            return null;
        }

        if (number < 1 || number > 65535)
        {
            _diagnostics.Report(value, "E2012", number.ToString(CultureInfo.InvariantCulture), "1..65535");
            return null;
        }

        return (int)number;
    }

    /// <summary>LANG-622/623 binding directives; calling conventions and hints are accepted, <c>message</c> is M2.5.</summary>
    private void BindMethodDirectives(MethodSymbol method, SyntaxList<DirectiveSyntax> directives, bool isClass)
    {
        method.CallingConvention = CallConvOf(directives.Select(d => d.Keyword));                          // CMP-1011
        method.Hints = BindHints(directives);                                                              // LANG-472 (C-232)
        if (method.IsSafecall && (method.MethodKind is MethodKind.Constructor or MethodKind.Destructor || method.IsRecordConstructor || method.OperatorName is not null))
        {
            // LANG-685 (C-301)
            _diagnostics.Report(directives.First(d => d.Keyword.ContextualKind == SyntaxKind.SafeCallKeyword).Keyword, "E2656",
                method.OperatorName is not null ? "an operator" : method.MethodKind == MethodKind.Destructor ? "a destructor" : "a constructor");
            method.CallingConvention = CallConvDirective.Default;
        }

        foreach (var directive in directives)
        {
            if (IsRestrictedDirective(directive))
            {
                continue;                                                                                  // LANG-978 (C-420)
            }

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
                case SyntaxKind.MessageKeyword when directive.Value is { } id:
                    method.MessageId = BindMessageId(id);                                                    // LANG-622 (C-113)
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
            if (method.IsOverload && inherited is not null)
            {
                // LANG-441 (C-104): an overload overrides or hides only the inherited virtual method with its own parameters
                var same = slots.LastOrDefault(s => IdentifierComparer.Instance.Equals(s.Name, method.Name) && !ReferenceEquals(s.ContainingType, type)
                    && SameSignature(s, method));
                inherited = same ?? (method.IsOverride ? inherited : null);
            }

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
        a.MethodKind == b.MethodKind && SameParameterTypes(a, b) && a.IsSafecall == b.IsSafecall        // LANG-689 (C-301)
        && SameRefParameters(a, b)                                                                      // LANG-763 (C-232)
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
        if (heading.Name is not QualifiedNameSyntax { Left: { } className, Right: IdentifierNameSyntax { Identifier: { IsMissing: false } identifier } }
            || className.ContainsDiagnostics || declaration.Block is null || !IsGlobalLevel)
        {
            _diagnostics.Report(heading.Name, "E2900", "method implementation");
            return;
        }

        // the path's segments, left to right; the first generic one names the generic the implementation belongs to (LANG-704, 776, 778)
        var segments = new List<(NameSyntax Prefix, SimpleNameSyntax Segment)>();
        for (var node = className; ; node = ((QualifiedNameSyntax)node).Left)
        {
            if (node is QualifiedNameSyntax step)
            {
                segments.Insert(0, (step, step.Right));
                continue;
            }

            segments.Insert(0, (node, (SimpleNameSyntax)node));
            break;
        }

        if (segments.FindIndex(s => s.Segment is GenericNameSyntax) is var first and >= 0)
        {
            var generic = (GenericNameSyntax)segments[first].Segment;
            ImmutableArray<SimpleNameSyntax> rest = [.. segments.Skip(first + 1).Select(s => s.Segment)];
            if (first == 0)
            {
                BindGenericMethodImplementation(declaration, generic, identifier, rest);   // C-116, C-344: TList<T>.TEnumerator.M
                return;
            }

            // LANG-778 (C-345): TOuter.TBox<T>.M — a generic nested in a type of this unit
            if (ResolveTypeQualifier(((QualifiedNameSyntax)segments[first].Prefix).Left, implementationPath: true) is { } outer)
            {
                BindGenericMethodImplementation(declaration, generic, identifier, rest, outer);
            }

            return;
        }

        switch (ResolveTypeQualifier(className, implementationPath: true))
        {
            case null:
                return;
            case (ClassTypeSymbol or RecordTypeSymbol or HelperTypeSymbol) and var type when ReferenceEquals(TypeMembers.Outermost(type).ContainingSymbol, _container):
                DefineMethod(type, declaration, identifier);                    // C-122: records and nested types (LANG-730, 770)
                return;
            case (ClassTypeSymbol or RecordTypeSymbol or HelperTypeSymbol) and var type:
                _diagnostics.Report(identifier, "E2606", NameOf(identifier), type.Name);
                return;
            default:
                _diagnostics.Report(className, "E2603");
                return;
        }
    }

    private void DefineMethod(TypeSymbol type, RoutineDeclarationSyntax declaration, SyntaxToken identifier)
    {
        var heading = declaration.Heading;
        var name = NameOf(identifier);
        if (IsClassConstructorHeading(heading))
        {
            DefineClassConstructor(type, declaration, identifier);                      // LANG-788 (C-374)
            return;
        }

        if (heading.TypeParameterList is { } typeParameters)
        {
            DefineGenericMethod(type, declaration, identifier, typeParameters);         // C-117 (LANG-704)
            return;
        }

        var candidates = TypeMembers.Methods(type).Where(m => IdentifierComparer.Instance.Equals(m.Name, name)).ToList();
        MethodSymbol? method = null;
        if (heading.ParameterList is null && candidates.Count == 1)
        {
            method = candidates[0];
        }
        else if (candidates.Count > 0)
        {
            var probe = new RoutineSymbol(name, _container, [], declaration);
            var headingBinder = NestedNamesBinder(type);
            headingBinder.BindSignature(probe, heading, identifier, report: false);
            method = candidates.FirstOrDefault(m => SameParameterTypes(m, probe) && (m.OperatorName is null || ReferenceEquals(m.ReturnType, probe.ReturnType)));
            var defaultsDiffer = method is not null && headingBinder.RepeatedDefaultsDiffer(method, heading);          // LANG-401 (C-365)
            if (method is not null && (!method.Parameters.Select(p => p.Name).SequenceEqual(probe.Parameters.Select(p => p.Name), IdentifierComparer.Instance)
                || !SameRefParameters(method, probe) || defaultsDiffer))                                // LANG-763 (C-232)
            {
                _diagnostics.Report(identifier, "E2101", name);
            }
        }

        if (method is not null && DisagreesOnSafecall(method, declaration))
        {
            _diagnostics.Report(identifier, "E2101", name);                         // LANG-689 (C-301)
        }

        if (method is null)
        {
            _diagnostics.Report(identifier, "E2606", name, TypeMembers.DisplayName(type));          // LANG-775: TOuter.TInner
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

    /// <summary>The classes a unit declares (its own, each once; nested ones after their enclosing type, LANG-771), in declaration order.</summary>
    internal static IEnumerable<ClassTypeSymbol> ClassesOf(UnitSymbol unit) =>
        OwnTypesOf(unit).OfType<ClassTypeSymbol>().Concat(ClosureInfo.Of(unit));

    /// <summary>E2605 at the declaration of every non-abstract method of the unit's classes without implementation.</summary>
    private static void ReportUnimplementedMethods(UnitSymbol unit, DiagnosticBag diagnostics)
    {
        foreach (var method in MethodsOf(unit).Where(m => m is { IsAbstract: false, Definition: null }))
        {
            diagnostics.Report(method.MethodDeclaration.Heading.Name, "E2605", method.Owner.Name + "." + method.Name);
        }
    }
}
