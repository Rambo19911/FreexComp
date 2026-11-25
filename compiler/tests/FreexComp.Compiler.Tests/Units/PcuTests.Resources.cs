using System.Text.Json.Nodes;
using FreexComp.Compiler.Tests.Emit;

namespace FreexComp.Compiler.Tests.Units;

public sealed partial class PcuTests
{
    /// <summary>
    /// C-333 (CMP-1325): the resource inputs of a unit are recorded as <c>linkInputs.resources</c> — only when there are any, so the
    /// file of a unit without the directive is what it was — and survive write → read → write.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1325")]
    public void RoundTrip_LinkInputsResources()
    {
        var withDirectives = UnitU.Replace("unit U;", "unit U;\n{$R *.res}\n{$RESOURCE 'data.rc'}\n{$R *.res}", StringComparison.Ordinal);
        Assert.NotEqual(UnitU, withDirectives);
        var sources = Files(u: withDirectives, program: PcuImportTests.ImportingProgram).Add(@"C:\p\U.res", "").Add(@"C:\p\data.rc", "");
        var original = Pcus(sources);

        var u = (JsonObject)JsonNode.Parse(original["U"])!["linkInputs"]!;
        Assert.Equal(["dlls", "objects", "defs", "resources"], u.Select(p => p.Key));
        Assert.Equal([@"C:\p\U.res", @"C:\p\data.rc"], u["resources"]!.AsArray().Select(v => v!.GetValue<string>()));

        // a unit without the directive: the key is absent
        Assert.Equal(["dlls", "objects", "defs"], ((JsonObject)JsonNode.Parse(original["V"])!["linkInputs"]!).Select(p => p.Key));
        Assert.Equal(["dlls", "objects", "defs"], ((JsonObject)JsonNode.Parse(Pcus(Files())["U"])!["linkInputs"]!).Select(p => p.Key));

        // the units as .pcu only: written again from what was read
        var fs = new InMemoryFileSystem().Add(@"C:\p\P.pas", PcuImportTests.ImportingProgram).AddSystem();
        foreach (var unit in new[] { "U", "V" })
        {
            fs.Add(Path.Combine(PcuImportTests.Lib, unit + ".pcu"), original[unit]).Add(Path.Combine(PcuImportTests.Lib, unit + ".obj"), "");
        }

        var again = Compilation.Create(Options() with { SearchPaths = [PcuImportTests.Lib] }, [], fs).PcuTexts().ToDictionary(t => t.Unit, t => t.Text);

        Assert.Equal(original["U"], again["U"]);
        Assert.Equal(original["V"], again["V"]);
    }
}
