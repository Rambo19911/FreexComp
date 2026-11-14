using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Emit;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Types;
using static FreexComp.Compiler.Tests.Binding.BinderTestHelper;

namespace FreexComp.Compiler.Tests.Emit;

/// <summary>C-160: <c>external</c>, <c>public name</c>, calling conventions within one compilation (LANG-462, 463, 470).</summary>
[Trait("Phase", "2")]
public sealed class ExternalDeclTests
{
    private static BindingResult Bind(string source)
    {
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\M.pas", source));
        Assert.Empty(tree.Diagnostics);
        return Binder.BindCompilationUnit(tree, new BuiltInTypes());
    }

    private static IEnumerable<Symbol> Members(BindingResult result) => result.Unit.InterfaceMembers.Concat(result.Unit.ImplementationMembers);

    [Fact]
    [Trait("Req", "LANG-470")]
    public void Parser_VariablePublicName_IsADirectiveAfterTheSemicolon()
    {
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\p\M.pas", """
            unit M;
            interface
            var
              X: Integer = 1; public name 'x_sym';
              public: Integer;
              Y: Integer;
            implementation
            end.
            """));

        Assert.Empty(tree.Diagnostics);
        var declarations = tree.Root.DescendantNodes().OfType<VarDeclarationSyntax>().ToList();
        Assert.Equal(3, declarations.Count);
        Assert.Equal("public name 'x_sym';", declarations[0].PublicDirective!.ToString());
        Assert.Null(declarations[1].PublicDirective);
        Assert.Equal(tree.Text.Text, tree.Root.ToFullString());
    }

    [Fact]
    [Trait("Req", "LANG-463")]
    [Trait("Req", "CMP-430")]
    public void Binder_LibraryAndImportNames()
    {
        var result = Bind(IrSnapshotTests.ExternalDeclSource);

        Assert.Empty(Format(result.Diagnostics));
        var routines = Members(result).OfType<RoutineSymbol>().ToDictionary(r => r.Name);
        Assert.Equal("kernel32.dll", routines["GetCurrentProcessId"].LibraryName);
        Assert.Equal("KERNEL32", routines["Sleep"].LibraryName);
        Assert.Equal("kernel32.dll", LinkInputs.NormalizeDllName(routines["Sleep"].LibraryName!));
        Assert.Null(routines["Helper"].LibraryName);
        Assert.Equal("Sleep", routines["Sleep"].LinkName);
        Assert.Equal("other_helper", routines["Renamed"].LinkName);
    }

    [Fact]
    [Trait("Req", "LANG-470")]
    public void Binder_VariablePublicName_SetsTheLinkName()
    {
        var result = Bind(IrSnapshotTests.ExternalDeclSource);

        var counter = Members(result).OfType<VariableSymbol>().Single(v => v.Name == "Counter");
        Assert.Equal("_counter", Mangler.GlobalVariable(counter).Text);
    }

    [Fact]
    [Trait("Req", "LANG-470")]
    public void Binder_PublicNameOnALocalVariable_IsE2900()
    {
        var result = Bind("""
            unit M;
            interface
            implementation
            procedure P;
            var L: Integer; public name 'l';
            begin
            end;
            end.
            """);

        Assert.Contains(Format(result.Diagnostics), d => d.Contains("E2900", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "CMP-620")]
    public void LinkInputs_DllsOfTheUnits_AlwaysIncludeKernel32AndNtdll_SortedAndDistinct()
    {
        var result = Bind("""
            unit M;
            interface
            procedure A; external 'User32.DLL';
            procedure B; external 'user32';
            procedure C; external 'kernel32.dll';
            procedure D; external 'Gdi32' name 'DD';
            procedure E; external;
            implementation
            end.
            """);

        Assert.Equal(["gdi32.dll", "kernel32.dll", "ntdll.dll", "user32.dll"], LinkInputs.Dlls([result.Unit]));
        Assert.Equal(
            [("A", "user32.dll"), ("B", "user32.dll"), ("C", "kernel32.dll"), ("DD", "gdi32.dll")],
            LinkInputs.Imports([result.Unit]).Select(i => (i.ImportName, i.DllName)));
    }
}
