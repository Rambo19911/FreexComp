using FreexComp.Compiler.Targets;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Toolchain;

/// <summary>targets.md CMP-1023 (task C-283): the import-definition lookup tries the target's sub-directory before the flat folder.</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Unit")]
public sealed class ImportLibraryPlanTests
{
    private const string RtlSrc = @"C:\repo\rtl\src";
    private const string Flat = @"C:\repo\rtl\winapi\def";
    private static readonly Location Main = Location.ForFile(@"C:\p\P.pas");

    private static string[] Planned(InMemoryFileSystem fs, TargetInfo? target, params string[] dlls)
    {
        var unit = new LinkUnit(@"C:\p", new UnitLinkInputs([.. dlls], [], []));
        var (libraries, errors) = target is null
            ? ImportLibraryPlan.Create(fs, [unit], @"C:\rtl-sys", RtlSrc, Main)
            : ImportLibraryPlan.Create(fs, [unit], @"C:\rtl-sys", RtlSrc, Main, target);
        Assert.Empty(errors);
        return [.. libraries.Select(l => l.Dll + " <- " + l.Definition)];
    }

    [Fact]
    [Trait("Req", "CMP-1023")]
    public void TargetDefSubdirectoryWinsOverFlatFolder()
    {
        var fake = TargetInfo.Win64 with { Id = "fake32", ImportDefSubdirectory = "fake32" };
        var fs = new InMemoryFileSystem()
            .Add(Flat + @"\kernel32.def", "flat")
            .Add(Flat + @"\ntdll.def", "flat")
            .Add(Flat + @"\user32.def", "flat")
            .Add(Flat + @"\fake32\user32.def", "decorated")
            .Add(Flat + @"\fake32\kernel32.def", "decorated");

        // the target's sub-directory first, the flat folder for what it does not hold
        Assert.Equal(
            [$@"kernel32.dll <- {Flat}\fake32\kernel32.def", $@"ntdll.dll <- {Flat}\ntdll.def", $@"user32.dll <- {Flat}\fake32\user32.def"],
            Planned(fs, fake, "user32.dll"));

        // the baseline target has no sub-directory on disk: skipped silently, every definition from the flat folder as before
        string[] flat = [$@"kernel32.dll <- {Flat}\kernel32.def", $@"ntdll.dll <- {Flat}\ntdll.def", $@"user32.dll <- {Flat}\user32.def"];
        Assert.Equal(flat, Planned(fs, TargetInfo.Win64, "user32.dll"));
        Assert.Equal(flat, Planned(fs, null, "user32.dll"));

        // … and would win there too once it exists
        fs.Add(Flat + @"\win64\user32.def", "x64");
        Assert.Equal($@"user32.dll <- {Flat}\win64\user32.def", Planned(fs, TargetInfo.Win64, "user32.dll")[2]);

        // the earlier steps of CMP-620 are unchanged: the declaring unit's directory still wins over both
        fs.Add(@"C:\p\user32.def", "unit");
        Assert.Equal(@"user32.dll <- C:\p\user32.def", Planned(fs, fake, "user32.dll")[2]);

        // not found anywhere → E4006, as before
        var (path, diagnostic) = DllToolInvoker.FindDefinition(fs, "gdi32.dll", [], [@"C:\p"], RtlSrc, Main, fake);
        Assert.Null(path);
        Assert.Equal("E4006", diagnostic!.Code);
    }
}
