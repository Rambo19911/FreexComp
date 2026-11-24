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
    /// Started normally, every windowed control of <c>AlignForm</c> in the dump has its own child window of the form, matched
    /// by position in <c>Controls[]</c> order (child windows back to front, UIF-322) and by window class (U-131), whose
    /// <c>RectInParent</c> equals the dump bounds scaled by edges (UIF-280) exactly; WM_CLOSE ends the program with exit
    /// code 0 and an empty stderr. At 96 DPI the expectation is the dump itself. At another DPI the run-time realign after
    /// load scaling (UIF-283) places the two controls next to <c>RightPanel</c>'s scaled margins (<c>MulDiv(3, DPI, 96)</c>)
    /// by the §4 arithmetic, so <c>RightPanel</c> and <c>Memo1</c> are computed by that arithmetic from the edge-scaled
    /// sizes, again exactly (no tolerance).
    /// </summary>
    [Fact]
    [Trait("Category", "Foreground")]
    [Trait("Req", "UIF-010")]                // the real windows sit where the formulas put them
    [Trait("Req", "UIF-012")]
    [Trait("Req", "UIF-280")]
    [Trait("Req", "UIF-283")]
    public void LayoutGallery_RunsAndCloses()
    {
        Harness.RequireDesktop();
        using var json = JsonDocument.Parse(Dump());
        var dumped = json.RootElement.GetProperty("forms")[0].GetProperty("controls").EnumerateArray()
            .Where(c => !Graphic.Contains(c.GetProperty("class").GetString()))
            .Select(c => new Windowed(c.GetProperty("class").GetString()!, c.GetProperty("name").GetString()!,
                (c.GetProperty("left").GetInt32(), c.GetProperty("top").GetInt32(), c.GetProperty("width").GetInt32(), c.GetProperty("height").GetInt32())))
            .ToList();
        Assert.Equal(["ToolBar", "StatusBar", "SidePanel", "RightPanel", "Memo1"], dumped.Select(c => c.Name));

        using var app = Gui.StartGui(Gui.BuildSample(SampleDirectory), [], new Dictionary<string, string>());
        var form = Gui.WaitForMainWindow(app, "PN.TForm", "Align");
        Assert.True(form != IntPtr.Zero, "AlignForm window not found\n" + app.StdErr);
        Gui.WaitIdle(app);
        var expected = Expected(dumped, Gui.Dpi(form));
        var children = Gui.Descendants(form).Where(h => Gui.ParentOf(h) == form).Reverse().ToList();       // back to front
        string Actual() => string.Join("\n", children.Select(h => $"  {Gui.ClassName(h)} {Gui.RectInParent(h)}"));

        Assert.True(children.Count == expected.Count, $"{children.Count} child windows, expected {expected.Count}\n{Actual()}");
        for (var i = 0; i < expected.Count; i++)
        {
            var c = expected[i];
            Assert.True(string.Equals(WindowClass(c.Class), Gui.ClassName(children[i]), StringComparison.OrdinalIgnoreCase),
                $"{c.Name}: child window {i} has class {Gui.ClassName(children[i])}, expected {WindowClass(c.Class)}\n{Actual()}");
            Assert.True(c.Bounds == Gui.RectInParent(children[i]),
                $"{c.Name} at {Gui.Dpi(form)} DPI: expected {c.Bounds}, actual {Gui.RectInParent(children[i])}\n{Actual()}");
        }

        Gui.Post(form, Gui.WM_CLOSE);
        Assert.True(app.WaitForExit(), app.ExitFailure("Gallery.exe, after WM_CLOSE,"));
        Assert.Equal(0, app.Process.ExitCode);
        Assert.Equal("", app.StdErr);
    }

    /// <summary>
    /// U-131: the expectation of <see cref="LayoutGallery_RunsAndCloses"/> at 96 DPI is the dump itself (so a 1 px offset is
    /// a difference), the window class of each control is distinct from its neighbours' where it must be (a swapped
    /// control fails the class check), and the arithmetic at 120 DPI gives the hand-computed values.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-280")]
    public void LayoutGallery_ExpectationIsExact()
    {
        List<Windowed> dump =
        [
            new("TToolBar", "ToolBar", (0, 0, 640, 29)), new("TStatusBar", "StatusBar", (0, 381, 640, 19)),
            new("TPanel", "SidePanel", (0, 29, 150, 352)), new("TPanel", "RightPanel", (537, 32, 100, 346)),
            new("TMemo", "Memo1", (153, 29, 381, 352)),
        ];

        Assert.Equal(dump, Expected(dump, 96));
        Assert.NotEqual(Expected(dump, 96), [.. dump.Select(c => c.Name == "Memo1" ? c with { Bounds = (154, 29, 381, 352) } : c)]);
        Assert.Equal(["PN.TToolBar", "msctls_statusbar32", "PN.TPanel", "PN.TPanel", "EDIT"], dump.Select(c => WindowClass(c.Class)));
        var at120 = Expected(dump, 120);
        Assert.Equal((0, 0, 800, 36), at120[0].Bounds);
        Assert.Equal((671, 40, 125, 432), at120[3].Bounds);                     // L = 800 - 4 - 125, T = 36 + 4, H = 476 - 36 - 8
        Assert.Equal((191, 36, 476, 440), at120[4].Bounds);                     // L = E(153), W = 671 - 4 - 191
    }

    private static readonly string[] Graphic = ["TToolButton", "TSplitter", "TLabel", "TImage", "TShape", "TBevel"];

    internal sealed record Windowed(string Class, string Name, (int Left, int Top, int Width, int Height) Bounds);

    /// <summary>The window class of a dumped class: native subclasses keep the native class, own classes are <c>PN.&lt;ClassName&gt;</c>.</summary>
    internal static string WindowClass(string dumpClass) => dumpClass switch
    {
        "TMemo" or "TEdit" => "EDIT",
        "TStatusBar" => "msctls_statusbar32",
        _ => "PN." + dumpClass,
    };

    /// <summary>The bounds of the windowed <c>AlignForm</c> controls at <paramref name="dpi"/> (see <see cref="LayoutGallery_RunsAndCloses"/>).</summary>
    internal static List<Windowed> Expected(IReadOnlyList<Windowed> dump, int dpi)
    {
        int E(int v) => Gui.Px(v, dpi);
        var scaled = dump.Select(c => c with { Bounds = (E(c.Bounds.Left), E(c.Bounds.Top), E(c.Bounds.Left + c.Bounds.Width) - E(c.Bounds.Left),
            E(c.Bounds.Top + c.Bounds.Height) - E(c.Bounds.Top)) }).ToList();
        if (dpi == 96)
        {
            return scaled;
        }

        // §4 arithmetic for TAlignForm with the scaled sizes and margins: R = (E(153), E(29), E(640), E(381)) after the
        // tool bar, status bar, side panel and splitter (all edge-scaled exactly); RightPanel alRight with margins m.
        var m = E(3);
        Windowed Named(string name) => scaled.Single(c => c.Name == name);
        var top = Named("ToolBar").Bounds.Top + Named("ToolBar").Bounds.Height;
        var bottom = Named("StatusBar").Bounds.Top;
        var left = E(153);
        var right = E(640);
        var w = Named("RightPanel").Bounds.Width;
        var rightPanel = (right - m - w, top + m, w, bottom - top - 2 * m);
        var memo = (left, top, right - (w + 2 * m) - left, bottom - top);
        return [.. scaled.Select(c => c.Name switch
        {
            "RightPanel" => c with { Bounds = rightPanel },
            "Memo1" => c with { Bounds = memo },
            _ => c,
        })];
    }

    /// <summary>The dump of a normal start-up path (Gallery.exe --dump-layout), text of the file.</summary>
    internal string Dump()
    {
        var exe = Gui.BuildSample(SampleDirectory);
        var dump = Path.Combine(temp, "gallery.json");
        using var app = Gui.StartGui(exe, ["--dump-layout", dump], new Dictionary<string, string>());
        Assert.True(app.WaitForExit(), app.ExitFailure("Gallery.exe --dump-layout"));
        Assert.True(app.Process.ExitCode == 0, $"exit code {app.Process.ExitCode}\n--- stderr\n{app.StdErr}");
        return File.ReadAllText(dump, Encoding.UTF8);
    }
}
