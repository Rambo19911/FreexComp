using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>I-245: the full-path tooltips (UI-TW-111, UI-ED-075) and tooltips during a drag in the tree (UI-TW-119).</summary>
public sealed partial class ProjectManagerViewTests
{
    /// <summary>A project directory deep enough for its paths to need several tooltip lines; no spaces in it.</summary>
    internal const string DeepProjectDir =
        @"Workspace\SharedComponentsLibrary\DataAccessLayerImplementation\RepositoryPatternInfrastructure\CustomerRelationshipManagement\Hello\";

    internal static void DeepProject(HarnessOptions o)
    {
        o.InitialFiles[DeepProjectDir + "Hello.pproj"] = """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "lib/StrUtils2.pas" } ] }
            """;
        o.InitialFiles[DeepProjectDir + "Hello.pas"] = "program Hello;\nbegin\nend.\n";
        o.InitialFiles[DeepProjectDir + "Unit1.pas"] = "unit Unit1;\ninterface\nimplementation\nend.\n";
        o.InitialFiles[DeepProjectDir + @"lib\StrUtils2.pas"] = "unit StrUtils2;\ninterface\nimplementation\nend.\n";
    }

    /// <summary>The tooltip that is open in the window (tooltips are shown in the window's overlay layer here).</summary>
    internal static ToolTip OpenToolTip(HeadlessHarness h) => Assert.Single(h.Window.GetVisualDescendants().OfType<ToolTip>());

    /// <summary>The lines the text block shows, in order, without their line ends.</summary>
    internal static List<string> ShownLines(TextBlock block)
    {
        var text = block.Text ?? "";
        return [.. block.TextLayout.TextLines.Select(l =>
            text.Substring(l.FirstTextSourceIndex, Math.Min(l.Length, text.Length - l.FirstTextSourceIndex)).TrimEnd('\r', '\n'))];
    }

