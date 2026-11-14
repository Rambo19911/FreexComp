using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Variable sections and unit/block-level lookup (grammar.md varDecl, LANG-310, 402, 518, units-programs.md).</summary>
[Trait("Phase", "2")]
[Trait("Req", "LANG-310")]
public sealed class VarBindingTests
{
    private static VariableSymbol Variable(BindingResult result, string name) =>
        Assert.IsType<VariableSymbol>(result.Scope.Lookup(name));

    [Fact]
    public void GlobalVariables_GetTheirTypes()
    {
        var (result, tree) = Bind("program P;\nvar\n  A, B: Integer;\n  C: Char;\n  S: string;\nbegin end.");

        Assert.Empty(result.Diagnostics);
        var a = Variable(result, "a");
        Assert.Equal(
            ("A", SymbolKind.Variable, "Integer", true, "P.A", "var A: Integer"),
            (a.Name, a.Kind, a.Type.Name, a.IsGlobal, a.QualifiedName, a.ToDisplayString()));
        var location = Assert.Single(a.Locations);
        Assert.Equal((2, 2), (location.LineSpan.Start.Line, location.LineSpan.Start.Column));
        Assert.Equal("Integer", Variable(result, "B").Type.Name);
        Assert.Equal("Char", Variable(result, "C").Type.Name);
        Assert.Equal("string", Variable(result, "S").Type.Name);
        Assert.Equal(["A", "B", "C", "S"], result.Unit.ImplementationMembers.Select(s => s.Name));
    }

    [Fact]
    public void Alias_TypeNames_BindToTheTargetType()
    {
        var (result, _) = Bind("var L: LongInt; W: WideChar; begin end.");

        Assert.Empty(result.Diagnostics);
        Assert.Equal("Integer", Variable(result, "L").Type.Name);
        Assert.Equal("Char", Variable(result, "W").Type.Name);
    }

    [Fact]
    public void UnitVariables_AreSplitIntoInterfaceAndImplementation()
    {
        var (result, _) = Bind("unit U; interface var A: Integer; implementation const N = 1; var B: Byte; end.");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(["A"], result.Unit.InterfaceMembers.Select(s => s.Name));
        Assert.Equal(["N", "B"], result.Unit.ImplementationMembers.Select(s => s.Name));
        Assert.True(Variable(result, "A").IsGlobal);
        Assert.Same(result.InterfaceScope, result.Scope.Parent);
    }

    [Fact]
    public void AVariableNamedLikeABuiltInType_ShadowsIt()
    {
        var (result, _) = Bind("var Integer: Byte; begin end.");

        Assert.Empty(result.Diagnostics);
        Assert.Equal("Byte", Variable(result, "Integer").Type.Name);
    }

    [Fact]
    public void ErroneousType_GivesTheErrorType()
    {
        var (result, _) = Bind("var X: Missing; begin end.");

        Assert.Equal(FreexComp.Compiler.Types.TypeKind.Error, Variable(result, "X").Type.TypeKind);
    }

    [Fact]
    [Trait("Req", "LANG-340")]
    public void Variable_IsNotAConstant() =>
        Assert.Equal(["E2050(1,27): Constant expression expected"], BindDiagnostics("var V: Integer; const C = V; begin end."));

    [Fact]
    [Trait("Req", "LANG-522")]
    public void GlobalInitializer_IsAConstantExpression()
    {
        var (result, _) = Bind("const N = 4; var X: Integer = N * 2; begin end.");

        Assert.Empty(result.Diagnostics);
        Assert.Equal("8", Variable(result, "X").Initializer!.ToDisplayString());
    }

    [Theory]
    [InlineData("var X: Integer = Missing; begin end.", "E2003(1,18): Undeclared identifier 'Missing'")]
    [InlineData("var A, B: Integer = 1; begin end.", "E1101(1,19): ';' expected but '=' found")]
    [InlineData("var X: Integer = (1, 2); begin end.", "E2017(1,18): Array type required")]          // C-054: no longer E2900
    public void InvalidInitializers(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    [Theory]
    [InlineData("threadvar T: Integer; begin end.", "E2900(1,1): Feature not supported: threadvar")]
    [InlineData("var X: array of Integer; begin end.", "E2900(1,8): Feature not supported: dynamic array type")]
    [InlineData("var X: ^Integer; begin end.", "E2900(1,8): Feature not supported: pointer type")]
    [InlineData("var X: string[10]; begin end.", "E2900(1,8): Feature not supported: short string type")]
    [InlineData("var X: Currency; begin end.", "E2900(1,8): Feature not supported: Currency")]
    [InlineData("var X: System.Integer; begin end.", "E2900(1,8): Feature not supported: unit-qualified name")]
    public void LaterMilestoneConstructs_AreE2900(string text, string expected) => Assert.Equal([expected], BindDiagnostics(text));

    // ------------------------------------------------------------------ local (routine-level) declarations, LANG-402

    /// <summary>Binds the block of the first routine of <paramref name="text"/> into a routine scope inside the program scope.</summary>
    private static (Scope Scope, string[] Diagnostics) BindFirstRoutineBlock(string text)
    {
        var (result, tree) = Bind(text);
        Assert.Empty(result.Declarations.Diagnostics);                  // unit level clean; routine bodies report their own
        var routine = ((ProgramSyntax)tree.Root).Block.Declarations.OfType<RoutineDeclarationSyntax>().First();
        var scope = new Scope(ScopeKind.Routine, result.Scope);
        var diagnostics = new DiagnosticBag();
        new DeclarationBinder(new FreexComp.Compiler.Types.BuiltInTypes(), scope, result.Unit, diagnostics).BindDeclarations(routine.Block!.Declarations);
        return (scope, Format(diagnostics.ToImmutable()));
    }

    [Fact]
    [Trait("Req", "LANG-402")]
    public void LocalVariables_AreNotGlobal_AndShadowGlobals()
    {
        var (scope, diagnostics) = BindFirstRoutineBlock("var G: Integer; procedure P; var G: Byte; L: Char; begin end; begin end.");

        Assert.Empty(diagnostics);
        var g = Assert.IsType<VariableSymbol>(scope.Lookup("G"));
        Assert.Equal(("Byte", false, SymbolKind.Local), (g.Type.Name, g.IsGlobal, g.Kind));
        Assert.IsType<VariableSymbol>(scope.LookupLocal("L"));
        Assert.True(Assert.IsType<VariableSymbol>(scope.Parent!.Lookup("G")).IsGlobal);
    }

    [Fact]
    [Trait("Req", "LANG-402")]
    public void LocalRedeclaration_IsE2004() =>
        Assert.Equal(
            ["E2004(1,30): Identifier redeclared: 'X'"],
            BindFirstRoutineBlock("procedure P; var X: Integer; X: Byte; begin end; begin end.").Diagnostics);

    [Fact]
    public void LocalInitializer_IsE2207() =>
        Assert.Equal(
            ["E2207(1,31): Initialized variables are only allowed at unit or program level"],
            BindFirstRoutineBlock("procedure P; var X: Integer = 1; begin end; begin end.").Diagnostics);
}
