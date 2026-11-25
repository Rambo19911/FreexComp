using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Icons;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The library-source bar and the lock glyph of a protected library document (phase8-ui-additions.md §2, I-232).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class LibrarySourceViewTests
{
    internal const string LibraryUnit = "unit SysUtils;\r\ninterface\r\nfunction UpperCase(const S: string): string;\r\nimplementation\r\nfunction UpperCase(const S: string): string;\r\nbegin\r\n  Result := S;\r\nend;\r\nend.\r\n";

    internal static string[] BarsTopToBottom(HeadlessHarness h) =>
        [.. h.FindAll("InfoBar.")
            .Where(c => c is Ide.Views.InfoBarView && c.IsEffectivelyVisible)
            .OrderBy(c => c.TranslatePoint(default, h.Window)!.Value.Y)
            .Select(c => AutomationProperties.GetAutomationId(c)!)];

    internal static IdeIcon LockOf(HeadlessHarness h, string fileName) =>
        h.Find<Control>(AutomationIds.DocumentTab(fileName)).GetVisualDescendants().OfType<IdeIcon>().Single(i => i.IconName == "lock");

    private static async Task<TextDocumentViewModel> OpenAsync(HeadlessHarness h, string relativePath)
    {
        var result = await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, relativePath));
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-144")]
    [Trait("Req", "UI-ED-145")]
    [Trait("Req", "IDE-LIB-003")]
    [Trait("Req", "IDE-LIB-004")]
    public async Task BarAndLockGlyphRender()
    {
        await using var h = await BuildScenario.StartAsync(o =>
        {
            // Mixed line endings: the load shows its own bar; the library bar is above it.
            o.InitialFiles[@"tc\rtl\SysUtils.pas"] = LibraryUnit.Replace("interface\r\n", "interface\n", StringComparison.Ordinal);
            o.InitialFiles[@"tc\rtl\Classes.pas"] = LibraryUnit.Replace("SysUtils", "Classes", StringComparison.Ordinal);
        });
        var document = await OpenAsync(h, @"tc\rtl\SysUtils.pas");
        var editor = h.Find<TextEditor>("Editor.SysUtils.pas");
        Assert.True(editor.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();

        // The bar: info severity, its text, its two actions (a project is open: UI-ED-147), no dismiss button; top-most
        // (UI-ED-144).
        Assert.Equal(["InfoBar.LibrarySource", "InfoBar.MixedLineEndings"], BarsTopToBottom(h));
        var bar = h.Find<Control>("InfoBar.LibrarySource");
        Assert.Equal("info", bar.GetVisualDescendants().OfType<IdeIcon>().Single().IconName);
        Assert.Equal("This file is part of the library source installed with FreexComp. It is protected because a change affects every project that uses it.",
            h.Find<TextBlock>("InfoBar.LibrarySource.Text").Text);
        var action = h.Find<Button>("InfoBar.LibrarySource.Action.EditAnyway");
        Assert.Equal(("Edit Anyway", "Edit Anyway"), (action.Content, AutomationProperties.GetName(action)));
        Assert.Null(h.TryFind<Control>("InfoBar.LibrarySource.Dismiss"));
        var copy = h.Find<Button>("InfoBar.LibrarySource.Action.CopyToProject");
        Assert.Equal([copy, action], bar.GetVisualDescendants().OfType<Button>());
        Assert.Empty(VisualSnapshot.LayoutDefects(bar));
        Assert.Null(VisualSnapshot.HiddenPart(action, h.Window));
        Assert.Null(VisualSnapshot.HiddenPart(copy, h.Window));

        // The existing read-only presentation: lock glyph on the tab, the tooltip line, typing changes nothing (UI-ED-145).
        var tab = h.Find<Control>(AutomationIds.DocumentTab("SysUtils.pas"));
        Assert.True(LockOf(h, "SysUtils.pas").IsEffectivelyVisible);
        Assert.Equal(document.FilePath + Environment.NewLine + Strings.Msg_Editor_Tip_ReadOnly, ToolTip.GetTip(tab));
        Assert.True(editor.IsReadOnly);
        var text = document.Buffer.Text;
        h.Type("x");
        Assert.Equal(text, document.Buffer.Text);

        // Keyboard: Shift+Tab reaches the first action, Tab the next; Enter runs it. Bar and glyph go in the same turn
        // and the keyboard focus is in the text area again; the other bar stays.
        h.Press("Shift+Tab");
        Assert.Equal("InfoBar.LibrarySource.Action.CopyToProject", h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal("InfoBar.LibrarySource.Action.EditAnyway", h.FocusedAutomationId);
        h.Press("Enter");
        Assert.Equal(["InfoBar.MixedLineEndings"], BarsTopToBottom(h));
        Assert.False(LockOf(h, "SysUtils.pas").IsVisible);
        Assert.Equal(document.FilePath, ToolTip.GetTip(tab));
        Assert.Equal("Editor.SysUtils.pas", h.FocusedAutomationId);
        Assert.False(editor.IsReadOnly);
        Assert.False(document.IsLibraryProtected);
        h.Type("x");
        Assert.Equal("x" + text, document.Buffer.Text);

        // The same with the mouse, and with the command, on other protected documents.
        var second = await OpenAsync(h, @"tc\rtl\Classes.pas");
        Assert.Equal(["InfoBar.LibrarySource"], BarsTopToBottom(h));
        Assert.True(LockOf(h, "Classes.pas").IsEffectivelyVisible);
        h.Click("InfoBar.LibrarySource.Action.EditAnyway");
        Assert.Empty(BarsTopToBottom(h));
        Assert.False(LockOf(h, "Classes.pas").IsVisible);
        Assert.Equal("Editor.Classes.pas", h.FocusedAutomationId);
        Assert.False(second.IsReadOnly);

        Assert.True(await h.Services.Documents.CloseAsync(second));
        second = await OpenAsync(h, @"tc\rtl\Classes.pas");
        Assert.True(LockOf(h, "Classes.pas").IsEffectivelyVisible); // closing and reopening protects it again
        Assert.True(h.Find<TextEditor>("Editor.Classes.pas").TextArea.Focus());
        await h.ExecuteAsync("file.editLibrarySource");
        Assert.Empty(BarsTopToBottom(h));
        Assert.False(LockOf(h, "Classes.pas").IsVisible);
        Assert.Equal("Editor.Classes.pas", h.FocusedAutomationId);
        Assert.False(second.IsReadOnly);

        // A project member never shows the bar.
        await OpenAsync(h, @"Hello\Unit1.pas");
        Assert.Empty(BarsTopToBottom(h));
        Assert.False(LockOf(h, "Unit1.pas").IsVisible);
    }

    /// <summary>
    /// UI-ED-147…149, IDE-LIB-037: with a project open the library bar offers <c>Copy to Project</c> before
    /// <c>Edit Anyway</c> — neither an accent button, no dismiss button, nothing cut at the minimum window width; the
    /// action is shown disabled while a build runs and is absent for a file that is not a unit and without a project.
    /// It leaves the keyboard focus in the text area of the new document.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-147")]
    [Trait("Req", "UI-ED-148")]
    [Trait("Req", "UI-ED-149")]
    [Trait("Req", "IDE-LIB-037")]
    [Trait("Req", "IDE-LIB-033")]
    public async Task BarOffersCopyToProjectBeforeEditAnyway()
    {
        const string CopyId = "InfoBar.LibrarySource.Action.CopyToProject";
        const string EditId = "InfoBar.LibrarySource.Action.EditAnyway";
        await using var h = await BuildScenario.StartAsync(o =>
        {
            o.WindowSize = new Size(800, 500); // the minimum window size
            o.InitialFiles[@"tc\rtl\SysUtils.pas"] = LibraryUnit;
            o.InitialFiles[@"tc\rtl\Classes.pas"] = LibraryUnit.Replace("SysUtils", "Classes", StringComparison.Ordinal);
            o.InitialFiles[@"tc\rtl\Tables.inc"] = "const Zero = 0;\r\n";
        });
        Assert.Equal(800, h.Window.ClientSize.Width);
        var library = await OpenAsync(h, @"tc\rtl\SysUtils.pas");
        Assert.True(h.Find<TextEditor>("Editor.SysUtils.pas").TextArea.Focus());
        Dispatcher.UIThread.RunJobs();

        // Two buttons, Copy to Project first; AutomationIds and names; no dismiss button; the text is unchanged.
        var bar = h.Find<Control>("InfoBar.LibrarySource");
        var copy = h.Find<Button>(CopyId);
        var edit = h.Find<Button>(EditId);
        Assert.Equal([copy, edit], bar.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible));
        Assert.Equal(("Copy to Project", "Copy to Project"), (copy.Content, AutomationProperties.GetName(copy)));
        Assert.Equal(("Edit Anyway", "Edit Anyway"), (edit.Content, AutomationProperties.GetName(edit)));
        Assert.Null(h.TryFind<Control>("InfoBar.LibrarySource.Dismiss"));
        Assert.Equal(Strings.Msg_File_LibrarySource, h.Find<TextBlock>("InfoBar.LibrarySource.Text").Text);
        Assert.All((Button[])[copy, edit], b => Assert.DoesNotContain("accent", b.Classes)); // neither is an accent button
        Assert.True(copy.IsEffectivelyEnabled && edit.IsEffectivelyEnabled);

        // Layout at the minimum window width: in this order from the left, on one line, at the bar's right edge, nothing
        // cut, nothing overlapping; the text is completely shown.
        var copyBox = new Rect(copy.TranslatePoint(default, bar)!.Value, copy.Bounds.Size);
        var editBox = new Rect(edit.TranslatePoint(default, bar)!.Value, edit.Bounds.Size);
        Assert.True(copyBox.Right <= editBox.Left, $"order: {copyBox} / {editBox}");
        Assert.Equal(copyBox.Top, editBox.Top);
        Assert.InRange(bar.Bounds.Width - editBox.Right, 0, 8.5); // right-aligned (the bar's side margin)
        Assert.Empty(VisualSnapshot.LayoutDefects(bar));
        foreach (var part in (Control[])[copy, edit, h.Find<TextBlock>("InfoBar.LibrarySource.Text"), bar])
            Assert.Null(VisualSnapshot.HiddenPart(part, h.Window));

        // UI-ED-148: where text and buttons do not fit in one line (a document area narrower than the minimum window
        // gives), the buttons move to a second line, right-aligned, and are never cut; the bar grows.
        var narrowBar = new Ide.Views.InfoBarView { DataContext = library.InfoBars[0] };
        // Wide enough for both buttons on one line, too narrow for the text beside them.
        var narrow = new Window { Width = copyBox.Width + editBox.Width + 190, Height = 200, Content = new DockPanel { Children = { narrowBar } } };
        DockPanel.SetDock(narrowBar, Dock.Top);
        narrow.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var buttons = narrowBar.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal([CopyId, EditId], buttons.Select(b => AutomationProperties.GetAutomationId(b)));
            var narrowText = narrowBar.GetVisualDescendants().OfType<TextBlock>().First(t => AutomationProperties.GetAutomationId(t) == "InfoBar.LibrarySource.Text");
            var textBottom = narrowText.TranslatePoint(new Point(0, narrowText.Bounds.Height), narrowBar)!.Value.Y;
            var first = new Rect(buttons[0].TranslatePoint(default, narrowBar)!.Value, buttons[0].Bounds.Size);
            var second = new Rect(buttons[1].TranslatePoint(default, narrowBar)!.Value, buttons[1].Bounds.Size);
            Assert.True(first.Top >= textBottom, $"the buttons are not below the text: {first} / text bottom {textBottom}");
            Assert.Equal(first.Top, second.Top);
            Assert.True(first.Right <= second.Left, $"order: {first} / {second}");
            Assert.InRange(narrowBar.Bounds.Width - second.Right, 0, 8.5); // right-aligned
            Assert.True(narrowBar.Bounds.Height > bar.Bounds.Height, "the bar did not grow");
            Assert.Empty(VisualSnapshot.LayoutDefects(narrowBar));
            foreach (var part in buttons.Cast<Control>().Append(narrowText))
                Assert.Null(VisualSnapshot.HiddenPart(part, narrow));
        }
        finally
        {
            narrow.Close();
        }

        // Keyboard order: Shift+Tab → Copy to Project → Edit Anyway → the text area.
        h.Press("Shift+Tab");
        Assert.Equal(CopyId, h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal(EditId, h.FocusedAutomationId);
        h.Press("Tab");
        Assert.Equal("Editor.SysUtils.pas", h.FocusedAutomationId);

        // While a build runs the action is shown disabled; Edit Anyway is not affected.
        Assert.True(h.Services.State.TryTransition(new Core.Build.IdeStateInfo(Core.Models.IdeState.Building, Core.Build.BuildKind.Build, false, null, null)));
        Dispatcher.UIThread.RunJobs();
        Assert.True(copy.IsVisible);
        Assert.False(copy.IsEffectivelyEnabled);
        Assert.True(edit.IsEffectivelyEnabled);
        h.Press("Shift+Tab");
        Assert.Equal(EditId, h.FocusedAutomationId); // the disabled button is passed over
        h.Press("Escape");
        Assert.True(h.Services.State.TryTransition(Core.Build.IdeStateInfo.Idle));
        Dispatcher.UIThread.RunJobs();
        Assert.True(copy.IsEffectivelyEnabled);

        // A library file that is not a unit: Edit Anyway alone.
        await OpenAsync(h, @"tc\rtl\Tables.inc");
        var includeBar = h.FindAll("InfoBar.LibrarySource").OfType<Ide.Views.InfoBarView>().Single(b => b.IsEffectivelyVisible);
        Assert.Equal([EditId], includeBar.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible).Select(b => AutomationProperties.GetAutomationId(b)));
        Assert.Empty(VisualSnapshot.LayoutDefects(includeBar));

        // The mouse: Copy to Project copies the file into the project folder, opens the copy without bar and lock and
        // leaves the keyboard focus in the text area of the new document; the library document stays protected.
        h.Services.Documents.Activate(library);
        Dispatcher.UIThread.RunJobs();
        // The action opens the copy with file reads on a pool thread (IDE-ARCH-081): the wait is for its last step, the
        // confirmation in the status bar (UI-ED-149).
        h.Click(CopyId);
        await h.WaitForAsync(() => h.Services.Status.Message == "SysUtils.pas was copied into the project. This project is now built from the copy.",
            "Copy to Project finished");
        var target = Path.Combine(h.TempDir, @"Hello\SysUtils.pas");
        Assert.Equal(File.ReadAllBytes(library.FilePath!), File.ReadAllBytes(target));
        var copied = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.ActiveDocument);
        Assert.Equal(target, copied.FilePath);
        Assert.Empty(copied.InfoBars);
        Assert.False(copied.IsReadOnly);
        Assert.True(library.IsLibraryProtected);
        Assert.True(h.Services.Projects.IsMember(target));
        var focused = Assert.IsAssignableFrom<Visual>(h.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("Editor.SysUtils.pas", h.FocusedAutomationId);
        Assert.Same(copied, focused.GetVisualAncestors().OfType<Ide.Views.TextDocumentView>().First().DataContext);
        Assert.Empty(BarsTopToBottom(h));
        var text = copied.Buffer.Text;
        h.Type("x");
        Assert.Equal("x" + text, copied.Buffer.Text);
        Assert.Equal("SysUtils.pas was copied into the project. This project is now built from the copy.", h.Services.Status.Message);
        Assert.Empty(h.Messages.Requests); // confirmations and refusals are status-bar texts (UI-ED-149)

        // Without a project: one button.
        Assert.True(await h.Services.Documents.SaveAsync(copied));
        Assert.True(await h.Services.Projects.CloseAsync());
        await OpenAsync(h, @"tc\rtl\Classes.pas");
        var loneBar = h.FindAll("InfoBar.LibrarySource").OfType<Ide.Views.InfoBarView>().Single(b => b.IsEffectivelyVisible);
        var lone = Assert.Single(loneBar.GetVisualDescendants().OfType<Button>(), b => b.IsVisible);
        Assert.Equal(EditId, AutomationProperties.GetAutomationId(lone));
        Assert.Empty(VisualSnapshot.LayoutDefects(loneBar));
        Assert.Null(VisualSnapshot.HiddenPart(lone, h.Window));
    }
}
