using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Tests.Binding;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Units;

/// <summary>
/// C-333: the resource inputs of one unit — the files named by its <c>{$R file}</c> / <c>{$RESOURCE file}</c> directives
/// (resource-compiler.md §6, CMP-1323…1327), gathered where the <c>{$L}</c> files are gathered.
/// </summary>
[Trait("Phase", "8")]
public sealed class UnitLinkInputsTests
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
    [Trait("Req", "CMP-1323")]
    [Trait("Req", "CMP-1325")]
    public void ResourceDirective_StarNoExtensionQuotedAndRelative()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\U.res", "")
            .Add(@"C:\p\U.rc", "")
            .Add(@"C:\p\plain.res", "")
            .Add(@"C:\p\res\My File.res", "")
            .Add(@"C:\shared\icons.rc", "")
            .Add(@"C:\abs\Big.RES", "")
            .Add(@"C:\p\sub\U.res", "")
            .Add(@"C:\p\inc\part.inc", "{$R *.res}{$R near.rc}")
            .Add(@"C:\p\inc\part.res", "")
            .Add(@"C:\p\inc\near.rc", "")
            .Add(@"C:\p\helper.obj", "");

        var (inputs, diagnostics) = Collect("""
            unit U;
            {$R *.res}
            {$R plain}
            {$RESOURCE 'res/My File.res'}
            interface
            {$R ..\shared\icons.rc}
            {$r C:\abs\Big.RES}
            {$R *.rc}
            {$R *}
            {$R sub/*.res}
            {$I inc\part.inc}
            {$R U.RES}
            {$L helper.obj}
            {$R+}
            {$R-,Q+}
            {$IFDEF NEVER} {$R missing.res} {$ENDIF}
            implementation
            end.
            """, fs);

        Assert.Empty(diagnostics);
        Assert.Equal(
            [@"C:\p\U.res", @"C:\p\plain.res", @"C:\p\res\My File.res", @"C:\shared\icons.rc", @"C:\abs\Big.RES", @"C:\p\U.rc", @"C:\p\sub\U.res",
                @"C:\p\inc\part.res", @"C:\p\inc\near.rc"],
            inputs.Resources);
        Assert.Equal([@"C:\p\helper.obj"], inputs.Objects);      // the two directives do not mix
        Assert.Empty(inputs.Defs);
        Assert.Empty(inputs.Dlls);
    }

    [Theory]
    [Trait("Req", "CMP-1324")]
    [InlineData("{$R gone.res}", @"C:\p\U.pas(3,3): error E0002: File not found: 'C:\p\gone.res'")]
    [InlineData("{$R gone}", @"C:\p\U.pas(3,3): error E0002: File not found: 'C:\p\gone.res'")]
    [InlineData("{$RESOURCE 'no such.rc'}", @"C:\p\U.pas(3,3): error E0002: File not found: 'C:\p\no such.rc'")]
    [InlineData("{$R *.res}", @"C:\p\U.pas(3,3): error E0002: File not found: 'C:\p\U.res'")]
    [InlineData("{$R there.bin}", @"C:\p\U.pas(3,3): error E1206: Invalid directive argument 'there.bin'")]              // another extension
    [InlineData("{$R there.res.txt}", @"C:\p\U.pas(3,3): error E1206: Invalid directive argument 'there.res.txt'")]
    [InlineData("{$RESOURCE}", @"C:\p\U.pas(3,3): error E1206: Invalid directive argument ''")]                          // an empty name
    [InlineData("{$RESOURCE ''}", @"C:\p\U.pas(3,3): error E1206: Invalid directive argument ''''")]
    [InlineData("{$R there.res there.rc}", @"C:\p\U.pas(3,3): error E1206: Invalid directive argument 'there.res there.rc'")]   // the two-file form
    [InlineData("{$R 'there.res' there.rc}", @"C:\p\U.pas(3,3): error E1206: Invalid directive argument ''there.res' there.rc'")]
    public void ResourceDirective_MissingFile_E0002_BadName_E1206(string directive, string expected)
    {
        var fs = new InMemoryFileSystem().Add(@"C:\p\there.bin", "").Add(@"C:\p\there.res", "").Add(@"C:\p\there.rc", "").Add(@"C:\p\there.res.txt", "");

        var (inputs, diagnostics) = Collect("unit U;\ninterface\n  " + directive + "\nimplementation\nend.", fs);

        Assert.Empty(inputs.Resources);
        Assert.Equal([expected], diagnostics.Select(d => d.ToText()));
    }

    /// <summary>CMP-1327: a compilation reports the directive's errors where it reports those of <c>{$L}</c> — at the check stage, in a used unit.</summary>
    [Fact]
    [Trait("Req", "CMP-1327")]
    public void Compilation_ReportsResourceDirectiveErrorsOfAUsedUnit_AtTheCheckStage()
    {
        var fs = new InMemoryFileSystem()
            .Add(@"C:\p\P.pas", "program P; uses U; begin end.")
            .Add(@"C:\p\U.pas", "unit U;\n{$R lost.res}\n{$L lost.obj}\ninterface\n{$R notes.txt}\nimplementation\nend.")
            .AddSystem();
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };

        var result = Compilation.Create(options, [], fs).Emit(new EmitOptions { Stage = EmitStage.Check }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(
            [@"C:\p\U.pas(2,1): error E0002: File not found: 'C:\p\lost.res'",
                @"C:\p\U.pas(3,1): error E0002: File not found: 'C:\p\lost.obj'",
                @"C:\p\U.pas(5,1): error E1206: Invalid directive argument 'notes.txt'"],
            result.Diagnostics.Select(d => d.ToText()));
    }
}
