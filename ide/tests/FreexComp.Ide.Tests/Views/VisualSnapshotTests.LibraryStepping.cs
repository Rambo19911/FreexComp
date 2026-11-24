using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subjects of the library-stepping choice (F02-004, I-233).</summary>
public sealed partial class VisualSnapshotTests
{
    private const string LibrarySteppingCommand = "run.toggleLibraryStepping";

    private static MenuItem MenuItemOf(HeadlessHarness h, string automationId)
    {
        static IEnumerable<MenuItem> Descendants(IEnumerable<object?> items) =>
            items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(Descendants(i.Items.Cast<object?>())));
        return Descendants(h.Find<Menu>(AutomationIds.MenuBar).Items.Cast<object?>()).Single(i => AutomationIds.Get(i) == automationId);
    }

    /// <summary>
    /// UI-ED-146: <c>Step Into Library Source</c> is a check item of the Run menu, after Set Next Statement; it has no
    /// toolbar button. The main window (800 × 600) with the Run menu open is rendered in light and dark with the item unchecked and checked
    /// (<c>run-menu-library-stepping-unchecked</c>, <c>run-menu-library-stepping-checked</c>). Rules: the item is enabled
    /// with a project, its label is shown completely inside the menu, and the check mark is drawn left of the label when — and
    /// only when — the choice is on.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-ED-146")]
    [Trait("Req", "IDE-LIB-021")]
    public async Task RunMenu_LibrarySteppingCheckItem_RendersUncheckedAndChecked()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        string[] subjects = ["run-menu-library-stepping-unchecked", "run-menu-library-stepping-checked"];
        foreach (var subject in subjects)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var size = new Size(800, 600);
        Assert.Null(ToolBarDefinitions.IconOf(LibrarySteppingCommand)); // no toolbar button
        var files = new List<(string Subject, string Theme, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await DebugScenario.StartAsync(o => o.Settings["appearance.theme"] = theme);
            VisualSnapshot.Arrange(h.Window, size, 1.0);
            var run = MenuItemOf(h, AutomationIds.TopMenu("Run"));
            var item = MenuItemOf(h, AutomationIds.Menu(LibrarySteppingCommand));
            var siblings = run.Items.OfType<MenuItem>().ToList();
            Assert.Equal("run.setNextStatement", ((IdeCommand)siblings[siblings.IndexOf(item) - 1].Command!).Id);
            Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
            Assert.Equal("Step Into Librar_y Source", item.Header);

            var marks = new Dictionary<string, int>();
            foreach (var subject in subjects)
            {
                var isChecked = subject == subjects[1];
                if (isChecked)
                {
                    await h.ExecuteAsync(LibrarySteppingCommand);
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.Equal(isChecked, h.Services.Debug!.StepIntoLibrarySource);
                Assert.Equal(isChecked, item.IsChecked);
                run.Open();
                Dispatcher.UIThread.RunJobs();
                var popup = h.Window; // the menu is drawn on the window's overlay layer
                Assert.True(item.IsEffectivelyVisible && item.IsEffectivelyEnabled, $"{subject} {theme}: the item is not shown enabled");

                // The whole item and its label lie inside the menu.
                var label = item.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.Text?.Replace("_", "", StringComparison.Ordinal) == "Step Into Library Source");
                foreach (var control in (Control[])[item, label])
                {
                    var box = new Rect(control.TranslatePoint(default, popup)!.Value, control.Bounds.Size);
                    Assert.True(new Rect(popup.ClientSize).Contains(box), $"{subject} {theme}: {control} {box} leaves the menu {popup.ClientSize}");
                }

                Assert.True(label.DesiredSize.Width <= label.Bounds.Width + 0.5, $"{subject} {theme}: the label is cut");
                // What the item draws left of its label (it has no icon): nothing, or the check mark.
                var origin = item.TranslatePoint(default, popup)!.Value;
                var left = new Rect(origin, new Size(label.TranslatePoint(default, popup)!.Value.X - origin.X, item.Bounds.Height));
                marks[subject] = VisualSnapshot.PixelsOf(popup, left).Distinct().Count();
                files.Add((subject, theme, VisualSnapshot.Capture(h.Window, subject, theme)));
                run.Close();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(marks[subjects[0]] == 1, $"{theme}: something is drawn left of the unchecked item ({marks[subjects[0]]} colours)");
            Assert.True(marks[subjects[1]] >= 2, $"{theme}: no check mark is drawn");
        }

        foreach (var subject in subjects)
            Assert.Equal(2, Directory.GetFiles(VisualSnapshot.FolderOf(subject), "*.png").Length);
        foreach (var (_, _, file) in files)
        {
            Assert.Equal(((int)size.Width, (int)size.Height), VisualSnapshot.PngSize(file.Path));
            Assert.True(file.DistinctColors >= 16, $"{file.Path}: only {file.DistinctColors} colours — not a real rendering");
        }

        foreach (var light in files.Where(f => f.Theme == "light"))
        {
            var dark = files.Single(f => f.Theme == "dark" && f.Subject == light.Subject);
            Assert.True(dark.File.MeanLuminance + 64 < light.File.MeanLuminance, $"{dark.File.Path}: {dark.File.MeanLuminance} against {light.File.MeanLuminance}");
        }
    }
}
