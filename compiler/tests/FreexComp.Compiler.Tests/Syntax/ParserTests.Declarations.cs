using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>grammar.md §2: label/const/resourcestring/type/var/threadvar sections, typed constants, hint directives (C-013).</summary>
public sealed partial class ParserTests
{
    /// <summary>The dump of the block's declarations of <c>&lt;declarations&gt; begin end.</c>, which must parse cleanly.</summary>
    private static string DumpDeclarations(string declarations)
    {
        var text = declarations + " begin end.";
        var program = Assert.IsType<ProgramSyntax>(Parse(text).Root);
        Assert.Equal(text, program.ToFullString());
        Assert.Empty(Parse(text).Diagnostics);
        return string.Join(" ", program.Block.Declarations.Select(SyntaxDump.Of));
    }

    private const string Int = "(NamedType (IdentifierName Integer))";

    private static string Lit(string text) => $"(LiteralExpression {text})";

    // ------------------------------------------------------------------ const / resourcestring

    [Fact]
    public void Declarations_TrueConstantsAndResourceStrings()
    {
        Assert.Equal(
            $"(ConstSection const (ConstDeclaration A = {Lit("1")} ;) (ConstDeclaration B = {Lit("'x'#13")} ;) "
            + $"(ConstDeclaration C = {Lit("nil")} ;) (ConstDeclaration D = {Lit("2.5")} ;)) "
            + $"(ConstSection resourcestring (ConstDeclaration R = {Lit("'r'")} ;))",
            DumpDeclarations("const A = 1; B = 'x'#13; C = nil; D = 2.5; resourcestring R = 'r';"));
    }

    [Fact]
    [Trait("Req", "LANG-171")]
    public void Declarations_ConstantExpressionsFollowThePrecedenceLevels()
    {
        Assert.Equal(
            "(ConstSection const (ConstDeclaration A = (BinaryExpression (UnaryExpression - (LiteralExpression 1)) + "
            + "(BinaryExpression (LiteralExpression 2) * (ParenthesizedExpression ( (BinaryExpression (LiteralExpression 3) - (IdentifierName B)) )))) ;) "
            + "(ConstDeclaration C = (BinaryExpression (IdentifierName A) <> (UnaryExpression not (IdentifierName B))) ;) "
            + "(ConstDeclaration D = (BinaryExpression (BinaryExpression (IdentifierName A) or (IdentifierName B)) = "
            + "(BinaryExpression (IdentifierName C) div (UnaryExpression - (LiteralExpression 4)))) ;))",
            DumpDeclarations("const A = -1 + 2 * (3 - B); C = A <> not B; D = A or B = C div -4;"));
    }

    [Fact]
    [Trait("Req", "LANG-170")]
    public void Declarations_ChainedRelationalOperators_AreE1110()
    {
        Assert.Equal(
            ["E1110(1,17): Relational operators cannot be chained; use parentheses"],
            Diagnostics("const A = 1 < 2 < 3; begin end."));
    }

    // ------------------------------------------------------------------ typed constants

    [Fact]
    public void Declarations_TypedConstants_ScalarArrayRecordAndNested()
    {
        Assert.Equal(
            $"(ConstSection const (ConstDeclaration P : {Int} = {Lit("5")} ;) "
            + "(ConstDeclaration Arr : (NamedType (IdentifierName TArr)) = (TypedConstantAggregate ( "
            + $"{Lit("1")} , {Lit("2")} , (TypedConstantAggregate ( {Lit("3")} , {Lit("4")} )) )) ;) "
            + "(ConstDeclaration Rec : (NamedType (IdentifierName TPoint)) = (TypedConstantAggregate ( "
            + $"(RecordConstantField (IdentifierName X) : {Lit("1")}) ; "
            + $"(RecordConstantField (IdentifierName Y) : (UnaryExpression - {Lit("2")})) ; )) ;) "
            + "(ConstDeclaration E : (NamedType (IdentifierName TEmpty)) = (TypedConstantAggregate ( )) ;))",
            DumpDeclarations("const P: Integer = 5; Arr: TArr = (1, 2, (3, 4)); Rec: TPoint = (X: 1; Y: -2;); E: TEmpty = ();"));
    }

    [Fact]
    public void Declarations_TypedConstant_ParenthesisedExpressionContinues()
    {
        Assert.Equal(
            $"(ConstSection const (ConstDeclaration Q : {Int} = (BinaryExpression (ParenthesizedExpression ( "
            + $"(BinaryExpression {Lit("1")} + {Lit("2")}) )) * {Lit("3")}) ;) "
            + $"(ConstDeclaration R : {Int} = (ParenthesizedExpression ( {Lit("7")} )) ;))",
            DumpDeclarations("const Q: Integer = (1 + 2) * 3; R: Integer = (7);"));
    }

    [Fact]
    public void Declarations_TypedConstant_RecordFieldsHoldNestedAggregates()
    {
        Assert.Equal(
            "(ConstSection const (ConstDeclaration R : (NamedType (IdentifierName T)) = (TypedConstantAggregate ( "
            + $"(RecordConstantField (IdentifierName A) : (TypedConstantAggregate ( {Lit("1")} , {Lit("2")} ))) ; "
            + $"(RecordConstantField (IdentifierName B) : (TypedConstantAggregate ( (RecordConstantField (IdentifierName C) : {Lit("'c'")}) ))) )) ;))",
            DumpDeclarations("const R: T = (A: (1, 2); B: (C: 'c'));"));
    }

    // ------------------------------------------------------------------ type

