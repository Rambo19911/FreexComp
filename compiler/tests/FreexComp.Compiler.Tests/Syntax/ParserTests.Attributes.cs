using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>grammar.md §7 attributeList and LANG-172: attribute lists before declarations, members and parameters (C-019).</summary>
public sealed partial class ParserTests
{
    private static string Attr(string name) => $"(AttributeList [ (Attribute (IdentifierName {name})) ])";

    [Fact]
    [Trait("Req", "LANG-172")]
    public void Attributes_OnTypeDeclarations()
    {
        Assert.Equal(
            "(TypeSection type (TypeDeclaration (AttributeList [ (Attribute (IdentifierName Serializable)) , "
            + "(Attribute (IdentifierName Version) (ArgumentList ( (Argument (LiteralExpression 2)) , (Argument (LiteralExpression 'x')) ))) ]) "
            + "(AttributeList [ (Attribute (QualifiedName (IdentifierName Sys) . (IdentifierName Tag))) ]) TFoo = (ClassType class end) ;) "
            + $"(TypeDeclaration B = (NamedType (IdentifierName Byte)) ;) (TypeDeclaration {Attr("Tag")} C = (NamedType (IdentifierName Byte)) ;))",
            DumpDeclarations("type [Serializable, Version(2, 'x')] [Sys.Tag] TFoo = class end; B = Byte; [Tag] C = Byte;"));

        var tree = Parse("type [A, B] [C()] T = Integer; begin end.");
        Assert.Empty(tree.Diagnostics);
        var declaration = ((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations[0];
        Assert.Equal(2, declaration.Attributes.Count);
        Assert.Equal(["A", "B"], declaration.Attributes[0].Attributes.Select(a => a.Name.ToString()));
        Assert.Empty(declaration.Attributes[1].Attributes[0].ArgumentList!.Arguments);
    }

    [Fact]
    [Trait("Req", "LANG-172")]
    public void Attributes_OnVarConstAndRoutineDeclarations()
    {
        Assert.Equal(
            $"(VarSection var (VarDeclaration {Attr("Volatile")} (IdentifierName X) : (NamedType (IdentifierName Integer)) ;)) "
            + $"(ConstSection const (ConstDeclaration {Attr("Tag")} C = (LiteralExpression 1) ;)) "
            + $"(RoutineDeclaration {Attr("Test")} (RoutineHeading procedure (IdentifierName Q)) ; (Block (CompoundStatement begin end)) ;)",
            DumpDeclarations("var [Volatile] X: Integer; const [Tag] C = 1; [Test] procedure Q; begin end;"));
    }

    [Fact]
    [Trait("Req", "LANG-763")]
    public void Attributes_OnParameters()
    {
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading procedure (IdentifierName P) (ParameterList ( "
            + $"(Parameter {Attr("Ref")} const (IdentifierName X) : (NamedType (IdentifierName TRec))) ; "
            + "(Parameter (IdentifierName Y) : (NamedType (IdentifierName Integer))) ))) ; (Block (CompoundStatement begin end)) ;)",
            DumpDeclarations("procedure P([Ref] const X: TRec; Y: Integer); begin end;"));
    }

    [Fact]
    [Trait("Req", "LANG-172")]
    public void Attributes_OnClassMembers()
    {
        Assert.Equal(
            $"(ClassType class (FieldDeclaration {Attr("Weak")} (IdentifierName FOwner) : (NamedType (IdentifierName TObject)) ;) "
            + $"(VisibilitySection published (MethodDeclaration {Attr("Test")} (RoutineHeading procedure (IdentifierName Run)) ;) "
            + $"(PropertyDeclaration {Attr("Stored")} property P : (NamedType (IdentifierName Integer)) (PropertySpecifier read (IdentifierName F)) ;) "
            + $"(ClassVarSection class var (FieldDeclaration {Attr("Tag")} (IdentifierName Count) : (NamedType (IdentifierName Integer)) ;))) end)",
            DumpType("class [Weak] FOwner: TObject; published [Test] procedure Run; [Stored] property P: Integer read F; class var [Tag] Count: Integer; end"));
    }

    [Fact]
    public void Attributes_OnRecordFieldsAndInterfaceMethods()
    {
        Assert.Equal(
            $"(RecordType record (FieldDeclaration {Attr("Unsafe")} (IdentifierName P) : (NamedType (IdentifierName Pointer)) ;) end)",
            DumpType("record [Unsafe] P: Pointer; end"));
        Assert.Equal(
            "(InterfaceType interface (Guid [ (LiteralExpression '{00000000-0000-0000-0000-000000000001}') ]) "
            + $"(MethodDeclaration {Attr("Test")} (RoutineHeading procedure (IdentifierName P)) ;) end)",
            DumpType("interface ['{00000000-0000-0000-0000-000000000001}'] [Test] procedure P; end"));
    }

    [Fact]
    public void Attributes_NotFollowedByAnAttributableDeclaration_StandAlone()
    {
        Assert.Equal(
            $"{Attr("Tag")} (TypeSection type (TypeDeclaration T = (NamedType (IdentifierName Integer)) ;))",
            DumpDeclarations("[Tag] type T = Integer;"));
        Assert.Equal(
            $"(ClassType class {Attr("Tag")} (ConstSection const (ConstDeclaration C = (LiteralExpression 1) ;)) end)",
            DumpType("class [Tag] const C = 1; end"));
    }

    [Fact]
    [Trait("Req", "LANG-172")]
    public void Attributes_BracketsInExpressionsStaySetConstructors()
    {
        Assert.Equal(
            "(ConstSection const (ConstDeclaration S = (SetConstructor [ (SetElement (LiteralExpression 1)) ]) ;) "
            + "(ConstDeclaration T = (SetConstructor [ ]) ;))",
            DumpDeclarations("const S = [1]; T = [];"));
    }

    [Theory]
    [InlineData("type [1] T = Integer; begin end.", "E1119(1,6): Invalid attribute syntax")]
    [InlineData("type [A, ] T = Integer; begin end.", "E1119(1,6): Invalid attribute syntax")]
    [InlineData("type [A T = Integer; begin end.", "E1101(1,9): ']' expected but 'T' found")]
    public void Attributes_Errors(string text, string expected)
    {
        Assert.Equal([expected], Diagnostics(text));
    }
}
