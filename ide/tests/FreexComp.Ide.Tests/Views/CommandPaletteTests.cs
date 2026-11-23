using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.Popups;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Command Palette popup (I-126, dialogs.md §14).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class CommandPaletteTests
{
    /// <summary>The ids of the list's rows (the list virtualizes: rows outside the viewport have no control).</summary>
    private static List<string> Items(HeadlessHarness h) =>
        h.TryFind<ListBox>("CommandPalette.List") is { IsVisible: true } list
            ? [.. (list.ItemsSource ?? Array.Empty<object>()).OfType<QuickPickItem>().Select(i => i.Id)]
            : [];

    private static async Task OpenAsync(HeadlessHarness h)
    {
        h.Press("Ctrl+Shift+P");
        await h.WaitForAsync(() => h.FocusedAutomationId == "CommandPalette.Input", "palette open with focus in the input");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-140")]
    [Trait("Req", "UI-DLG-141")]
    [Trait("Req", "UI-DLG-142")]
    [Trait("Req", "UI-DLG-143")]
    public async Task ExecutesAfterRestoringFocus()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ImplementedPhase = 3;
            o.InitialFiles["Main.pas"] = "unit Main;\r\ninterface\r\nimplementation\r\nend.\r\n";
        });
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Main.pas"))).Success);
        var editor = h.Find<TextEditor>("Editor.Main.pas");
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();

        // Opened from the editor: editor commands are listed; "<Category>: <Title>" with the first gesture.
        await OpenAsync(h);
        Assert.Equal(560, h.Find<Control>("CommandPalette").Bounds.Width);
        Assert.Contains("edit.duplicateLine", Items(h));
        Assert.DoesNotContain("file.openRecentFile", Items(h)); // parameterized
        Assert.DoesNotContain("file.print", Items(h)); // a later phase
        h.Type("duplicate line");
        await h.WaitForAsync(() => Items(h).FirstOrDefault() == "edit.duplicateLine", "filtered");
        Assert.Equal("Edit: Duplicate Line or Selection", string.Concat(h.Find<TextBlock>("CommandPalette.Item.edit.duplicateLine.Title").Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
        Assert.Equal("Ctrl+D", h.Find<TextBlock>("CommandPalette.Item.edit.duplicateLine.Gesture").Text);
        h.Find<TextBox>("CommandPalette.Input").Text = "";

        // Filtering by subsequence; Enter restores the focus first, then runs the command on the editor.
        h.Type("select all");
        await h.WaitForAsync(() => Items(h).FirstOrDefault() == "edit.selectAll", "filtered");
        h.Press("Enter");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Main.pas", "focus restored");
        Assert.Null(h.TryFind<Control>("CommandPalette.Input"));
        await h.WaitForAsync(() => editor.SelectionLength == editor.Document.TextLength, "select all ran on the editor");

        // Recent commands come first on the next open; Escape closes and restores focus.
        await OpenAsync(h);
        Assert.Equal("edit.selectAll", Items(h)[0]);
        h.Type("zzqq");
        await h.WaitForAsync(() => Items(h).Count == 0, "no match");
        Assert.Equal("No command matches zzqq", h.Find<TextBlock>("CommandPalette.NoMatch").Text);
        h.Press("Escape");
        await h.WaitForAsync(() => h.TryFind<Control>("CommandPalette.Input") is null, "closed");
        Assert.Equal("Editor.Main.pas", h.FocusedAutomationId);

        // A disabled command is listed but Enter does nothing.
        await OpenAsync(h);
        h.Type("cancel build");
        await h.WaitForAsync(() => Items(h).FirstOrDefault() == "project.cancelBuild", "disabled command listed");
        Assert.False(h.Find<Control>("CommandPalette.Item.project.cancelBuild").IsEnabled);
        h.Press("Enter");
        Assert.NotNull(h.TryFind<Control>("CommandPalette.Input"));
        h.Press("Escape");
        await h.WaitForAsync(() => h.TryFind<Control>("CommandPalette.Input") is null, "closed");

        // Opened from a tool window: editor commands are not listed.
        await h.ExecuteAsync("view.projectManager");
        await h.WaitForAsync(() => h.FocusedAutomationId?.StartsWith("Tool.ProjectManager", StringComparison.Ordinal) == true, "project manager focused");
        await OpenAsync(h);
        Assert.DoesNotContain("edit.duplicateLine", Items(h));
        Assert.Contains("edit.selectAll", Items(h));
        h.Press("Escape");
        await h.WaitForAsync(() => h.TryFind<Control>("CommandPalette.Input") is null, "closed");
    }
    /// <summary>
    /// DSG-SUR-191 (D-111): the verbs of the current selection's verb component are palette entries
    /// <c>Designer: &lt;caption&gt;</c> (also those whose command takes an argument); one runs its command on the verb
    /// component; a selection without verbs adds none.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-191")]
    public async Task ListsDesignerVerbs()
    {
        await using var h = await HeadlessHarness.StartAsync(DesignerViewTests.EditorFiles);
        var document = await DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        var surface = document.Designer.Surface!;

        // The status bar: its Panels Editor verb (designer.editCollection Panels) is listed and enabled.
        surface.Selection.Set(["StatusBar1"]);
        Dispatcher.UIThread.RunJobs();
        await OpenAsync(h);
        h.Type("panels editor");
        await h.WaitForAsync(() => Items(h).FirstOrDefault() == "verb.editPanels", "the verb listed");
        Assert.Equal("Designer: Panels Editor…", string.Concat(h.Find<TextBlock>("CommandPalette.Item.verb.editPanels.Title").Inlines!
            .OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
        Assert.True(h.Find<Control>("CommandPalette.Item.verb.editPanels").IsEnabled);

        // Enter runs it: the collection editor of StatusBar1.Panels opens.
        h.Press("Enter");
        var dlg = await h.WaitForDialogAsync("CollectionEditor");
        Assert.Equal("Edit StatusBar1.Panels", dlg.Window.Title);
        dlg.Press("Escape");
        Dispatcher.UIThread.RunJobs();

        // The action list: its editor verb.
        surface.Selection.Set(["ActionList1"]);
        Dispatcher.UIThread.RunJobs();
        await OpenAsync(h);
        Assert.Contains("verb.actionListEditor", Items(h));
        Assert.DoesNotContain("verb.editPanels", Items(h));
        h.Press("Escape");
        await h.WaitForAsync(() => h.TryFind<Control>("CommandPalette.Input") is null, "closed");

        // A component without verbs: no verb entries.
        surface.Selection.Set(["GreetButton"]);
        Dispatcher.UIThread.RunJobs();
        await OpenAsync(h);
        Assert.DoesNotContain(Items(h), id => id.StartsWith("verb.", StringComparison.Ordinal));
        h.Press("Escape");
        await h.WaitForAsync(() => h.TryFind<Control>("CommandPalette.Input") is null, "closed");
    }
}