    [Fact]
    public void Declarations_TypeSection()
    {
        Assert.Equal(
            $"(TypeSection type (TypeDeclaration T = {Int} ;) "
            + "(TypeDeclaration U = type (NamedType (QualifiedName (IdentifierName Sys) . (IdentifierName Word))) (HintDirective platform) ;) "
            + $"(TypeDeclaration S = (StringType string [ {Lit("20")} ]) ;) "
            + "(TypeDeclaration L = (StringType string) ;))",
            DumpDeclarations("type T = Integer; U = type Sys.Word platform; S = string[20]; L = string;"));

        var declaration = ((ProgramSyntax)Parse("type T = type Integer; begin end.").Root).Block.Declarations[0];
        var type = Assert.IsType<TypeSectionSyntax>(declaration).Declarations[0];
        Assert.Equal("T", type.Identifier.Text);
        Assert.Equal(SyntaxKind.TypeKeyword, type.TypeKeyword!.Value.Kind);
        Assert.IsType<NamedTypeSyntax>(type.Type);
    }

    // ------------------------------------------------------------------ var / threadvar

    [Fact]
    public void Declarations_VarAndThreadVarSections()
    {
        Assert.Equal(
            $"(VarSection var (VarDeclaration (IdentifierName A) , (IdentifierName B) : {Int} ;) "
            + $"(VarDeclaration (IdentifierName S) : (StringType string) = {Lit("'x'")} (HintDirective deprecated 'use T') ;)) "
            + $"(VarSection threadvar (VarDeclaration (IdentifierName C) : {Int} ;))",
            DumpDeclarations("var A, B: Integer; S: string = 'x' deprecated 'use T'; threadvar C: Integer;"));

        var section = (VarSectionSyntax)((ProgramSyntax)Parse("var A, B: Integer; begin end.").Root).Block.Declarations[0];
        Assert.Equal(["A", "B"], section.Declarations[0].Identifiers.Select(i => i.Identifier.Text));
        Assert.Null(section.Declarations[0].EqualsToken);
        Assert.Null(section.Declarations[0].Initializer);
    }

    [Fact]
    public void Declarations_VarInitialiserMayBeAnAggregate()
    {
        Assert.Equal(
            $"(VarSection var (VarDeclaration (IdentifierName V) : (NamedType (IdentifierName TArr)) = (TypedConstantAggregate ( {Lit("1")} , {Lit("2")} )) ;))",
            DumpDeclarations("var V: TArr = (1, 2);"));
    }

    // ------------------------------------------------------------------ label

    [Fact]
    public void Declarations_LabelSection()
    {
        Assert.Equal(
            $"(LabelSection label {Lit("10")} , (IdentifierName Done) ;)",
            DumpDeclarations("label 10, Done;"));
    }

    // ------------------------------------------------------------------ hint directives

    [Fact]
    public void Declarations_HintDirectivesOnConstants()
    {
        Assert.Equal(
            $"(ConstSection const (ConstDeclaration X = {Lit("1")} (HintDirective deprecated) ;) "
            + $"(ConstDeclaration Y = {Lit("2")} (HintDirective experimental) (HintDirective platform) ;))",
            DumpDeclarations("const X = 1 deprecated; Y = 2 experimental platform;"));
    }

    [Fact]
    public void Declarations_ContextualWordsAreOrdinaryNamesOutsideDirectivePositions()
    {
        Assert.Equal(
            $"(ConstSection const (ConstDeclaration platform = (IdentifierName deprecated) ;))",
            DumpDeclarations("const platform = deprecated;"));
    }

    // ------------------------------------------------------------------ sections in units, several sections in a row

    [Fact]
    public void Declarations_InUnitSections()
    {
        var text = "unit U; interface const A = 1; type T = Integer; var V: T; implementation var W: Integer; const B = 2; end.";

        Assert.Equal(
            $"(Unit unit (IdentifierName U) ; (InterfaceSection interface (ConstSection const (ConstDeclaration A = {Lit("1")} ;)) "
            + $"(TypeSection type (TypeDeclaration T = {Int} ;)) (VarSection var (VarDeclaration (IdentifierName V) : (NamedType (IdentifierName T)) ;))) "
            + $"(ImplementationSection implementation (VarSection var (VarDeclaration (IdentifierName W) : {Int} ;)) "
            + $"(ConstSection const (ConstDeclaration B = {Lit("2")} ;))) end . <eof>)",
            Dump(text));
    }

    // ------------------------------------------------------------------ errors

    [Theory]
    [InlineData("var X Integer; begin end.", "E1101(1,7): ':' expected but 'Integer' found")]
    [InlineData("var X: ; begin end.", "E1106(1,8): Type expected but ';' found")]
    [InlineData("const A = ; begin end.", "E1103(1,11): Expression expected but ';' found")]
    [InlineData("type T = Integer begin end.", "E1101(1,18): ';' expected but 'begin' found")]
    [InlineData("var A: Integer B: Integer; begin end.", "E1101(1,16): ';' expected but 'B' found")]
    [InlineData("const A: Integer = (1, 2; begin end.", "E1101(1,25): ')' expected but ';' found")]
    [InlineData("type = Integer; begin end.", "E1102(1,6): Identifier expected but '=' found")]
    public void Declarations_Errors(string text, string expected)
    {
        Assert.Equal([expected], Diagnostics(text));
    }

    [Fact]
    public void Declarations_MissingTypeIsAMissingNamedType()
    {
        var text = "var X: ; begin end.";
        var section = (VarSectionSyntax)((ProgramSyntax)Parse(text).Root).Block.Declarations[0];

        var type = Assert.IsType<NamedTypeSyntax>(section.Declarations[0].Type);
        Assert.True(type.IsMissing);
        Assert.Equal(";", section.Declarations[0].SemicolonToken.Text);
    }
}
