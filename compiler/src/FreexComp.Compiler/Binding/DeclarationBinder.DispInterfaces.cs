using System.Collections.Immutable;
using System.Numerics;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

// Dispatch interfaces (com-interfaces.md §6: LANG-693…695, CMP-1211; C-303).
internal sealed partial class DeclarationBinder
{
    /// <summary>
    /// LANG-693: the body of <c>D = dispinterface … end</c>. The type has no method table of its own: its values are references to
    /// an <c>IDispatch</c>, so <c>System.IDispatch</c> (else the root interface) is its internal ancestor — that gives the
    /// conversions to <c>IDispatch</c> and <c>IInterface</c> and the reference counting of interfaces. Members without a <c>dispid</c>
    /// are numbered after the highest written one (C-352); written ones are unique in the type (E2659); their types are Automation
    /// types (E2658); anything else is E2660.
    /// </summary>
    private void BindDispInterfaceBody(InterfaceTypeSymbol type, DispInterfaceTypeSyntax syntax)
    {
        type.IsForward = false;
        var root = (_types.IDispatch ?? _types.IInterface) as InterfaceTypeSymbol;
        type.Parent = ReferenceEquals(root, type) ? null : root;
        if (syntax.Heritage is { } heritage)
        {
            _diagnostics.Report(heritage, "E2660", "an ancestor");
        }

        if (syntax.Guid is { } guid)
        {
            type.Guid = BindGuid(guid.Value);
        }

        var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
        var properties = ImmutableArray.CreateBuilder<PropertySymbol>();
        var names = new Dictionary<string, Symbol>(IdentifierComparer.Instance);
        var dispIds = new Dictionary<int, SyntaxNode>();
        var unnumbered = new List<Symbol>();
        foreach (var member in syntax.Members)
        {
            switch (member)
            {
                case VisibilitySectionSyntax section:
                    _diagnostics.Report(section.StrictKeyword ?? section.VisibilityKeyword, "E2660",
                        (section.StrictKeyword ?? section.VisibilityKeyword).Text.ToLowerInvariant());
                    break;
                case MethodDeclarationSyntax method when BindDispMethod(type, method, dispIds, unnumbered) is { } symbol:
                    if (names.TryGetValue(symbol.Name, out var existing))
                    {
                        _diagnostics.ReportRedeclared(((IdentifierNameSyntax)method.Heading.Name).Identifier, symbol.Name, existing);   // no overloading
                        break;
                    }

                    names.Add(symbol.Name, symbol);
                    methods.Add(symbol);
                    break;
                case MethodDeclarationSyntax:
                    break;
                case PropertyDeclarationSyntax property when BindDispProperty(type, property, names, dispIds, unnumbered, properties) is { } symbol:
                    properties.Add(symbol);
                    break;
                case PropertyDeclarationSyntax:
                    break;
                case FieldDeclarationSyntax or ClassVarSectionSyntax:
                    _diagnostics.Report(member, "E2660", "a field");
                    break;
                case AttributeListSyntax:
                    break;
                default:
                    _diagnostics.Report(member.FirstToken(), "E2660", member.FirstToken().Text.ToLowerInvariant());
                    break;
            }
        }

        // LANG-694 (C-352): members without a dispid are numbered in declaration order after the highest written one (1 when none
        // is written or the highest is negative), so they never collide with an explicit number
        var next = Math.Max(dispIds.Count == 0 ? 0 : dispIds.Keys.Max(), 0);
        foreach (var member in unnumbered)
        {
            next++;
            switch (member)
            {
                case MethodSymbol method:
                    method.DispId = next;
                    break;
                case PropertySymbol property:
                    property.DispId = next;
                    property.ReadMethod?.DispId = next;
                    property.WriteMethod?.DispId = next;
                    break;
            }
        }

        type.Methods = methods.ToImmutable();
        type.Properties = properties.ToImmutable();
    }

