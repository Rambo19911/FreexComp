using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// com-interfaces.md §6 (C-303): <c>dispinterface</c> declarations — members with <c>dispid</c>, properties with
/// <c>readonly</c> / <c>writeonly</c> / <c>default</c> — and <c>dispid</c> on members of ordinary interfaces (LANG-693, 694).
/// </summary>
public sealed partial class ParserTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-693")]
    [Trait("Req", "LANG-694")]
    [Trait("Req", "CMP-1211")]
    public void DispInterface_MembersDispIdReadonlyDefault()
    {
        const string Text = """
            dispinterface ['{6D0F1C52-3A7E-4B19-9C55-1E2A7B8C9D01}']
              procedure Add(const Key: string; Value: Integer); dispid 1;
              function Exists(const Key: string): Boolean; dispid 2;
              property Count: Integer readonly dispid 3;
              property Sink: Integer writeonly dispid 4;
              property Item[const Key: string]: Integer dispid 0; default;
            end
            """;

        Assert.Equal(
            "(DispInterfaceType dispinterface (Guid [ (LiteralExpression '{6D0F1C52-3A7E-4B19-9C55-1E2A7B8C9D01}') ]) "
            + "(MethodDeclaration (RoutineHeading procedure (IdentifierName Add) (ParameterList ( "
            + "(Parameter const (IdentifierName Key) : (StringType string)) ; (Parameter (IdentifierName Value) : (NamedType (IdentifierName Integer))) ))) ; "
            + "(DispIdDirective dispid (LiteralExpression 1) ;)) "
            + "(MethodDeclaration (RoutineHeading function (IdentifierName Exists) (ParameterList ( "
            + "(Parameter const (IdentifierName Key) : (StringType string)) )) : (NamedType (IdentifierName Boolean))) ; "
            + "(DispIdDirective dispid (LiteralExpression 2) ;)) "
            + "(PropertyDeclaration property Count : (NamedType (IdentifierName Integer)) (PropertySpecifier readonly) "
            + "(PropertySpecifier dispid (LiteralExpression 3)) ;) "
            + "(PropertyDeclaration property Sink : (NamedType (IdentifierName Integer)) (PropertySpecifier writeonly) "
            + "(PropertySpecifier dispid (LiteralExpression 4)) ;) "
            + "(PropertyDeclaration property Item [ (Parameter const (IdentifierName Key) : (StringType string)) ] : "
            + "(NamedType (IdentifierName Integer)) (PropertySpecifier dispid (LiteralExpression 0)) ; default ;) end)",
            DumpType(Text));

        var type = (DispInterfaceTypeSyntax)((TypeSectionSyntax)((ProgramSyntax)Parse("type T = " + Text + "; begin end.").Root).Block.Declarations[0]).Declarations[0].Type;
        Assert.Equal(SyntaxKind.DispInterfaceKeyword, type.DispInterfaceKeyword.Kind);
        Assert.Null(type.Heritage);
        var method = (MethodDeclarationSyntax)type.Members[0];
        var dispId = Assert.IsType<DispIdDirectiveSyntax>(Assert.Single(method.Directives));
        Assert.Equal(SyntaxKind.DispIdKeyword, dispId.Keyword.ContextualKind);
        Assert.Equal("1", dispId.Value!.ToString().Trim());
        var property = (PropertyDeclarationSyntax)type.Members[2];
        Assert.Equal([SyntaxKind.ReadOnlyKeyword, SyntaxKind.DispIdKeyword], property.Specifiers.Select(s => s.Keyword.ContextualKind));
        Assert.NotNull(((PropertyDeclarationSyntax)type.Members[4]).DefaultKeyword);
    }

    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-693")]
    [InlineData("dispinterface", "(DispInterfaceType dispinterface)")]
    [InlineData("dispinterface end", "(DispInterfaceType dispinterface end)")]
    [InlineData("dispinterface(IFoo) end", "(DispInterfaceType dispinterface (Heritage ( (NamedType (IdentifierName IFoo)) )) end)")]    // E2660 is the binder's
    public void DispInterface_ForwardDeclaration(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));

        // the forward declaration and its completion in one type section, like interfaces
        var unit = (UnitSyntax)Parse("unit U; interface type IFoo = dispinterface; IFoo = dispinterface procedure P; dispid 1; end; implementation end.").Root;
        var declarations = ((TypeSectionSyntax)unit.InterfaceSection.Declarations[0]).Declarations;
        Assert.Null(((DispInterfaceTypeSyntax)declarations[0].Type).EndKeyword);
        Assert.NotNull(((DispInterfaceTypeSyntax)declarations[1].Type).EndKeyword);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-694")]
    public void DispIdOnOrdinaryInterfaceMember()
    {
        Assert.Equal(
            "(InterfaceType interface "
            + "(MethodDeclaration (RoutineHeading procedure (IdentifierName P)) ; (DispIdDirective dispid (LiteralExpression 7) ;) (Directive stdcall ;)) "
            + "(MethodDeclaration (RoutineHeading function (IdentifierName GetX) : (NamedType (IdentifierName Integer))) ; (Directive safecall ;) "
            + "(DispIdDirective dispid (UnaryExpression - (LiteralExpression 4)) ;)) "
            + "(PropertyDeclaration property X : (NamedType (IdentifierName Integer)) (PropertySpecifier read (IdentifierName GetX)) "
            + "(PropertySpecifier dispid (LiteralExpression 8)) ;) end)",
            DumpType("interface procedure P; dispid 7; stdcall; function GetX: Integer; safecall; dispid -4; property X: Integer read GetX dispid 8; end"));

        // anywhere else 'dispid' stays the syntax error it was: it is no directive of routines or class methods, no class property specifier
        Assert.NotEmpty(Diagnostics("procedure P; dispid 1; begin end; begin end."));
        Assert.NotEmpty(Diagnostics("type T = class procedure P; dispid 1; end; begin end."));
        Assert.Equal(["E1118(1,36): Property specifier expected but 'dispid' found"], Diagnostics("type T = class property X: Integer dispid 1; end; begin end."));

        // and the three words stay ordinary identifiers elsewhere
        Assert.Empty(Diagnostics("var dispid, readonly, writeonly: Integer; begin dispid := readonly + writeonly end."));
    }
}
