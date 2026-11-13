using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// grammar.md §7 genericParams / genericArgs and generics-anonymous.md §1: type parameter lists on type declarations and
/// methods, type arguments in types and expressions, the <c>&lt;</c> decision (LANG-705) and <c>&gt;=</c> splitting
/// (LANG-706) (C-019).
/// </summary>
public sealed partial class ParserTests
{
    private static string Args(params string[] types) => $"(TypeArgumentList < {string.Join(" , ", types)} >)";

    private static string Generic(string name, params string[] types) => $"(GenericName {name} {Args(types)})";

    private static string Param(string name) => $"(TypeParameter (IdentifierName {name}))";

    private static string Params(params string[] parameters) => $"(TypeParameterList < {string.Join(" ; ", parameters)} >)";

    // ------------------------------------------------------------------ type arguments in type positions

    [Theory]
    [Trait("Req", "LANG-705")]
    [InlineData("TList<Integer>", "(NamedType (GenericName TList (TypeArgumentList < (NamedType (IdentifierName Integer)) >)))")]
    [InlineData("TDictionary<string, TList<Integer>>",
        "(NamedType (GenericName TDictionary (TypeArgumentList < (StringType string) , "
        + "(NamedType (GenericName TList (TypeArgumentList < (NamedType (IdentifierName Integer)) >))) >)))")]
    [InlineData("Sys.TPair<K, V>.TInner",
        "(NamedType (QualifiedName (QualifiedName (IdentifierName Sys) . "
        + "(GenericName TPair (TypeArgumentList < (NamedType (IdentifierName K)) , (NamedType (IdentifierName V)) >))) . (IdentifierName TInner)))")]
    [InlineData("array of TArray<^Byte>",
        "(ArrayType array of (NamedType (GenericName TArray (TypeArgumentList < (PointerType ^ (NamedType (IdentifierName Byte))) >))))")]
    [InlineData("class(TList<T>, IComparer<T>) end",
        "(ClassType class (Heritage ( (NamedType (GenericName TList (TypeArgumentList < (NamedType (IdentifierName T)) >))) , "
        + "(NamedType (GenericName IComparer (TypeArgumentList < (NamedType (IdentifierName T)) >))) )) end)")]
    [InlineData("reference to function(A: TList<T>): TPair<A, B>",
        "(ProceduralType reference to function (ParameterList ( (Parameter (IdentifierName A) : "
        + "(NamedType (GenericName TList (TypeArgumentList < (NamedType (IdentifierName T)) >)))) )) : "
        + "(NamedType (GenericName TPair (TypeArgumentList < (NamedType (IdentifierName A)) , (NamedType (IdentifierName B)) >))))")]
    public void Generics_TypeArgumentsInTypes(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Fact]
    public void Generics_GenericNameTokens()
    {
        var tree = Parse("var L: TList<Integer>; begin end.");
        Assert.Empty(tree.Diagnostics);
        var type = (NamedTypeSyntax)((VarSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations[0].Type;
        var name = Assert.IsType<GenericNameSyntax>(type.Name);

        Assert.Equal("TList", name.Identifier.Text);
        Assert.Equal(SyntaxKind.LessThanToken, name.TypeArgumentList.LessThanToken.Kind);
        Assert.Equal(SyntaxKind.GreaterThanToken, name.TypeArgumentList.GreaterThanToken.Kind);
        Assert.Equal("Integer", Assert.Single(name.TypeArgumentList.Arguments).ToString());
    }

    // ------------------------------------------------------------------ type parameter lists

    [Fact]
    public void Generics_TypeDeclarationWithTypeParameters()
    {
        Assert.Equal(
            $"(TypeSection type (TypeDeclaration TList {Params(Param("T"))} = (ClassType class end) ;) "
            + $"(TypeDeclaration TArr {Params(Param("T"))} = (ArrayType array of (NamedType (IdentifierName T))) ;))",
            DumpDeclarations("type TList<T> = class end; TArr<T> = array of T;"));
    }

    [Fact]
    [Trait("Req", "LANG-702")]
    public void Generics_Constraints()
    {
        Assert.Equal(
            "(TypeSection type (TypeDeclaration TPair (TypeParameterList < "
            + "(TypeParameter (IdentifierName K) , (IdentifierName V) : (KeywordConstraint class)) ; "
            + "(TypeParameter (IdentifierName W) : (KeywordConstraint record) , (KeywordConstraint constructor)) ; "
            + "(TypeParameter (IdentifierName X) : (NamedType (IdentifierName TBase)) , "
            + $"(NamedType {Generic("IFoo", "(NamedType (IdentifierName X))")})) >) = (RecordType record end) ;))",
            DumpDeclarations("type TPair<K, V: class; W: record, constructor; X: TBase, IFoo<X>> = record end;"));
    }

    [Fact]
    [Trait("Req", "LANG-706")]
    public void Generics_GreaterThanEqualsIsSplitWhenClosingAList()
    {
        Assert.Equal(
            $"(TypeSection type (TypeDeclaration TList {Params(Param("T"))} = (ClassType class end) ;)) "
            + $"(ConstSection const (ConstDeclaration C : (NamedType {Generic("TFoo", "(NamedType (IdentifierName Integer))")}) = (LiteralExpression nil) ;)) "
            + "(VarSection var (VarDeclaration (IdentifierName V) : "
            + $"(NamedType {Generic("TList", $"(NamedType {Generic("TPair", "(NamedType (IdentifierName A))", "(NamedType (IdentifierName B))")})")}) "
            + "= (LiteralExpression nil) ;))",
            DumpDeclarations("type TList<T>= class end; const C: TFoo<Integer>= nil; var V: TList<TPair<A,B>>= nil;"));

        var tree = Parse("type TList<T>= class end; begin end.");
        var declaration = ((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations[0];
        Assert.Equal((">", 12), (declaration.TypeParameterList!.GreaterThanToken.Text, declaration.TypeParameterList.GreaterThanToken.Span.Start));
        Assert.Equal(("=", 13), (declaration.EqualsToken.Text, declaration.EqualsToken.Span.Start));
    }

    [Fact]
    [Trait("Req", "LANG-701")]
    public void Generics_GenericMethodDeclarations()
    {
        Assert.Equal(
            "(ClassType class (MethodDeclaration (RoutineHeading class function (IdentifierName Max) "
            + $"{Params("(TypeParameter (IdentifierName T) : (KeywordConstraint record))")} "
            + "(ParameterList ( (Parameter (IdentifierName A) , (IdentifierName B) : (NamedType (IdentifierName T))) )) : (NamedType (IdentifierName T))) ;) "
            + $"(MethodDeclaration (RoutineHeading procedure (IdentifierName Swap) {Params(Param("A"), Param("B"))}) ;) end)",
            DumpType("class class function Max<T: record>(A, B: T): T; procedure Swap<A; B>; end"));
    }

    [Fact]
    [Trait("Req", "LANG-704")]
    public void Generics_MethodImplementationsRepeatTheParameterNames()
    {
        Assert.Equal(
            $"(RoutineDeclaration (RoutineHeading procedure (QualifiedName {Generic("TList", "(NamedType (IdentifierName T))")} . (IdentifierName Add)) "
            + "(ParameterList ( (Parameter const (IdentifierName V) : (NamedType (IdentifierName T))) ))) ; (Block (CompoundStatement begin end)) ;) "
            + "(RoutineDeclaration (RoutineHeading function (QualifiedName (IdentifierName TUtil) . (IdentifierName Max)) "
            + $"{Params(Param("T"))} (ParameterList ( (Parameter (IdentifierName A) , (IdentifierName B) : (NamedType (IdentifierName T))) )) "
            + ": (NamedType (IdentifierName T))) ; (Block (CompoundStatement begin end)) ;) "
            + "(RoutineDeclaration (RoutineHeading procedure (QualifiedName (QualifiedName "
            + $"{Generic("TDict", "(NamedType (IdentifierName K))", "(NamedType (IdentifierName V))")} . (IdentifierName TPairs)) . (IdentifierName Clear))) ; "
            + "(Block (CompoundStatement begin end)) ;)",
            DumpDeclarations("procedure TList<T>.Add(const V: T); begin end; function TUtil.Max<T>(A, B: T): T; begin end; "
                             + "procedure TDict<K, V>.TPairs.Clear; begin end;"));
    }

    // ------------------------------------------------------------------ type arguments in expressions (LANG-705)

    [Theory]
    [Trait("Req", "LANG-705")]
    [InlineData("TList<Integer>.Create",
        "(MemberAccessExpression (GenericName TList (TypeArgumentList < (NamedType (IdentifierName Integer)) >)) . (IdentifierName Create))")]
    [InlineData("TUtil.Max<Integer>(1, 2)",
        "(InvocationExpression (MemberAccessExpression (IdentifierName TUtil) . (GenericName Max (TypeArgumentList < (NamedType (IdentifierName Integer)) >))) "
        + "(ArgumentList ( (Argument (LiteralExpression 1)) , (Argument (LiteralExpression 2)) )))")]
    [InlineData("SizeOf(TPair<string, ^Byte>)",
        "(InvocationExpression (IdentifierName SizeOf) (ArgumentList ( (Argument (GenericName TPair (TypeArgumentList < (StringType string) , "
        + "(PointerType ^ (NamedType (IdentifierName Byte))) >))) )))")]
    [InlineData("X = TFoo<Sys.T>",
        "(BinaryExpression (IdentifierName X) = (GenericName TFoo (TypeArgumentList < (NamedType (QualifiedName (IdentifierName Sys) . (IdentifierName T))) >)))")]
    [InlineData("TDict<string, TList<Integer>>.Create",
        "(MemberAccessExpression (GenericName TDict (TypeArgumentList < (StringType string) , "
        + "(NamedType (GenericName TList (TypeArgumentList < (NamedType (IdentifierName Integer)) >))) >)) . (IdentifierName Create))")]
    public void Generics_TypeArgumentsInExpressions(string expression, string expected)
    {
        Assert.Equal(expected, DumpExpression(expression));
    }

    [Theory]
    [Trait("Req", "LANG-705")]
    [InlineData("(A < B) and (C > D)",
        "(BinaryExpression (ParenthesizedExpression ( (BinaryExpression (IdentifierName A) < (IdentifierName B)) )) and "
        + "(ParenthesizedExpression ( (BinaryExpression (IdentifierName C) > (IdentifierName D)) )))")]
    [InlineData("F(A < B, C > D)",
        "(InvocationExpression (IdentifierName F) (ArgumentList ( (Argument (BinaryExpression (IdentifierName A) < (IdentifierName B))) , "
        + "(Argument (BinaryExpression (IdentifierName C) > (IdentifierName D))) )))")]
    [InlineData("A < B", "(BinaryExpression (IdentifierName A) < (IdentifierName B))")]
    [InlineData("A < 1", "(BinaryExpression (IdentifierName A) < (LiteralExpression 1))")]
    public void Generics_LessThanStaysAnOperatorWhenNoTypeArgumentListFits(string expression, string expected)
    {
        Assert.Equal(expected, DumpExpression(expression));
    }

    [Fact]
    [Trait("Req", "LANG-705")]
    public void Generics_FollowingTokenThatStartsAFactor_MeansLessThan()
    {
        // 'A < B > C': after '>' comes an identifier, so '<' is less-than (and the chain is E1110)
        Assert.Equal(["E1110(1,17): Relational operators cannot be chained; use parentheses"], ExpressionDiagnostics("A < B > C"));
    }

    [Fact]
    public void Generics_StatementsWithTypeArguments()
    {
        Assert.Equal(
            "(AssignmentStatement (IdentifierName L) := (InvocationExpression (MemberAccessExpression "
            + "(GenericName TList (TypeArgumentList < (NamedType (IdentifierName Integer)) >)) . (IdentifierName Create)) (ArgumentList ( )))) ; "
            + "(IfStatement if (BinaryExpression (IdentifierName I) < (IdentifierName N)) then (ExpressionStatement (IdentifierName Exit)))",
            DumpBody("L := TList<Integer>.Create(); if I < N then Exit"));
    }

    // ------------------------------------------------------------------ errors

    [Fact]
    public void Generics_UnclosedTypeArgumentList_InsertsGreaterThan()
    {
        Assert.Equal(["E1101(1,21): '>' expected but ';' found"], Diagnostics("var L: TList<Integer; begin end."));
    }

    [Fact]
    public void Generics_UnclosedTypeParameterList_InsertsGreaterThan()
    {
        Assert.Equal(["E1101(1,10): '>' expected but '=' found"], Diagnostics("type T<A = class end; begin end."));
    }
}
