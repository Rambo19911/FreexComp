using FreexComp.Compiler;

namespace FreexComp.Build.Tests;

/// <summary>
/// C-350: the form resources of a program or library are keyed by the upper-cased class name (ABI-150), so two units of it that
/// embed a form of one class name would leave one form loaded in place of the other.
/// </summary>
public sealed partial class BuildEngineTests
{
    /// <summary>A unit declaring <paramref name="form"/> whose form file <c>&lt;unit&gt;.form</c> it embeds; the directive is line 7.</summary>
    private static string FormUnit(string unit, string form) =>
        $"unit {unit};\ninterface\ntype\n  {form} = class\n  end;\nimplementation\n{{$FORM {unit}.form}}\nend.\n";

    /// <summary>
    /// Two units of the program (or library) embed a form of class <c>TForm1</c> (the second spelled in other case: the resource
    /// name is ASCII case-insensitive): E3107 at the second unit's directive, nothing is linked. Another class name in the second
    /// unit links.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1381")]
    public async Task FormClassEmbeddedByTwoUnits_E3107(bool library)
    {
        var w = library
            ? new BuildWorkspace("library Hello; uses UA, UB; begin end.", BuildWorkspace.ProjectJson("Hello", extra: "\"appType\": \"library\""))
            : new BuildWorkspace("program Hello; uses UA, UB; begin end.");
        w.Fs.Add(@"C:\p\UA.pas", FormUnit("UA", "TForm1")).Add(@"C:\p\UA.form", "object Form1: TForm1\nend\n");
        w.Fs.Add(@"C:\p\UB.pas", FormUnit("UB", "TFORM1")).Add(@"C:\p\UB.form", "object Form1: TFORM1\nend\n");

        var (result, log) = await w.RunAsync(w.Request());

        Assert.False(result.Success, string.Join("\n", log.Lines));
        Assert.Equal([@"C:\p\UB.pas(7,1): error E3107: Form class 'TFORM1' is embedded by units 'UA' and 'UB'"],
            log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToText()));
        Assert.DoesNotContain("lld-link.exe", w.Tools.Calls);

        w.Fs.Add(@"C:\p\UB.pas", FormUnit("UB", "TForm2")).Add(@"C:\p\UB.form", "object Form2: TForm2\nend\n");
        var (other, otherLog) = await w.RunAsync(w.Request());

        Assert.True(other.Success, string.Join("\n", otherLog.Lines));
        Assert.Contains("lld-link.exe", w.Tools.Calls);
    }
}
