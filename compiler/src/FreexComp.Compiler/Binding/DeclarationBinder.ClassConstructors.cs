using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// Class constructors and class destructors of classes, records and helpers (generics.md §14, LANG-786…788, 995, CMP-1395; C-374, C-441): one of each
/// per type, kept on the type outside its member scope (no lookup finds them, LANG-787), implemented by type path and bound like a
/// parameterless <c>class procedure …; static;</c> (LANG-788 b).
/// </summary>
internal sealed partial class DeclarationBinder
{
    /// <summary><c>class constructor Name</c> or <c>class destructor Name</c> (LANG-786), in a declaration or an implementation heading.</summary>
    internal static bool IsClassConstructorHeading(RoutineHeadingSyntax heading) =>
        heading.ClassKeyword is not null && heading.Keyword.Kind is SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword;

    /// <summary>The <c>class %s</c> argument of E2665…E2668: <c>constructor</c> or <c>destructor</c> (generics.md §14.6).</summary>
    private static string ClassConstructorWord(RoutineHeadingSyntax heading) =>
        heading.Keyword.Kind == SyntaxKind.ConstructorKeyword ? "constructor" : "destructor";

    /// <summary>
    /// LANG-786: the class constructor or class destructor declared in the body of <paramref name="type"/> (a class or record):
    /// E2666 for a second one of the kind, E2665 for a parameter or type parameter list (an empty <c>()</c> is none), E2667 for
    /// every directive except the hints (which are accepted and have no effect). The symbol is never entered into a member scope.
    /// </summary>
    private void BindClassConstructorDeclaration(TypeSymbol type, MethodDeclarationSyntax syntax, Visibility visibility)
    {
        var heading = syntax.Heading;
        if (heading.Name is not IdentifierNameSyntax { Identifier: { IsMissing: false } identifier })
        {
            return;
        }

        var word = ClassConstructorWord(heading);
        ReportClassConstructorLists(heading, word);
        foreach (var directive in syntax.Directives)
        {
            if (directive.Keyword.ContextualKind is not (SyntaxKind.DeprecatedKeyword or SyntaxKind.PlatformKeyword or SyntaxKind.ExperimentalKeyword))
            {
                _diagnostics.Report(directive.Keyword, "E2667", directive.Keyword.Text.ToLowerInvariant(), word);   // LANG-786 c
            }
        }

        var isConstructor = heading.Keyword.Kind == SyntaxKind.ConstructorKeyword;
        var existing = (type, isConstructor) switch
        {
            (ClassTypeSymbol c, true) => c.ClassConstructor,
            (ClassTypeSymbol c, false) => c.ClassDestructor,
            (RecordTypeSymbol r, true) => r.ClassConstructor,
            (RecordTypeSymbol r, false) => r.ClassDestructor,
            (HelperTypeSymbol h, true) => h.ClassConstructor,                               // LANG-995 (C-441)
            (HelperTypeSymbol h, false) => h.ClassDestructor,
            _ => null,
        };
        if (existing is not null)
        {
            var display = type.GenericDefinition is GenericTypeSymbol generic ? GenericDisplayName(generic) : TypeMembers.DisplayName(type);
            _diagnostics.Report(identifier, "E2666", display, word);                                     // LANG-786 a
            return;
        }

        var method = new MethodSymbol(NameOf(identifier), type, [DiagnosticBag.LocationOf(identifier)], syntax,
            isConstructor ? MethodKind.ClassConstructor : MethodKind.ClassDestructor, visibility);
        switch (type, isConstructor)
        {
            case (ClassTypeSymbol c, true):
                c.ClassConstructor = method;
                break;
            case (ClassTypeSymbol c, false):
                c.ClassDestructor = method;
                break;
            case (RecordTypeSymbol r, true):
                r.ClassConstructor = method;
                break;
            case (RecordTypeSymbol r, false):
                r.ClassDestructor = method;
                break;
            case (HelperTypeSymbol h, true):
                h.ClassConstructor = method;
                break;
            case (HelperTypeSymbol h, false):
                h.ClassDestructor = method;
                break;
        }
    }

    /// <summary>LANG-786 b, 788 a: E2665 at a type parameter list or a non-empty parameter list of the heading.</summary>
    private void ReportClassConstructorLists(RoutineHeadingSyntax heading, string word)
    {
        if (heading.TypeParameterList is { } typeParameters)
        {
            _diagnostics.Report(typeParameters, "E2665", word);
        }
        else if (heading.ParameterList is { Parameters.Count: > 0 } parameters)
        {
            _diagnostics.Report(parameters, "E2665", word);
        }
    }

    /// <summary>
    /// LANG-788 a: <c>class constructor TFoo.Create; begin … end;</c> completes the class constructor (destructor) of that name of
    /// <paramref name="type"/>: E2665 for a parameter or type parameter list, E2606 when the type has none of that kind and name,
    /// E2004 for a second implementation.
    /// </summary>
    private void DefineClassConstructor(TypeSymbol type, RoutineDeclarationSyntax declaration, SyntaxToken identifier)
    {
        var heading = declaration.Heading;
        var name = NameOf(identifier);
        ReportClassConstructorLists(heading, ClassConstructorWord(heading));
        var isConstructor = heading.Keyword.Kind == SyntaxKind.ConstructorKeyword;
        var method = (type, isConstructor) switch
        {
            (ClassTypeSymbol c, true) => c.ClassConstructor,
            (ClassTypeSymbol c, false) => c.ClassDestructor,
            (RecordTypeSymbol r, true) => r.ClassConstructor,
            (RecordTypeSymbol r, false) => r.ClassDestructor,
            (HelperTypeSymbol h, true) => h.ClassConstructor,                               // LANG-995 (C-441)
            (HelperTypeSymbol h, false) => h.ClassDestructor,
            _ => null,
        };
        if (method is null || !IdentifierComparer.Instance.Equals(method.Name, name))
        {
            _diagnostics.Report(identifier, "E2606", name, TypeMembers.DisplayName(type));
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
}
