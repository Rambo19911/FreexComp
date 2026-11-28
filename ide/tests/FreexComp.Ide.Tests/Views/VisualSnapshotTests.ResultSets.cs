using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>Visual subjects of the result sets of Search Results (F13-016a, I-354).</summary>
public sealed partial class VisualSnapshotTests
{
    /// <summary>
    /// The main window at 800 × 800 (a narrow bottom dock) and 1920 × 1040, light and dark, with two pinned References
    /// sets and a live Find in Files set in Search Results: <c>search-results-sets</c> (the live set shown) and
    /// <c>search-results-sets-pinned</c> (the first pinned set shown). Rules (UI-TW-615, UI-TW-616): the set strip is 26
    /// high and lies between the tool bar and the header; its three tabs are drawn completely inside the window, side
    /// by side, with untrimmed titles; a pinned tab draws its pin glyph, every tab the icon of its kind; the shown tab
    /// draws the 2 px accent line at its bottom edge and its close button, the others do not; the Keep Results button
    /// of the tool bar is drawn — enabled for the live set, disabled for a pinned one; nothing is clipped or overlaps.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "8")]
    [Trait("Req", "UI-TW-615")]
    [Trait("Req", "UI-TW-616")]
    public async Task SearchResultSets_RendersTheSetStripWithPinnedAndLiveSets_NoClippingOrOverlap()
    {
        if (!SkiaProcess.IsCurrent)
        {
            await SkiaProcess.RunThisTestAsync(typeof(VisualSnapshotTests));
            return;
        }

        const string live = "search-results-sets";
        const string pinned = "search-results-sets-pinned";
        string[] subjects = [live, pinned];
        Size[] sizes = [new(800, 800), new(1920, 1040)];
        foreach (var subject in subjects)
        {
            if (Directory.Exists(VisualSnapshot.FolderOf(subject)))
                Directory.Delete(VisualSnapshot.FolderOf(subject), recursive: true);
        }

        var defects = new List<(string Variant, string Defect)>();
        var files = new List<(string Subject, string Theme, Size Size, SnapshotFile File)>();
        foreach (var theme in Themes)
        {
            await using var h = await ReferencesViewTests.StartAsync(o => o.Settings["appearance.theme"] = theme);
            var results = h.Services.SearchResults;

            void Shoot(string subject, int shown, bool canPin)
            {
                foreach (var size in sizes)
                {
                    VisualSnapshot.Arrange(h.Window, size, 1.0);
                    h.Drag(AutomationIds.Splitter(AutomationIds.DockBottom), 0, -2000); // the tallest the bottom dock may be
                    var variant = $"{subject} {VisualSnapshot.FileName(theme, 1.0, size)}";
                    Assert.Equal(size, h.Window.ClientSize);
                    Rect In(Control control) => new(control.TranslatePoint(default, h.Window)!.Value, control.Bounds.Size);

                    // The strip: 26 high, between the tool bar and the header.
                    var strip = h.Find<Border>("Tool.SearchResults.Sets");
                    Assert.True(strip.IsEffectivelyVisible, $"{variant}: the set strip is not shown");
                    var header = h.Find<TextBlock>("Tool.SearchResults.Header");
                    var toolBar = h.Find<Button>("Tool.SearchResults.ToolBar.searchResults.pin");
                    if (Math.Abs(strip.Bounds.Height - SearchResultsView.StripHeight) > 0.5)
                        defects.Add((variant, $"the strip is {strip.Bounds.Height} high"));
                    if (VisualSnapshot.HiddenPart(strip, h.Window) is { } cut)
                        defects.Add((variant, "cut: the set strip — " + cut));
                    if (!(In(toolBar).Bottom <= In(strip).Top + 0.5 && In(strip).Bottom <= In(header).Top + 0.5))
                        defects.Add((variant, $"the strip is not between the tool bar and the header: {In(toolBar)}, {In(strip)}, {In(header)}"));
                    if (VisualSnapshot.TextWidth(header) > header.Bounds.Width + 0.5)
                        defects.Add((variant, "the header is trimmed"));

                    // The tabs: side by side, complete, each with what its state asks for.
                    Assert.Equal(["Count", "Draw", "\"Counter\""], results.Sets.Select(s => s.Title));
                    Assert.Equal(shown, results.Sets.Single(s => s.IsShown).Number);
                    double right = In(strip).Left;
                    foreach (var set in results.Sets)
                    {
                        var id = "Tool.SearchResults.Set." + set.Number;
                        var tab = h.Find<Control>(id);
                        var area = In(tab);
                        if (VisualSnapshot.HiddenPart(tab, h.Window) is { } hidden)
                            defects.Add((variant, $"cut: the tab {set.Title} — {hidden}"));
                        if (area.Left < right - 0.5)
                            defects.Add((variant, $"the tab {set.Title} overlaps the tab before it"));
                        right = area.Right;
                        if (area.Bottom > In(strip).Bottom + 0.5 || area.Top < In(strip).Top - 0.5)
                            defects.Add((variant, $"the tab {set.Title} leaves the strip"));
                        var title = tab.GetVisualDescendants().OfType<TextBlock>().Single();
                        Assert.Equal(set.Title, title.Text);
                        if (VisualSnapshot.TextWidth(title) > title.Bounds.Width + 0.5)
                            defects.Add((variant, $"the title of the tab {set.Title} is trimmed"));
                        if (VisualSnapshot.DistinctColors(h.Window, title) < 2)
                            defects.Add((variant, $"the title of the tab {set.Title} is not drawn"));

                        // Icons: the pin glyph on a pinned tab, the icon of the kind on every tab, the close button on the shown one.
                        var icons = tab.GetVisualDescendants().OfType<IdeIcon>().Where(i => i.IsEffectivelyVisible).ToList();
                        List<string> expected = [.. set.IsPinned ? (string[])["pin"] : [], set.IconName, .. set.IsShown ? (string[])["close"] : []];
                        Assert.Equal(expected, icons.Select(i => i.IconName));
                        foreach (var icon in icons)
                        {
                            if (VisualSnapshot.HiddenPart(icon, h.Window) is { } outside)
                                defects.Add((variant, $"cut: the icon {icon.IconName} of the tab {set.Title} — {outside}"));
                            if (VisualSnapshot.DistinctColors(h.Window, icon) < 2)
                                defects.Add((variant, $"the icon {icon.IconName} of the tab {set.Title} is not drawn"));
                        }

                        for (var i = 1; i < icons.Count; i++)
                        {
                            if (In(icons[i]).Left < In(icons[i - 1]).Right - 0.5)
                                defects.Add((variant, $"the icons {icons[i - 1].IconName} and {icons[i].IconName} of the tab {set.Title} overlap"));
                        }

                        if (In(title).Left < In(icons[set.IsPinned ? 1 : 0]).Right - 0.5 || (set.IsShown && In(icons[^1]).Left < In(title).Right - 0.5))
                            defects.Add((variant, $"the title of the tab {set.Title} overlaps an icon"));

                        // The shown tab: the 2 px accent line along its bottom edge; no other tab has it.
                        var line = VisualSnapshot.PixelsOf(h.Window, new Rect(area.Left + 2, area.Bottom - 2, area.Width - 4, 2));
                        var accent = Token(h.Window, "accent.default");
                        var drawn = line.Count(p => Near(p, accent));
                        if (set.IsShown && drawn < line.Length * 0.9)
                            defects.Add((variant, $"the shown tab {set.Title} has no accent line ({drawn} of {line.Length} pixels)"));
                        if (!set.IsShown && drawn > 0)
                            defects.Add((variant, $"the tab {set.Title} is not shown and has an accent line"));
                    }

                    // Keep Results on the tool bar: drawn; enabled only while the live set is shown.
                    if (VisualSnapshot.HiddenPart(toolBar, h.Window) is { } button)
                        defects.Add((variant, "cut: the Keep Results button — " + button));
                    if (VisualSnapshot.DistinctColors(h.Window, toolBar) < 2)
                        defects.Add((variant, "the Keep Results button is not drawn"));
                    Assert.Equal(canPin, toolBar.IsEffectivelyEnabled);

                    defects.AddRange(VisualSnapshot.LayoutDefects(h.Find<Control>(AutomationIds.DockBottom)).Select(d => (variant, d)));
                    files.Add((subject, theme, size, VisualSnapshot.Capture(h.Window, subject, theme)));
                }
            }

            // Two pinned References sets, then a live Find in Files set.
            await ReferencesViewTests.FindAsync(h, "Unit2.pas", ReferencesViewTests.Unit2, "Count >", "References to Count — 6 references in 3 files");
            await h.ExecuteAsync("searchResults.pin");
            await ReferencesViewTests.FindAsync(h, "Hello.pas", ReferencesViewTests.Main, "C.Draw", "References to Draw — 7 references in 3 files (3 in library sources)");
            await h.ExecuteAsync("searchResults.pin");
            await results.RunAsync(new FindInFilesRequest(new FindQuery("Counter", MatchCase: true, WholeWord: false, Regex: false), FindInFilesScope.Project));
            Dispatcher.UIThread.RunJobs();
            Shoot(live, shown: 3, canPin: true);

            // The first pinned set is shown: its rows, its tab selected, Keep Results disabled.
            h.Click("Tool.SearchResults.Set.1");
            Assert.Equal("References to Count — 6 references in 3 files", h.Find<TextBlock>("Tool.SearchResults.Header").Text);
            Shoot(pinned, shown: 1, canPin: false);
        }

        AssertSnapshots(files, subjects, 4, defects);
    }
}
