using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-126: attribute resolution, class-level attributes and the recognised built-ins (LANG-760…763).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-760")]
public sealed class AttributeBindingTests
{
    private const string SystemSource = """
        unit System;
        interface
        type
          TObject = class constructor Create; end;
          TCustomAttribute = class(TObject) end;
        var Input, Output: Text;
        implementation
        constructor TObject.Create; begin end;
        end.
        """;

    private const string Declarations = """
        program P;
        type
          TagAttribute = class(TCustomAttribute)
            constructor Create(N: Integer); overload;
            constructor Create; overload;
          end;
          TPlain = class end;
        constructor TagAttribute.Create(N: Integer); begin end;
        constructor TagAttribute.Create; begin end;

        """;

    private static BindingResult BindWithAttributes(string text)
    {
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", SystemSource)), types);
        Assert.Empty(system.Diagnostics);
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return Binder.BindCompilationUnit(tree, types, system);
    }

    [Fact]
    [Trait("Req", "LANG-762")]
    public void ClassAttributes_AreKeptWithAThunkEach()
    {
        var result = BindWithAttributes(Declarations + "type [Tag(1)] [TagAttribute] [Tag] TFoo = class end;\nbegin end.");

        Assert.Empty(Format(result.Diagnostics));
        var foo = Assert.IsType<ClassTypeSymbol>(result.Scope.Lookup("TFoo"));
        Assert.Equal(3, foo.Attributes.Length);
        Assert.All(foo.Attributes, a => Assert.Equal("TagAttribute", a.Type.Name));
        Assert.Equal(["TFoo#attr1", "TFoo#attr2", "TFoo#attr3"], foo.Attributes.Select(a => a.Thunk.Name));
        Assert.All(foo.Attributes, a => Assert.Contains(result.Routines, r => ReferenceEquals(r.Routine, a.Thunk)));
    }

    [Theory]
    [Trait("Req", "LANG-761")]
    [InlineData("type [Tag('x')] TFoo = class end;", "E2123")]                         // no constructor takes a string
    [InlineData("var V: Integer; type [Tag(V)] TFoo = class end;", "E2050")]           // not a constant
    [InlineData("type [TPlain] TFoo = class end;", "W1070")]                           // not an attribute class
    [InlineData("type [Missing] TFoo = class end;", "W1070")]
    [InlineData("type TFoo = class [Weak] F: TObject; end;", "H2010")]
    [InlineData("type TFoo = class [Unsafe] F: TObject; [Volatile] G: Integer; end;", "H2010,H2010")]
    [InlineData("procedure Q([Ref] const X: Integer); begin end;", "")]
    public void Diagnostics(string declaration, string codes) =>
        Assert.Equal(codes, string.Join(",", BindWithAttributes(Declarations + declaration + "\nbegin end.").Diagnostics.Select(d => d.Code)));

    [Fact]
    [Trait("Req", "LANG-762")]
    public void OtherAttributes_AreCheckedThenDiscarded()
    {
        var result = BindWithAttributes(Declarations + "type TFoo = class [Tag(2)] F: Integer; [Tag] procedure M([Tag] X: Integer); end;\n"
            + "procedure TFoo.M(X: Integer); begin F := X end;\nbegin end.");

        Assert.Empty(Format(result.Diagnostics));
        Assert.Empty(Assert.IsType<ClassTypeSymbol>(result.Scope.Lookup("TFoo")).Attributes);
    }

    private const string RefTypes = "type TP = procedure(const X: Integer); TQ = procedure([Ref] const X: Integer); "
        + "TM = procedure(const X: Integer) of object; TC = class procedure M(const [Ref] X: Integer); end; "
        + "procedure A(const [Ref] X: Integer); begin end; procedure B(const X: Integer); begin end; "
        + "procedure TC.M([Ref] const X: Integer); begin end; var P: TP; Q: TQ; M: TM; C: TC;";

