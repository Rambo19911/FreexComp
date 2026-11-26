using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Records with methods, constructors, properties, class vars, nested types and constants (LANG-730, 770; C-122).
internal sealed partial class DeclarationBinder
{
    /// <summary>
    /// A record with members other than fields (LANG-730): the symbol exists before its members are bound, so that method
    /// signatures may name the record and nested types; members are bound in a member scope (the record's own name, then its
    /// nested <c>const</c>/<c>type</c> sections, LANG-770). Fields are laid out as in <see cref="BindRecord"/> (ABI-060/061).
    /// Only named records at unit level or nested in a type may have them (E2900 otherwise).
    /// </summary>
    private TypeSymbol BindRecordWithMembers(RecordTypeSyntax syntax, string name, SyntaxToken? declaredAt, ImmutableArray<Symbol>.Builder? declared)
    {
        if (declaredAt is not { } at || !(IsGlobalLevel || _scope.Kind == ScopeKind.TypeMember))
        {
            return Unsupported(syntax, "local record type with methods");
        }

        var packing = syntax.PackedKeyword is not null ? 1 : syntax.RecordKeyword.DirectiveState.Align;
        var record = new RecordTypeSymbol(_types.Target, name, 0, 1, syntax.PackedKeyword is not null, packing, isAnonymous: false, _container,
            [DiagnosticBag.LocationOf(at)]);
        _onTypeCreated?.Invoke(record);                                                // C-116: a generic instance registers itself first
        _onTypeCreated = null;
        var memberScope = new Scope(ScopeKind.TypeMember, _scope, record);
        memberScope.TryDeclare(name, record, out _);                                  // the record names itself inside (LANG-730)
        var inner = new DeclarationBinder(_types, memberScope, record, _diagnostics)
        {
            _sectionTypeNames = _sectionTypeNames,
            _forwardPointers = _forwardPointers,
            _declaringType = _declaringType,
        };

        var fields = new List<(SyntaxToken Identifier, TypeSymbol Type, Visibility Visibility)>();
        var names = new Dictionary<string, Symbol>(IdentifierComparer.Instance);
        var methods = new List<(MethodDeclarationSyntax Syntax, Visibility Visibility)>();
        var genericMethods = new List<(MethodDeclarationSyntax Syntax, Visibility Visibility)>();
        var properties = new List<(PropertyDeclarationSyntax Syntax, Visibility Visibility)>();
        var classVarSections = new List<(ClassVarSectionSyntax Syntax, Visibility Visibility)>();
        var nested = ImmutableArray.CreateBuilder<Symbol>();
        var nestedAliases = ImmutableArray.CreateBuilder<(string Name, TypeSymbol Target, Visibility Visibility)>();
        var failed = false;
        foreach (var (member, visibility) in Members(syntax.Members, Visibility.Public))
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    failed |= !inner.BindRecordField(record, field, visibility, fields, names);
                    break;
                case ClassVarSectionSyntax { ClassKeyword: null } section:
                    foreach (var field in section.Fields)
                    {
                        failed |= !inner.BindRecordField(record, field, visibility, fields, names);
                    }

                    break;
                case ClassVarSectionSyntax section:
                    classVarSections.Add((section, visibility));
                    break;
                case MethodDeclarationSyntax { Heading.TypeParameterList: not null } genericMethod:
                    genericMethods.Add((genericMethod, visibility));                    // C-117 (LANG-701)
                    break;
                case MethodDeclarationSyntax method:
                    methods.Add((method, visibility));
                    break;
                case PropertyDeclarationSyntax property:
                    properties.Add((property, visibility));
                    break;
                case ConstSectionSyntax or TypeSectionSyntax:
                    nested.AddRange(inner.BindNestedSection(member, nestedAliases, visibility));    // LANG-770
                    break;
                default:
                    Unsupported(member, "record " + FeatureName(member.Kind));
                    failed = true;
                    break;
            }
        }

        var (offsets, size, alignment) = RecordTypeSymbol.Layout(fields.Select(f => f.Type), packing);
        var placed = fields.Select((f, i) => (f.Identifier, f.Type, Offset: offsets[i])).ToList();
        if (syntax.VariantPart is { } variantPart && !failed)
        {
            // ABI-061 (C-108): the variant part continues after the fixed part's last field
            var end = fields.Count == 0 ? 0 : offsets[^1] + fields[^1].Type.Size;
            var types = new Dictionary<FieldDeclarationSyntax, TypeSymbol>();
            var fieldNames = new HashSet<string>(fields.Select(f => NameOf(f.Identifier)), IdentifierComparer.Instance);
            failed = !inner.BindVariantFields(variantPart, types, declared)
                || !PlaceVariantPart(variantPart, types, fieldNames, packing, placed, ref end, ref alignment);
            size = (end + alignment - 1) / alignment * alignment;
        }

        if (size > int.MaxValue)
        {
            _diagnostics.Report(syntax, "E2031");
            failed = true;
        }

        if (failed)
        {
            return _types.Error;
        }

        record.SetLayout(size, alignment);
        record.HasVariantPart = syntax.VariantPart is not null;
        record.Fields = [.. placed.Select((f, i) => new FieldSymbol(NameOf(f.Identifier), f.Type, f.Offset, record, [DiagnosticBag.LocationOf(f.Identifier)],
            i < fields.Count ? fields[i].Visibility : Visibility.Public))];
        foreach (var field in record.Fields)
        {
            names[field.Name] = field;
        }
        record.NestedMembers = nested.ToImmutable();
        record.NestedAliases = nestedAliases.ToImmutable();

        var classVariables = ImmutableArray.CreateBuilder<VariableSymbol>();
        foreach (var (section, visibility) in classVarSections)
        {
            inner.BindClassVariables(record, section, visibility, classVariables, names);
        }

        record.ClassVariables = classVariables.ToImmutable();
        var boundMethods = ImmutableArray.CreateBuilder<MethodSymbol>();
        foreach (var (method, visibility) in methods)
        {
            if (inner.BindRecordMethodDeclaration(record, method, visibility) is not { } symbol)
            {
                continue;
            }

            // overloads differ in their parameters; operators also in their result (C-123: Implicit to several types)
            if (names.TryGetValue(symbol.Name, out var existing)
                && (existing is not MethodSymbol || !symbol.IsOverload
                    || boundMethods.FirstOrDefault(other => IdentifierComparer.Instance.Equals(other.Name, symbol.Name)
                        && (!other.IsOverload || SameParameterTypes(other, symbol) && ReferenceEquals(other.ReturnType, symbol.ReturnType)
                            || symbol.OperatorName is null && SameParameterTypes(other, symbol))) is not null))
            {
                _diagnostics.ReportRedeclared(((IdentifierNameSyntax)method.Heading.Name).Identifier, symbol.Name, existing);
                continue;
            }

            names.TryAdd(symbol.Name, symbol);
            boundMethods.Add(symbol);
        }

        record.Methods = boundMethods.ToImmutable();
        record.GenericMethods = [.. genericMethods.Select(g => inner.BindGenericMethodDeclaration(record, g.Syntax, g.Visibility))];
        record.Properties = inner.BindProperties(record, properties, names);
        return record;
    }

    /// <summary>One field declaration of a record with members; false after an error. A field of the record's own type is E2007.</summary>
    private bool BindRecordField(RecordTypeSymbol record, FieldDeclarationSyntax field, Visibility visibility,
        List<(SyntaxToken Identifier, TypeSymbol Type, Visibility Visibility)> fields, Dictionary<string, Symbol> names)
    {
        var type = BindType(field.Type);
        if (type.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (ReferenceEquals(type, record) || IsEnclosingRecordUnderConstruction(type))
        {
            _diagnostics.Report(field.Type, "E2007", type.Name);                    // C-344: also an enclosing record (LANG-777)
            return false;
        }

        if (type.Size < 0 || type is TextTypeSymbol)
        {
            Unsupported(field.Type, "record field of type " + type.Name);
            return false;
        }

        foreach (var identifier in field.Identifiers.Select(i => i.Identifier).Where(i => !i.IsMissing))
        {
            var name = NameOf(identifier);
            if (names.ContainsKey(name))
            {
                _diagnostics.Report(identifier, "E2004", name);
                return false;
            }

            // a placeholder until the field symbols exist (E2004 against later members)
            names[name] = record;
            fields.Add((identifier, type, visibility));
        }

        return true;
    }

    /// <summary>A nested <c>const</c> or <c>type</c> section of a class or record (LANG-770): its symbols, in this member scope.</summary>
    private ImmutableArray<Symbol> BindNestedSection(SyntaxNode section, ImmutableArray<(string Name, TypeSymbol Target, Visibility Visibility)>.Builder aliases,
        Visibility visibility)
    {
        var declared = ImmutableArray.CreateBuilder<Symbol>();
        switch (section)
        {
            case ConstSectionSyntax constSection when constSection.Declarations.FirstOrDefault(d => d.Type is not null) is { } typed:
                Unsupported(typed.Type!, "typed constant in a type");
                break;
            case ConstSectionSyntax constSection:
                BindConstSection(constSection, declared);
                break;
            case TypeSectionSyntax typeSection:
                BindTypeSection(typeSection, declared);
                foreach (var declaration in typeSection.Declarations)
                {
                    // C-311: `TA = TOther;` names an existing type — a nested alias, not a nested member called TOther
                    if (declaration is not { Identifier.IsMissing: false, TypeKeyword: null, TypeParameterList: null, Type: NamedTypeSyntax or StringTypeSyntax { Length: null } }
                        || _scope.LookupLocal(NameOf(declaration.Identifier)) is not TypeSymbol target || target.TypeKind == TypeKind.Error
                        || aliases.Any(a => IdentifierComparer.Instance.Equals(a.Name, NameOf(declaration.Identifier))))
                    {
                        continue;
                    }

                    var index = declared.LastIndexOf(target);
                    if (index >= 0)
                    {
                        declared.RemoveAt(index);
                    }

                    aliases.Add((NameOf(declaration.Identifier), target, visibility));     // LANG-774 (C-359): the section's visibility
                }

                foreach (var type in declared.OfType<TypeSymbol>().Where(t => ReferenceEquals(t.ContainingSymbol, _container)))
                {
                    type.NestedVisibility = visibility;                         // LANG-774 (C-344): the section's visibility
                }

                break;
        }

        return declared.ToImmutable();
    }

    /// <summary>
    /// A method heading in a record (LANG-730): instance methods take <c>Self</c> as a <c>var</c> parameter (ABI-084); class methods
    /// are static (no <c>Self</c>); a constructor (≥ 1 parameter) is a static function returning the record
    /// (<see cref="MethodSymbol.IsRecordConstructor"/>). Destructors, virtual methods and message handlers are E2730.
    /// </summary>
    private MethodSymbol? BindRecordMethodDeclaration(RecordTypeSymbol record, MethodDeclarationSyntax syntax, Visibility visibility)
    {
        var heading = syntax.Heading;
        if (heading.Name is not IdentifierNameSyntax { Identifier: { IsMissing: false } identifier })
        {
            return null;
        }

        var isClass = heading.ClassKeyword is not null;
        if (isClass && heading.Keyword.ContextualKind == SyntaxKind.OperatorKeyword)
        {
            return BindOperatorDeclaration(record, syntax, identifier, visibility);         // LANG-731 (C-123)
        }

        var unsupported = heading.Keyword.Kind switch
        {
            SyntaxKind.ConstructorKeyword when isClass => "class constructor",
            SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword => null,
            _ => heading.Keyword.Text.ToLowerInvariant(),
        } ?? (heading.TypeParameterList is not null && !_genericMethodInstance ? "generic method" : null);
        if (unsupported is not null)
        {
            _diagnostics.Report(heading, "E2900", unsupported);
            return null;
        }

        if (heading.Keyword.Kind == SyntaxKind.DestructorKeyword)
        {
            _diagnostics.Report(heading.Keyword, "E2730", "destructor");                                   // LANG-730
            return null;
        }

        if (syntax.Directives.FirstOrDefault(d => d.Keyword.ContextualKind is SyntaxKind.VirtualKeyword or SyntaxKind.DynamicKeyword
                or SyntaxKind.OverrideKeyword or SyntaxKind.AbstractKeyword or SyntaxKind.MessageKeyword or SyntaxKind.FinalKeyword
                or SyntaxKind.ReintroduceKeyword) is { } directive)
        {
            _diagnostics.Report(directive.Keyword, "E2730", directive.Keyword.Text.ToLowerInvariant());
            return null;
        }

        var isConstructor = heading.Keyword.Kind == SyntaxKind.ConstructorKeyword;
        var kind = isClass || isConstructor ? MethodKind.ClassStatic : MethodKind.Ordinary;
        var method = new MethodSymbol(NameOf(identifier), record, [DiagnosticBag.LocationOf(identifier)], syntax, kind, visibility)
        {
            IsRecordConstructor = isConstructor,
        };
        BindMethodDirectives(method, syntax.Directives, isClass);
        BindSignature(method, heading, identifier, report: true);
        if (isConstructor)
        {
            if (method.Parameters.IsEmpty)
            {
                _diagnostics.Report(identifier, "E2730", "parameterless constructor");                    // LANG-730
                return null;
            }

            method.ReturnType = record;
            method.ResultVariable = new VariableSymbol("Result", record, isGlobal: false, method, []);
        }
        else if (!isClass)
        {
            method.SelfParameter = new ParameterSymbol("Self", record, ParameterMode.Var, -1, method, []);   // ABI-084: ptr to the record
        }

        return method;
    }

    /// <summary>
    /// <c>class operator Name(…): T;</c> (LANG-731): a static function. E2732 for a name outside the table, E2733 for the wrong number of
    /// parameters, E2731 when neither a parameter nor the result is the record; the custom managed record operators are E2900
    /// (LANG-741). Operators are implicitly overloaded (also by result type for conversions).
    /// </summary>
    private MethodSymbol? BindOperatorDeclaration(RecordTypeSymbol record, MethodDeclarationSyntax syntax, SyntaxToken identifier, Visibility visibility)
    {
        var name = NameOf(identifier);
        if (Operators.IsManagedRecordOperator(name))
        {
            _diagnostics.Report(identifier, "E2900", "custom managed record operator " + name);
            return null;
        }

        if (!Operators.Arity.TryGetValue(name, out var arity))
        {
            _diagnostics.Report(identifier, "E2732", name);
            return null;
        }

        var canonical = Operators.Arity.Keys.First(k => IdentifierComparer.Instance.Equals(k, name));
        var method = new MethodSymbol(name, record, [DiagnosticBag.LocationOf(identifier)], syntax, MethodKind.ClassStatic, visibility)
        {
            OperatorName = canonical,
        };
        BindMethodDirectives(method, syntax.Directives, isClass: true);
        method.IsOverload = true;
        BindSignature(method, syntax.Heading, identifier, report: true);
        if (method.Parameters.Length != arity)
        {
            _diagnostics.Report(identifier, "E2733", name, arity);
            return null;
        }

        if (!ReferenceEquals(method.ReturnType, record) && !method.Parameters.Any(p => ReferenceEquals(p.Type, record)))
        {
            _diagnostics.Report(identifier, "E2731", name, record.Name);
            return null;
        }

        return method;
    }

    /// <summary>The records a unit declares with methods (nested ones in classes and records too), in declaration order.</summary>
    internal static IEnumerable<RecordTypeSymbol> RecordsWithMethodsOf(UnitSymbol unit) =>
        OwnTypesOf(unit).OfType<RecordTypeSymbol>().Where(r => !r.Methods.IsEmpty || !r.ClassVariables.IsEmpty);

    /// <summary>The unit's own types and, depth first, their nested types (LANG-770).</summary>
    internal static IEnumerable<TypeSymbol> OwnTypesOf(UnitSymbol unit)
    {
        IEnumerable<TypeSymbol> WithNested(TypeSymbol type) =>
            new[] { type }.Concat(TypeMembers.NestedMembers(type).OfType<TypeSymbol>().Where(t => ReferenceEquals(t.ContainingSymbol, type)).SelectMany(WithNested));

        return unit.InterfaceMembers.Concat(unit.ImplementationMembers).OfType<TypeSymbol>()
            .Where(t => ReferenceEquals(t.ContainingSymbol, unit)).Distinct().SelectMany(WithNested).Distinct();
    }

    /// <summary>The methods with bodies of the unit's classes and records (C-081, C-122).</summary>
    internal static IEnumerable<MethodSymbol> MethodsOf(UnitSymbol unit) =>
        ClassesOf(unit).SelectMany(c => c.Methods).Concat(RecordsWithMethodsOf(unit).SelectMany(r => r.Methods)).Concat(HelpersOf(unit).SelectMany(h => h.Methods));

    /// <summary>The class vars of the unit's classes and records (CMP-370, LANG-730).</summary>
    internal static IEnumerable<VariableSymbol> ClassVariablesOf(UnitSymbol unit) =>
        ClassesOf(unit).SelectMany(c => c.ClassVariables).Concat(RecordsWithMethodsOf(unit).SelectMany(r => r.ClassVariables))
            .Concat(HelpersOf(unit).SelectMany(h => h.ClassVariables));

    /// <summary>
    /// This binder, or one whose scope adds the nested types and constants of <paramref name="type"/>, its ancestors and its
    /// enclosing types (LANG-770): a method implementation's heading names them unqualified.
    /// </summary>
    private DeclarationBinder NestedNamesBinder(TypeSymbol type)
    {
        var types = TypeMembers.EnclosingTypes(type).Reverse().Concat(TypeMembers.SelfAndAncestors(type).Reverse()).ToList();
        if (types.All(t => TypeMembers.NestedMembers(t).IsEmpty && TypeMembers.NestedAliases(t).IsEmpty))
        {
            return this;
        }

        var scope = _scope;
        foreach (var owner in types)
        {
            scope = new Scope(ScopeKind.TypeMember, scope, owner);
            foreach (var member in TypeMembers.NestedMembers(owner))
            {
                scope.TryDeclare(member, out _);
            }

            foreach (var (name, target, _) in TypeMembers.NestedAliases(owner))
            {
                scope.TryDeclare(name, target, out _);                              // C-311
            }
        }

        return new DeclarationBinder(_types, scope, _container, _diagnostics);
    }

    /// <summary>
    /// LANG-780 (C-345): the one resolver of type names — a unit qualifier first (the longest dotted prefix naming a used unit or the
    /// own unit), then type segments, each in the member scope of the previous one; a <c>G&lt;…&gt;</c> segment instantiates the
    /// generic of that arity there. E2003 for an unknown first segment, E2005 for a later one, E2703 for a wrong arity.
    /// </summary>
    private TypeSymbol ResolveTypeName(NameSyntax name)
    {
        switch (name)
        {
            case IdentifierNameSyntax identifier:
                return BindTypeName(identifier);
            case GenericNameSyntax generic:
                return BindGenericName(generic);                                            // C-116 (LANG-710)
            case QualifiedNameSyntax { Right: GenericNameSyntax generic } qualified when UnitQualifiers.Qualifier(_scope, qualified.Left) is { } unit:
                return BindGenericName(generic, unit.Qualified);                            // U.TBox<X>
            case QualifiedNameSyntax { Right: IdentifierNameSyntax right } qualified when UnitQualifiers.Qualifier(_scope, qualified.Left) is not null:
                return BindTypeName(right);                                                 // U.TName (LANG-311, 538)
            case QualifiedNameSyntax { Right: var right } qualified when !right.Identifier.IsMissing:
                return ResolveTypeName(qualified.Left) switch
                {
                    ErrorTypeSymbol error => error,
                    var outer => NestedTypeNamed(outer, right),
                };
            default:
                return _types.Error;
        }
    }

    /// <summary>The nested type <paramref name="name"/> (plain or generic) of <paramref name="outer"/> (LANG-770, 774, 778, 780).</summary>
    private TypeSymbol NestedTypeNamed(TypeSymbol outer, SimpleNameSyntax name)
    {
        if (name is GenericNameSyntax generic)
        {
            return BindGenericName(generic, outer: outer);
        }

        var text = NameOf(name.Identifier);
        if (TypeMembers.MemberNestedType(outer, text) is { } inner)                         // C-354: inherited too
        {
            if (NestedNames.InaccessibleNestedType(_scope, outer, text) is { } denied)
            {
                // LANG-774 (C-344; C-354: the declaring type; C-359: a nested alias has its own section's visibility)
                _diagnostics.Report(name, "E2610", denied.Visibility, denied.Name, TypeMembers.DisplayName(denied.Declaring));
                return _types.Error;
            }

            return inner;
        }

        if (TypeMembers.MemberNestedGenerics(outer, text).Select(g => g.ParameterNames.Length).DefaultIfEmpty(0).Min() is var arity and > 0)
        {
            _diagnostics.Report(name, "E2703", text, arity);                                // LANG-781
            return _types.Error;
        }

        _diagnostics.Report(name, "E2005", TypeMembers.DisplayName(outer), text);
        return _types.Error;
    }

    /// <summary>The leftmost identifier of a qualified name names a type (a nested type follows, LANG-770).</summary>
    private bool LeftmostNamesType(QualifiedNameSyntax qualified)
    {
        NameSyntax left = qualified;
        while (left is QualifiedNameSyntax q)
        {
            if (UnitQualifiers.Qualifier(_scope, q.Left) is not null)
            {
                // C-344: U.TOuter.TInner — the segment after the (longest) unit qualifier names the type
                return q.Right is IdentifierNameSyntax { Identifier: { IsMissing: false } first } firstName && _scope.For(firstName).Lookup(NameOf(first)) is TypeSymbol;
            }

            left = q.Left;
        }

        return left is IdentifierNameSyntax { Identifier: { IsMissing: false } token } name && _scope.For(name).Lookup(NameOf(token)) is TypeSymbol;
    }

    /// <summary>
    /// The class or record a method implementation's qualifier names: <c>TFoo</c>, a nested <c>TOuter.TInner</c> (LANG-770), or a
    /// unit-qualified name. Null after a reported error.
    /// </summary>
    private TypeSymbol? ResolveTypeQualifier(NameSyntax qualifier, bool implementationPath = false)
    {
        switch (qualifier)
        {
            case IdentifierNameSyntax { Identifier: { IsMissing: false } token } name:
                switch (_scope.For(name).Lookup(NameOf(token)))
                {
                    case null:
                        _diagnostics.ReportUndeclared(name, NameOf(token));
                        return null;
                    case ErrorTypeSymbol:
                        return null;
                    case TypeSymbol type:
                        return type;
                    default:
                        _diagnostics.Report(name, "E2603");
                        return null;
                }

            case QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier: { IsMissing: false } right } rightName } qualified:
                if (UnitQualifiers.Qualifier(_scope, qualified.Left) is not null)
                {
                    return ResolveTypeQualifier(rightName, implementationPath);
                }

                if (ResolveTypeQualifier(qualified.Left, implementationPath) is not { } outer)
                {
                    return null;
                }

                if ((implementationPath ? TypeMembers.NestedType(outer, NameOf(right)) : TypeMembers.MemberNestedType(outer, NameOf(right))) is { } inner)
                {
                    if (!implementationPath && NestedNames.InaccessibleNestedType(_scope, outer, NameOf(right)) is { } denied)
                    {
                        _diagnostics.Report(rightName, "E2610", denied.Visibility, denied.Name, TypeMembers.DisplayName(denied.Declaring));   // LANG-774 (C-354, C-359)
                        return null;
                    }

                    return inner;
                }

                if (implementationPath)
                {
                    _diagnostics.Report(rightName, "E2606", NameOf(right), TypeMembers.DisplayName(outer));   // LANG-776 (C-344)
                    return null;
                }

                _diagnostics.Report(rightName, "E2005", outer.Name, NameOf(right));
                return null;
            default:
                return null;
        }
    }
}
