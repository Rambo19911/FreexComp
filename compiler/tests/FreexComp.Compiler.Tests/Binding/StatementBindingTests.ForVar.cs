using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>C-386: the type of a counted <c>for var</c> loop from both bounds and the common <c>for var</c> rules (inline-variables.md LANG-575, 576).</summary>
public sealed partial class StatementBindingTests
{
    /// <summary>
    /// The semantic model of a program whose procedure <c>Run</c> has the one-line <paramref name="body"/> on line 5 (column 3), after the
    /// one-line <paramref name="declarations"/>, compiled against the real <c>System</c>.
    /// </summary>
    private static SemanticModel ForVarModel(string declarations, string body)
    {
        var program = $"program P;\n{declarations}\nprocedure Run;\nbegin\n  {body}\nend;\nbegin\n  Run;\nend.";
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        return Compilation.Create(options, [], fs).GetSemanticModel(@"C:\p\P.pas");
    }

    /// <summary>Errors and warnings of <paramref name="model"/> (hints left out).</summary>
    private static string[] ErrorsAndWarnings(SemanticModel model) =>
        [.. Format(model.GetDiagnostics()).Where(d => d.StartsWith('E') || d.StartsWith('W'))];

    /// <summary>
    /// LANG-575: the type of <c>I</c> in <c>for var I := A to B</c> is the natural type of <c>A</c>, widened when the end bound's value
    /// (a constant) or type range does not fit to the first of <c>Integer</c>, <c>Int64</c>, <c>UInt64</c> containing both bounds;
    /// <c>UInt64</c> with W1010 at <c>B</c> when none does; other ordinal families must agree (E2010). <paramref name="expected"/> lists
    /// the errors and warnings as <c>CODE@A</c> / <c>CODE@B</c> (at the start / end bound), in source order.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-575")]
    [InlineData("", "0", "9", "Integer", "")]
    [InlineData("var Count: NativeInt;", "0", "Count - 1", "Int64", "")]
    [InlineData("var U: UInt64;", "0", "High(U)", "UInt64", "")]
    [InlineData("var B1, B2: Byte;", "B1", "B2", "Byte", "")]
    [InlineData("", "'a'", "'z'", "Char", "")]
    [InlineData("type TColor = (Red, Green, Blue);", "Low(TColor)", "High(TColor)", "TColor", "")]
    [InlineData("", "0", "3000000000", "Int64", "")]                                   // a constant end bound by its value
    [InlineData("var B1: Byte;", "B1", "300", "Integer", "")]
    [InlineData("var B1: Byte;", "B1", "200", "Byte", "")]
    [InlineData("var C: Cardinal; K: Integer;", "C", "K", "Int64", "")]
    [InlineData("var K: Integer; U: UInt64;", "K", "U", "UInt64", "W1010@B")]         // a signed start and an end above High(Int64)
    [InlineData("var U: UInt64;", "-1", "High(U)", "UInt64", "E2012@A W1010@B")]      // and a negative constant is no UInt64
    [InlineData("", "'a'", "5", "Char", "E2010@B")]
    public void ForVar_CountedType(string declarations, string start, string finish, string type, string expected)
    {
        var body = $"for var I := {start} to {finish} do ;";
        var model = ForVarModel(declarations, body);
        var loop = model.SyntaxTree.Root.DescendantNodes().OfType<ForStatementSyntax>().Single();
        var symbol = Assert.IsType<VariableSymbol>(model.GetDeclaredSymbol(loop.Variable.Identifier, TestContext.Current.CancellationToken));

        Assert.Equal(type, symbol.Type.Name);
        var startColumn = 2 + "for var I := ".Length + 1;
        var finishColumn = startColumn + start.Length + " to ".Length;
        Assert.Equal(
            [.. expected.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => $"{e[..5]}(5,{(e.EndsWith("@A", StringComparison.Ordinal) ? startColumn : finishColumn)})")],
            ErrorsAndWarnings(model).Select(d => d[..d.IndexOf(':', StringComparison.Ordinal)]));
    }

    /// <summary>
    /// LANG-576: a <c>for var</c> variable (counted or <c>for … in</c>) is read-only in the body — assignment, <c>Inc</c>, a <c>var</c>
    /// argument and <c>@X</c> are E2053 at the variable — and exists only inside the loop statement: a use after it is E2003.
    /// </summary>
    [Theory]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-576")]
    [InlineData("for var I := 1 to 3 do I := 5;", "I := 5", "E2053")]
    [InlineData("for var I := 1 to 3 do Inc(I);", "I);", "E2053")]
    [InlineData("for var I := 1 to 3 do Change(I);", "I);", "E2053")]
    [InlineData("for var I := 1 to 3 do P := @I;", "I;", "E2053")]
    [InlineData("for var X in A do X := 5;", "X := 5", "E2053")]
    [InlineData("for var X in A do Change(X);", "X);", "E2053")]
    [InlineData("for var I := 1 to 3 do ; WriteLn(I);", "I);", "E2003")]
    [InlineData("for var X in A do ; WriteLn(X);", "X);", "E2003")]
    public void ForVar_ReadOnlyAndScoped(string body, string marker, string code)
    {
        var model = ForVarModel("var A: array of Integer; P: Pointer; procedure Change(var N: Integer); begin end;", body);

        var column = 2 + body.IndexOf(marker, StringComparison.Ordinal) + 1;
        Assert.StartsWith($"{code}(5,{column}): ", Assert.Single(ErrorsAndWarnings(model)));
    }
}
