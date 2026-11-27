using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Tests.Binding;

/// <summary>Parses a source text and binds it (declarations and unit-level bodies).</summary>
internal static class BinderTestHelper
{
    public const string TestPath = @"C:\p\test.pas";

    /// <summary>
    /// The smallest <c>System</c> a white-box unit is bound against (C-069: the default files of <c>Write</c>/<c>Read</c> are the
    /// bound <c>System.Output</c>/<c>System.Input</c>, LANG-910, 940).
    /// </summary>
    public const string StubSystemSource = "unit System; interface var Input, Output: Text; implementation end.";

    /// <summary>
    /// <see cref="StubSystemSource"/> plus the enumeration <c>TTypeKind</c> as <c>rtl/src/System.pas</c> declares it: the result type
    /// of <c>GetTypeKind</c> (intrinsics.md LANG-974, C-416).
    /// </summary>
    public const string TypeKindSystemSource =
        "unit System; interface type TTypeKind = (tkUnknown, tkInteger, tkChar, tkEnumeration, tkFloat, tkSet, tkClass, tkMethod, tkWChar, "
        + "tkUString, tkAString, tkSString, tkRecord, tkArray, tkDynArray, tkInterface, tkInt64, tkPointer, tkClassRef, tkProcedure); "
        + "var Input, Output: Text; implementation end.";

    public static (BindingResult Result, SyntaxTree Tree) Bind(string text)
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return (BindWithStubSystem(tree), tree);
    }

    /// <summary><see cref="Bind"/> against <see cref="TypeKindSystemSource"/>.</summary>
    public static BindingResult BindWithTypeKinds(string text)
    {
        var tree = SyntaxTree.Parse(SourceText.From(TestPath, text));
        Assert.Empty(tree.Diagnostics);
        return BindWithStubSystem(tree, TypeKindSystemSource);
    }

    /// <summary>Binds <paramref name="tree"/> against <paramref name="systemSource"/> (one <see cref="BuiltInTypes"/> for both).</summary>
    public static BindingResult BindWithStubSystem(SyntaxTree tree, string systemSource = StubSystemSource)
    {
        var types = new BuiltInTypes();
        var system = Binder.BindCompilationUnit(SyntaxTree.Parse(SourceText.From(@"C:\p\System.pas", systemSource)), types);
        Assert.Empty(system.Diagnostics);
        return Binder.BindCompilationUnit(tree, types, system);
    }

    /// <summary>"CODE(line,col): message" for every binder diagnostic, 1-based.</summary>
    public static string[] Format(IEnumerable<Diagnostic> diagnostics) =>
        [.. diagnostics.Select(d => $"{d.Code}({d.Location.LineSpan.Start.Line + 1},{d.Location.LineSpan.Start.Column + 1}): {d.Message}")];

    public static string[] BindDiagnostics(string text) => Format(Bind(text).Result.Diagnostics);
}
