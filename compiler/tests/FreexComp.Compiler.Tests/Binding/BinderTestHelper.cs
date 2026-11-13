using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Parses a source text and binds it (declarations and unit-level bodies).</summary>
internal static class BinderTestHelper
{
    public const string TestPath = @"C:\p\test.pas";

    public static (BindingResult Result, SyntaxTree Tree) Bind(string text)
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return (Binder.BindCompilationUnit(tree, new BuiltInTypes()), tree);
    }

    /// <summary>"CODE(line,col): message" for every binder diagnostic, 1-based.</summary>
    public static string[] Format(IEnumerable<Diagnostic> diagnostics) =>
        [.. diagnostics.Select(d => $"{d.Code}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Message}")];

    public static string[] BindDiagnostics(string text) => Format(Bind(text).Result.Diagnostics);
}
