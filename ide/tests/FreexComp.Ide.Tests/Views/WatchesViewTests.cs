using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Watches tool window (I-143; tool-windows.md §13, the debug-session interface §6.1): the user's expression list
/// with in-place adding and editing, evaluation in the selected frame (<c>EvaluateContext.Watch</c>), errors, children,
/// in-place modification, deletion and persistence in the project session — over the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class WatchesViewTests
{
    private static string Row(DebugGridRow row) => $"{row.Key}: {row.Cells[0].Text} = {row.Cells[1].Text} : {row.Cells[2].Text}";

    private static IEnumerable<string> Watches(WatchesViewModel watches) => watches.Rows.Where(r => r.Key != WatchesViewModel.NewKey).Select(Row);

    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-1100")]
    [Trait("Req", "UI-TW-1101")]
    [Trait("Req", "UI-TW-1102")]
    [Trait("Req", "UI-TW-1103")]
    [Trait("Req", "UI-TW-1104")]
    [Trait("Req", "UI-TW-1105")]
    [Trait("Req", "UI-TW-1106")]
    [Trait("Req", "IDE-ARCH-076")]
    public async Task AddEditDeleteAndEvaluate()
    {
        await using var h = await DebugScenario.StartDemoAsync();
        var demo = DebugScenario.DemoPath(h);
        var debug = h.Services.Debug!;
        var watches = h.Services.DebugShell!.Watches;
        var locals = h.Services.DebugShell.Locals;
        var commands = h.Services.Commands;
        var list = debug.Watches;
        h.DebugSessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("entry")}} ] },
              {{DebugDemo.AtStop(demo)}},
              {{DebugDemo.Scopes}},
              { "on": "next", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("step")}} ] },
              {{DebugDemo.AtStop(demo, 72)}},
              {{DebugScenarios.Continue()}} ] }
            """);
        TextBox Editor(string key) => h.Find<TextBox>($"Tool.Watches.Row.{key}.Editor");
        void ShowWatches()
        {
            Assert.True(h.Services.ToolWindows.Show(WatchesViewModel.ToolName, focus: true));
            Dispatcher.UIThread.RunJobs();
        }

        // UI-TW-1100 / UI-TW-1104: visible without a session; the empty state above the trailing placeholder row.
        ShowWatches();
        Assert.Equal(["Expression", "Value", "Type"], watches.Columns.Select(c => c.Id));
        Assert.Equal(Strings.Msg_Debug_WatchesEmpty, watches.EmptyHeading);
        Assert.True(h.Find<Control>("Tool.Watches.Empty").IsEffectivelyVisible);
        Assert.Equal([WatchesViewModel.NewKey], watches.Rows.Select(r => r.Key));
        Assert.Equal("Add an expression", DebugScenario.Cell(h, "Watches", "$new", "Expression"));
        Assert.Equal(DebugCellStyle.Tertiary, watches.Rows[0].Cells[0].Style);

        // Clicking the placeholder row starts watches.add: an empty row whose Expression cell is in edit mode; Enter commits.
        h.Click("Tool.Watches.Row.$new");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("0", "Expression", ""), (watches.Edit!.RowKey, watches.Edit.Column, watches.Edit.Text));
        Assert.Equal(["0", WatchesViewModel.NewKey], watches.Rows.Select(r => r.Key));
        Assert.True(Editor("0").IsFocused);
        Editor("0").Text = " Total ";
        h.Press("Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(watches.Edit);
        Assert.Equal(["Total"], list.Expressions);
        Assert.Null(watches.EmptyHeading);

        // UI-TW-1101: without a session the value is <not available> in text.tertiary and the type is empty.
        Assert.Equal(["0: Total = <not available> : "], Watches(watches));
        Assert.Equal(Strings.Msg_Debug_WatchNotAvailable, DebugScenario.Cell(h, "Watches", "0", "Value"));
        Assert.Equal(DebugCellStyle.Tertiary, watches.Rows[0].Cells[1].Style);

        // UI-TW-1103: Insert (watches.add inside the tool window) appends a row in edit mode; Escape cancels and removes it;
        // an empty expression removes it too.
        h.Click("Tool.Watches.Row.0.Expression");
        h.Press("Insert");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("1", watches.Edit!.RowKey);
        Assert.Empty(h.AllWindows.Skip(1)); // in place: no dialog
        Editor("1").Text = "P";
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(watches.Edit);
        Assert.Equal(["0", WatchesViewModel.NewKey], watches.Rows.Select(r => r.Key));
        h.Click("Tool.Watches.Row.0.Expression");
        h.Press("Insert");
        Dispatcher.UIThread.RunJobs();
        Editor("1").Text = "   ";
        h.Press("Enter");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Total"], list.Expressions);
        foreach (var expression in (string[])["P", "Foo", "Total * 2", "Total *"])
        {
            h.Click("Tool.Watches.Row.$new");
            Dispatcher.UIThread.RunJobs();
            Editor(watches.Edit!.RowKey).Text = expression;
            h.Press("Enter");
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(["Total", "P", "Foo", "Total * 2", "Total *"], list.Expressions); // creation order

        // UI-TW-1100, IDE-ARCH-076: the list is stored per project in session.json.
        h.Services.Session.Save();
        using (var session = JsonDocument.Parse(File.ReadAllText(h.Services.Session.SessionPath)))
            Assert.Equal(["Total", "P", "Foo", "Total * 2", "Total *"], session.RootElement.GetProperty("watches").EnumerateArray().Select(w => w.GetString()));

        // Paused: every expression is evaluated in the selected frame; errors show the evaluator's message in status.error.
        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);
        ShowWatches();
        Assert.Equal(
        [
            "0: Total = 24 : Integer", "1: P = (X: 3; Y: 4) : TPoint", "2: Foo = Undeclared identifier 'Foo' : ", "3: Total * 2 = 48 : Integer",
            "4: Total * = Expression expected at column 8 : ",
        ], Watches(watches));
        Assert.Equal([DebugCellStyle.Code, DebugCellStyle.Code, DebugCellStyle.Error, DebugCellStyle.Code, DebugCellStyle.Error], watches.Rows.Take(5).Select(r => r.Cells[1].Style));
        Assert.Equal("48", DebugScenario.Cell(h, "Watches", "3", "Value"));
        Assert.Equal(WatchesViewModel.NewKey, watches.Rows[^1].Key);

        // UI-TW-1102 / UI-TW-1106: children as in Locals; a child's key is the watch's index plus the member path.
        h.Click("Tool.Watches.Row.1.Expander");
        await DebugScenario.SettleAsync(h);
        Assert.Equal(["1.X: X = 3 : Integer", "1.Y: Y = 4 : Integer"], watches.Rows.Where(r => r.Level == 1).Select(Row));
        Assert.Equal("3", DebugScenario.Cell(h, "Watches", "1.X", "Value"));

        // In-place value modification as in Locals (locals.modifyValue acts on the window that has focus): every value
        // is read again — the dependent watch and Locals too.
        h.Click("Tool.Watches.Row.0.Expression");
        Assert.True(commands.CanExecute("locals.modifyValue"));
        await h.ExecuteAsync("locals.modifyValue");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("0", "Value", "24"), (watches.Edit!.RowKey, watches.Edit.Column, watches.Edit.Text));
        Editor("0").Text = "50";
        h.Press("Enter");
        await h.WaitForAsync(() => watches.Edit is null, "the edit committed");
        await DebugScenario.SettleAsync(h);
        Assert.Equal(("0: Total = 50 : Integer", "3: Total * 2 = 100 : Integer"), (Row(watches.Rows[0]), Row(watches.Rows.Single(r => r.Key == "3"))));
        Assert.Equal("50", locals.Rows.Single(r => r.Key == "Total").Cells[1].Text);
        Assert.True(watches.Rows.Single(r => r.Key == "1").IsExpanded); // the expansion is kept
        h.Click("Tool.Watches.Row.2.Expression"); // an error row has no value to modify or expand
        Assert.False(commands.CanExecute("locals.modifyValue"));
        Assert.Null(watches.Rows.Single(r => r.Key == "2").IsExpanded);

        // watches.edit (F2): the Expression cell in place, pre-filled; the new expression is evaluated.
        h.Click("Tool.Watches.Row.2.Expression");
        Assert.True(commands.CanExecute("watches.edit"));
        h.Press("F2");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("2", "Expression", "Foo"), (watches.Edit!.RowKey, watches.Edit.Column, watches.Edit.Text));
        Editor("2").Text = "Total + 1";
        h.Press("Enter");
        await DebugScenario.SettleAsync(h);
        Assert.Equal(["Total", "P", "Total + 1", "Total * 2", "Total *"], list.Expressions);
        Assert.Equal("2: Total + 1 = 51 : Integer", Row(watches.Rows.Single(r => r.Key == "2")));
        // Double-click on an Expression cell edits it too.
        h.DoubleClick("Tool.Watches.Row.4.Expression");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("4", "Total *"), (watches.Edit!.RowKey, Editor("4").Text));
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(watches.Edit);

        // The changed-value highlight against the previous stop, as in Locals: the program changes Total, then stops again.
        await debug.Session!.SetValueAsync("Total", "7", null);
        h.Press("F10");
        await DebugScenario.WaitForStopAsync(h, 72);
        Assert.Equal(["0: Total = 7 : Integer", "2: Total + 1 = 8 : Integer", "3: Total * 2 = 14 : Integer"],
            watches.Rows.Where(r => r.Cells[1].Style == DebugCellStyle.Changed).Select(Row));
        Assert.Equal("Total, changed", watches.Rows[0].AutomationName);
        Assert.Equal(DebugCellStyle.Code, watches.Rows.Single(r => r.Key == "1").Cells[1].Style);

        // UI-TW-1105: multi-selection of top-level rows; Delete (watches.delete) removes them.
        ShowWatches();
        Assert.Equal(SelectionMode.Multiple, h.Find<ListBox>("Tool.Watches.List").SelectionMode & SelectionMode.Multiple);
        h.Click("Tool.Watches.Row.2.Expression");
        watches.SelectedKeys = ["2", "4", "1.X"]; // a child row is not a watch
        Assert.True(commands.CanExecute("watches.delete"));
        h.Press("Delete");
        await DebugScenario.SettleAsync(h);
        Assert.Equal(["Total", "P", "Total * 2"], list.Expressions);
        Assert.Equal(["0: Total = 7 : Integer", "1: P = (X: 3; Y: 4) : TPoint", "2: Total * 2 = 14 : Integer"], Watches(watches).Where(r => !r.StartsWith("1.", StringComparison.Ordinal)));

        // Running: <not available> again.
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["0: Total = <not available> : ", "1: P = <not available> : ", "2: Total * 2 = <not available> : "], Watches(watches));
        Assert.False(commands.CanExecute("locals.modifyValue"));
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());

        // Tool bar and context menu of tool-windows.md §13.
        Assert.Equal(["watches.add", "watches.edit", "watches.delete", "watches.deleteAll"], watches.ToolBarCommands.Select(c => c.CommandId));
        Assert.Equal(["watches.add", "watches.edit", "watches.delete", "watches.deleteAll", null, "locals.modifyValue", "locals.copyValue", null, "run.evaluate"], watches.ContextCommands);
        Assert.NotNull(h.TryFind<Control>("Tool.Watches.ToolBar.watches.add"));

        // The watches belong to the project: gone when it closes, back when it opens again.
        Assert.True(await h.Services.Projects.CloseAsync());
        Assert.Empty(list.Expressions);
        Assert.True(await h.Services.Projects.OpenAsync(BuildScenario.ProjectPath(h)));
        Assert.Equal(["Total", "P", "Total * 2"], list.Expressions);
        Assert.Equal(["0: Total = <not available> : ", "1: P = <not available> : ", "2: Total * 2 = <not available> : "], Watches(watches));

        // watches.deleteAll removes all without confirmation.
        Assert.True(commands.CanExecute("watches.deleteAll"));
        await h.ExecuteAsync("watches.deleteAll");
        Assert.Empty(list.Expressions);
        Assert.Empty(h.Messages.Requests);
        Assert.Equal(Strings.Msg_Debug_WatchesEmpty, watches.EmptyHeading);
        Assert.False(commands.CanExecute("watches.deleteAll"));
    }
}
