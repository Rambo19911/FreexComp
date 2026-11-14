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

    [Fact]
    [Trait("Req", "LANG-601")]
    public void Classes_UncompletedForwardDeclaration_IsE2015()
    {
        Assert.Equal(["E2015(1,6): Unsatisfied forward declaration of type 'TB'"], BindDiagnostics("type TB = class; begin end."));
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

    /// <summary>A definition whose parameters match no declaration defines nothing: E2606, and the declaration stays E2605.</summary>
    [Fact]
    [Trait("Req", "LANG-620")]
    public void Methods_ParameterMismatch_IsE2606AndLeavesTheDeclarationUnimplemented()
    {
        Assert.Equal(["E2605(1,27): Method 'TA.P' is not implemented", "E2606(1,60): 'P' is not a member of class 'TA'"],
            BindDiagnostics("type TA = class procedure P(X: Integer); end; procedure TA.P(Y: Byte); begin end; begin end."));
    }
}