    /// <summary>
    /// C-232 (LANG-763): <c>[Ref]</c> may follow <c>const</c> (<c>const [Ref] X</c>) and means the same as before it; a declaration and
    /// its implementation agree on it (E2101), as do an overriding method and the method it overrides (E2621), a method and the interface
    /// method it implements (E2651) and a routine or method
    /// and the procedural type it is assigned to (E2010) — the two are passed differently.
    /// </summary>
    [Theory]
    [Trait("Req", "LANG-763")]
    [InlineData("procedure Q(const [Ref] X: Integer); begin end;", "", "")]
    [InlineData("procedure Q(const [Ref] X, Y: Integer; [Ref] const Z: string); forward; procedure Q([Ref] const X, Y: Integer; const [Ref] Z: string); begin end;", "", "")]
    [InlineData("procedure Q([Ref] const X: Integer); forward; procedure Q(const X: Integer); begin end;", "", "E2101")]
    [InlineData("procedure Q(const X: Integer); forward; procedure Q(const [Ref] X: Integer); begin end;", "", "E2101")]
    [InlineData("procedure Q([Ref] const X: Integer); forward; procedure Q; begin end;", "", "")]            // parameter list omitted
    [InlineData("type TD = class procedure M([Ref] const X: Integer); end; procedure TD.M(const X: Integer); begin end;", "", "E2101")]
    [InlineData("type TD = class procedure M(const [Ref] X: Integer); end; procedure TD.M([Ref] const X: Integer); begin end;", "", "")]
    [InlineData("type TR = record procedure M(const X: Integer); end; procedure TR.M([Ref] const X: Integer); begin end;", "", "E2101")]
    [InlineData("type TA = class procedure M([Ref] const X: Integer); virtual; end; TB = class(TA) procedure M(const X: Integer); override; end; "
        + "procedure TA.M([Ref] const X: Integer); begin end; procedure TB.M(const X: Integer); begin end;", "", "E2621")]
    [InlineData("type TA = class procedure M([Ref] const X: Integer); virtual; end; TB = class(TA) procedure M(const [Ref] X: Integer); override; end; "
        + "procedure TA.M([Ref] const X: Integer); begin end; procedure TB.M([Ref] const X: Integer); begin end;", "", "")]
    [InlineData("type IFoo = interface procedure M([Ref] const X: Integer); end; TI = class(TObject, IFoo) procedure M(const X: Integer); end; "
        + "procedure TI.M(const X: Integer); begin end;", "", "E2651")]
    [InlineData("type IFoo = interface procedure M([Ref] const X: Integer); end; TI = class(TObject, IFoo) procedure M(const [Ref] X: Integer); end; "
        + "procedure TI.M(const [Ref] X: Integer); begin end;", "", "")]
    [InlineData(RefTypes, "Q := A; P := B; Q(1); P(1);", "")]
    [InlineData(RefTypes, "P := A;", "E2010")]
    [InlineData(RefTypes, "Q := B;", "E2010")]
    [InlineData(RefTypes, "Q := P;", "E2010")]
    [InlineData(RefTypes, "C := nil; M := C.M;", "E2010")]
    public void RefSyntaxAndChecks(string declaration, string statements, string codes)
    {
        var result = BindWithAttributes(Declarations + declaration + "\nbegin " + statements + " end.");

        Assert.Equal(codes, string.Join(",", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code)));
    }

    /// <summary>C-232: <c>const [Ref] X</c> gives the parameter symbol of <c>[Ref] const X</c> (by reference, still <c>const</c>).</summary>
    [Fact]
    [Trait("Req", "LANG-763")]
    public void RefAfterConst_IsTheSameParameter()
    {
        var result = BindWithAttributes(Declarations + "procedure Q(const [Ref] X: Integer; [Ref] const Y: Integer; const Z: Integer); begin end;\nbegin end.");

        Assert.Empty(Format(result.Diagnostics));
        var q = Assert.IsAssignableFrom<FreexComp.Compiler.Symbols.RoutineSymbol>(result.Scope.Lookup("Q"));
        Assert.Equal([true, true, false], q.Parameters.Select(p => p.IsRef));
        Assert.All(q.Parameters, p => Assert.Equal(FreexComp.Compiler.Symbols.ParameterMode.Const, p.Mode));
    }
}
