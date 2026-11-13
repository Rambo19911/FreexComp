using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>grammar.md §5: routine headings, parameters, directives, blocks, nested routines (C-015; LANG-160, 161).</summary>
public sealed partial class ParserTests
{
    private const string Body = "(Block (CompoundStatement begin end))";

    [Fact]
    public void Routines_ProcedureWithBody()
    {
        Assert.Equal(
            $"(RoutineDeclaration (RoutineHeading procedure (IdentifierName P)) ; {Body} ;)",
            DumpDeclarations("procedure P; begin end;"));
    }

    [Fact]
    public void Routines_FunctionWithParametersAndResult()
    {
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading function (IdentifierName F) (ParameterList ( "
            + "(Parameter (IdentifierName A) : (NamedType (IdentifierName Integer))) ; (Parameter const (IdentifierName B) : (StringType string)) )) "
            + $": (NamedType (IdentifierName Boolean))) ; {Body} ;)",
            DumpDeclarations("function F(A: Integer; const B: string): Boolean; begin end;"));

        var routine = (RoutineDeclarationSyntax)((ProgramSyntax)Parse("function F(A: Integer): Boolean; begin end; begin end.").Root).Block.Declarations[0];
        Assert.Equal(SyntaxKind.FunctionKeyword, routine.Heading.Keyword.Kind);
        Assert.Equal("F", routine.Heading.Name.ToString());
        Assert.Single(routine.Heading.ParameterList!.Parameters);
        Assert.IsType<NamedTypeSyntax>(routine.Heading.ResultType);
        Assert.NotNull(routine.Block);
    }

    [Fact]
    public void Routines_FunctionResultMayBeOmitted()
    {
        Assert.Equal($"(RoutineDeclaration (RoutineHeading function (IdentifierName F)) ; {Body} ;)", DumpDeclarations("function F; begin end;"));
    }

    [Fact]
    public void Routines_DirectivesEachWithTheirSemicolon()
    {
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading procedure (IdentifierName P)) ; (Directive overload ;) (Directive inline ;) (Directive stdcall ;) "
            + $"{Body} ;)",
            DumpDeclarations("procedure P; overload; inline; stdcall; begin end;"));

