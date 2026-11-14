using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>grammar.md §3: every type syntax except class / interface / helper bodies (C-014).</summary>
public sealed partial class ParserTests
{
    /// <summary>The dump of the type of <c>type T = &lt;type&gt;;</c>, which must parse cleanly.</summary>
    private static string DumpType(string type)
    {
        var text = $"type T = {type}; begin end.";
        var tree = Parse(text);
        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);
        var section = Assert.IsType<TypeSectionSyntax>(((ProgramSyntax)tree.Root).Block.Declarations[0]);
        return SyntaxDump.Of(Assert.Single(section.Declarations).Type);
    }

    private static string Named(string name) => $"(NamedType (IdentifierName {name}))";

    [Theory]
    [InlineData("Integer", "(NamedType (IdentifierName Integer))")]
    [InlineData("Sys.Types.TFoo",
        "(NamedType (QualifiedName (QualifiedName (IdentifierName Sys) . (IdentifierName Types)) . (IdentifierName TFoo)))")]
    [InlineData("string[255]", "(StringType string [ (LiteralExpression 255) ])")]
    [InlineData("^TNode", "(PointerType ^ (NamedType (IdentifierName TNode)))")]
    [InlineData("class of TObject", "(ClassRefType class of (NamedType (IdentifierName TObject)))")]
    public void Types_NamedStringPointerClassRef(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    /// <summary>C-179: <c>^</c> accepts the reserved type name <c>string</c> as well as identifiers such as <c>AnsiString</c>.</summary>
    [Theory]
    [InlineData("^string", "(PointerType ^ (StringType string))")]
    [InlineData("^AnsiString", "(PointerType ^ (NamedType (IdentifierName AnsiString)))")]
    [InlineData("^ShortString", "(PointerType ^ (NamedType (IdentifierName ShortString)))")]
    public void PointerToString(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Theory]
    [InlineData("(Red, Green = 5, Blue)",
        "(EnumType ( (EnumMember Red) , (EnumMember Green = (LiteralExpression 5)) , (EnumMember Blue) ))")]
    [InlineData("(Single)", "(EnumType ( (EnumMember Single) ))")]
    public void Types_Enumerations(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Theory]
    [InlineData("1..10", "(SubrangeType (LiteralExpression 1) .. (LiteralExpression 10))")]
    [InlineData("-5..Max", "(SubrangeType (UnaryExpression - (LiteralExpression 5)) .. (IdentifierName Max))")]
    [InlineData("'a'..'z'", "(SubrangeType (LiteralExpression 'a') .. (LiteralExpression 'z'))")]
    [InlineData("Low..High", "(SubrangeType (IdentifierName Low) .. (IdentifierName High))")]
    [InlineData("A + 1..B * 2",
        "(SubrangeType (BinaryExpression (IdentifierName A) + (LiteralExpression 1)) .. (BinaryExpression (IdentifierName B) * (LiteralExpression 2)))")]
    public void Types_Subranges(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Theory]
    [InlineData("array[1..10] of Integer",
        "(ArrayType array [ (SubrangeType (LiteralExpression 1) .. (LiteralExpression 10)) ] of (NamedType (IdentifierName Integer)))")]
    [InlineData("array[Boolean, 0..2] of Char",
        "(ArrayType array [ (NamedType (IdentifierName Boolean)) , (SubrangeType (LiteralExpression 0) .. (LiteralExpression 2)) ] of (NamedType (IdentifierName Char)))")]
    [InlineData("array of string", "(ArrayType array of (StringType string))")]
    [InlineData("array of const", "(ArrayType array of const)")]
    [InlineData("packed array[(a, b)] of array of Byte",
        "(ArrayType packed array [ (EnumType ( (EnumMember a) , (EnumMember b) )) ] of (ArrayType array of (NamedType (IdentifierName Byte))))")]
    public void Types_Arrays(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Theory]
    [InlineData("set of Char", "(SetType set of (NamedType (IdentifierName Char)))")]
    [InlineData("set of 0..255", "(SetType set of (SubrangeType (LiteralExpression 0) .. (LiteralExpression 255)))")]
    [InlineData("packed set of (x, y)", "(SetType packed set of (EnumType ( (EnumMember x) , (EnumMember y) )))")]
    [InlineData("file", "(FileType file)")]
    [InlineData("file of Integer", "(FileType file of (NamedType (IdentifierName Integer)))")]
    public void Types_SetsAndFiles(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Fact]
    public void Types_RecordWithFields_LastSemicolonOptional()
    {
        Assert.Equal(
            $"(RecordType record (FieldDeclaration (IdentifierName X) , (IdentifierName Y) : {Named("Integer")} ;) "
            + "(FieldDeclaration (IdentifierName Name) : (StringType string) (HintDirective deprecated)) end)",
            DumpType("record X, Y: Integer; Name: string deprecated end"));
        Assert.Equal("(RecordType packed record end)", DumpType("packed record end"));
        Assert.Equal(
            $"(RecordType record (FieldDeclaration (IdentifierName A) : (RecordType record (FieldDeclaration (IdentifierName B) : {Named("Byte")} ;) end) ;) end)",
            DumpType("record A: record B: Byte; end; end"));
    }

    [Fact]
    public void Types_RecordWithVariantPart()
    {
        Assert.Equal(
            $"(RecordType record (FieldDeclaration (IdentifierName Kind) : {Named("Integer")} ;) "
            + $"(VariantPart case Tag : {Named("Byte")} of "
            + $"(Variant (LiteralExpression 0) : ( (FieldDeclaration (IdentifierName I) : {Named("Integer")}) )) ; "
            + $"(Variant (LiteralExpression 1) , (LiteralExpression 2) : ( (FieldDeclaration (IdentifierName S) : {Named("Single")} ;) "
            + $"(FieldDeclaration (IdentifierName D) : {Named("Double")}) )) ; "
            + "(Variant (LiteralExpression 3) : ( )) ;) end)",
            DumpType("record Kind: Integer; case Tag: Byte of 0: (I: Integer); 1, 2: (S: Single; D: Double); 3: (); end"));
    }

    [Fact]
    public void Types_RecordWithUntaggedNestedVariantParts()
    {
        Assert.Equal(
            $"(RecordType record (VariantPart case {Named("Byte")} of "
            + $"(Variant (LiteralExpression 0) : ( (FieldDeclaration (IdentifierName A) : {Named("Byte")} ;) "
            + $"(VariantPart case {Named("Boolean")} of (Variant (IdentifierName True) : ( (FieldDeclaration (IdentifierName B) : {Named("Byte")}) ))) ))) end)",
            DumpType("record case Byte of 0: (A: Byte; case Boolean of True: (B: Byte)) end"));
    }

    [Theory]
    [InlineData("procedure", "(ProceduralType procedure)")]
    [InlineData("procedure(A: Integer; var B: string)",
        "(ProceduralType procedure (ParameterList ( (Parameter (IdentifierName A) : (NamedType (IdentifierName Integer))) ; "
        + "(Parameter var (IdentifierName B) : (StringType string)) )))")]
    [InlineData("function(const X, Y: Double): Boolean of object",
        "(ProceduralType function (ParameterList ( (Parameter const (IdentifierName X) , (IdentifierName Y) : (NamedType (IdentifierName Double))) )) "
        + ": (NamedType (IdentifierName Boolean)) of object)")]
    [InlineData("procedure of object; stdcall", "(ProceduralType procedure of object (CallingConvention ; stdcall))")]
    [InlineData("procedure cdecl", "(ProceduralType procedure (CallingConvention cdecl))")]
    [InlineData("reference to function: Integer", "(ProceduralType reference to function : (NamedType (IdentifierName Integer)))")]
    [InlineData("procedure(out P; const Q; Count: Integer = 0; Items: array of const)",
        "(ProceduralType procedure (ParameterList ( (Parameter out (IdentifierName P)) ; (Parameter const (IdentifierName Q)) ; "
        + "(Parameter (IdentifierName Count) : (NamedType (IdentifierName Integer)) = (LiteralExpression 0)) ; "
        + "(Parameter (IdentifierName Items) : (ArrayType array of const)) )))")]
    [InlineData("procedure()", "(ProceduralType procedure (ParameterList ( )))")]
    public void Types_Procedural(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Fact]
    public void Types_ContextualKeywordsOfProceduralTypes()
    {
        var tree = Parse("type T = reference to procedure(out P); U = procedure stdcall; begin end.");
        Assert.Empty(tree.Diagnostics);
        var declarations = ((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations;
        var t = (ProceduralTypeSyntax)declarations[0].Type;
        var u = (ProceduralTypeSyntax)declarations[1].Type;

        Assert.Equal(SyntaxKind.ReferenceKeyword, t.ReferenceKeyword!.Value.ContextualKind);
        Assert.Equal(SyntaxKind.OutKeyword, t.ParameterList!.Parameters[0].Modifier!.Value.ContextualKind);
        Assert.Equal(SyntaxKind.StdCallKeyword, u.CallingConventions[0].Keyword.ContextualKind);
    }

    [Fact]
    public void Types_ProceduralTypeEndsBeforeTheNextDeclaration()
    {
        Assert.Equal(
            "(TypeSection type (TypeDeclaration P = (ProceduralType procedure) ;) (TypeDeclaration Q = (NamedType (IdentifierName Integer)) ;))",
            DumpDeclarations("type P = procedure; Q = Integer;"));
    }

    [Fact]
    public void Types_InVarAndTypedConstDeclarations()
    {
        Assert.Equal(
            "(VarSection var (VarDeclaration (IdentifierName P) : (PointerType ^ (NamedType (IdentifierName Integer))) ;) "
            + "(VarDeclaration (IdentifierName A) : (ArrayType array [ (SubrangeType (LiteralExpression 0) .. (LiteralExpression 1)) ] of "
            + "(EnumType ( (EnumMember x) , (EnumMember y) ))) ;)) "
            + "(ConstSection const (ConstDeclaration S : (SetType set of (NamedType (IdentifierName Byte))) = (IdentifierName E) ;) "
            + "(ConstDeclaration R : (SubrangeType (LiteralExpression 1) .. (LiteralExpression 3)) = (LiteralExpression 2) ;))",
            DumpDeclarations("var P: ^Integer; A: array[0..1] of (x, y); const S: set of Byte = E; R: 1..3 = 2;"));
    }

    [Theory]
    [InlineData("type T = packed Integer; begin end.", "E1106(1,17): Type expected but 'Integer' found")]
    [InlineData("type T = array[1..2] Integer; begin end.", "E1101(1,22): 'of' expected but 'Integer' found")]
    [InlineData("type T = (a, ); begin end.", "E1102(1,14): Identifier expected but ')' found")]
    [InlineData("type T = 1 + 2; begin end.", "E1101(1,15): '..' expected but ';' found")]
    [InlineData("type T = set Byte; begin end.", "E1101(1,14): 'of' expected but 'Byte' found")]
    [InlineData("type T = record X: Integer; case of 0: (); end; begin end.", "E1106(1,34): Type expected but 'of' found")]
    public void Types_Errors(string text, string expected)
    {
        Assert.Equal([expected], Diagnostics(text));
    }
}
