using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>LANG-862: <c>{$IF}</c> / <c>{$ELSEIF}</c> evaluated through the binder callback (C-032).</summary>
public sealed partial class PreprocessorTests
{
    private static ParseOptions BinderOptions(string[] defines) => DirectiveConditionEvaluator.WithBinder(new ParseOptions([.. defines]));

    private static (string[] Active, string[] Diagnostics) Evaluated(string text, params string[] defines)
    {
        var source = SourceText.From(Path, text);
        var options = BinderOptions(defines);
        var diagnostics = Lexer.GetDiagnostics(source, options)
            .Select(d => $"{d.Code}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1})");
        return ([.. Lexer.Tokenize(source, options).SkipLast(1).Select(t => t.Text)], [.. diagnostics]);
    }

    private static string[] EvaluatedActive(string text, params string[] defines)
    {
        var (active, diagnostics) = Evaluated(text, defines);
        Assert.Empty(diagnostics);
        return active;
    }

    [Theory]
    [Trait("Req", "LANG-862")]
    [InlineData("{$IF Defined(X)} a {$ELSE} b {$ENDIF}", new string[0], new[] { "b" })]
    [InlineData("{$IF Defined(X)} a {$ELSE} b {$ENDIF}", new[] { "X" }, new[] { "a" })]
    [InlineData("{$IF Defined(x) and not Defined(Y)} a {$ELSE} b {$ENDIF}", new[] { "X" }, new[] { "a" })]
    [InlineData("{$IF Defined(X) or Defined(WIN64)} a {$IFEND}", new string[0], new[] { "a" })]
    [InlineData("{$IF False} a {$ELSE} b {$ENDIF}", new string[0], new[] { "b" })]
    [InlineData("{$IF (1 + 2 = 3) and (SizeOf(Integer) = 4)} a {$ENDIF} z", new string[0], new[] { "a", "z" })]
    [InlineData("{$IF CompilerVersion >= 1.0} a {$ELSE} b {$ENDIF}", new string[0], new[] { "a" })]
    [InlineData("{$IF Declared(Integer) and Declared(Writeln) and not Declared(Nothing)} a {$ENDIF}", new string[0], new[] { "a" })]
    public void IfExpr_EvaluatesConstantBooleanExpressions(string text, string[] defines, string[] active)
    {
        Assert.Equal(active, EvaluatedActive(text, defines));
    }

    [Theory]
    [Trait("Req", "LANG-862")]
    [InlineData("{$IF False} a {$ELSEIF 1 > 2} b {$ELSEIF True} c {$ELSE} d {$ENDIF}", new[] { "c" })]
    [InlineData("{$IF True} a {$ELSEIF True} b {$ELSE} c {$ENDIF}", new[] { "a" })]
    [InlineData("{$IF False} a {$ELSEIF False} b {$ELSE} c {$ENDIF}", new[] { "c" })]
    [InlineData("{$IFDEF X} a {$ELSEIF Defined(WIN64)} b {$ENDIF}", new[] { "b" })]
    [InlineData("{$IF True}{$IF False} a {$ELSE} b {$ENDIF}{$ELSE} c {$ENDIF}", new[] { "b" })]
    public void IfExpr_ElseIfChains_TakeTheFirstTrueBranch(string text, string[] active)
    {
        Assert.Equal(active, EvaluatedActive(text));
    }

    [Fact]
    [Trait("Req", "LANG-862")]
    public void IfExpr_SeesTheUnitsDeclarationsBeforeTheDirective()
    {
        // directives.md §6
        const string text = "program D;\n{$DEFINE FAST}\nconst Limit = 10;\nbegin\n" +
            "  {$IF Defined(DEBUG) and (Limit > 5)} Writeln('debug big');\n  {$ELSEIF Limit > 5} Writeln('big');\n" +
            "  {$ELSE} Writeln('small');\n  {$ENDIF}\nend.";
        Assert.Contains("'big'", EvaluatedActive(text));
        Assert.DoesNotContain("'small'", EvaluatedActive(text));
        Assert.Contains("'debug big'", EvaluatedActive(text, "DEBUG"));

        Assert.Equal(["unit", "U", ";", "interface", "const", "A", "=", "1", ";", "implementation", "b", "end", "."],
            EvaluatedActive("unit U; interface const A = 1; implementation {$IF (A = 1) and Declared(A)} b {$ENDIF} end."));
        Assert.Contains("x", EvaluatedActive("program T; var V: Integer; procedure P; begin end; {$IF Declared(V) and Declared(P)} x {$ENDIF} begin end."));
        Assert.Contains("x", EvaluatedActive("program T; const L = 10 {$IF L = 10} + 0 {$ENDIF}; {$IF L = 10} x {$ENDIF} begin end."));
    }

