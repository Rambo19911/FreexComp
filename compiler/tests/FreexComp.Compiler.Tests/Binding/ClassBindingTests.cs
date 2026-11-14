using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-080 / C-081: class declarations (LANG-600…604, 615), methods and their implementations (LANG-620, 621).</summary>
[Trait("Phase", "2")]
public sealed class ClassBindingTests
{
    private static T Lookup<T>(BindingResult result, string name) => Assert.IsType<T>(result.Declarations.Scope.Lookup(name));

    [Fact]
    [Trait("Req", "LANG-601")]
    public void Classes_ForwardDeclarationIsCompletedBySameSymbol()
    {
        var (result, _) = Bind("""
            type
              TB = class;
              TA = class F: TB; end;
              TB = class G: TA; end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var a = Lookup<ClassTypeSymbol>(result, "TA");
        var b = Lookup<ClassTypeSymbol>(result, "TB");
        Assert.Same(b, a.Fields[0].Type);
        Assert.Same(a, b.Fields[0].Type);
        Assert.False(b.IsForward);
        Assert.Equal(2, b.Locations.Length);
    }

    /// <summary>
    /// C-212 (LANG-622 clarified): a second <c>message</c> handler for the same id in one class is E2004 at its id, with the first
    /// handler as related location; a descendant may handle the same id.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-622")]
    public void DuplicateMessageId()
    {
        var (result, _) = Bind("""
            type
              TMsg = record Id: Cardinal; end;
              TA = class
                procedure One(var M: TMsg); message 7;
                procedure Two(var M: TMsg); message 3 + 4;
              end;
              TB = class(TA)
                procedure Mine(var M: TMsg); message 7;
              end;
            procedure TA.One(var M: TMsg); begin end;
            procedure TA.Two(var M: TMsg); begin end;
            procedure TB.Mine(var M: TMsg); begin end;
            begin end.
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("E2004(5,41): Identifier redeclared: '7'", Format([diagnostic])[0]);
        Assert.Equal((3, 14), (diagnostic.Related[0].Location.LineSpan.Start.Line, diagnostic.Related[0].Location.LineSpan.Start.Column));
    }

    [Fact]
    [Trait("Req", "LANG-601")]
    public void Classes_UncompletedForwardDeclaration_IsE2015()
    {
        Assert.Equal(["E2015(1,6): Unsatisfied forward declaration of type 'TB'"], BindDiagnostics("type TB = class; begin end."));
    }

    /// <summary>
    /// C-210 (LANG-441): inside a method, a bare call of an overloaded name binds like <c>Self.Name</c> — the ancestor method an
    /// <c>override</c> replaces is not a second candidate (no E2122), whether the name is called with or without parentheses.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-441")]
    public void ImplicitCall_OverriddenOverload_IsNotAmbiguous()
    {
        var (result, _) = Bind("""
            type
              TA = class function ToString: string; virtual; end;
              TB = class(TA)
                function ToString: string; overload; override;
                function ToString(N: Integer): string; reintroduce; overload;
                function Twice: string;
              end;
            function TA.ToString: string; begin Result := 'a' end;
            function TB.ToString: string; begin Result := 'b' end;
            function TB.ToString(N: Integer): string; begin Result := 'n' end;
            function TB.Twice: string; begin Result := ToString + ToString() + ToString(1) + Self.ToString end;
            begin end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        var twice = BoundDump.Of(result.Routines.Single(r => r.Routine.Name == "Twice").Body);
        Assert.DoesNotContain("TA.", twice);
    }

    [Fact]
    [Trait("Req", "LANG-601")]
    public void Classes_ForwardCompletedInAnotherSection_IsE2015AndE2004()
    {
        Assert.Equal(["E2015(1,6): Unsatisfied forward declaration of type 'TB'", "E2004(2,6): Identifier redeclared: 'TB'"],
            BindDiagnostics("type TB = class;\ntype TB = class end; begin end."));
    }

    [Fact]
    [Trait("Req", "LANG-600")]
    public void Classes_ParentAndMembers()
    {
        var (result, _) = Bind("""
            type
              TA = class X: Integer; procedure P; end;
              TB = class(TA) Y: Integer; end;
            procedure TA.P; begin end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var b = Lookup<ClassTypeSymbol>(result, "TB");
        Assert.Same(Lookup<ClassTypeSymbol>(result, "TA"), b.Parent);
        Assert.Equal(["Y", "X", "P"], b.GetMembers("Y").Concat(b.GetMembers("X")).Concat(b.GetMembers("P")).Select(m => m.Name));
        Assert.True(b.InheritsFrom(b.Parent!));
    }

    [Theory]
    [Trait("Req", "LANG-600")]
    [Trait("Req", "LANG-603")]
    [InlineData("type TA = class; TB = class(TA) end; TA = class end; begin end.", "E2007(1,29): Type 'TA' is not yet completely defined")]
    [InlineData("type TA = class(Integer) end; begin end.", "E2603(1,17): Class type required")]
    [InlineData("type TA = class sealed end; TB = class(TA) end; begin end.", "E2601(1,40): Cannot derive from sealed class 'TA'")]
    [InlineData("procedure P; type TA = class end; begin end; begin end.", "E2602(1,24): Class and interface types must be declared at unit level or nested in a type")]
    [InlineData("type TA = class X: Integer; X: Byte end; begin end.", "E2004(1,29): Identifier redeclared: 'X'")]
    [InlineData("type TA = class X: Integer; procedure X; end; begin end.", "E2004(1,39): Identifier redeclared: 'X'")]
    public void Classes_DeclarationErrors(string source, string expected)
    {
        Assert.Equal([expected], BindDiagnostics(source));
    }

    [Fact]
    [Trait("Req", "LANG-620")]
    public void Methods_KindsSelfAndFlags()
    {
        var (result, _) = Bind("""
            type
              TA = class
                constructor Create(N: Integer);
                destructor Done;
                function F: Integer;
                procedure P; virtual;
              end;
            constructor TA.Create(N: Integer); begin end;
            destructor TA.Done; begin end;
            function TA.F: Integer; begin Result := 1; end;
            procedure TA.P; begin end;
            begin end.
            """);

        Assert.Empty(Format(result.Diagnostics));
        var a = Lookup<ClassTypeSymbol>(result, "TA");
        var create = a.Methods[0];
        Assert.Equal(MethodKind.Constructor, create.MethodKind);
        Assert.Same(a, create.ReturnType);
        Assert.Equal(["Self", "Alloc"], create.ImplicitParameters.Select(p => p.Name));
        Assert.Equal(["N"], create.Parameters.Select(p => p.Name));
        Assert.Equal(["Self", "Dealloc"], a.Methods[1].ImplicitParameters.Select(p => p.Name));
        Assert.Equal(["Self"], a.Methods[2].ImplicitParameters.Select(p => p.Name));
        Assert.Equal((-1, 0), (a.Methods[2].VmtSlot, a.Methods[3].VmtSlot));
        Assert.Equal("test.TA.Create", create.LinkName);
        Assert.All(a.Methods, m => Assert.NotNull(m.Definition));
    }

    [Theory]
    [Trait("Req", "LANG-620")]
    [InlineData("type TA = class procedure P; end; begin end.", "E2605(1,27): Method 'TA.P' is not implemented")]
    [InlineData("type TA = class end; procedure TA.P; begin end; begin end.", "E2606(1,35): 'P' is not a member of class 'TA'")]
    [InlineData("type TA = class procedure P; end; procedure TA.P; begin end; procedure TA.P; begin end; begin end.", "E2004(1,75): Identifier redeclared: 'P'")]
    [InlineData("procedure TB.P; begin end; begin end.", "E2003(1,11): Undeclared identifier 'TB'")]
    [InlineData("type TB = Integer; procedure TB.P; begin end; begin end.", "E2603(1,30): Class type required")]
    public void Methods_ImplementationErrors(string source, string expected)
    {
        Assert.Equal([expected], BindDiagnostics(source));
    }

    private const string VisibilityClasses =
        "type TA = class strict private F: Integer; procedure SP; strict protected G: Integer; private H: Integer; " +
        "protected K: Integer; public procedure Run; end; TB = class(TA) procedure Run2(Other: TA); end; " +
        "procedure TA.SP; begin end; procedure TA.Run; begin F := 1; SP; G := 2; Self.F := 3 end; ";

    /// <summary>
    /// C-084 / LANG-610, 612 within one unit: <c>private</c>/<c>protected</c> are unit-wide; <c>strict private</c> only in the
    /// class's own methods; <c>strict protected</c> in the class and in descendants through a reference of the descendant
    /// type (implicit <c>Self</c> included), not through an ancestor-typed reference.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-610")]
    [Trait("Req", "LANG-612")]
    [InlineData("", "A.F := 1;", "E2610: Cannot access strict private member 'F' of 'TA'")]
    [InlineData("", "A.SP;", "E2610: Cannot access strict private member 'SP' of 'TA'")]
    [InlineData("", "A.G := 1;", "E2610: Cannot access strict protected member 'G' of 'TA'")]
    [InlineData("", "A.H := 1; A.K := 1; A.Run;", null)]
    [InlineData("F := 1;", "", "E2610: Cannot access strict private member 'F' of 'TA'")]
    [InlineData("SP;", "", "E2610: Cannot access strict private member 'SP' of 'TA'")]
    [InlineData("G := 1; Self.G := 2; Other.H := 3; Other.K := 4; H := 5; K := 6;", "", null)]
    [InlineData("Other.G := 1;", "", "E2610: Cannot access strict protected member 'G' of 'TA'")]
    public void Visibility_SameUnit(string inDescendant, string inMain, string? expected)
    {
        var diagnostics = BindDiagnostics(VisibilityClasses + $"procedure TB.Run2(Other: TA); begin {inDescendant} end; var A: TA; begin {inMain} end.")
            .Select(d => System.Text.RegularExpressions.Regex.Replace(d, @"\(\d+,\d+\)", ""));
        Assert.Equal(expected is null ? [] : [expected], diagnostics);
    }

    private const string AbstractClasses =
        "type TA = class constructor Create; procedure P; virtual; abstract; end; TB = class(TA) end; " +
        "TC = class(TB) procedure P; override; end; TD = class abstract(TC) end; " +
        "constructor TA.Create; begin end; procedure TC.P; begin end; var A: TA; ";

    /// <summary>
    /// C-083 / LANG-602, 622: a constructor called on the name of a <c>class abstract</c> is E2600; on a class with an abstract
    /// slot (own or inherited, not overridden) W1061 naming the first such slot; on an instance, via <c>inherited</c> or for a
    /// concrete class nothing.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-602")]
    [Trait("Req", "LANG-622")]
    [InlineData("begin A := TA.Create end.", "W1061(1,249): Constructing instance of 'TA' containing abstract method 'TA.P'")]
    [InlineData("begin A := TB.Create end.", "W1061(1,249): Constructing instance of 'TB' containing abstract method 'TA.P'")]
    [InlineData("begin A := TD.Create end.", "E2600(1,249): Cannot create an instance of abstract class 'TD'")]
    [InlineData("begin A := TC.Create; A.Create end.", null)]
    public void AbstractInstantiation(string body, string? expected)
    {
        Assert.Equal(expected is null ? [] : [expected], BindDiagnostics(AbstractClasses + body));
    }

    /// <summary>
    /// C-081 / CMP-367: every method call carries its receiver — a class name gives the VMT and Alloc = 1, an instance the
    /// reference (virtual methods dispatched, a destructor Dealloc = 1), a bare name inside a method Self; bare
    /// <c>inherited</c> without an ancestor method is an empty statement (LANG-624).
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-367")]
    [Trait("Req", "LANG-621")]
    [Trait("Req", "LANG-624")]
    public void Methods_CallsCarryTheirReceiver()
    {
        var (result, _) = Bind("""
            type
              TA = class
                F: Integer;
                constructor Create;
                destructor Done;
                procedure P; virtual;
                procedure Q(F: Integer);
              end;
            constructor TA.Create; begin end;
            destructor TA.Done; begin end;
            procedure TA.P; begin end;
            procedure TA.Q(F: Integer); begin P; Self.F := F; inherited; end;
            var A: TA;
            begin A := TA.Create; A.P; A.Q(1); A.Done end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        var main = result.Bodies[0].Statements;

        var create = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundAssignmentStatement>(main[0]).Value);
        Assert.Equal((false, 1), (create.Receiver!.IsVirtual, create.Receiver.Flag));
        Assert.Same(Lookup<ClassTypeSymbol>(result, "TA"), Assert.IsType<BoundClassReferenceExpression>(create.Receiver.Self).Class);
        Assert.Same(create.Routine.ContainingSymbol, create.Type);
        var receivers = main.Skip(1).Cast<BoundCallStatement>().Select(c => (c.Routine.Name, c.Receiver!.IsVirtual, c.Receiver.Flag, c.Receiver.Self is BoundVariableExpression));
        Assert.Equal([("P", true, null), ("Q", false, null), ("Done", false, 1)], receivers.Select(r => (r.Name, r.IsVirtual, r.Flag)));
        Assert.All(receivers, r => Assert.True(r.Item4));

        var q = result.Routines.Single(r => r.Routine.Name == "Q").Body.Statements;
        var p = Assert.IsType<BoundCallStatement>(q[0]);
        Assert.Equal("Self", Assert.IsType<BoundParameterExpression>(p.Receiver!.Self).Parameter.Name);
        var field = Assert.IsType<BoundFieldAccessExpression>(Assert.IsType<BoundAssignmentStatement>(q[1]).Target);
        Assert.IsType<BoundParameterExpression>(Assert.IsType<BoundAssignmentStatement>(q[1]).Value);     // the parameter hides the field
        Assert.Equal("F", field.Field.Name);
        Assert.Empty(Assert.IsType<BoundBlock>(q[2]).Statements);
    }

    private const string PropertyClasses =
        "type TRec = record X: Integer; end; TA = class F: Integer; B: Byte; R: TRec; function GetR: TRec; function GetI(I: Integer): Integer; " +
        "procedure SetF(V: Integer); {0} end; function TA.GetR: TRec; begin end; function TA.GetI(I: Integer): Integer; begin end; " +
        "procedure TA.SetF(V: Integer); begin end; procedure V(var X: Integer); begin end; var A: TA; I: Integer; begin {1} end.";

    /// <summary>C-085 / LANG-641…646: property declaration and use errors (positions stripped).</summary>
    [Theory]
    [Trait("Req", "LANG-641")]
    [Trait("Req", "LANG-642")]
    [Trait("Req", "LANG-645")]
    [Trait("Req", "LANG-646")]
    [InlineData("property P: Integer;", "", "E2631: Property 'P' needs a read or write specifier")]
    [InlineData("property P: Integer write F;", "I := A.P", "E2632: Cannot read a write-only property")]
    [InlineData("property P: Integer read F;", "A.P := 1", "E2633: Cannot assign to a read-only property")]
    [InlineData("property P[I: Integer]: Integer read F;", "", "E2634: Array property accessors must be methods")]
    [InlineData("property P[I: Integer]: Integer read GetI; default; property Q[I: Integer]: Integer read GetI; default;", "", "E2635: Class 'TA' already has a default array property")]
    [InlineData("property P: Integer read B;", "", "E2636: Property accessor 'B' is not compatible with property type 'Integer'")]
    [InlineData("property P: Integer read GetI;", "", "E2636: Property accessor 'GetI' is not compatible with property type 'Integer'")]
    [InlineData("property P: Integer write GetI;", "", "E2636: Property accessor 'GetI' is not compatible with property type 'Integer'")]
    [InlineData("property P: Integer read Nope;", "", "E2003: Undeclared identifier 'Nope'")]
    [InlineData("property P: Integer read F write F;", "V(A.P)", "E2113: Variable required")]
    [InlineData("property P: Integer read F write F;", "Inc(A.P)", "E2113: Variable required")]
    [InlineData("property P: Integer read F write SetF;", "V(A.P)", "E2113: Variable required")]
    [InlineData("property P: Integer read F write F;", "A.P := @A.P", "E2033: Cannot take the address of this expression")]
    [InlineData("property P: TRec read GetR;", "A.P.X := 1", "E2053: Left side cannot be assigned to")]
    [InlineData("property P[I: Integer]: Integer read GetI;", "I := A.P", "E2110: Not enough actual parameters for 'P'")]
    [InlineData("property P[I: Integer]: Integer read GetI;", "I := A.P[1, 2]", "E2017: Array type required")]
    [InlineData("property P: Integer read R.X write R.X; property Q: TRec read R; property S[I: Integer]: Integer read GetI write SetF;", "A.P := A.Q.X; A.R.X := A.P", "E2636: Property accessor 'SetF' is not compatible with property type 'Integer'")]
    public void Properties_Errors(string member, string body, string expected)
    {
        var diagnostics = BindDiagnostics(string.Format(PropertyClasses, member, body))
            .Select(d => System.Text.RegularExpressions.Regex.Replace(d, @"\(\d+,\d+\)", ""));
        Assert.Equal([expected], diagnostics);
    }

    /// <summary>LANG-646: a redeclaration without type needs an inherited property (E2637); it keeps the type and replaces listed accessors.</summary>
    [Fact]
    [Trait("Req", "LANG-646")]
    public void Properties_Redeclaration()
    {
        Assert.Equal(["E2637(1,86): Property 'Q' does not exist in an ancestor class"],
            BindDiagnostics("type TA = class F: Integer; property P: Integer read F; end; TB = class(TA) property Q; end; begin end."));
        var (result, _) = Bind("type TA = class F, G: Integer; property P: Integer read F write F; end; TB = class(TA) property P read G; end; begin end.");
        Assert.Empty(Format(result.Diagnostics));
        var p = Lookup<ClassTypeSymbol>(result, "TB").Properties.Single();
        Assert.Equal(("Integer", "G", "F"), (p.Type.Name, p.ReadPath.Single().Name, p.WritePath.Single().Name));
    }

    /// <summary>C-085 / CMP-367: reads become field accesses or getter calls (virtual getters dispatch), writes stores or setter calls.</summary>
    [Fact]
    [Trait("Req", "LANG-641")]
    [Trait("Req", "LANG-643")]
    public void Properties_BindToAccessors()
    {
        var (result, _) = Bind("""
            type
              TA = class
                F: Integer;
                function Get(Index: Integer): Integer; virtual;
                procedure SetIt(Index: Integer; V: Integer);
                property P: Integer read F write F;
                property Q: Integer index 7 read Get write SetIt;
              end;
            function TA.Get(Index: Integer): Integer; begin Result := Index end;
            procedure TA.SetIt(Index: Integer; V: Integer); begin end;
            var A: TA; I: Integer;
            begin I := A.P; A.P := 1; I := A.Q; A.Q := 2 end.
            """);
        Assert.Empty(Format(result.Diagnostics));
        var main = result.Bodies[0].Statements;
        var read = Assert.IsType<BoundFieldAccessExpression>(Assert.IsType<BoundAssignmentStatement>(main[0]).Value);
        Assert.Equal("P", read.Property!.Name);
        Assert.Null(Assert.IsType<BoundFieldAccessExpression>(Assert.IsType<BoundAssignmentStatement>(main[1]).Target).Property);
        var get = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundAssignmentStatement>(main[2]).Value);
        Assert.Equal(("Get", true, "7"), (get.Routine.Name, get.Receiver!.IsVirtual, get.Arguments.Single().ConstantValue!.ToDisplayString()));
        var set = Assert.IsType<BoundCallStatement>(main[3]);
        Assert.Equal(("SetIt", 2), (set.Routine.Name, set.Arguments.Length));
    }

    private const string ReferenceClasses =
        "type TA = class F: Integer; class var N: Integer; constructor Create; virtual; procedure P; class procedure CP; virtual; " +
        "class procedure SP; static; end; TB = class(TA) end; TX = class end; TAClass = class of TA; TBClass = class of TB; " +
        "constructor TA.Create; begin end; procedure TA.P; begin end; class procedure TA.CP; begin {0} end; class procedure TA.SP; begin {1} end; " +
        "var A: TA; CA: TAClass; CB: TBClass; begin {2} end.";

    /// <summary>C-086 / LANG-652, 653, 620, 615: class references, class and static methods, class vars (positions stripped).</summary>
    [Theory]
    [Trait("Req", "LANG-652")]
    [Trait("Req", "LANG-653")]
    [Trait("Req", "LANG-620")]
    [InlineData("", "", "CA := TB; CA := CB; CA := nil; CB := TB; A := CA.Create; CA.CP; CA.SP; TA.CP; A.CP; A.SP; TA.N := 1; A.N := CA.N; if CA = CB then; if CA <> TB then;", null)]
    [InlineData("", "", "CB := CA;", "E2010: Incompatible types: 'TAClass' and 'TBClass'")]
    [InlineData("", "", "CA := TX;", "E2010: Incompatible types: 'class of TX' and 'TAClass'")]
    [InlineData("", "", "CA.P;", "E2607: Constructor or class method expected for a call on a class reference")]
    [InlineData("", "", "TA.P;", "E2607: Constructor or class method expected for a call on a class reference")]
    [InlineData("F := 1;", "", "", "E2608: Instance member 'F' is not accessible from a class method")]
    [InlineData("P;", "", "", "E2608: Instance member 'P' is not accessible from a class method")]
    [InlineData("N := 1; SP; CP; Self.CP; if Create = nil then; if Self.Create <> nil then;", "N := 2; SP; CP;", "", null)]
    public void ClassReferences(string inClassMethod, string inStaticMethod, string inMain, string? expected)
    {
        var diagnostics = BindDiagnostics(string.Format(ReferenceClasses, inClassMethod, inStaticMethod, inMain))
            .Select(d => System.Text.RegularExpressions.Regex.Replace(d, @"\(\d+,\d+\)", ""));
        Assert.Equal(expected is null ? [] : [expected], diagnostics);
    }

    /// <summary>
    /// C-086 / CMP-367: on a class name Self is the VMT (static call), on a class-reference value the value itself (virtual
    /// class methods and constructors dispatch; constructors get Alloc = 1), on an instance its VMT; static methods have no Self.
    /// </summary>
    [Fact]
    [Trait("Req", "CMP-367")]
    [Trait("Req", "LANG-633")]
    public void ClassReferences_Receivers()
    {
        var (result, _) = Bind(string.Format(ReferenceClasses, "", "", "A := CA.Create; TA.CP; CA.CP; A.CP; A.SP"));
        Assert.Empty(Format(result.Diagnostics));
        var main = result.Bodies[0].Statements;
        var create = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundAssignmentStatement>(main[0]).Value);
        Assert.Equal((true, 1), (create.Receiver!.IsVirtual, create.Receiver.Flag));
        Assert.IsType<BoundVariableExpression>(create.Receiver.Self);
        var calls = main.Skip(1).Cast<BoundCallStatement>().Select(c => c.Receiver!).ToList();
        Assert.Equal((false, typeof(BoundClassReferenceExpression)), (calls[0].IsVirtual, calls[0].Self!.GetType()));
        Assert.Equal((true, typeof(BoundVariableExpression)), (calls[1].IsVirtual, calls[1].Self!.GetType()));
        Assert.Equal((true, typeof(BoundDereferenceExpression)), (calls[2].IsVirtual, calls[2].Self!.GetType()));
        Assert.Same(BoundReceiver.Static, calls[3]);
        var cp = result.Declarations.Scope.Lookup("TA") as ClassTypeSymbol;
        Assert.IsType<ClassRefTypeSymbol>(cp!.Methods.Single(m => m.Name == "CP").SelfParameter!.Type);
        Assert.Null(cp.Methods.Single(m => m.Name == "SP").SelfParameter);
    }

    /// <summary>C-088 / LANG-624, 621: <c>inherited</c> and <c>Self</c> outside a method; <c>inherited</c> of an abstract method.</summary>
    [Theory]
    [Trait("Req", "LANG-624")]
    [InlineData("procedure P; begin inherited; end; begin end.", "E2133(1,20): 'inherited' is only allowed inside a method")]
    [InlineData("function F: Integer; begin Result := inherited F; end; begin end.", "E2133(1,38): 'inherited' is only allowed inside a method")]
    [InlineData("var X: Pointer; begin X := Self end.", "E2134(1,28): 'Self' is only available inside a method")]
    [InlineData("type TA = class procedure P; virtual; abstract; end; TB = class(TA) procedure P; override; end; procedure TB.P; begin inherited P end; begin end.",
        "E2626(1,119): Cannot call abstract method 'P' with 'inherited'")]
    public void Inherited_Errors(string source, string expected)
    {
        Assert.Equal([expected], BindDiagnostics(source));
    }

    /// <summary>C-087 / LANG-650, 651: <c>is</c>/<c>as</c> operands (positions stripped).</summary>
    [Theory]
    [Trait("Req", "LANG-650")]
    [Trait("Req", "LANG-651")]
    [InlineData("if A is TB then; B := A as TB; A := B as TA; if C is TB then; if B is TA then;", null)]
    [InlineData("if A is TX then;", "E2641: Types 'TA' and 'TX' are unrelated; this test is never true")]
    [InlineData("B := X as TB;", "E2641: Types 'TX' and 'TB' are unrelated; this test is never true")]
    [InlineData("if I is TA then;", "E2603: Class type required")]
    [InlineData("if A is Integer then;", "E2603: Class type required")]
    [InlineData("A := C as TA;", "E2603: Class type required")]
    [InlineData("if A is Nope then;", "E2003: Undeclared identifier 'Nope'")]
    public void IsAs(string body, string? expected)
    {
        var diagnostics = BindDiagnostics("type TA = class end; TB = class(TA) end; TX = class end; TAClass = class of TA; " +
                $"var A: TA; B: TB; X: TX; C: TAClass; I: Integer; begin {body} end.")
            .Select(d => System.Text.RegularExpressions.Regex.Replace(d, @"\(\d+,\d+\)", ""));
        Assert.Equal(expected is null ? [] : [expected], diagnostics);
    }

    /// <summary>C-100 / LANG-650, 651, 665: <c>is</c>/<c>as</c> with interfaces (positions stripped); E2640 without a GUID.</summary>
    [Theory]
    [Trait("Req", "LANG-650")]
    [Trait("Req", "LANG-651")]
    [InlineData("if A is IG then; if X is IG then; G := A as IG; G := X as IG; G := G as IG; G := N as IG; G := H as IH; G := A; H := A; G := H;", null)]
    [InlineData("if A is IZ then;", "E2640: Interface 'IZ' has no GUID; 'is'/'as' requires one")]
    [InlineData("N := G as IZ;", "E2640: Interface 'IZ' has no GUID; 'is'/'as' requires one")]
    [InlineData("if G is IG then;", "E2603: Class type required")]
    [InlineData("if I is IG then;", "E2603: Class type required")]
    [InlineData("A := G as TA;", "E2603: Class type required")]
    [InlineData("if G = nil then; if nil <> G then; if G = H then; if N <> G then;", null)]
    [InlineData("if G = A then;", "E2011: Operator '=' not applicable to operand type(s) 'IG' and 'TA'")]
    [InlineData("if G < H then;", "E2011: Operator '<' not applicable to operand type(s) 'IG' and 'IH'")]
    [InlineData("A := G;", "E2010: Incompatible types: 'IG' and 'TA'")]
    [InlineData("G := X;", "E2010: Incompatible types: 'TX' and 'IG'")]
    [InlineData("G := N;", "E2010: Incompatible types: 'IZ' and 'IG'")]
    public void IsAs_Interfaces(string body, string? expected)
    {
        var diagnostics = BindDiagnostics("type IZ = interface end; IG = interface ['{00000001-0002-0003-0004-000000000005}'] end; " +
                "IH = interface(IG) ['{00000001-0002-0003-0004-000000000006}'] end; TA = class(IH) end; TX = class end; " +
                $"var A: TA; X: TX; G: IG; H: IH; N: IZ; I: Integer; begin {body} end.")
            .Select(d => System.Text.RegularExpressions.Regex.Replace(d, @"\(\d+,\d+\)", ""));
        Assert.Equal(expected is null ? [] : [expected], diagnostics);
    }

    /// <summary>C-101 / LANG-660, 664: interface property accessors are methods of the interface chain (positions stripped).</summary>
    [Theory]
    [Trait("Req", "LANG-660")]
    [Trait("Req", "LANG-664")]
    [InlineData("property P: Integer read Get write Put; property Q[I: Integer]: Integer read At; default;", "G.P := G.P + G.Q[1] + G[2];", null)]
    [InlineData("property P: Integer read F;", "", "E2003: Undeclared identifier 'F'")]
    [InlineData("property P: string read Get;", "", "E2636: Property accessor 'Get' is not compatible with property type 'string'")]
    [InlineData("property P: Integer read Get;", "G.P := 1;", "E2633: Cannot assign to a read-only property")]
    [InlineData("class property P: Integer read Get;", "", "E2900: Feature not supported: interface class property")]
    [InlineData("property P: Integer read Get;", "G.X := 1;", "E2005: 'IH' does not contain a member named 'X'")]
    public void InterfaceProperties(string declarations, string body, string? expected)
    {
        var diagnostics = BindDiagnostics("type IG = interface function Get: Integer; procedure Put(V: Integer); function At(I: Integer): Integer; " +
                $"end; IH = interface(IG) {declarations} end; var G: IH; begin {body} end.")
            .Select(d => System.Text.RegularExpressions.Regex.Replace(d, @"\(\d+,\d+\)", ""));
        Assert.Equal(expected is null ? [] : [expected], diagnostics);
    }

    /// <summary>A definition whose parameters match no declaration defines nothing: E2606, and the declaration stays E2605.</summary>
    [Fact]
    [Trait("Req", "LANG-620")]
    public void Methods_ParameterMismatch_IsE2606AndLeavesTheDeclarationUnimplemented()
    {
        Assert.Equal(["E2605(1,27): Method 'TA.P' is not implemented", "E2606(1,60): 'P' is not a member of class 'TA'"],
            BindDiagnostics("type TA = class procedure P(X: Integer); end; procedure TA.P(Y: Byte); begin end; begin end."));
    }
}