        var routine = (RoutineDeclarationSyntax)((ProgramSyntax)Parse("procedure P; overload; inline; begin end; begin end.").Root).Block.Declarations[0];
        Assert.Equal([SyntaxKind.OverloadKeyword, SyntaxKind.InlineKeyword], routine.Directives.Select(d => d.Keyword.ContextualKind));
    }

    [Fact]
    [Trait("Req", "LANG-160")]
    public void Routines_DirectiveWithoutPrecedingSemicolon()
    {
        Assert.Equal(
            $"(RoutineDeclaration (RoutineHeading procedure (IdentifierName P)) (Directive overload ;) {Body} ;)",
            DumpDeclarations("procedure P overload; begin end;"));
    }

    [Fact]
    public void Routines_ForwardAndExternalHaveNoBody()
    {
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading procedure (IdentifierName P)) ; (Directive forward ;)) "
            + $"(RoutineDeclaration (RoutineHeading procedure (IdentifierName P)) ; {Body} ;)",
            DumpDeclarations("procedure P; forward; procedure P; begin end;"));
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading function (IdentifierName GetTickCount) : (NamedType (IdentifierName Cardinal))) ; (Directive stdcall ;) "
            + "(Directive external (LiteralExpression 'kernel32.dll') name (LiteralExpression 'GetTickCount') ;)) "
            + "(RoutineDeclaration (RoutineHeading procedure (IdentifierName X)) ; (Directive external (IdentifierName Lib) name (LiteralExpression 'x') delayed ;)) "
            + "(RoutineDeclaration (RoutineHeading procedure (IdentifierName Y)) ; (Directive external ;))",
            DumpDeclarations(
                "function GetTickCount: Cardinal; stdcall; external 'kernel32.dll' name 'GetTickCount'; "
                + "procedure X; external Lib name 'x' delayed; procedure Y; external;"));
    }

    [Fact]
    public void Routines_PublicNameMessageAndHintDirectives()
    {
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading procedure (IdentifierName Q)) ; (Directive public name (LiteralExpression 'q_export') ;) "
            + $"(Directive message (LiteralExpression 5) ;) (Directive deprecated (LiteralExpression 'old') ;) (Directive platform ;) {Body} ;)",
            DumpDeclarations("procedure Q; public name 'q_export'; message 5; deprecated 'old'; platform; begin end;"));
    }

    [Fact]
    public void Routines_MethodImplementationsAndClassRoutines()
    {
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading procedure (QualifiedName (IdentifierName TFoo) . (IdentifierName Bar)) "
            + $"(ParameterList ( (Parameter (IdentifierName X) : (NamedType (IdentifierName Integer))) ))) ; {Body} ;) "
            + "(RoutineDeclaration (RoutineHeading class function (QualifiedName (IdentifierName TFoo) . (IdentifierName Make)) : "
            + $"(NamedType (IdentifierName TFoo))) ; {Body} ;) "
            + $"(RoutineDeclaration (RoutineHeading constructor (QualifiedName (IdentifierName TFoo) . (IdentifierName Create))) ; {Body} ;) "
            + $"(RoutineDeclaration (RoutineHeading destructor (QualifiedName (IdentifierName TFoo) . (IdentifierName Destroy))) ; (Directive override ;) {Body} ;) "
            + "(RoutineDeclaration (RoutineHeading class operator (QualifiedName (IdentifierName TVec) . (IdentifierName Add)) (ParameterList ( "
            + "(Parameter const (IdentifierName A) , (IdentifierName B) : (NamedType (IdentifierName TVec))) )) : (NamedType (IdentifierName TVec))) ; "
            + $"{Body} ;)",
            DumpDeclarations(
                "procedure TFoo.Bar(X: Integer); begin end; class function TFoo.Make: TFoo; begin end; constructor TFoo.Create; begin end; "
                + "destructor TFoo.Destroy; override; begin end; class operator TVec.Add(const A, B: TVec): TVec; begin end;"));

        var tree = Parse("class operator TVec.Add(const A, B: TVec): TVec; begin end; begin end.");
        var heading = ((RoutineDeclarationSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Heading;
        Assert.Equal(SyntaxKind.OperatorKeyword, heading.Keyword.ContextualKind);
        Assert.Equal(SyntaxKind.ClassKeyword, heading.ClassKeyword!.Value.Kind);
    }

    [Fact]
    [Trait("Req", "LANG-161")]
    public void Routines_NestedRoutinesAndLocalDeclarations()
    {
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading procedure (IdentifierName Outer)) ; (Block "
            + "(VarSection var (VarDeclaration (IdentifierName X) : (NamedType (IdentifierName Integer)) ;)) "
            + "(RoutineDeclaration (RoutineHeading function (IdentifierName Inner) : (NamedType (IdentifierName Integer))) ; (Block "
            + "(ConstSection const (ConstDeclaration C = (LiteralExpression 1) ;)) "
            + "(RoutineDeclaration (RoutineHeading procedure (IdentifierName Deepest)) ; (Block (CompoundStatement begin end)) ;) "
            + "(CompoundStatement begin end)) ;) "
            + "(CompoundStatement begin end)) ;)",
            DumpDeclarations(
                "procedure Outer; var X: Integer; function Inner: Integer; const C = 1; procedure Deepest; begin end; begin end; begin end;"));
    }

    [Fact]
    public void Routines_InterfaceHeadingsHaveNoBodies()
    {
        var text = "unit U; interface procedure P; function F(X: Integer): Integer; overload; "
                   + "implementation procedure P; begin end; function F(X: Integer): Integer; begin end; begin end.";

        Assert.Equal(
            "(Unit unit (IdentifierName U) ; (InterfaceSection interface "
            + "(RoutineDeclaration (RoutineHeading procedure (IdentifierName P)) ;) "
            + "(RoutineDeclaration (RoutineHeading function (IdentifierName F) (ParameterList ( (Parameter (IdentifierName X) : (NamedType (IdentifierName Integer))) )) "
            + ": (NamedType (IdentifierName Integer))) ; (Directive overload ;))) "
            + "(ImplementationSection implementation "
            + $"(RoutineDeclaration (RoutineHeading procedure (IdentifierName P)) ; {Body} ;) "
            + "(RoutineDeclaration (RoutineHeading function (IdentifierName F) (ParameterList ( (Parameter (IdentifierName X) : (NamedType (IdentifierName Integer))) )) "
            + $": (NamedType (IdentifierName Integer))) ; {Body} ;)) "
            + "(InitializationSection begin) end . <eof>)",
            Dump(text));
    }

    [Fact]
    public void Routines_DirectiveWordsFollowedByColonAreNotDirectives()
    {
        // 'name' after a heading is a directive only in directive position: here the next line starts a parameter-like field
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading procedure (IdentifierName P) (ParameterList ( (Parameter (IdentifierName overload) : "
            + $"(NamedType (IdentifierName Integer))) ))) ; {Body} ;)",
            DumpDeclarations("procedure P(overload: Integer); begin end;"));
    }

    [Theory]
    [InlineData("procedure; begin end; begin end.", "E1102(1,10): Identifier expected but ';' found")]
    [InlineData("procedure P begin end; begin end.", "E1101(1,13): ';' expected but 'begin' found")]
    [InlineData("procedure P; begin end begin end.", "E1101(1,24): ';' expected but 'begin' found")]
    [InlineData("function F(A: ): Integer; begin end; begin end.", "E1106(1,15): Type expected but ')' found")]
    public void Routines_Errors(string text, string expected)
    {
        Assert.Equal([expected], Diagnostics(text));
    }
}
