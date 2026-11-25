using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Build;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The dialog <c>TrustProject</c> as the user sees it (dialogs.md §29, UI-DLG-290…296; card F09-008).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed partial class TrustDialogTests
{
    internal const string HelloEvents = """, "events": { "preBuild": [ "tools\\gen.exe --out src\\Generated.pas" ], "postBuild": [ "copy /y bin\\Debug\\Hello.exe ..\\deploy\\" ] }""";

    /// <summary>A second project, built before Hello, with post-build commands only.</summary>
    internal static void AddLibrary(HarnessOptions o, params string[] postBuild)
    {
        var commands = string.Join(", ", postBuild.Select(c => System.Text.Json.JsonSerializer.Serialize(c)));
        o.InitialFiles[@"Lib\Lib.pproj"] = $$"""
            { "formatVersion": 1, "name": "Lib", "appType": "console", "mainSource": "Lib.pas",
              "files": [ { "path": "Lib.pas" } ], "events": { "postBuild": [ {{commands}} ] } }
            """;
        o.InitialFiles[@"Lib\Lib.pas"] = "program Lib;\r\nbegin\r\nend.\r\n";
    }

    internal const string DependsOnLibrary = """, "dependencies": [ "../Lib/Lib.pproj" ]""";

    private static string TrustFile(HeadlessHarness h) => Path.Combine(h.Services.UserPaths.RoamingDirectory, "trust.json");

    private static string Id(string field) => "Dialog.TrustProject." + field;

    private static object? Brush(DialogDriver dlg, string key) =>
        dlg.Window.TryFindResource(key, dlg.Window.ActualThemeVariant, out var brush) ? brush : throw new InvalidOperationException(key);

    /// <summary>Runs <paramref name="body"/>; after a failure the open prompt is cancelled, so that the failure is what the test reports.</summary>
    internal static async Task Guarded(HeadlessHarness h, Func<Task> body)
    {
        try
        {
            await body();
        }
        catch
        {
            foreach (var open in h.AllWindows.OfType<FreexComp.Ide.Views.DialogWindow>().Where(w => !w.Model.IsClosed).Reverse().ToList())
                _ = open.Model.PressCancelAsync();
            Dispatcher.UIThread.RunJobs();
            throw;
        }
    }

    /// <summary>
    /// UI-DLG-290…292, 295, 296: title, size, ids, button order; the default button <c>BuildWithoutEvents</c> has the initial
    /// focus and is the only accent button; Enter chooses it; Escape cancels; mnemonics; singular / plural text of the trust
    /// button; no row for an empty list.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-290")]
    [Trait("Req", "UI-DLG-291")]
    [Trait("Req", "UI-DLG-292")]
    [Trait("Req", "UI-DLG-295")]
    [Trait("Req", "UI-DLG-296")]
    public async Task LayoutDefaultButtonAndKeyboard()
    {
        await using var h = await BuildScenario.StartAsync(o => AddLibrary(o, "echo done"), HelloEvents + DependsOnLibrary);
        await Guarded(h, () => LayoutDefaultButtonAndKeyboardAsync(h));
    }

    private static async Task LayoutDefaultButtonAndKeyboardAsync(HeadlessHarness h)
    {
        var hello = BuildScenario.ProjectPath(h);
        var lib = Path.Combine(h.TempDir, @"Lib\Lib.pproj");

        // 1. Enter on the freshly opened dialog: Build Without Events.
        var building = h.Services.Build.BuildAsync(BuildKind.Build);
        var dlg = await h.WaitForDialogAsync("TrustProject");
        Assert.True(h.Services.Trust.IsPromptOpen);
        Assert.Equal(IdeState.Idle, h.Services.State.Current.State);
        Assert.Equal("Project Commands — Hello", dlg.Window.Title);
        Assert.Equal(new Size(640, 420), dlg.Window.ClientSize);
        Assert.True(dlg.Window.CanResize);
        Assert.Equal((640, 420), (dlg.Window.MinWidth, dlg.Window.MinHeight)); // minimum = the stated size
        Assert.Equal(Strings.Msg_Trust_Intro, dlg.Find<TextBlock>("Intro").Text);

        // The tree: both projects in build order, their lists in file order, no row for Lib's empty "Before build".
        var list = dlg.Find<ListBox>("Commands");
        Assert.Equal("Commands", Avalonia.Automation.AutomationProperties.GetName(list));
        Assert.Equal(
        [
            (Id("Project.0"), lib),
            (Id("Command.0.0"), "After build: echo done"),
            (Id("Project.1"), hello),
            (Id("Command.1.0"), @"Before build: tools\gen.exe --out src\Generated.pas"),
            (Id("Command.1.1"), @"After build: copy /y bin\Debug\Hello.exe ..\deploy\"),
        ], list.Items.Cast<ListBoxItem>().Select(i => (AutomationIds.Get(i)!, Avalonia.Automation.AutomationProperties.GetName(i)!)));
        Assert.Empty(dlg.FindAll(Id("Project.0.Changed")));
        Assert.False(dlg.Find<TextBlock>("Error").IsEffectivelyVisible);

        // The buttons: order, texts with their mnemonics, one row (that the row fits into the dialog at the minimum size
        // is checked with real text metrics by VisualSnapshotTests.TrustPrompt_RendersWithoutClipping).
        string[] ids = ["BuildWithoutEvents", "TrustProject", "TrustFolder", "Cancel"];
        var buttons = ids.Select(dlg.Find<Button>).ToList();
        Assert.Equal(["Build _Without Events", "_Trust These Projects", "Trust _Folder…", "Cancel"], buttons.Select(b => b.Content));
        var boxes = buttons.Select(b => new Rect(b.TranslatePoint(default, dlg.Window)!.Value, b.Bounds.Size)).ToList();
        Assert.Equal(boxes.OrderBy(b => b.X), boxes); // left to right as listed
        Assert.All(boxes, b => Assert.Equal(boxes[0].Y, b.Y));
        Assert.Equal([true, false, false, false], buttons.Select(b => b.Classes.Contains("accent"))); // no trust button is accent
        Assert.Equal(Id("BuildWithoutEvents"), dlg.FocusedAutomationId); // the safe answer has the focus

        dlg.Press("Enter");
        var result = await building;
        Assert.True(result!.Success);
        Assert.Equal(EventsPolicy.Skip, Assert.Single(h.Build.Requests).Events);
        Assert.False(File.Exists(TrustFile(h)));
        Assert.Equal("640,420", h.Services.Layout.GetDialogValue("TrustProject", "size")); // UI-DLG-290

        // 2. Enter with the focus in the tree is still the default button; Tab goes from the tree to the first button.
        building = h.Services.Build.BuildAsync(BuildKind.Build);
        dlg = await h.WaitForDialogAsync("TrustProject");
        dlg.Press("Shift+Tab"); // from the default button back into the tree
        Assert.StartsWith(Id("Project.0"), dlg.FocusedAutomationId, StringComparison.Ordinal);
        dlg.Press("Down");
        Assert.Equal(Id("Command.0.0"), dlg.FocusedAutomationId);
        dlg.Press("Tab");
        Assert.Equal(Id("BuildWithoutEvents"), dlg.FocusedAutomationId);
        dlg.Press("Tab");
        Assert.Equal(Id("TrustProject"), dlg.FocusedAutomationId);
        dlg.Press("Tab");
        Assert.Equal(Id("TrustFolder"), dlg.FocusedAutomationId);
        dlg.Press("Tab");
        Assert.Equal(Id("Cancel"), dlg.FocusedAutomationId);
        dlg.Press("Tab");
        Assert.StartsWith(Id("Command.0.0"), dlg.FocusedAutomationId, StringComparison.Ordinal);
        dlg.Press("Enter");
        Assert.NotNull(await building);
        Assert.Equal([EventsPolicy.Skip, EventsPolicy.Skip], h.Build.Requests.Select(r => r.Events));

        // 3. Escape: no build.
        building = h.Services.Build.BuildAsync(BuildKind.Build);
        dlg = await h.WaitForDialogAsync("TrustProject");
        dlg.Press("Escape");
        Assert.Null(await building);
        Assert.Equal(2, h.Build.Requests.Count);
        Assert.Equal(Strings.Msg_Trust_BuildCancelled, h.Services.Status.Message);
        Assert.False(h.Services.Trust.IsPromptOpen);
        Assert.False(File.Exists(TrustFile(h)));

        // 4. Alt+F: Trust Folder with Lib's folder — Lib leaves the tree, the button reads "Trust This Project", the
        //    message row appears. Alt+T: Hello is trusted and the build runs.
        building = h.Services.Build.BuildAsync(BuildKind.Build);
        dlg = await h.WaitForDialogAsync("TrustProject");
        h.FileDialogs.NextFolder(Path.GetDirectoryName(lib)!);
        dlg.Press("Alt+F");
        await h.WaitForAsync(() => dlg.Find<TextBlock>("Error").IsEffectivelyVisible, "the folder message");
        Assert.Equal(Path.GetDirectoryName(lib), h.FileDialogs.Requests[^1].InitialFolder); // the first listed project's directory
        Assert.Equal($"The folder {Path.GetDirectoryName(lib)} is now trusted, but it does not contain the projects still listed.", dlg.Find<TextBlock>("Error").Text);
        Assert.Equal([Id("Project.0"), Id("Command.0.0"), Id("Command.0.1")],
            dlg.Find<ListBox>("Commands").Items.Cast<ListBoxItem>().Select(i => AutomationIds.Get(i)));
        Assert.Equal(hello, Avalonia.Automation.AutomationProperties.GetName(dlg.Find<ListBoxItem>("Project.0")));
        Assert.Equal("_Trust This Project", dlg.Find<Button>("TrustProject").Content);
        Assert.Equal("Trust This Project", Avalonia.Automation.AutomationProperties.GetName(dlg.Find<Button>("TrustProject")));
        Assert.Equal(2, h.Build.Requests.Count); // still asking

        dlg.Press("Alt+T");
        Assert.NotNull(await building);
        Assert.Equal(EventsPolicy.RequireTrust, h.Build.Requests[^1].Events);
        Assert.Equal(
        [
            new TrustEntry(TrustEntryKind.Folder, Path.GetDirectoryName(lib)!, null),
            new TrustEntry(TrustEntryKind.Project, hello, ProjectTrust.CommandsHash(h.Services.Projects.Definition!)),
        ], h.Services.Trust.Store.Read().Entries);

        // Trusted: the next build does not ask.
        Assert.NotNull(await h.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Equal(4, h.Build.Requests.Count);
    }

    /// <summary>
    /// UI-DLG-293, 294, 295: the note only below a project whose commands changed; a 400-character command is complete in the
    /// automation name, in the tooltip and in the copy; a line break and a tab are drawn as glyphs and copied as themselves;
    /// Ctrl+A, Ctrl+C copies every row as lines.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-293")]
    [Trait("Req", "UI-DLG-294")]
    [Trait("Req", "UI-DLG-295")]
    public async Task ChangedNoteLongCommandAndCopy()
    {
        var longCommand = "tools\\gen.exe " + string.Join(' ', Enumerable.Range(0, 30).Select(i => $"--opt{i:00}"));
        longCommand += new string('x', 400 - longCommand.Length);
        var events = $$""", "events": { "postBuild": [ "{{longCommand.Replace(@"\", @"\\", StringComparison.Ordinal)}}", "echo a\necho b", "copy\ta b" ] }""";
        await using var h = await BuildScenario.StartAsync(o => AddLibrary(o, "echo lib"), events + DependsOnLibrary);
        await Guarded(h, () => ChangedNoteLongCommandAndCopyAsync(h, longCommand));
    }

    private static async Task ChangedNoteLongCommandAndCopyAsync(HeadlessHarness h, string longCommand)
    {
        const string twoLines = "echo a\necho b";
        const string tabbed = "copy\ta b";
        var hello = BuildScenario.ProjectPath(h);
        var lib = Path.Combine(h.TempDir, @"Lib\Lib.pproj");
        h.Services.Trust.Store.TrustProject(hello, "sha256:" + new string('0', 64)); // trusted earlier, with other commands
        var clipboard = (FakeClipboard)h.Services.Clipboard;

        var building = h.Services.Build.BuildAsync(BuildKind.Build);
        var dlg = await h.WaitForDialogAsync("TrustProject");
        var list = dlg.Find<ListBox>("Commands");
        Assert.Equal([Id("Project.0"), Id("Command.0.0"), Id("Project.1"), Id("Project.1.Changed"), Id("Command.1.0"), Id("Command.1.1"), Id("Command.1.2")],
            list.Items.Cast<ListBoxItem>().Select(i => AutomationIds.Get(i)));

        // UI-DLG-293: the note with the warning glyph, only for the project that was trusted before.
        var note = dlg.Find<ListBoxItem>("Project.1.Changed");
        Assert.True(note.IsEffectivelyVisible);
        var noteTexts = note.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Equal(["⚠ ", Strings.Msg_Trust_Changed], noteTexts.Select(t => t.Text));
        Assert.Same(Brush(dlg, "FxcStatusWarningBrush"), noteTexts[0].Foreground);
        Assert.Null(dlg.TryFind<ListBoxItem>("Project.0.Changed"));

        // UI-DLG-294: the long command — complete in the name, the tooltip and the drawn text; not wrapped; the tree scrolls.
        var row = dlg.Find<ListBoxItem>("Command.1.0");
        Assert.Equal(400, longCommand.Length);
        Assert.Equal("After build: " + longCommand, Avalonia.Automation.AutomationProperties.GetName(row));
        Assert.Equal(longCommand, ToolTip.GetTip(row));
        var drawn = row.GetVisualDescendants().OfType<TextBlock>().Last();
        Assert.Equal(longCommand, string.Concat(drawn.Inlines!.OfType<Run>().Select(r => r.Text)));
        Assert.Equal((Avalonia.Media.TextWrapping.NoWrap, Avalonia.Media.TextTrimming.None), (drawn.TextWrapping, drawn.TextTrimming));
        var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width, "the tree scrolls horizontally instead of cutting the command");
        var cells = row.GetVisualDescendants().OfType<TextBlock>().ToList();
        // UI-DLG-298 (replaces "fixed width 96"): at least 96 and never narrower than the name needs.
        Assert.Equal("After build", cells[0].Text);
        Assert.True(cells[0].Width >= Math.Max(96, cells[0].TextLayout.WidthIncludingTrailingWhitespace), $"name cell width {cells[0].Width}");

        // Control characters are glyphs in text.disabled; nothing of the command is on a second line.
        var twoLineRuns = dlg.Find<ListBoxItem>("Command.1.1").GetVisualDescendants().OfType<TextBlock>().Last().Inlines!.OfType<Run>().ToList();
        Assert.Equal(["echo a", "↵", "echo b"], twoLineRuns.Select(r => r.Text));
        Assert.Same(Brush(dlg, "FxcTextDisabledBrush"), twoLineRuns[1].Foreground);
        Assert.Equal(["copy", "→", "a b"],
            dlg.Find<ListBoxItem>("Command.1.2").GetVisualDescendants().OfType<TextBlock>().Last().Inlines!.OfType<Run>().Select(r => r.Text));
        Assert.Equal(dlg.Find<ListBoxItem>("Command.1.0").Bounds.Height, dlg.Find<ListBoxItem>("Command.1.1").Bounds.Height);
        Assert.Equal("After build: " + twoLines, Avalonia.Automation.AutomationProperties.GetName(dlg.Find<ListBoxItem>("Command.1.1")));

        // UI-DLG-295: Up / Down move the selection; Ctrl+C copies the selected row.
        dlg.Press("Shift+Tab"); // from the default button back into the tree: its first row is selected
        Assert.Equal(0, list.SelectedIndex);
        dlg.Press("Ctrl+C");
        Assert.Equal(lib, clipboard.Text); // a project row: its path
        foreach (var expected in (string[])["echo lib", hello, Strings.Msg_Trust_Changed, longCommand, twoLines, tabbed])
        {
            dlg.Press("Down");
            dlg.Press("Ctrl+C");
            Assert.Equal(expected, clipboard.Text);
        }

        Assert.Equal(6, list.SelectedIndex);
        dlg.Press("Up");
        Assert.Equal(5, list.SelectedIndex);

        // Ctrl+A, Ctrl+C: all rows as lines.
        dlg.Press("Ctrl+A");
        dlg.Press("Ctrl+C");
        Assert.Equal(string.Join(Environment.NewLine,
        [
            lib,
            "  After build: echo lib",
            hello,
            "  After build: " + longCommand,
            "  After build: " + twoLines,
            "  After build: " + tabbed,
        ]), clipboard.Text);
        Assert.False(dlg.Window.Model.IsClosed); // copying answers nothing

        dlg.Press("Escape");
        Assert.Null(await building);
        Assert.Empty(h.Build.Requests);
    }
}
