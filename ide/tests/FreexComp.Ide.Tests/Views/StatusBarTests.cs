using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed partial class StatusBarTests
{
    private static readonly string[] EditorFields = ["CaretPosition", "Selection", "InsertMode", "Indent", "LineEnding", "Encoding", "Language"];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-040")]
    [Trait("Req", "UI-WIN-042")]
    public async Task FieldsFollowActiveEditor()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var status = h.Services.Status;

        // No text editor active (Welcome page, empty document area): only the state and the message.
        Assert.Equal(Strings.Msg_State_Idle, h.Status.State);
        Assert.Equal("", h.Status.Message);
        Assert.All(EditorFields, f => Assert.Null(h.Status.Field(f)));
        Assert.Null(h.Status.Field("Diagnostics")); // R1: no build yet
        Assert.Null(h.Status.Field("Configuration")); // R9: no project

        status.SetEditor(new EditorStatus(12, 5, 0, 0, Overwrite: false, InsertSpaces: true, IndentSize: 2, LineEndingKind.CrLf, "utf-8", "Object Pascal"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Ln 12, Col 5", h.Status.Field("CaretPosition"));
        Assert.Null(h.Status.Field("Selection")); // empty selection
        Assert.Equal("INS", h.Status.Field("InsertMode"));
        Assert.Equal("Spaces: 2", h.Status.Field("Indent"));
        Assert.Equal("CRLF", h.Status.Field("LineEnding"));
        Assert.Equal("UTF-8", h.Status.Field("Encoding"));
        Assert.Equal("Object Pascal", h.Status.Field("Language"));

        status.SetEditor(new EditorStatus(3, 1, 25, 3, Overwrite: true, InsertSpaces: false, IndentSize: 4, LineEndingKind.Lf, "utf-8-bom", "Plain Text"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Ln 3, Col 1", h.Status.Field("CaretPosition"));
        Assert.Equal("Sel 25 (3 lines)", h.Status.Field("Selection"));
        Assert.Equal("OVR", h.Status.Field("InsertMode"));
        Assert.Equal("Tabs: 4", h.Status.Field("Indent"));
        Assert.Equal("LF", h.Status.Field("LineEnding"));
        Assert.Equal("UTF-8 BOM", h.Status.Field("Encoding"));
        Assert.Equal("Plain Text", h.Status.Field("Language"));

        status.SetEditor(new EditorStatus(1, 7, 10, 1, false, true, 2, LineEndingKind.Mixed, "utf-16le", "JSON"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Sel 10", h.Status.Field("Selection"));
        Assert.Equal("Mixed", h.Status.Field("LineEnding"));
        Assert.Equal("UTF-16 LE", h.Status.Field("Encoding"));
        status.SetEditor(new EditorStatus(1, 1, 0, 0, false, true, 2, LineEndingKind.CrLf, "utf-16be", "Form"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("UTF-16 BE", h.Status.Field("Encoding"));
        status.SetEditor(new EditorStatus(1, 1, 0, 0, false, true, 2, LineEndingKind.CrLf, "windows-1252", "Form"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Windows-1252", h.Status.Field("Encoding"));

        // Clickable fields are flat buttons whose tooltip names the action (UI-WIN-042).
        Assert.Equal(h.Services.Commands.Get("search.goToLine").Title, ToolTip.GetTip(h.Find<Button>("StatusBar.CaretPosition")));
        Assert.Equal(h.Services.Commands.Get("edit.toggleOverwrite").Title, ToolTip.GetTip(h.Find<Button>("StatusBar.InsertMode")));
        Assert.Equal(h.Services.Commands.Get("view.messages").Title, ToolTip.GetTip(h.Find<Button>("StatusBar.State")));

        // Back on the Welcome page: the editor group disappears.
        status.SetEditor(null);
        Dispatcher.UIThread.RunJobs();
        Assert.All(EditorFields, f => Assert.Null(h.Status.Field(f)));

        // UI-WIN-044: view.statusBar hides the whole bar.
        await h.ExecuteAsync("view.statusBar");
        Assert.False(h.Services.Settings.Get(SettingsCatalog.AppearanceShowStatusBar));
        Assert.False(h.Find<Control>(AutomationIds.StatusBar).IsVisible);
        Assert.False(h.Services.Commands.Get("view.statusBar").IsChecked);
        await h.ExecuteAsync("view.statusBar");
        Assert.True(h.Find<Control>(AutomationIds.StatusBar).IsVisible);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-MSG-003")]
    [Trait("Req", "UI-WIN-043")]
    public async Task TransientMessageExpires()
    {
        await using var h = await HeadlessHarness.StartAsync();
        var status = h.Services.Status;

        status.ShowMessage("Saved Unit1.pas", TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Saved Unit1.pas", h.Status.Message);
        h.Clock.Advance(TimeSpan.FromMilliseconds(4999));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Saved Unit1.pas", h.Status.Message);
        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", h.Status.Message);

        // A new message replaces the current one at once, with its own duration.
        status.ShowMessage("first", TimeSpan.FromSeconds(5));
        h.Clock.Advance(TimeSpan.FromSeconds(4));
        status.ShowMessage("second", TimeSpan.FromSeconds(3));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("second", h.Status.Message);
        h.Clock.Advance(TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("second", h.Status.Message);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", h.Status.Message);

        // Sticky: stays until its operation ends (or a newer message replaces it).
        var sticky = status.ShowStickyMessage("Opening Hello…");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Opening Hello…", h.Status.Message);
        sticky.Dispose();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", h.Status.Message);

        var replaced = status.ShowStickyMessage("Opening Hello…");
        status.ShowMessage("Saved Unit1.pas", TimeSpan.FromSeconds(5));
        replaced.Dispose(); // ends an operation whose message is no longer shown: the newer one stays
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Saved Unit1.pas", h.Status.Message);
    }

    private static List<MenuItem> MenuOf(HeadlessHarness h, string field) =>
        [.. Assert.IsType<ContextMenu>(h.Find<Control>(AutomationIds.StatusBarField(field)).ContextMenu).Items.OfType<MenuItem>()];

    private static void Choose(MenuItem item)
    {
        Assert.True(item.IsEffectivelyEnabled, AutomationIds.Get(item) + " is disabled");
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-042")]
    [Trait("Req", "IDE-P1-074")]
    [Trait("Req", "IDE-P1-076")]
    public async Task EncodingAndLineEndingMenus()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.InitialFiles["A.pas"] = "one\r\ntwo\r\n");
        var path = Path.Combine(h.TempDir, "A.pas");
        var document = Assert.IsType<Core.ViewModels.TextDocumentViewModel>((await h.Services.Documents.OpenAsync(path)).Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("CRLF", h.Status.Field("LineEnding"));
        Assert.Equal(h.Services.Commands.Get("edit.convertLineEndings").Title, ToolTip.GetTip(h.Find<Button>("StatusBar.LineEnding")));

        // R6: the conversion commands; the current kind is disabled.
        h.Click("StatusBar.LineEnding");
        var items = MenuOf(h, "LineEnding");
        Assert.Equal(["Context.edit.convertLineEndings:crlf", "Context.edit.convertLineEndings:lf"], items.Select(AutomationIds.Get));
        Assert.False(items[0].IsEffectivelyEnabled);
        Choose(items[1]);
        h.Find<Control>("StatusBar.LineEnding").ContextMenu!.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("one\ntwo\n", document.Buffer.Text);
        Assert.Equal("LF", h.Status.Field("LineEnding"));
        Assert.True(document.IsModified);

        // R7: Reopen with Encoding…, then Save with Encoding ▸ with a bullet on the current encoding.
        h.Click("StatusBar.Encoding");
        items = MenuOf(h, "Encoding");
        Assert.Equal(["StatusBar.Encoding.Reopen", "StatusBar.Encoding.SaveWith"], items.Select(AutomationIds.Get));
        var saveWith = items[1].Items.OfType<MenuItem>().ToList();
        Assert.Equal(
            ["Context.edit.setEncoding:utf-8", "Context.edit.setEncoding:utf-8-bom", "Context.edit.setEncoding:utf-16le",
             "Context.edit.setEncoding:utf-16be", "Context.edit.setEncoding:windows-1252"],
            saveWith.Select(AutomationIds.Get));
        Assert.Equal(["UTF-8", "UTF-8 with BOM", "UTF-16 LE", "UTF-16 BE", "Western (Windows-1252)"], saveWith.Select(i => i.Header as string));
        Assert.Equal([true, false, false, false, false], saveWith.Select(i => i.IsChecked));
        Choose(saveWith[2]);
        h.Find<Control>("StatusBar.Encoding").ContextMenu!.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Core.Text.FileEncoding.Utf16Le, document.Encoding);
        Assert.Equal("UTF-16 LE", h.Status.Field("Encoding"));
        await h.ExecuteAsync("file.save");
        Assert.Equal([0xFF, 0xFE, (byte)'o', 0], File.ReadAllBytes(path)[..4]);
        Assert.False(document.IsModified);

        // "Reopen with Encoding…" opens the dialog for the active document.
        h.Click("StatusBar.Encoding");
        Choose(MenuOf(h, "Encoding")[0]);
        h.Find<Control>("StatusBar.Encoding").ContextMenu!.Close();
        var dlg = await h.WaitForDialogAsync("ReopenEncoding");
        Assert.Equal("A.pas is currently read as: UTF-16 LE", dlg.Find<TextBlock>("Current").Text);
        dlg.Press("Escape");
        await h.WaitForAsync(() => !dlg.Window.IsVisible, "dialog closed");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-042")]
    public async Task ArrowKeysMoveBetweenFields()
    {
        await using var h = await HeadlessHarness.StartAsync();
        h.Services.Status.SetEditor(new EditorStatus(1, 1, 0, 0, false, true, 2, LineEndingKind.CrLf, "utf-8", "Object Pascal"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Find<Button>("StatusBar.State").Focusable); // a click leaves focus where it was

        h.Press("Shift+F6"); // from outside every pane, backwards: the last pane is the status bar
        Assert.Equal("StatusBar.State", h.FocusedAutomationId);
        h.Press("Right");
        Assert.Equal("StatusBar.CaretPosition", h.FocusedAutomationId);
        h.Press("Right");
        Assert.Equal("StatusBar.InsertMode", h.FocusedAutomationId);
        h.Press("Right");
        Assert.Equal("StatusBar.Indent", h.FocusedAutomationId);
        h.Press("Right");
        Assert.Equal("StatusBar.LineEnding", h.FocusedAutomationId); // R6/R7 are menu buttons (I-082)
        h.Press("Right");
        Assert.Equal("StatusBar.Encoding", h.FocusedAutomationId);
        h.Press("Right"); // the last field: stays
        Assert.Equal("StatusBar.Encoding", h.FocusedAutomationId);
        h.Press("Left");
        Assert.Equal("StatusBar.LineEnding", h.FocusedAutomationId);
        h.Press("Left");
        h.Press("Left");
        Assert.Equal("StatusBar.InsertMode", h.FocusedAutomationId);

        h.Press("Escape"); // back to the document, like the toolbar row
        Assert.Equal(AutomationIds.DocumentArea, h.FocusedAutomationId);
        Assert.False(h.Find<Button>("StatusBar.State").Focusable);
    }
}
