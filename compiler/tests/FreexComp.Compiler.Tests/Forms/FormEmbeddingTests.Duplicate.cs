namespace FreexComp.Compiler.Tests.Forms;

/// <summary>
/// C-350: the form resources of a program are keyed by the upper-cased class name (ABI-150), so two units of it may not embed a
/// form of the same class name (E3107, after the units' own diagnostics); a unit taken from its <c>.pcu</c> counts by its key
/// <c>forms</c> (CMP-1381) and, having no directive here, is reported at the main file (no position).
/// </summary>
public sealed partial class FormEmbeddingTests
{
    private static string FormUnit(string unit) =>
        $"unit {unit};\ninterface\ntype\n  TForm1 = class\n  end;\nimplementation\n{{$FORM {unit}.form}}\nend.\n";

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1381")]
    public void FormClassInTwoUnits_E3107_AtTheLaterDirective_OrAtTheMainFileForAPcu()
    {
        const string program = "program P;\nuses UB, UA;\nbegin end.\n";
        const string form = "object Form1: TForm1\nend\n";
        var options = new CompilationOptions { MainFile = @"C:\p\P.pas", RtlDirectory = @"C:\rtl", ObjectDirectory = @"C:\p\obj" };
        var sources = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).Add(@"C:\p\UA.pas", FormUnit("UA")).Add(@"C:\p\UA.form", form)
            .Add(@"C:\p\UB.pas", FormUnit("UB")).Add(@"C:\p\UB.form", form).AddSystem();
        string[] Check(InMemoryFileSystem fs, CompilationOptions o) =>
            Errors(Compilation.Create(o, [], fs).Emit(new EmitOptions { Stage = EmitStage.Ll }, TestContext.Current.CancellationToken));

        Assert.Equal(["UA.pas(7,1): error E3107: Form class 'TForm1' is embedded by units 'UB' and 'UA'"], Check(sources, options));

        // UA as .pcu only: built from a program that links it alone
        var single = new InMemoryFileSystem().Add(@"C:\p\P.pas", "program P;\nuses UA;\nbegin end.\n").Add(@"C:\p\UA.pas", FormUnit("UA"))
            .Add(@"C:\p\UA.form", form).AddSystem();
        var pcu = Compilation.Create(options, [], single).PcuTexts().Single(t => t.Unit == "UA").Text;
        var precompiled = new InMemoryFileSystem().Add(@"C:\p\P.pas", program).Add(@"C:\p\UB.pas", FormUnit("UB")).Add(@"C:\p\UB.form", form)
            .Add(@"C:\lib\UA.pcu", pcu).Add(@"C:\lib\UA.obj", "").AddSystem();

        Assert.Equal(["P.pas(0,0): error E3107: Form class 'TForm1' is embedded by units 'UB' and 'UA'"],
            Check(precompiled, options with { SearchPaths = [@"C:\lib"] }));
    }
}