    /// <summary>A method of a dispatch interface: a procedure or function whose only directive is <c>dispid N</c> (LANG-693, 694).</summary>
    private MethodSymbol? BindDispMethod(InterfaceTypeSymbol type, MethodDeclarationSyntax syntax, Dictionary<int, SyntaxNode> dispIds, List<Symbol> unnumbered)
    {
        var heading = syntax.Heading;
        if (heading.Name is not IdentifierNameSyntax { Identifier: { IsMissing: false } identifier })
        {
            return null;
        }

        if (heading.ClassKeyword is { } classKeyword)
        {
            _diagnostics.Report(classKeyword, "E2660", "class");
            return null;
        }

        if (heading.Keyword.Kind is not (SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword))
        {
            _diagnostics.Report(heading.Keyword, "E2660", heading.Keyword.Text.ToLowerInvariant());
            return null;
        }

        if (heading.TypeParameterList is { } typeParameters)
        {
            _diagnostics.Report(typeParameters, "E2660", "a type parameter list");
            return null;
        }

        var method = new MethodSymbol(NameOf(identifier), type, [DiagnosticBag.LocationOf(identifier)], syntax, MethodKind.Ordinary, Visibility.Public)
        {
            DispatchFlags = 1,                                                      // LANG-697: a method call
        };
        BindSignature(method, heading, identifier, report: true);
        method.SelfParameter = new ParameterSymbol("Self", type, ParameterMode.Value, -1, method, []);
        CheckAutomationParameters(heading.ParameterList?.Parameters ?? default, method.Parameters);
        if (heading.ResultType is { } resultType && method.ReturnType is { TypeKind: not TypeKind.Error } result && AutomationTypes.CodeOf(result) is null)
        {
            _diagnostics.Report(resultType, "E2658", result.Name);
        }

        ExpressionSyntax? number = null;
        foreach (var directive in syntax.Directives)
        {
            if (directive is DispIdDirectiveSyntax { Value: { } value })
            {
                number = value;
            }
            else
            {
                _diagnostics.Report(directive.Keyword, "E2660", directive.Keyword.Text.ToLowerInvariant());
            }
        }

        method.DispId = BindMemberDispId(type, number, dispIds);
        if (number is null)
        {
            unnumbered.Add(method);
        }

        return method;
    }

    /// <summary>
    /// A property of a dispatch interface (LANG-693…695): <c>property Name[index parameters]: Type [readonly | writeonly] dispid N;
    /// [default;]</c>. It has no accessors in source; the binder gives it one hidden method per direction, each a dispatch member
    /// with the property's number (get: flags 2; put: 4, or 8 for an interface-typed property — LANG-697), so that reads, writes,
    /// index arguments and the default property bind like those of every other property.
    /// </summary>
    private PropertySymbol? BindDispProperty(InterfaceTypeSymbol type, PropertyDeclarationSyntax syntax, Dictionary<string, Symbol> names,
        Dictionary<int, SyntaxNode> dispIds, List<Symbol> unnumbered, ImmutableArray<PropertySymbol>.Builder bound)
    {
        if (syntax.Identifier.IsMissing)
        {
            return null;
        }

        if (syntax.ClassKeyword is { } classKeyword)
        {
            _diagnostics.Report(classKeyword, "E2660", "class");
            return null;
        }

        var name = NameOf(syntax.Identifier);
        var property = new PropertySymbol(name, type, [DiagnosticBag.LocationOf(syntax.Identifier)], Visibility.Public);
        if (!names.TryAdd(name, property))
        {
            _diagnostics.ReportRedeclared(syntax.Identifier, name, names[name]);
            return null;
        }

        if (syntax.Type is null)
        {
            _diagnostics.Report(syntax.Identifier, "E2900", "interface property redeclaration");
            return null;
        }

        property.Type = BindType(syntax.Type);
        property.Parameters = BindParameters(syntax.Parameters, property, new Scope(ScopeKind.Routine, parent: null, property), report: true);
        var ok = property.Type.TypeKind != TypeKind.Error && property.Parameters.All(p => p.Type.TypeKind != TypeKind.Error);
        if (ok && AutomationTypes.CodeOf(property.Type) is null)
        {
            _diagnostics.Report(syntax.Type, "E2658", property.Type.Name);
            ok = false;
        }

        ok &= CheckAutomationParameters(syntax.Parameters, property.Parameters);
        ExpressionSyntax? number = null;
        foreach (var specifier in syntax.Specifiers)
        {
            switch (specifier.Keyword.ContextualKind)
            {
                case SyntaxKind.DispIdKeyword:
                    number = specifier.Value;
                    break;
                case SyntaxKind.ReadOnlyKeyword when !property.IsWriteOnly:
                    property.IsReadOnly = true;
                    break;
                case SyntaxKind.WriteOnlyKeyword when !property.IsReadOnly:
                    property.IsWriteOnly = true;
                    break;
                default:
                    _diagnostics.Report(specifier.Keyword, "E2660", specifier.Keyword.Text.ToLowerInvariant());      // read, write, index, stored, …
                    ok = false;
                    break;
            }
        }

        property.DispId = BindMemberDispId(type, number, dispIds);
        if (number is null)
        {
            unnumbered.Add(property);
        }

        if (syntax.DefaultKeyword is { } defaultKeyword)
        {
            if (!property.IsArrayProperty)
            {
                _diagnostics.Report(defaultKeyword, "E2900", "default on a non-array property");
                ok = false;
            }
            else if (bound.Any(p => p.IsDefault))
            {
                _diagnostics.Report(defaultKeyword, "E2635", type.Name);
            }
            else
            {
                property.IsDefault = true;
            }
        }

        if (!ok)
        {
            return null;
        }

        AddDispatchAccessors(type, property);
        return property;
    }

