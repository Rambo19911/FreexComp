using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Tests.Themes;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed partial class IconTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-TOK-040")]
    [Trait("Req", "UI-TOK-041")]
    public void EveryCatalogIconExists()
    {
        var spec = SpecIcons();
        Assert.True(spec.Count >= 60, $"icon catalog not read: {spec.Count}");

        foreach (var (name, token) in spec)
        {
            var icon = IconCatalog.Get(name);

            Assert.NotEmpty(icon.Layers);
            Assert.Equal(token, icon.ColorToken);
            foreach (var layer in icon.Layers)
            {
                var bounds = layer.Geometry.Bounds;
                Assert.True(bounds.Width > 0 || bounds.Height > 0, $"{name}: empty geometry");
                Assert.True(bounds.X >= -0.01 && bounds.Y >= -0.01 && bounds.Right <= 16.01 && bounds.Bottom <= 16.01, $"{name}: {bounds} outside the 16 x 16 grid");
                Assert.True(Application.Current!.TryFindResource(IconCatalog.BrushKey(layer.ColorToken), Application.Current!.ActualThemeVariant, out _), $"{name}: no brush for {layer.ColorToken}");
            }
        }

        Assert.Equal(spec.Keys.Order(StringComparer.Ordinal), IconCatalog.Names.Order(StringComparer.Ordinal)); // no unlisted files
        Assert.Contains(IconCatalog.Get("bring-front").Layers, l => l.IsFilled && l.ColorToken == "accent.default");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-042")]
    public void IconBuildsVectorDrawingWithTheGivenBrushes()
    {
        var image = IconCatalog.CreateImage("run", token => token == "status.success" ? Brushes.Green : Brushes.Black);

        var drawing = Assert.IsType<DrawingGroup>(image.Drawing);
        var children = drawing.Children.OfType<GeometryDrawing>().ToList();
        Assert.Null(children[0].Pen); // invisible 16 x 16 frame
        var stroke = Assert.Single(children.Skip(1));
        Assert.Same(Brushes.Green, stroke.Pen!.Brush);
        Assert.Equal(1.25, stroke.Pen.Thickness);
        Assert.Null(stroke.Brush); // outline only
        Assert.Equal(new Size(16, 16), image.Size);
        Assert.False(IconCatalog.TryGet("no-such-icon", out _));
        Assert.Throws<KeyNotFoundException>(() => IconCatalog.Get("no-such-icon"));
    }

    /// <summary>
    /// I-290 (found by I-287): the cut-out exclamation mark of <c>warning-corner</c> is readable where the icon is used,
    /// 10 × 10 DIP (UI-TW-104): every edge of the bar and of the dot lies on the device-pixel grid at 100 % and 200 %
    /// (and the vertical edges also at 150 %), so that those pixels are drawn fully in <c>bg.panel</c> and not as a faint
    /// blend; the bar is at least 2 × 2 device pixels at 100 %, the dot at least 2 × 1, with a full pixel row between them.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-104")]
    [Trait("Req", "UI-TOK-061")]
    public void WarningCorner_CutOutIsOnTheDevicePixelGridAtItsSize()
    {
        const double size = 10; // DIP, UI-TW-104
        var cutOuts = IconCatalog.Get("warning-corner").Layers.Where(l => l.ColorToken == "bg.panel")
            .Select(l => l.Geometry.Bounds).OrderBy(b => b.Y).ToList();
        Assert.Equal(2, cutOuts.Count); // the bar, then the dot
        var problems = new List<string>();
        void OnGrid(double unit, double scale, string what)
        {
            var px = unit * size / 16 * scale;
            if (Math.Abs(px - Math.Round(px)) > 0.01)
                problems.Add(FormattableString.Invariant($"{what} at {unit} of 16 is {px:0.###} device pixels at {scale:0.#}x"));
        }

        foreach (var (box, name) in cutOuts.Zip((string[])["bar", "dot"]))
        {
            foreach (var scale in (double[])[1.0, 2.0])
            {
                OnGrid(box.Left, scale, name + " left");
                OnGrid(box.Right, scale, name + " right");
                OnGrid(box.Top, scale, name + " top");
                OnGrid(box.Bottom, scale, name + " bottom");
            }

            OnGrid(box.Left, 1.5, name + " left");
            OnGrid(box.Right, 1.5, name + " right");
        }

        double Px(double units) => units * size / 16;
        var (bar, dot) = (cutOuts[0], cutOuts[1]);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.True(Px(bar.Width) >= 1.99 && Px(bar.Height) >= 1.99, $"bar {bar}");
        Assert.True(Px(dot.Width) >= 1.99 && Px(dot.Height) >= 0.99, $"dot {dot}");
        Assert.True(Px(dot.Top - bar.Bottom) >= 0.99, $"bar {bar} and dot {dot} run together");
        Assert.Equal(8.0, (bar.Left + bar.Right) / 2, 3); // centred under the apex
    }

    /// <summary>
    /// I-223 (tool-windows.md §10 and §13, toolbars.md §4): the tool bars of Breakpoints and Watches show their own icons —
    /// <c>plus</c>, <c>x</c>, <c>x-all</c>, <c>check-all</c>, <c>uncheck-all</c> and <c>pencil</c> — instead of the nearest
    /// catalogue icons that stood in for them; each is an outline drawing in <c>text.primary</c> on the 16 grid.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-008")]
    [Trait("Req", "UI-TOK-040")]
    [Trait("Req", "UI-TW-1107")]
    public async Task DebugToolBarsShowTheirOwnIcons()
    {
        await using var h = await Infrastructure.DebugScenario.StartAsync();
        string Icon(string tool, string command) =>
            h.Find<Button>($"Tool.{tool}.ToolBar.{command}").GetVisualDescendants().OfType<IdeIcon>().Single().IconName!;

        Assert.True(h.Services.ToolWindows.Show(Core.ViewModels.BreakpointsViewModel.ToolName, focus: false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(["plus", "x", "x-all", "check-all", "uncheck-all"],
            ((string[])["run.addBreakpoint", "breakpoints.delete", "run.deleteAllBreakpoints", "breakpoints.enableAll", "breakpoints.disableAll"]).Select(c => Icon("Breakpoints", c)));
        Assert.True(h.Services.ToolWindows.Show(Core.ViewModels.WatchesViewModel.ToolName, focus: false));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(["plus", "pencil", "x", "x-all"],
            ((string[])["watches.add", "watches.edit", "watches.delete", "watches.deleteAll"]).Select(c => Icon("Watches", c)));

        foreach (var name in (string[])["plus", "x", "x-all", "check-all", "uncheck-all", "pencil"])
        {
            var icon = IconCatalog.Get(name);
            Assert.Equal("text.primary", icon.ColorToken);
            Assert.All(icon.Layers, layer =>
            {
                var bounds = layer.Geometry.Bounds;
                Assert.True((bounds.Width > 0 || bounds.Height > 0) && bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= 16 && bounds.Bottom <= 16, $"{name}: {bounds}");
                Assert.False(layer.IsFilled, $"{name}: an outline drawing");
            });
        }

        Assert.NotEqual(IconCatalog.Get("x").Layers[0].Geometry.Bounds, IconCatalog.Get("close").Layers[0].Geometry.Bounds); // 10 × 10, not the 8 × 8 close
    }

    /// <summary>toolbars.md §4: icon name → colour token (the brief's "; token" suffix, else <c>text.primary</c>).</summary>
    private static Dictionary<string, string> SpecIcons()
    {
        var rows = SpecTables.Read(SpecTables.DocPath("spec/ui/toolbars.md"), "4. Icon catalog");
        var icons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var token = TokenSuffix().Match(row["Drawing brief"]) is { Success: true } m ? m.Groups[1].Value : "text.primary";
            foreach (var name in row["Name"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                icons.Add(name, token);
            }
        }

        // Lead notes that add an icon to the §4 catalogue until it gets its table row.
        foreach (Match note in NoteAddition().Matches(File.ReadAllText(SpecTables.DocPath("spec/ui/toolbars.md"))))
        {
            icons.TryAdd(note.Groups["name"].Value, note.Groups["token"].Value);
        }

        return icons;
    }

    [GeneratedRegex(@";\s*([a-z]+\.[a-zA-Z]+)\s*$")]
    private static partial Regex TokenSuffix();

    [GeneratedRegex(@"^\*\*Lead note[^*]*\*\*.*?icon `(?<name>[a-z0-9-]+)` \([^)]*token `(?<token>[a-z]+\.[a-zA-Z]+)`\) is added to the §4 catalogue", RegexOptions.Multiline)]
    private static partial Regex NoteAddition();
}
