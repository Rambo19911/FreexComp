using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// grammar.md §4 and the record members of §3: class, interface, helper and record-with-members types, visibility
/// sections, methods, method resolution clauses, properties, class var sections (C-018; LANG-155, 156).
/// </summary>
public sealed partial class ParserTests
{
    private const string IntType = "(NamedType (IdentifierName Integer))";

    private static string Method(string heading, string directives = "") =>
        $"(MethodDeclaration (RoutineHeading {heading}) ;{directives})";

    // ------------------------------------------------------------------ class headings (LANG-155)

    [Theory]
    [Trait("Req", "LANG-155")]
    [InlineData("class end", "(ClassType class end)")]
    [InlineData("class", "(ClassType class)")]
    [InlineData("class(Exception)", "(ClassType class (Heritage ( (NamedType (IdentifierName Exception)) )))")]
    [InlineData("class(TBase, IFoo) end",
        "(ClassType class (Heritage ( (NamedType (IdentifierName TBase)) , (NamedType (IdentifierName IFoo)) )) end)")]
    [InlineData("class abstract(Sys.TBase) end",
        "(ClassType class abstract (Heritage ( (NamedType (QualifiedName (IdentifierName Sys) . (IdentifierName TBase))) )) end)")]
    [InlineData("class sealed end", "(ClassType class sealed end)")]
    public void Classes_Headings(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Fact]
    public void Classes_HeadingTokens()
    {
        var tree = Parse("type A = class; B = class sealed(A) end; begin end.");
        Assert.Empty(tree.Diagnostics);
        var declarations = ((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations;

        var forward = Assert.IsType<ClassTypeSyntax>(declarations[0].Type);
        Assert.Null(forward.Modifier);
        Assert.Null(forward.Heritage);
        Assert.Null(forward.EndKeyword);
        Assert.Empty(forward.Members);

        var sealedClass = Assert.IsType<ClassTypeSyntax>(declarations[1].Type);
        Assert.Equal(SyntaxKind.SealedKeyword, sealedClass.Modifier!.Value.ContextualKind);
        Assert.Equal("A", Assert.Single(sealedClass.Heritage!.Types).ToString());
        Assert.Equal(SyntaxKind.EndKeyword, sealedClass.EndKeyword!.Value.Kind);
    }

    [Fact]
    public void Classes_ModifierWordsFollowedByColonAreFieldNames()
    {
        Assert.Equal(
            $"(ClassType class (FieldDeclaration (IdentifierName Abstract) : {IntType} ;) end)",
            DumpType("class Abstract: Integer; end"));
    }

    // ------------------------------------------------------------------ members and visibility sections

    [Fact]
    [Trait("Req", "LANG-156")]
    public void Classes_MembersAndVisibilitySections()
    {
        const string type = """
            class(TObject)
              FA: Integer;
            private
              FName: string;
            strict protected
              procedure DoIt; virtual; abstract;
            public
              constructor Create(const AName: string);
              destructor Destroy; override;
              class function Make: TFoo; static;
              function Area: Double; virtual; abstract;
              class var Count, Total: Integer;
              var Late: Byte;
              const Max = 10;
              type TInner = Integer;
            published
              FPublished: TObject;
            end
            """;

        Assert.Equal(
            "(ClassType class (Heritage ( (NamedType (IdentifierName TObject)) )) "
            + $"(FieldDeclaration (IdentifierName FA) : {IntType} ;) "
            + "(VisibilitySection private (FieldDeclaration (IdentifierName FName) : (StringType string) ;)) "
            + $"(VisibilitySection strict protected {Method("procedure (IdentifierName DoIt)", " (Directive virtual ;) (Directive abstract ;)")}) "
            + "(VisibilitySection public "
            + Method("constructor (IdentifierName Create) (ParameterList ( (Parameter const (IdentifierName AName) : (StringType string)) ))") + " "
            + Method("destructor (IdentifierName Destroy)", " (Directive override ;)") + " "
            + Method("class function (IdentifierName Make) : (NamedType (IdentifierName TFoo))", " (Directive static ;)") + " "
            + Method("function (IdentifierName Area) : (NamedType (IdentifierName Double))", " (Directive virtual ;) (Directive abstract ;)") + " "
            + $"(ClassVarSection class var (FieldDeclaration (IdentifierName Count) , (IdentifierName Total) : {IntType} ;)) "
            + "(ClassVarSection var (FieldDeclaration (IdentifierName Late) : (NamedType (IdentifierName Byte)) ;)) "
            + "(ConstSection const (ConstDeclaration Max = (LiteralExpression 10) ;)) "
            + $"(TypeSection type (TypeDeclaration TInner = {IntType} ;))) "
            + "(VisibilitySection published (FieldDeclaration (IdentifierName FPublished) : (NamedType (IdentifierName TObject)) ;)) end)",
            DumpType(type));
    }

    [Fact]
    public void Classes_VisibilityKeywordsAreContextual()
    {
        var tree = Parse("type T = class private A: Byte; strict private B: Byte; protected public published end; begin end.");
        Assert.Empty(tree.Diagnostics);
        var cls = (ClassTypeSyntax)((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations[0].Type;
        var sections = cls.Members.Cast<VisibilitySectionSyntax>().ToArray();

        Assert.Equal(
            [SyntaxKind.PrivateKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword, SyntaxKind.PublicKeyword, SyntaxKind.PublishedKeyword],
            sections.Select(s => s.VisibilityKeyword.ContextualKind));
        Assert.All(sections, s => Assert.Equal(SyntaxKind.IdentifierToken, s.VisibilityKeyword.Kind));
        Assert.Equal(SyntaxKind.StrictKeyword, sections[1].StrictKeyword!.Value.ContextualKind);
        Assert.Null(sections[0].StrictKeyword);
        Assert.Empty(sections[2].Members);
    }

    [Fact]
    [Trait("Req", "LANG-156")]
    public void Classes_FieldsMayFollowMethods()
    {
        Assert.Equal(
            $"(ClassType class {Method("procedure (IdentifierName P)")} (FieldDeclaration (IdentifierName X) : {IntType}) end)",
            DumpType("class procedure P; X: Integer end"));
    }

    [Fact]
    public void Classes_PublicFollowedByNameIsAVisibilitySectionInAClass()
    {
        Assert.Equal(
            $"(ClassType class {Method("procedure (IdentifierName P)")} "
            + "(VisibilitySection public (FieldDeclaration (IdentifierName Name) : (StringType string) ;)) end)",
            DumpType("class procedure P; public Name: string; end"));
    }

    [Fact]
    public void Classes_MethodDirectives_AreTheRoutineDirectiveSet()
    {
        Assert.Equal(
            "(ClassType class "
            + Method("procedure (IdentifierName WMPaint) (ParameterList ( (Parameter var (IdentifierName Msg) : (NamedType (IdentifierName TMessage))) ))",
                " (Directive message (IdentifierName WM_PAINT) ;)") + " "
            + Method("procedure (IdentifierName A)", " (Directive overload ;) (Directive dynamic ;) (Directive final ;) (Directive reintroduce ;) (Directive inline ;)") + " "
            + "(MethodDeclaration (RoutineHeading function (IdentifierName B) : (NamedType (IdentifierName Integer))) (Directive stdcall ;) (Directive deprecated (LiteralExpression 'x') ;)) "
            + "end)",
            DumpType("class procedure WMPaint(var Msg: TMessage); message WM_PAINT; procedure A; overload; dynamic; final; reintroduce; inline; "
                     + "function B: Integer stdcall; deprecated 'x'; end"));

        var tree = Parse("type T = class procedure A; override; end; begin end.");
        var method = (MethodDeclarationSyntax)((ClassTypeSyntax)((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations[0].Type).Members[0];
        Assert.Equal(SyntaxKind.OverrideKeyword, Assert.Single(method.Directives).Keyword.ContextualKind);
        Assert.Equal(SyntaxKind.SemicolonToken, method.SemicolonToken!.Value.Kind);
    }

    /// <summary>LANG-788 (C-374): the implementation headings repeat <c>class constructor</c> / <c>class destructor</c> with the type path.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-788")]
    public void Classes_ClassConstructorImplementationHeadings()
    {
        const string Body = "; (Block (CompoundStatement begin end)) ;)";
        Assert.Equal(
            "(RoutineDeclaration (RoutineHeading class constructor (QualifiedName (IdentifierName TFoo) . (IdentifierName Create))) " + Body + " "
            + "(RoutineDeclaration (RoutineHeading class constructor (QualifiedName (GenericName TBox (TypeArgumentList < (NamedType (IdentifierName T)) >)) . "
            + "(IdentifierName Create))) " + Body + " "
            + "(RoutineDeclaration (RoutineHeading class destructor (QualifiedName (QualifiedName (IdentifierName TOuter) . (IdentifierName TInner)) . "
            + "(IdentifierName Destroy))) " + Body + " "
            + "(RoutineDeclaration (RoutineHeading class destructor (QualifiedName (IdentifierName TRec) . (IdentifierName Done))) " + Body,
            DumpDeclarations("class constructor TFoo.Create; begin end; class constructor TBox<T>.Create; begin end; "
                             + "class destructor TOuter.TInner.Destroy; begin end; class destructor TRec.Done; begin end;"));

        var program = (ProgramSyntax)Parse("type TRec = record class constructor Create; class destructor Done; end; class constructor TRec.Create; begin end; "
            + "class destructor TRec.Done; begin end; begin end.").Root;
        var implementation = Assert.IsType<RoutineDeclarationSyntax>(program.Block.Declarations[1]);
        Assert.Equal(SyntaxKind.ClassKeyword, implementation.Heading.ClassKeyword!.Value.Kind);
        Assert.Equal(SyntaxKind.ConstructorKeyword, implementation.Heading.Keyword.Kind);
    }

    [Fact]
    public void Classes_ClassConstructorsOperatorsAndProperties()
    {
        Assert.Equal(
            "(ClassType class "
            + Method("class constructor (IdentifierName Create)") + " "
            + Method("class destructor (IdentifierName Destroy)") + " "
            + $"(PropertyDeclaration class property Count : {IntType} (PropertySpecifier read (IdentifierName FCount)) ;) end)",
            DumpType("class class constructor Create; class destructor Destroy; class property Count: Integer read FCount; end"));
    }

    [Fact]
    public void Classes_MethodResolutionClauses()
    {
        Assert.Equal(
            "(ClassType class (Heritage ( (NamedType (IdentifierName TInterfacedObject)) , (NamedType (IdentifierName IFoo)) )) "
            + "(MethodResolution procedure (QualifiedName (IdentifierName IFoo) . (IdentifierName Bar)) = (IdentifierName MyBar) ;) "
            + "(MethodResolution function (QualifiedName (IdentifierName IFoo) . (IdentifierName Baz)) = (IdentifierName MyBaz) ;) "
            + Method("procedure (IdentifierName MyBar)") + " end)",
            DumpType("class(TInterfacedObject, IFoo) procedure IFoo.Bar = MyBar; function IFoo.Baz = MyBaz; procedure MyBar; end"));
    }

    [Fact]
    public void Classes_NestedClassTypes()
    {
        Assert.Equal(
            "(ClassType class (TypeSection type (TypeDeclaration TInner = (ClassType class "
            + $"(FieldDeclaration (IdentifierName X) : {IntType} ;) end) ;)) "
            + "(VisibilitySection public (FieldDeclaration (IdentifierName I) : (NamedType (IdentifierName TInner)) ;)) end)",
            DumpType("class type TInner = class X: Integer; end; public I: TInner; end"));
    }

    // ------------------------------------------------------------------ properties

    [Theory]
    [InlineData("property Name: string read FName write SetName;",
        "(PropertyDeclaration property Name : (StringType string) (PropertySpecifier read (IdentifierName FName)) "
        + "(PropertySpecifier write (IdentifierName SetName)) ;)")]
    [InlineData("property Left: Integer read FRect.Left;",
        "(PropertyDeclaration property Left : (NamedType (IdentifierName Integer)) "
        + "(PropertySpecifier read (QualifiedName (IdentifierName FRect) . (IdentifierName Left))) ;)")]
    [InlineData("property P: Integer index 3 read GetP write SetP stored False default 0;",
        "(PropertyDeclaration property P : (NamedType (IdentifierName Integer)) (PropertySpecifier index (LiteralExpression 3)) "
        + "(PropertySpecifier read (IdentifierName GetP)) (PropertySpecifier write (IdentifierName SetP)) "
        + "(PropertySpecifier stored (IdentifierName False)) (PropertySpecifier default (LiteralExpression 0)) ;)")]
    [InlineData("property Q: Byte read F stored IsStored nodefault;",
        "(PropertyDeclaration property Q : (NamedType (IdentifierName Byte)) (PropertySpecifier read (IdentifierName F)) "
        + "(PropertySpecifier stored (IdentifierName IsStored)) (PropertySpecifier nodefault) ;)")]
    [InlineData("property Items[I: Integer; const S: string]: TObject read GetItem write SetItem; default;",
        "(PropertyDeclaration property Items [ (Parameter (IdentifierName I) : (NamedType (IdentifierName Integer))) ; "
        + "(Parameter const (IdentifierName S) : (StringType string)) ] : (NamedType (IdentifierName TObject)) "
        + "(PropertySpecifier read (IdentifierName GetItem)) (PropertySpecifier write (IdentifierName SetItem)) ; default ;)")]
    [InlineData("property Caption;", "(PropertyDeclaration property Caption ;)")]
    [InlineData("property OnClick default nil;", "(PropertyDeclaration property OnClick (PropertySpecifier default (LiteralExpression nil)) ;)")]
    [InlineData("property Impl: TImpl read FImpl implements IFoo, Sys.IBar;",
        "(PropertyDeclaration property Impl : (NamedType (IdentifierName TImpl)) (PropertySpecifier read (IdentifierName FImpl)) "
        + "(PropertySpecifier implements (IdentifierName IFoo) , (QualifiedName (IdentifierName Sys) . (IdentifierName IBar))) ;)")]
    public void Classes_Properties(string property, string expected)
    {
        Assert.Equal($"(ClassType class {expected} end)", DumpType($"class {property} end"));
    }

    [Fact]
    public void Classes_PropertySpecifierKeywordsAreContextual()
    {
        var tree = Parse("type T = class property P: Integer index 1 read F write F stored True default 2; property D[I: Byte]: Byte read G; default; end; begin end.");
        Assert.Empty(tree.Diagnostics);
        var members = ((ClassTypeSyntax)((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations[0].Type).Members;
        var p = (PropertyDeclarationSyntax)members[0];
        var d = (PropertyDeclarationSyntax)members[1];

        Assert.Equal(
            [SyntaxKind.IndexKeyword, SyntaxKind.ReadKeyword, SyntaxKind.WriteKeyword, SyntaxKind.StoredKeyword, SyntaxKind.DefaultKeyword],
            p.Specifiers.Select(s => s.Keyword.ContextualKind));
        Assert.Equal("1", p.Specifiers[0].Value!.ToString());
        Assert.Null(p.DefaultKeyword);
        Assert.Equal(SyntaxKind.DefaultKeyword, d.DefaultKeyword!.Value.ContextualKind);
        Assert.Single(d.Parameters);
        Assert.Equal("P", p.Identifier.Text);
    }

    [Fact]
    public void Classes_PropertyWithUnknownSpecifier_IsE1118()
    {
        Assert.Equal(
            ["E1118(1,36): Property specifier expected but 'reed' found"],
            Diagnostics("type T = class property X: Integer reed F; end; begin end."));
        Assert.Equal(
            "(ClassType class (PropertyDeclaration property X : (NamedType (IdentifierName Integer)) ;) end)",
            SyntaxDump.Of(((TypeSectionSyntax)((ProgramSyntax)Parse("type T = class property X: Integer reed F; end; begin end.").Root)
                .Block.Declarations[0]).Declarations[0].Type));
    }

    // ------------------------------------------------------------------ interfaces

    [Theory]
    [InlineData("interface", "(InterfaceType interface)")]
    [InlineData("interface end", "(InterfaceType interface end)")]
    [InlineData("interface(IInterface) ['{00000000-0000-0000-C000-000000000046}'] function GetX: Integer; property X: Integer read GetX; end",
        "(InterfaceType interface (Heritage ( (NamedType (IdentifierName IInterface)) )) "
        + "(Guid [ (LiteralExpression '{00000000-0000-0000-C000-000000000046}') ]) "
        + "(MethodDeclaration (RoutineHeading function (IdentifierName GetX) : (NamedType (IdentifierName Integer))) ;) "
        + "(PropertyDeclaration property X : (NamedType (IdentifierName Integer)) (PropertySpecifier read (IdentifierName GetX)) ;) end)")]
    [InlineData("interface [IID_Foo] procedure P; stdcall; end",
        "(InterfaceType interface (Guid [ (IdentifierName IID_Foo) ]) "
        + "(MethodDeclaration (RoutineHeading procedure (IdentifierName P)) ; (Directive stdcall ;)) end)")]
    public void Classes_Interfaces(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Fact]
    public void Classes_InterfaceTypeInAUnitInterfaceSection()
    {
        var text = "unit U; interface type IFoo = interface; IFoo = interface procedure P; end; implementation end.";

        var unit = (UnitSyntax)Parse(text).Root;

        Assert.Equal(text, unit.ToFullString());
        Assert.Empty(Parse(text).Diagnostics);
        var types = ((TypeSectionSyntax)unit.InterfaceSection.Declarations[0]).Declarations;
        Assert.Null(((InterfaceTypeSyntax)types[0].Type).EndKeyword);
        Assert.Single(((InterfaceTypeSyntax)types[1].Type).Members);
    }

    // ------------------------------------------------------------------ helpers

    [Theory]
    [InlineData("class helper for TFoo procedure Hello; end",
        "(HelperType class helper for (NamedType (IdentifierName TFoo)) "
        + "(MethodDeclaration (RoutineHeading procedure (IdentifierName Hello)) ;) end)")]
    [InlineData("record helper(TBaseHelper) for Integer function IsEven: Boolean; public const Zero = 0; end",
        "(HelperType record helper (Heritage ( (NamedType (IdentifierName TBaseHelper)) )) for (NamedType (IdentifierName Integer)) "
        + "(MethodDeclaration (RoutineHeading function (IdentifierName IsEven) : (NamedType (IdentifierName Boolean))) ;) "
        + "(VisibilitySection public (ConstSection const (ConstDeclaration Zero = (LiteralExpression 0) ;))) end)")]
    public void Classes_Helpers(string type, string expected)
    {
        Assert.Equal(expected, DumpType(type));
    }

    [Fact]
    public void Classes_HelperKeywordIsContextual()
    {
        var tree = Parse("type H = class helper for TObject end; R = record helper: Integer; end; begin end.");
        Assert.Empty(tree.Diagnostics);
        var declarations = ((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations;

        var helper = Assert.IsType<HelperTypeSyntax>(declarations[0].Type);
        Assert.Equal(SyntaxKind.ClassKeyword, helper.Keyword.Kind);
        Assert.Equal(SyntaxKind.HelperKeyword, helper.HelperKeyword.ContextualKind);
        Assert.Equal("TObject", helper.ExtendedType.ToString());
        Assert.IsType<RecordTypeSyntax>(declarations[1].Type);   // a field named 'helper'
    }

    // ------------------------------------------------------------------ records with members (LANG-730)

    [Fact]
    [Trait("Req", "LANG-730")]
    public void Classes_RecordWithMembers()
    {
        Assert.Equal(
            "(RecordType record (VisibilitySection private (FieldDeclaration (IdentifierName FX) : (NamedType (IdentifierName Single)) ;)) "
            + "(VisibilitySection public "
            + Method("constructor (IdentifierName Create) (ParameterList ( (Parameter (IdentifierName X) : (NamedType (IdentifierName Single))) ))") + " "
            + Method("class operator (IdentifierName Add) (ParameterList ( (Parameter const (IdentifierName A) , (IdentifierName B) : (NamedType (IdentifierName TVec))) )) "
                     + ": (NamedType (IdentifierName TVec))") + " "
            + "(PropertyDeclaration property X : (NamedType (IdentifierName Single)) (PropertySpecifier read (IdentifierName FX)) ;) "
            + $"(ClassVarSection class var (FieldDeclaration (IdentifierName Count) : {IntType} ;)) "
            + "(ConstSection const (ConstDeclaration Origin = (LiteralExpression 0) ;))) end)",
            DumpType("record private FX: Single; public constructor Create(X: Single); class operator Add(const A, B: TVec): TVec; "
                     + "property X: Single read FX; class var Count: Integer; const Origin = 0; end"));
    }

    [Fact]
    public void Classes_RecordVisibilityWordsAreNoLongerFieldNames()
    {
        var tree = Parse("type R = record public A: Byte; strict private B: Byte; end; begin end.");
        Assert.Empty(tree.Diagnostics);
        var record = (RecordTypeSyntax)((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations[0].Type;

        Assert.All(record.Members, m => Assert.IsType<VisibilitySectionSyntax>(m));
        Assert.Equal(2, record.Members.Count);
    }

    [Fact]
    public void Classes_VisibilityWordsFollowedByColonOrCommaAreFieldNames()
    {
        Assert.Equal(
            $"(RecordType record (FieldDeclaration (IdentifierName Private) , (IdentifierName Public) : {IntType} ;) "
            + $"(FieldDeclaration (IdentifierName Published) : {IntType} ;) end)",
            DumpType("record Private, Public: Integer; Published: Integer; end"));
    }

    [Fact]
    public void Classes_RecordVariantPartAfterVisibilitySections()
    {
        Assert.Equal(
            "(RecordType record (VisibilitySection public (FieldDeclaration (IdentifierName A) : (NamedType (IdentifierName Byte)) ;)) "
            + "(VariantPart case (NamedType (IdentifierName Byte)) of (Variant (LiteralExpression 0) : ( "
            + "(FieldDeclaration (IdentifierName B) : (NamedType (IdentifierName Byte))) ))) end)",
            DumpType("record public A: Byte; case Byte of 0: (B: Byte) end"));
    }

    // ------------------------------------------------------------------ the spec example (classes-interfaces.md §11)

    [Fact]
    public void Classes_SpecExampleParsesWithoutDiagnostics()
    {
        const string text = """
            program Shapes;
            type
              TShape = class
              private
                FName: string;
              public
                constructor Create(const AName: string);
                function Area: Double; virtual; abstract;
                procedure Describe;
                property Name: string read FName;
              end;
              TRect = class(TShape)
              private
                FW, FH: Double;
              public
                constructor Create(W, H: Double);
                function Area: Double; override;
              end;
              TCircle = class(TShape)
                FR: Double;
                constructor Create(R: Double);
                function Area: Double; override;
                destructor Destroy; override;
              end;

            constructor TShape.Create(const AName: string);
            begin inherited Create; FName := AName end;
            procedure TShape.Describe;
            begin Writeln(Name, ' ', Area:0:2) end;
            destructor TCircle.Destroy;
            begin Writeln('circle gone'); inherited end;

            var s: TShape;
            begin
              s := TRect.Create(2, 3); s.Describe;
              s.Free;
            end.
            """;

        var tree = Parse(text);

        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);
        var types = ((TypeSectionSyntax)((ProgramSyntax)tree.Root).Block.Declarations[0]).Declarations;
        Assert.Equal(3, types.Count);
        Assert.All(types, t => Assert.IsType<ClassTypeSyntax>(t.Type));
    }

    // ------------------------------------------------------------------ errors and recovery

    [Fact]
    public void Classes_UnterminatedClass_InsertsEndAtTheNextBlock()
    {
        var text = "type T = class procedure X; begin end.";

        Assert.Equal(["E1101(1,29): 'end' expected but 'begin' found"], Diagnostics(text));
        Assert.Equal(
            $"(TypeSection type (TypeDeclaration T = (ClassType class {Method("procedure (IdentifierName X)")} <missing EndKeyword>) <missing SemicolonToken>))",
            SyntaxDump.Of(((ProgramSyntax)Parse(text).Root).Block.Declarations[0]));
    }

    [Fact]
    public void Classes_UnterminatedClassInAUnit_StopsAtImplementation()
    {
        var text = "unit U; interface type T = class X: Integer; implementation end.";

        Assert.Equal(["E1101(1,46): 'end' expected but 'implementation' found"], Diagnostics(text));
    }

    [Fact]
    public void Classes_GarbageInAClassBody_IsSkippedWithOneE1100()
    {
        var text = "type T = class 1 2 X: Integer; end; begin end.";

        Assert.Equal(["E1100(1,16): Syntax error: unexpected '1'"], Diagnostics(text));
        Assert.Equal(
            $"(ClassType class (FieldDeclaration (IdentifierName X) : {IntType} ;) end)",
            SyntaxDump.Of(((TypeSectionSyntax)((ProgramSyntax)Parse(text).Root).Block.Declarations[0]).Declarations[0].Type));
    }
}
