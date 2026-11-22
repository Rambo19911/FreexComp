using System.Text;
using System.Text.Json;

namespace FreexComp.UiFramework.Tests;

/// <summary>Group C tests of the sample <c>ui-framework/samples/layout-gallery</c> (<c>sample-apps.md</c> §4.1).</summary>
[Trait("Kind", "Native")]
[Trait("Category", "Toolchain")]
[Trait("Category", "Integration")]
[Collection(DesktopCollection.Name)]
public sealed class LayoutGalleryTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "gallery-" + Guid.NewGuid().ToString("N"));

    public LayoutGalleryTests() => Directory.CreateDirectory(temp);

    public static string SampleDirectory => Path.Combine(UiCorpus.AreaDirectory, "samples", "layout-gallery");

    public void Dispose() => Rtl.Tests.Harness.GoldenRunner.TryDelete(temp);

    /// <summary>The four forms in creation order equal the hand-written expectation (structurally, UIF-324).</summary>
    [Fact]
    [Trait("Req", "UIF-011")]                // IntrinsicForm: the label's stored width and the edits' stored heights survive loading
    [Trait("Req", "UIF-315")]
    [Trait("Req", "UIF-986")]
    [Trait("Req", "UIF-222")]
    [Trait("Req", "UIF-219")]
    [Trait("Req", "UIF-800")]
    public void LayoutGallery_DumpMatchesExpected()
    {
        var actual = Dump();
        var expected = File.ReadAllText(Path.Combine(SampleDirectory, "expected-layout.json"), Encoding.UTF8);
        Assert.True(LayoutHost.Compare(expected, actual) is null, $"{LayoutHost.Compare(expected, actual)}\n--- actual\n{actual}");
    }

    /// <summary>
    /// Started normally, every windowed control of <c>AlignForm</c> in the dump has its own child window of the form
    /// at the dump bounds scaled by edges (UIF-280); WM_CLOSE ends the program with exit code 0 and an empty stderr.
    /// </summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-010")]                // the real windows sit where the formulas put them
    [Trait("Req", "UIF-012")]
    [Trait("Req", "UIF-280")]
    public void LayoutGallery_RunsAndCloses()
    {
        Harness.RequireDesktop();
        string[] graphic = ["TToolButton", "TSplitter", "TLabel", "TImage", "TShape", "TBevel"];
        using var json = JsonDocument.Parse(Dump());
        var windowed = json.RootElement.GetProperty("forms")[0].GetProperty("controls").EnumerateArray()
            .Where(c => !graphic.Contains(c.GetProperty("class").GetString()))
            .Select(c => (Name: c.GetProperty("name").GetString()!, L: c.GetProperty("left").GetInt32(), T: c.GetProperty("top").GetInt32(),
                W: c.GetProperty("width").GetInt32(), H: c.GetProperty("height").GetInt32()))
            .ToList();
        Assert.Equal(["ToolBar", "StatusBar", "SidePanel", "RightPanel", "Memo1"], windowed.Select(c => c.Name));

        using var app = Gui.StartGui(Gui.BuildSample(SampleDirectory), [], new Dictionary<string, string>());
        var form = Gui.WaitForMainWindow(app, "PN.TForm", "Align");
        Assert.True(form != IntPtr.Zero, "AlignForm window not found\n" + app.StdErr);
        Gui.WaitIdle(app);
        var dpi = Gui.Dpi(form);
        int E(int v) => Gui.Px(v, dpi);
        // Exact at 96 DPI. At another DPI the run-time realign after scaling (UIF-283) works with scaled margins
        // (MulDiv(3, 120, 96) = 4, not 3.75), so an edge next to an AlignWithMargins control may differ from the
        // edge-scaled dump by 1 px (UIF-281's bound); the check allows exactly that per edge.
        var slack = dpi == 96 ? 0 : 1;
        bool Near((int L, int T, int W, int H) a, (int L, int T, int W, int H) b) =>
            Math.Abs(a.L - b.L) <= slack && Math.Abs(a.T - b.T) <= slack
            && Math.Abs(a.L + a.W - (b.L + b.W)) <= slack && Math.Abs(a.T + a.H - (b.T + b.H)) <= slack;
        var children = Gui.Descendants(form).Where(h => Gui.ParentOf(h) == form).ToList();
        var unmatched = new List<IntPtr>(children);
        foreach (var c in windowed)
        {
            var expected = (E(c.L), E(c.T), E(c.L + c.W) - E(c.L), E(c.T + c.H) - E(c.T));
            var match = unmatched.FirstOrDefault(h => Near(Gui.RectInParent(h), expected));
            Assert.True(match != IntPtr.Zero,
                $"{c.Name}: no child window at {expected}; child windows: {string.Join(", ", children.Select(h => $"{Gui.ClassName(h)} {Gui.RectInParent(h)}"))}");
            unmatched.Remove(match);
        }

        Assert.Empty(unmatched);
        Gui.Post(form, Gui.WM_CLOSE);
        Assert.True(app.WaitForExit(TimeSpan.FromSeconds(5)), "the program did not exit within 5 s of WM_CLOSE");
        Assert.Equal(0, app.Process.ExitCode);
        Assert.Equal("", app.StdErr);
    }

    /// <summary>The dump of a normal start-up path (Gallery.exe --dump-layout), text of the file.</summary>
    internal string Dump()
    {
        var exe = Gui.BuildSample(SampleDirectory);
        var dump = Path.Combine(temp, "gallery.json");
        using var app = Gui.StartGui(exe, ["--dump-layout", dump], new Dictionary<string, string>());
        Assert.True(app.WaitForExit(TimeSpan.FromSeconds(10)), "Gallery.exe --dump-layout did not exit within 10 s");
        Assert.True(app.Process.ExitCode == 0, $"exit code {app.Process.ExitCode}\n--- stderr\n{app.StdErr}");
        return File.ReadAllText(dump, Encoding.UTF8);
    }
}
