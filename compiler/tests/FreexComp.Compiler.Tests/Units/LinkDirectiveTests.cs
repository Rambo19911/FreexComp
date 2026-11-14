using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Tests.Binding;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-077: the link inputs of one unit (the <c>.pcu</c> <c>linkInputs</c>, architecture.md §6): DLLs of its <c>external</c>
/// declarations (CMP-430) and the files of its <c>{$L file}</c> / <c>{$LINK file}</c> directives (directives.md, CMP-620, 625).
/// </summary>
[Trait("Phase", "2")]
public sealed class LinkDirectiveTests
{
    private const string Dir = @"C:\p";

    private static (UnitLinkInputs Inputs, List<Diagnostic> Diagnostics) Collect(string source, InMemoryFileSystem fs)
    {
        var path = Path.Combine(Dir, "U.pas");
        fs.Add(path, source);
        var tree = SyntaxTree.Parse(SourceText.From(path, source), new ParseOptions([]), fs);
        Assert.Empty(tree.Diagnostics);
        var bound = BinderTestHelper.BindWithStubSystem(tree);
        Assert.Empty(bound.Diagnostics);
        var diagnostics = new List<Diagnostic>();
        return (UnitLinkInputs.Collect(bound.Unit, tree, fs, diagnostics), diagnostics);
    }

    [Fact]
    [Trait("Req", "CMP-430")]
    public void Dlls_AreTheNormalisedLibrariesOfTheUnitsExternals_SortedAndDistinct()
    {
        var (inputs, diagnostics) = Collect("""
            unit U;
            interface
            const Lib = 'USER32';
            function A: Integer; external Lib;
            function B: Integer; stdcall; external 'kernel32.dll' name 'GetTickCount';
            implementation
            function C: Integer; external 'User32.dll' name 'X';
            procedure D; external;
            end.
            """, new InMemoryFileSystem());

        Assert.Empty(diagnostics);
        Assert.Equal(["kernel32.dll", "user32.dll"], inputs.Dlls);
        Assert.Empty(inputs.Objects);
        Assert.Empty(inputs.Defs);
    }

    [Fact]
    [Trait("Req", "CMP-620")]
    [Trait("Req", "CMP-625")]
    public void LinkDirectives_SplitIntoObjectsAndDefs_ResolvedAgainstTheUnitsDirectory()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\helper.obj", "")
            .Add(@"C:\p\defs\my lib.def", "")
            .Add(@"C:\p\plain.obj", "")
            .Add(@"C:\abs\gdi32.def", "");

        var (inputs, diagnostics) = Collect("""
            unit U;
            {$L helper.obj}
            {$LINK 'defs/my lib.def'}
            interface
            {$L plain}
            {$L C:\abs\gdi32.def}
            {$L helper.obj}
            {$L+}
            {$L-,R+}
            {$IFDEF NEVER} {$L missing.obj} {$ENDIF}
            implementation
            end.
            """, fs);

        Assert.Empty(diagnostics);
        Assert.Equal([@"C:\p\helper.obj", @"C:\p\plain.obj"], inputs.Objects);
        Assert.Equal([@"C:\p\defs\my lib.def", @"C:\abs\gdi32.def"], inputs.Defs);
        Assert.Empty(inputs.Dlls);
    }

    [Fact]
    [Trait("Req", "CMP-625")]
    public void LinkDirective_InAnIncludeFile_IsResolvedAgainstTheIncludesDirectory()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\inc\links.inc", "{$L native.obj}")
            .Add(@"C:\p\inc\native.obj", "");

        var (inputs, diagnostics) = Collect("unit U; interface {$I inc\\links.inc} implementation end.", fs);

        Assert.Empty(diagnostics);
        Assert.Equal([@"C:\p\inc\native.obj"], inputs.Objects);
    }

    [Fact]
    [Trait("Req", "CMP-625")]
    public void LinkDirective_MissingFile_IsE0002AtTheDirective()
    {
        var (inputs, diagnostics) = Collect("unit U;\ninterface\n  {$L gone.obj}\nimplementation\nend.", new InMemoryFileSystem());

        Assert.Empty(inputs.Objects);
        Assert.Equal([@"C:\p\U.pas(3,3): error E0002: File not found: 'C:\p\gone.obj'"], diagnostics.Select(d => d.ToText()));
    }

    [Fact]
    [Trait("Req", "CMP-625")]
    public void Compilation_ReportsAMissingLinkFileOfAUsedUnit()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", "program P; uses U; begin end.")
            .Add(@"C:\p\U.pas", "unit U;\n{$L lost.def}\ninterface\nimplementation\nend.")
            .Add(@"C:\rtl\System.pas", File.ReadAllText(Path.Combine(MiniRtlCompilesTests.MiniRtlDirectory, "System.pas")));
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal([@"C:\p\U.pas(2,1): error E0002: File not found: 'C:\p\lost.def'"], result.Diagnostics.Select(d => d.ToText()));
    }
}