    [Fact]
    [Trait("Req", "LANG-862")]
    public void IfExpr_UndeclaredIdentifier_IsE1203_AndTheBranchIsInactive()
    {
        var (active, diagnostics) = Evaluated("program T;\n{$IF Limit > 5} a {$ELSE} b {$ENDIF}\nconst Limit = 10;");
        Assert.Equal(["E1203(2,6)"], diagnostics);
        Assert.Contains("b", active);
        Assert.DoesNotContain("a", active);
        Assert.Equal("Undeclared identifier 'Limit' in {$IF} expression",
            Lexer.GetDiagnostics(SourceText.From(Path, "{$IF Limit} a {$ENDIF}"), BinderOptions([])).Single().Message);
    }

    /// <summary>
    /// C-417 (lead decision 2026-11-27): a directive condition is folded by the constant folder, so the type queries of
    /// intrinsics.md LANG-972 are constants there too — of the types declared before the directive; a type declared later is the
    /// undeclared identifier of every directive condition (E1203, the branch is inactive).
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-862")]
    [Trait("Req", "LANG-976")]
    [InlineData("", "IsManagedType(string)", "a", "")]
    [InlineData("", "IsManagedType(Integer)", "b", "")]
    [InlineData("", "not IsManagedType(Integer) and (SizeOf(Integer) = 4)", "a", "")]
    [InlineData("", "HasWeakRef(string)", "b", "")]
    [InlineData("", "IsConstValue(1 + 2)", "a", "")]
    [InlineData("type TRec = record S: string; end;", "IsManagedType(TRec)", "a", "")]
    [InlineData("type TKind = (kA, kB);", "GetTypeKind(TKind) = tkEnumeration", "a", "")]
    [InlineData("", "GetTypeKind(Int64) in [tkInteger, tkInt64]", "a", "")]
    [InlineData("const Wide = GetTypeKind(Int64) = tkInt64;", "Wide", "a", "")]
    [InlineData("", "IsManagedType(TLater)", "b", "E1203(2,21)")]
    [InlineData("", "GetTypeKind(TLater) = tkUString", "b", "E1203(2,19)")]
    [InlineData("var V: Integer;", "IsManagedType(V)", "b", "E2002(2,36)")]
    public void IfExpr_TypeQueryInIfDirective(string before, string condition, string branch, string diagnostic)
    {
        var text = $"program T;\n{before} {{$IF {condition}}} var a: Integer; {{$ELSE}} var b: Integer; {{$ENDIF}}\ntype TLater = string;\nbegin\nend.";
        var fs = new InMemoryFileSystem().Add(Path, text).AddSystem();
        var options = new CompilationOptions { MainFile = Path, RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var compilation = Compilation.Create(options, [], fs);

        var tokens = compilation.GetSemanticModel(Path).SyntaxTree.Root.DescendantTokens().Select(t => t.Text).ToList();
        Assert.Contains(branch, tokens);
        Assert.DoesNotContain(branch == "a" ? "b" : "a", tokens);
        Assert.Equal(
            diagnostic.Length == 0 ? [] : [diagnostic],
            compilation.GetDiagnostics(ct: TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Code}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1})"));
    }

    [Theory]
    [Trait("Req", "LANG-862")]
    [InlineData("{$IF 1} a {$ENDIF} b", "E2061(1,6)")]
    [InlineData("{$IF (} a {$ENDIF} b", "E1108(1,7)")]
    [InlineData("{$IF True True} a {$ENDIF} b", "E1100(1,11)")]
    [InlineData("{$IF} a {$ENDIF} b", "E1206(1,1)")]
    [InlineData("{$IF Defined(1)} a {$ENDIF} b", "E1206(1,13)")]
    public void IfExpr_InvalidConditions_AreReported_AndFalse(string text, string diagnostic)
    {
        var (active, diagnostics) = Evaluated(text);
        Assert.Equal([diagnostic], diagnostics);
        Assert.Equal(["b"], active);
    }

    [Theory]
    [Trait("Req", "LANG-862")]
    [InlineData("{$IFDEF X}{$IF Bogus} a {$ENDIF}{$ELSE} b {$ENDIF}")]
    [InlineData("{$IF True} b {$ELSEIF Bogus} a {$ENDIF}")]
    public void IfExpr_ConditionsInInactiveOrAlreadyTakenBranches_AreNotEvaluated(string text)
    {
        Assert.Equal(["b"], EvaluatedActive(text));
    }

    [Fact]
    [Trait("Req", "LANG-862")]
    public void IfExpr_FromSyntaxTreeParse_WithIncludes()
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\c.inc", "const FromInc = 3;");
        var tree = SyntaxTree.Parse(SourceText.From(Path, "program T; {$I c.inc} {$IF FromInc = 3} var x: Integer; {$ENDIF} begin x := 1 end."),
            BinderOptions([]), fs);
        Assert.Empty(tree.Diagnostics);
        Assert.Contains("var x: Integer;", tree.Root.ToFullString().Replace("{$IF FromInc = 3} ", ""));
        Assert.DoesNotContain(tree.DisabledRegions, _ => true);
    }
}