    /// <summary>
    /// Opens the tooltip of <paramref name="owner"/> and checks that it shows the whole <paramref name="path"/> broken
    /// only after path separators, inside the tooltip's maximum width (design-tokens.md §8: 480). Returns the lines.
    /// </summary>
    internal static List<string> AssertPathToolTip(HeadlessHarness h, Control owner, string path)
    {
        ToolTip.SetIsOpen(owner, true);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tip = OpenToolTip(h);
            Assert.Equal(480, tip.MaxWidth);
            Assert.True(tip.Bounds.Width <= 480, $"the tooltip is {tip.Bounds.Width} wide");
            var block = Assert.Single(tip.GetVisualDescendants().OfType<TextBlock>());
            var lines = ShownLines(block);
            Assert.True(lines.Count > 1, "the path needs more than one line: " + path);
            Assert.Equal(path, string.Concat(lines)); // the full path, nothing trimmed or added
            Assert.All(lines.Take(lines.Count - 1), line => Assert.True(line.EndsWith('\\'), $"line '{line}' does not end at a separator"));
            // The first line is filled, not the drive alone: the next folder would not have fit on any line but the last.
            var folders = path.Split('\\').Length - 1;
            Assert.True(lines.Count < folders, $"{lines.Count} lines for {folders} folders: {string.Join(" | ", lines)}");
            Assert.True(lines[0].Length > 3, $"first line '{lines[0]}'");
            Assert.All(block.TextLayout.TextLines, l => Assert.False(l.HasCollapsed));
            Assert.All(block.TextLayout.TextLines, l => Assert.True(l.Width <= block.Bounds.Width + 0.5, $"a line is {l.Width} wide in {block.Bounds.Width}"));
            return lines;
        }
        finally
        {
            ToolTip.SetIsOpen(owner, false);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// I-245 (a): a long path without spaces is broken only after path separators — never after the drive's colon or
    /// inside a name — within the tooltip's maximum width; the tooltip value stays the full path (UI-TW-111). The same
    /// holds for the other full-path tooltips, here the document tab (UI-ED-075) with its second line; a tooltip that
    /// is no path wraps at its spaces as before.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-111")]
    [Trait("Req", "UI-ED-075")]
    public async Task PathTooltipWrapsAtSeparators()
    {
        await using var h = await HeadlessHarness.StartAsync(DeepProject);
        var projectDir = Path.Combine(h.TempDir, DeepProjectDir);
        Assert.DoesNotContain(' ', projectDir);
        Assert.True(await h.Services.Projects.OpenAsync(projectDir + "Hello.pproj"));
        var pm = Pm(h);
        await h.WaitForAsync(() => !pm.IsCheckingMissingFiles, "missing-file check done");
        Assert.True(h.Services.ToolWindows.Show(Core.ViewModels.ProjectManagerViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();

        // File row, root row, folder row.
        foreach (var (key, path) in new[] { ("Unit1.pas", projectDir + "Unit1.pas"), ("$project", projectDir + "Hello.pproj"), ("lib/", projectDir + "lib") })
        {
            var row = h.Find<TreeViewItem>(Row + key);
            Assert.Equal(path, ToolTip.GetTip(row));
            var lines = AssertPathToolTip(h, row, path);
            Assert.StartsWith(Path.GetPathRoot(path)!, lines[0], StringComparison.Ordinal);
            Assert.NotEqual(Path.GetPathRoot(path)!.TrimEnd('\\'), lines[0]); // not "C:" alone
        }

        // The document tab: the same path tooltip.
        pm.Select(pm.Find("Unit1.pas")!);
        await pm.OpenSelectionAsync();
        Dispatcher.UIThread.RunJobs();
        var tab = h.Find<Control>("DocumentTab.Unit1.pas");
        Assert.Equal(projectDir + "Unit1.pas", ToolTip.GetTip(tab));
        AssertPathToolTip(h, tab, projectDir + "Unit1.pas");

        // A text that is no path still wraps at its spaces, and a short path stays on one line.
        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
        var probe = new Border { Width = 4, Height = 4 };
        ((Panel)tree.Parent!).Children.Add(probe);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var sentence = string.Join(' ', Enumerable.Repeat("several words that wrap", 6));
            foreach (var (text, wraps) in new[] { (sentence, true), (@"C:\p\Unit1.pas", false) })
            {
                ToolTip.SetTip(probe, text);
                ToolTip.SetIsOpen(probe, true);
                Dispatcher.UIThread.RunJobs();
                var lines = ShownLines(Assert.Single(OpenToolTip(h).GetVisualDescendants().OfType<TextBlock>()));
                Assert.Equal(wraps, lines.Count > 1);
                Assert.Equal(text, wraps ? string.Join(' ', lines.Select(l => l.TrimEnd())) : lines[0]);
                ToolTip.SetIsOpen(probe, false);
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            ((Panel)tree.Parent!).Children.Remove(probe);
        }
    }

    /// <summary>
    /// I-245 (b): a tooltip that is open when a drag starts is closed, and no row tooltip opens while rows are dragged
    /// (it would cover the drop target); the drop-target feedback is unaffected, and tooltips work again when the drag
    /// has ended. The rows get a show delay of 0 so that a tooltip opens with the pointer move itself.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-119")]
    [Trait("Req", "UI-TW-121")]
    public async Task TooltipsSuppressedDuringDrag()
    {
        await using var h = await HeadlessHarness.StartAsync(o => HelloProject(o));
        var pm = await OpenHelloAsync(h);
        Assert.True(h.Services.ToolWindows.Show(Core.ViewModels.ProjectManagerViewModel.ToolName, focus: true));
        Dispatcher.UIThread.RunJobs();
        var tree = h.Find<TreeView>("Tool.ProjectManager.Tree");
        List<TreeViewItem> Rows() => [.. tree.GetVisualDescendants().OfType<TreeViewItem>()];
        foreach (var item in Rows())
            ToolTip.SetShowDelay(item, 0);
        string[] OpenTips() => [.. Rows().Where(ToolTip.GetIsOpen).Select(i => ((Core.ViewModels.ProjectNodeViewModel)i.DataContext!).Key)];
        void Hover(Point point)
        {
            h.Window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
        }

        // Without a drag, the row under the pointer shows its tooltip.
        Hover(RowPoint(h, "lib/"));
        Assert.Equal(["lib/"], OpenTips());
        var start = RowPoint(h, "Unit1.pas");
        Hover(start);
        Assert.Equal(["Unit1.pas"], OpenTips());

        // Pressed; the tooltip of the row opens while the button is held (its delay has run out). The drag starts: closed.
        Press(h, start);
        ToolTip.SetIsOpen(h.Find<TreeViewItem>(Row + "Unit1.pas"), true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Unit1.pas"], OpenTips());
        Move(h, start + new Point(0, 8));
        Assert.Equal(["Unit1.pas"], pm.DraggedRows.Select(n => n.Key));
        Assert.Empty(OpenTips());
        Assert.Empty(h.Window.GetVisualDescendants().OfType<ToolTip>());

        // Over rows during the drag: drop-target feedback, no tooltip.
        foreach (var key in new[] { "lib/", "aaa/", "notes.md", "lib/" })
        {
            Move(h, RowPoint(h, key));
            Assert.Empty(OpenTips());
            Assert.Empty(h.Window.GetVisualDescendants().OfType<ToolTip>());
        }

        Assert.Equal(["lib/"], DropTargets(h));
        Assert.NotNull(h.TryFind<Control>(Adorner));

        // Escape cancels the drag: tooltips open again.
        h.Press("Escape");
        Release(h, RowPoint(h, "lib/"));
        Assert.Empty(pm.DraggedRows);
        Hover(RowPoint(h, "notes.md"));
        Hover(RowPoint(h, "lib/"));
        Assert.Equal(["lib/"], OpenTips());

        // A drag that ends with the release of the button (outside a valid target: nothing moves): the same.
        start = RowPoint(h, "notes.md");
        Hover(start);
        Press(h, start);
        Move(h, start + new Point(0, 8));
        Assert.Equal(["notes.md"], pm.DraggedRows.Select(n => n.Key));
        Move(h, RowPoint(h, "../Shared/Common.pas"));
        Assert.Empty(OpenTips());
        Release(h, RowPoint(h, "../Shared/Common.pas"));
        Assert.Empty(pm.DraggedRows);
        Assert.NotNull(pm.Find("notes.md"));
        Hover(RowPoint(h, "notes.md"));
        Hover(RowPoint(h, "lib/"));
        Assert.Equal(["lib/"], OpenTips());
    }
}
