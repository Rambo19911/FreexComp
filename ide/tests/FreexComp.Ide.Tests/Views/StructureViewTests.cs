using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>The Structure tool window (I-124, tool-windows.md §3).</summary>
[Trait("Phase", "3")]
[Trait("Kind", "Ui")]
public sealed class StructureViewTests
{
    private const string Unit = "unit Main;\r\ninterface\r\nuses SysUtils;\r\ntype\r\n  TMainForm = class\r\n    procedure Greet;\r\n    procedure Alpha;\r\n  end;\r\n" +
                                "var\r\n  MainForm: TMainForm;\r\nimplementation\r\nprocedure TMainForm.Greet; begin end;\r\nprocedure TMainForm.Alpha; begin end;\r\nend.\r\n";

    private static List<string> RowKeys(HeadlessHarness h) =>
        [.. h.FindAll("Tool.Structure.Row.").Select(c => AutomationProperties.GetAutomationId(c)!)
            .Where(id => !id.EndsWith(".Message", StringComparison.Ordinal) && !id.EndsWith(".Position", StringComparison.Ordinal))
            .Select(id => id["Tool.Structure.Row.".Length..])];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TW-200")]
    [Trait("Req", "UI-TW-201")]
    [Trait("Req", "UI-TW-204")]
    [Trait("Req", "UI-TW-205")]
    [Trait("Req", "UI-TW-206")]
    [Trait("Req", "IDE-ED-131")]
    [Trait("Req", "IDE-ED-132")]
    public async Task GroupsAndCaretTracking()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.ImplementedPhase = 3;
            o.InitialFiles["Main.pas"] = Unit;
            o.InitialFiles["notes.txt"] = "plain";
        });
        await h.ExecuteAsync("view.structure");
        var empty = h.Find<TextBlock>("Tool.Structure.Empty");
        Assert.True(empty.IsVisible); // no document
        Assert.Equal(Core.Resources.Strings.Msg_Structure_Empty, empty.Text);

        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "Main.pas"))).Success);
        await h.WaitForAsync(() => RowKeys(h).Count > 0, "structure built on document switch");
        Assert.False(empty.IsVisible);
        Assert.Equal(["Uses", "Uses/SysUtils", "Types", "Types/TMainForm", "Types/TMainForm/Greet", "Types/TMainForm/Alpha", "Variables",
            "Variables/MainForm", "Routines", "Routines/TMainForm.Greet", "Routines/TMainForm.Alpha"], RowKeys(h));

        // The buffer, not the disk: an unsaved syntax error appears 400 ms after the edit, Errors first with its position.
        var editor = h.Find<TextEditor>("Editor.Main.pas");
        editor.TextArea.Focus();
        editor.Document.Insert(editor.Document.Text.IndexOf("var", StringComparison.Ordinal), "const X = ;\r\n");
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("Errors", RowKeys(h));
        h.Clock.Advance(StructureViewModel.RefreshDelay);
        await h.WaitForAsync(() => RowKeys(h).FirstOrDefault() == "Errors", "errors group first");
        Assert.Matches(@"^\d+:\d+$", h.Find<TextBlock>("Tool.Structure.Row.Errors/0.Position").Text);
        editor.Document.Text = Unit;
        h.Clock.Advance(StructureViewModel.RefreshDelay);
        await h.WaitForAsync(() => RowKeys(h).FirstOrDefault() == "Uses", "errors gone");

        // Caret tracking: 300 ms after the caret rests the innermost declaration is selected; the editor keeps the focus.
        var model = (StructureViewModel)h.Find<ListBox>("Tool.Structure.List").DataContext!;
        editor.TextArea.Caret.Offset = editor.Document.Text.IndexOf("Alpha;", StringComparison.Ordinal) + 2;
        Dispatcher.UIThread.RunJobs();
        h.Clock.Advance(StructureViewModel.TrackDelay);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Types/TMainForm/Alpha", model.SelectedRow?.Node.Key);
        Assert.Equal("Editor.Main.pas", h.FocusedAutomationId);

        // Go to source (Enter): caret on the name, editor focused.
        var before = editor.Document.Text;
        h.Click("Tool.Structure.Row.Variables/MainForm"); // selecting does not move the caret
        Assert.Equal("Tool.Structure.List", h.FocusedAutomationId);
        Assert.Equal("Variables/MainForm", model.SelectedRow?.Node.Key);
        h.Press("Enter");
        await h.WaitForAsync(() => h.FocusedAutomationId == "Editor.Main.pas", "editor focused");
        Assert.Equal(before, editor.Document.Text);
        Assert.Equal(editor.Document.Text.IndexOf("MainForm:", StringComparison.Ordinal), editor.TextArea.Caret.Offset);

        // Collapse a type (kept by key across refreshes), then sort alphabetically (persisted).
        model.SetExpanded(model.Rows.Single(r => r.Node.Key == "Types/TMainForm"), false);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("Types/TMainForm/Greet", RowKeys(h));
        await h.ExecuteAsync("structure.sortAlphabetically");
        Assert.Equal(true, h.Services.Layout.GetUiFlag("toolWindows.Structure", "sorted"));
        Assert.DoesNotContain("Types/TMainForm/Greet", RowKeys(h));
        var routines = RowKeys(h).Where(k => k.StartsWith("Routines/", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Routines/TMainForm.Alpha", "Routines/TMainForm.Greet"], routines);

        // Another document type: the empty state.
        Assert.True((await h.Services.Documents.OpenAsync(Path.Combine(h.TempDir, "notes.txt"))).Success);
        Dispatcher.UIThread.RunJobs();
        Assert.True(empty.IsVisible);
    }
    /// <summary>
    /// DSG-SUR-191, UI-DSG-082 (D-111): in the Design view a row's context menu is the designer context menu — for a
    /// component with designer verbs the verb group comes first (<c>Context.Verb.&lt;id&gt;</c>, a separator, then Cut);
    /// the verb runs on that component; a component without verbs starts with Cut.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-191")]
    [Trait("Req", "UI-DSG-082")]
    public async Task ContextMenuShowsVerbs()
    {
        await using var h = await HeadlessHarness.StartAsync(Views.DesignerViewTests.EditorFiles);
        var document = await Views.DesignerViewTests.OpenFormAsync(h);
        h.Click(AutomationIds.DocumentViewTab("MainForm.pas", "Design"));
        await h.ExecuteAsync("view.structure");
        Dispatcher.UIThread.RunJobs();
        var list = h.Find<ListBox>("Tool.Structure.List");
        static string Id(object item) => item is Separator ? "-" : AutomationIds.Get((Control)item)!;

        // The action list's row: selected by the right-click, the verb group first.
        h.RightClick("Tool.Structure.Row.Form/ActionList1");
        Assert.Equal(["ActionList1"], document.Designer.Surface!.Selection.Items.Select(o => o.Name));
        var menu = Assert.IsType<ContextMenu>(list.ContextMenu);
        Assert.True(menu.IsOpen);
        Assert.Equal(["Context.Verb.actionListEditor", "-", "Context.edit.cut"], menu.Items.Cast<object>().Select(Id).Take(3));
        var verb = menu.Items.OfType<MenuItem>().First();
        Assert.Equal("_Action List Editor…", verb.Header);
        Assert.True(verb.IsEffectivelyEnabled);

        // The verb opens the action list's editor; Close only closes.
        menu.Close();
        verb.Command!.Execute(verb.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        var dlg = await h.WaitForDialogAsync("ChildListEditor");
        Assert.Equal("Edit ActionList1", dlg.Window.Title);
        dlg.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, document.Designer.Undo.UndoCount);

        // A component without verbs: the standard designer menu.
        h.RightClick("Tool.Structure.Row.Form/GreetButton");
        Assert.Equal(["GreetButton"], document.Designer.Surface.Selection.Items.Select(o => o.Name));
        menu = Assert.IsType<ContextMenu>(list.ContextMenu);
        Assert.Equal("Context.edit.cut", Id(menu.Items[0]!));
        menu.Close();
    }
}
