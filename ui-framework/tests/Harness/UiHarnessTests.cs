using FreexComp.Rtl.Tests.Harness;

namespace FreexComp.UiFramework.Tests;

/// <summary>U-001: discovery, markers and the desktop check of the harness itself (the program run is <c>pascal/UiHarness_SelfTest</c>).</summary>
public sealed class UiHarnessTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));

    public UiHarnessTests() => Directory.CreateDirectory(temp);

    public void Dispose() => GoldenRunner.TryDelete(temp);

    [Fact]
    [Trait("Req", "UIF-1200")]
    public void UiHarness_DiscoversProgramsButNotSharedKits()
    {
        File.WriteAllText(Path.Combine(temp, "B_Two.pas"), "program B_Two; begin end.");
        File.WriteAllText(Path.Combine(temp, "A_One.pas"), "program A_One; begin end.");
        File.WriteAllText(Path.Combine(temp, "UiTestKit.pas"), "unit UiTestKit; interface implementation end.");

        var ids = UiCorpus.DiscoverPascal(temp).Select(c => c.Id);

        Assert.Equal(["pascal/A_One", "pascal/B_Two"], ids);
        Assert.Empty(UiCorpus.DiscoverPascal(Path.Combine(temp, "absent")));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void UiHarness_RealTreeHasSelfTestRow()
    {
        Assert.Contains(UiCorpus.Rows(4, desktop: false), r => r.Data == "pascal/UiHarness_SelfTest");
    }

    [Fact]
    [Trait("Req", "UIF-1200")]
    public void UiHarness_DiscoversLayoutCorpusWithSiblings()
    {
        File.WriteAllText(Path.Combine(temp, "E02-anchors.form"), "");
        File.WriteAllText(Path.Combine(temp, "E02-anchors.steps"), "");
        File.WriteAllText(Path.Combine(temp, "E01-align.form"), "");
        File.WriteAllText(Path.Combine(temp, "E01-align.expected.json"), "{}");

        var cases = UiCorpus.DiscoverLayout(temp);

        Assert.Equal(["layout/E01-align", "layout/E02-anchors"], cases.Select(c => c.Id));
        Assert.Null(cases[0].StepsPath);
        Assert.EndsWith("E01-align.expected.json", cases[0].ExpectedPath);
        Assert.EndsWith("E02-anchors.steps", cases[1].StepsPath);
        Assert.Null(cases[1].ExpectedPath);
        Assert.Empty(UiCorpus.DiscoverLayout(Path.Combine(temp, "absent")));
    }

    [Fact]
    [Trait("Req", "UIF-1209")]
    public void UiHarness_PhaseDefaultsToFourAndDesktopMarkerIsRead()
    {
        var plain = Write("P_Plain.pas", "// covers: UIF-500\nprogram P_Plain; begin end.");
        var late = Write("P_Late.pas", "// phase: 7\n// needs: desktop\nprogram P_Late; begin end.");
        var afterCode = Write("P_After.pas", "program P_After;\n// needs: desktop\nbegin end.");

        Assert.Equal(4, UiCorpus.PhaseOf(plain));
        Assert.Equal(7, UiCorpus.PhaseOf(late));
        Assert.False(UiCorpus.NeedsDesktop(plain));
        Assert.True(UiCorpus.NeedsDesktop(late));
        Assert.False(UiCorpus.NeedsDesktop(afterCode));
    }

    [Fact]
    [Trait("Req", "UIF-1201")]
    public void UiHarness_ProgramMustPutFrameworkSourceOnUnitPath()
    {
        var pascal = Path.Combine(temp, "ui-framework", "tests", "pascal");
        Directory.CreateDirectory(pascal);
        var without = new RtlCase("pascal/X", Path.Combine(pascal, "X.pas"));
        File.WriteAllText(without.SourcePath, "// covers: UIF-1\nprogram X; begin end.");

        Assert.Contains("// opc: -U ..\\..\\src", UiCorpus.CheckOptions(without));
        foreach (var real in UiCorpus.DiscoverPascal())
        {
            Assert.Null(UiCorpus.CheckOptions(real));
        }
    }

    [Fact]
    [Trait("Req", "UIF-1203")]
    public void UiHarness_RequireDesktopSkipsOnlyWithoutWindowStation()
    {
        var thrown = Record.Exception(Harness.RequireDesktop);

        if (Harness.HasInteractiveWindowStation())
        {
            Assert.Null(thrown);
        }
        else
        {
            Assert.NotNull(thrown);
            Assert.Contains(Harness.NoDesktopReason, thrown.Message);
        }
    }

    private RtlCase Write(string name, string text)
    {
        var path = Path.Combine(temp, name);
        File.WriteAllText(path, text);
        return new RtlCase("pascal/" + Path.GetFileNameWithoutExtension(name), path);
    }
}
