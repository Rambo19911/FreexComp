using System.Text;
using FreexComp.Designer.Layout;
using FreexComp.Ide.TestSupport;
using static FreexComp.Designer.Tests.Layout.LayoutHarness;

namespace FreexComp.Designer.Tests.Layout;

/// <summary>The shared corpus and the performance budget (D-033).</summary>
public sealed partial class LayoutEngineTests
{
    /// <summary>The <c>Enn-&lt;slug&gt;</c> names of the shared corpus <c>tests/layout-corpus/</c> (layout.md §15).</summary>
    public static TheoryData<string> CorpusCases()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(CorpusFolder, "*.form").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileNameWithoutExtension(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-055")]
    [Trait("Req", "UIF-332")]
    public void ExpectedDumpsMatch(string caseName)
    {
        var basePath = Path.Combine(CorpusFolder, caseName);
        var form = Parse(File.ReadAllText(basePath + ".form"), caseName + ".form");
        var stepsPath = basePath + ".steps";
        var steps = File.Exists(stepsPath) ? File.ReadAllText(stepsPath) : "dump\n";
        var results = Engine.ComputeWithSteps(form, steps, LayoutMode.RunTime);

        var expected = LayoutDump.ParseDocuments(File.ReadAllText(basePath + ".expected.json"));
        Assert.Equal(expected.Count, results.Count);
        var differences = new List<string>();
        for (var i = 0; i < expected.Count; i++)
        {
            var actual = LayoutDump.Parse(LayoutDump.Serialize([results[i].ToDump()]));
            differences.AddRange(LayoutDump.Compare(actual, expected[i]).Select(d => $"dump {i + 1}: {d}"));
        }

        Assert.True(differences.Count == 0, caseName + "\n" + string.Join("\n", differences));
    }

    /// <summary>Spec budget for computing a 5 000-control form (DSG-LAY-035), for optimized builds.</summary>
    private const double ComputeBudgetMilliseconds = 20;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Category", "Performance")]
    [Trait("Req", "DSG-LAY-035")]
    public void Computes5000ControlsWithinBudget()
    {
        // 50 containers (aligned, anchored, nested one level) with 99 children each = 5 000 controls.
        var body = new StringBuilder();
        string[] aligns = ["alTop", "alLeft", "alBottom", "alRight", "alNone"];
        for (var p = 0; p < 50; p++)
        {
            var children = new StringBuilder();
            for (var c = 0; c < 99; c++)
            {
                var props = (c % 4) switch
                {
                    0 => "Anchors = [akLeft, akTop, akRight]",
                    1 => "Anchors = [akRight, akBottom]",
                    2 => "Align = alTop",
                    _ => "Anchors = []",
                };
                children.Append(Obj($"C{p}_{c}", c % 7 == 0 ? "TEdit" : "TButton", c % 30, c * 3 % 90, 40, 20, props));
            }

            body.Append(Obj($"P{p}", "TPanel", p * 7 % 300, p * 5 % 200, 120, 90, $"Align = {aligns[p % aligns.Length]}",
                children.ToString()));
        }

        var form = Form(1200, 900, body.ToString());
        Assert.Equal(5001, form.AllObjects().Count());
        var engine = Engine;

        var placed = false;
        var fastest = PerformanceBudget.MinThreadCpu(() => placed = engine.Compute(form).HasBounds(form.FindByName("C49_98")!));

        Assert.True(placed);
        var budget = PerformanceBudget.Of(ComputeBudgetMilliseconds, typeof(LayoutEngine).Assembly);
        Assert.True(budget.IsMetBy(fastest), budget.Describe("Compute of 5 000 controls took", fastest));
    }
}
