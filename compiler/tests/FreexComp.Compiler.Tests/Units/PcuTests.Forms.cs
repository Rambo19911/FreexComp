using System.Text.Json.Nodes;
using FreexComp.Compiler.Tests.Emit;
using FreexComp.Compiler.Units;

namespace FreexComp.Compiler.Tests.Units;

public sealed partial class PcuTests
{
    /// <summary>
    /// C-343 (CMP-1381): the classes whose forms a unit embeds are recorded as the key <c>forms</c> (declared casing, directive
    /// order) — only when there are any, so <c>formatVersion</c> stays 1 and the file of a unit without forms is what it was — and
    /// survive write → read → write; a <c>.pcu</c> without the key reads as "no forms".
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1381")]
    public void FormsKey_WrittenOnlyWhenNotEmpty_RoundTrips()
    {
        var withForms = UnitU.Replace("implementation", """
            type
              TSecondForm = class
              end;
              TFirstForm = class
              end;
            implementation
            {$FORM Second.form}
            {$FORM First.form}
            """, StringComparison.Ordinal);
        Assert.NotEqual(UnitU, withForms);
        var sources = Files(u: withForms, program: PcuImportTests.ImportingProgram)
            .Add(@"C:\p\Second.form", "object Second: TSECONDFORM\nend\n").Add(@"C:\p\First.form", "object First: tfirstform\nend\n");
        var original = Pcus(sources);

        var u = (JsonObject)JsonNode.Parse(original["U"])!;
        Assert.Equal(1, (int)u["formatVersion"]!);
        Assert.Equal(["TSecondForm", "TFirstForm"], u["forms"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal(["TSecondForm", "TFirstForm"], PcuReader.Forms(u));

        // a unit without forms: the key is absent and reads as no forms
        var plain = (JsonObject)JsonNode.Parse(original["V"])!;
        Assert.Null(plain["forms"]);
        Assert.Empty(PcuReader.Forms(plain));
        Assert.DoesNotContain("\"forms\"", Pcus(Files())["U"], StringComparison.Ordinal);

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