    /// <summary>The hidden get and put members of a dispatch property (see <see cref="BindDispProperty"/>); also used when a unit is loaded.</summary>
    internal static void AddDispatchAccessors(InterfaceTypeSymbol type, PropertySymbol property)
    {
        MethodSymbol Accessor(int flags, bool put)
        {
            var accessor = new MethodSymbol(property.Name, type, property.Locations, declaration: null!, MethodKind.Ordinary, Visibility.Public)
            {
                DispId = property.DispId,
                DispatchFlags = flags,
                ReturnType = put ? null : property.Type,
            };
            var parameters = property.Parameters.Select(p => new ParameterSymbol(p.Name, p.Type, p.Mode, p.Ordinal, accessor, [])).ToList();
            if (put)
            {
                parameters.Add(new ParameterSymbol("Value", property.Type, ParameterMode.Value, parameters.Count, accessor, []));
            }

            accessor.Parameters = [.. parameters];
            accessor.SelfParameter = new ParameterSymbol("Self", type, ParameterMode.Value, -1, accessor, []);
            return accessor;
        }

        property.ReadMethod = property.IsWriteOnly ? null : Accessor(2, put: false);
        property.WriteMethod = property.IsReadOnly ? null : Accessor(property.Type is InterfaceTypeSymbol ? 8 : 4, put: true);
    }

    /// <summary>
    /// LANG-695: every parameter type is an Automation type (E2658 at the type; open arrays and untyped parameters included);
    /// a default value is E2660. False when something was reported.
    /// </summary>
    private bool CheckAutomationParameters(SeparatedSyntaxList<ParameterSyntax> syntax, ImmutableArray<ParameterSymbol> parameters)
    {
        var ok = true;
        var index = 0;
        foreach (var parameter in syntax)
        {
            var count = parameter.Identifiers.Count(i => !i.Identifier.IsMissing);
            var symbol = index < parameters.Length && count > 0 ? parameters[index] : null;
            index += count;
            if (symbol is null || symbol.Type.TypeKind == TypeKind.Error)
            {
                continue;
            }

            if (AutomationTypes.CodeOf(symbol.Type) is null)
            {
                _diagnostics.Report(parameter.Type ?? (SyntaxNode)parameter, "E2658", symbol.Type is UntypedTypeSymbol ? "untyped parameter" : symbol.Type.Name);
                ok = false;
            }

            if (parameter.DefaultValue is { } defaultValue)
            {
                _diagnostics.Report(defaultValue, "E2660", "a default value");
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>
    /// LANG-694: the written <c>dispid</c> of a dispatch-interface member — an integer constant in the <c>Integer</c> range, not used
    /// by an earlier member (E2659 with the first as related location); none written is numbered later (C-352: no E2657).
    /// </summary>
    private int? BindMemberDispId(InterfaceTypeSymbol type, ExpressionSyntax? value, Dictionary<int, SyntaxNode> dispIds)
    {
        if (value is null)
        {
            return null;
        }

        if (BindDispId(value) is not { } dispId)
        {
            return null;
        }

        if (dispIds.TryGetValue(dispId, out var first))
        {
            _diagnostics.Report(value, "E2659", [new RelatedLocation(DiagnosticBag.LocationOf(first), "first member with this dispid")], dispId, type.Name);
        }
        else
        {
            dispIds.Add(dispId, value);
        }

        return dispId;
    }

    /// <summary>LANG-694: <c>dispid N</c>, an integer constant in the <c>Integer</c> range (E2014 otherwise).</summary>
    private int? BindDispId(ExpressionSyntax value)
    {
        if (_folder.Fold(value) is not { } constant)
        {
            return null;
        }

        if (constant.Type is not IntegerTypeSymbol || constant.Value is not BigInteger number || number < int.MinValue || number > int.MaxValue)
        {
            _diagnostics.Report(value, "E2014", "Integer");
            return null;
        }

        return (int)number;
    }
}
