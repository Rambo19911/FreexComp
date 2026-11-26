using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Breakpoints tool window (I-144; tool-windows.md §10, the debug-session interface §6.1): one row per source
/// breakpoint of the project with its columns, with or without a session, and its commands.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class BreakpointsViewTests
{
    private static string Row(DebugGridRow row) =>
        $"{(row.Cells[0].Checked == true ? "x" : "-")} {row.Cells[1].Glyph} | {row.Cells[2].Text} | {row.Cells[3].Text} | {row.Cells[4].Text} | {row.Cells[5].Text}";

    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-800")]
    [Trait("Req", "UI-TW-801")]
    [Trait("Req", "UI-TW-802")]
    [Trait("Req", "UI-TW-803")]
    [Trait("Req", "UI-TW-804")]
    public async Task RowsColumnsAndCommands()
    {
        await using var h = await DebugScenario.StartAsync(o => o.InitialFiles[@"Hello\sub\Unit1.pas"] = "unit Unit1;\r\ninterface\r\nimplementation\r\nbegin\r\n  X;\r\nend.\r\n");
        var unitPath = DebugScenario.UnitPath(h);
        var mainPath = DebugScenario.MainPath(h);
        var otherUnit = Path.Combine(h.TempDir, @"Hello\sub\Unit1.pas");
        var breakpoints = h.Services.Breakpoints!;
        var view = h.Services.DebugShell!.Breakpoints;
        var commands = h.Services.Commands;
        void Show()
        {
            h.Services.Layout.Layout.BottomSize = 400; // room for every row of the test (rows outside the view are not drawn)
            Assert.True(h.Services.ToolWindows.Show(BreakpointsViewModel.ToolName, focus: true));
            Dispatcher.UIThread.RunJobs();
        }

        // Without breakpoints: the empty state.
        Show();
        Assert.Equal(Strings.Msg_Debug_BreakpointsEmpty, view.EmptyHeading);
        Assert.True(h.Find<Control>("Tool.Breakpoints.Empty").IsEffectivelyVisible);
        Assert.Empty(view.Rows);

        // UI-TW-800: every source breakpoint of the project, in creation order, visible and editable without a session.
        breakpoints.Toggle(unitPath, 5);
        breakpoints.Set(new BreakpointInfo(unitPath, 6, Condition: "Foo1 > 10", HitCount: BreakpointModel.HitCountOf(5)));
        breakpoints.Set(new BreakpointInfo(mainPath, 3, Enabled: false));
        breakpoints.Toggle(otherUnit, 5);
        breakpoints.Set(new BreakpointInfo(mainPath, 4, HitCount: new HitCountSpec(HitCountMode.Equal, 3)));
        breakpoints.Set(new BreakpointInfo(mainPath, 2, HitCount: new HitCountSpec(HitCountMode.Multiple, 2)));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(view.EmptyHeading);
        Assert.Equal(["Enabled", "State", "Location", "Condition", "PassCount", "Hits"], view.Columns.Select(c => c.Id));
        Assert.Equal(
        [
            "x Breakpoint | Unit1.pas:5 |  |  | 0",
            "x BreakpointConditional | Unit1.pas:6 | Foo1 > 10 | 5 | 0",
            "- BreakpointDisabled | Hello.pas:3 |  |  | 0",
            "x Breakpoint | Unit1.pas:5 |  |  | 0",
            "x BreakpointConditional | Hello.pas:4 |  | = 3 | 0", // the other hit-count modes read-only (the debug-session interface §6.1)
            "x BreakpointConditional | Hello.pas:2 |  | every 2 | 0",
        ], view.Rows.Select(Row));

        // UI-TW-804: the row key is <file name>:<line>; equal names in different folders get @<n> in creation order.
        Assert.Equal(["Unit1.pas:5", "Unit1.pas:6", "Hello.pas:3", "Unit1.pas:5@2", "Hello.pas:4", "Hello.pas:2"], view.Rows.Select(r => r.Key));
        Assert.Equal(("Unit1.pas:6", "Foo1 > 10", "5", "0"), (DebugScenario.Cell(h, "Breakpoints", "Unit1.pas:6", "Location"),
            DebugScenario.Cell(h, "Breakpoints", "Unit1.pas:6", "Condition"), DebugScenario.Cell(h, "Breakpoints", "Unit1.pas:6", "PassCount"), DebugScenario.Cell(h, "Breakpoints", "Unit1.pas:6", "Hits")));
        Assert.Equal(unitPath, ToolTip.GetTip(h.Find<TextBlock>("Tool.Breakpoints.Row.Unit1.pas:5.Location"))); // tooltip = full path
        Assert.Equal(otherUnit, ToolTip.GetTip(h.Find<TextBlock>("Tool.Breakpoints.Row.Unit1.pas:5@2.Location")));
        Assert.Equal(LineGlyphKind.BreakpointConditional, h.Find<DebugGlyph>("Tool.Breakpoints.Row.Unit1.pas:6.State").Kind); // the editor's glyph
        Assert.Equal(LineGlyphKind.BreakpointDisabled, h.Find<DebugGlyph>("Tool.Breakpoints.Row.Hello.pas:3.State").Kind);
        Assert.True(h.Find<CheckBox>("Tool.Breakpoints.Row.Unit1.pas:5.Enabled").IsChecked);
        Assert.False(h.Find<CheckBox>("Tool.Breakpoints.Row.Hello.pas:3.Enabled").IsChecked);
        Assert.Contains("codeSmall", h.Find<TextBlock>("Tool.Breakpoints.Row.Unit1.pas:6.Condition").Classes);

        // The check box and Space toggle the row's breakpoint (run.toggleBreakpointEnabled for the row).
        h.Click("Tool.Breakpoints.Row.Hello.pas:3.Enabled");
        Assert.True(breakpoints.Find(mainPath, 3)!.Enabled);
        h.Click("Tool.Breakpoints.Row.Hello.pas:3.Location");
        Assert.Equal(["Hello.pas:3"], view.SelectedKeys);
        h.Press("Space");
        Assert.False(breakpoints.Find(mainPath, 3)!.Enabled);
        Assert.Equal(["Hello.pas:3"], view.SelectedKeys); // the selection survives the redraw

        // The row commands of the context menu act on the selected row, not on the editor's caret.
        await h.ExecuteAsync("run.toggleBreakpointEnabled");
        Assert.True(breakpoints.Find(mainPath, 3)!.Enabled);
        await h.ExecuteAsync("run.toggleBreakpointEnabled");
        Assert.True(commands.CanExecute("run.breakpointProperties"));
        var opening = commands.ExecuteAsync("run.breakpointProperties");
        var dialog = await h.WaitForDialogAsync("Breakpoint");
        Assert.Equal((mainPath, "3"), (dialog.Find<TextBox>("File").Text, dialog.Find<TextBox>("Line").Text));
        dialog.Press("Escape");
        await h.WaitForAsync(() => !dialog.Window.IsVisible, "dialog canceled");
        await opening;
        Assert.Equal(
            ["breakpoints.goToSource", null, "run.toggleBreakpointEnabled", "run.breakpointProperties", null, "breakpoints.delete", "run.deleteAllBreakpoints", null,
                "breakpoints.enableAll", "breakpoints.disableAll", null, "run.addBreakpoint"],
            view.ContextCommands);
        Assert.Equal(["run.addBreakpoint", "breakpoints.delete", "run.deleteAllBreakpoints", "breakpoints.enableAll", "breakpoints.disableAll"], view.ToolBarCommands.Select(c => c.CommandId));
        Assert.NotNull(h.TryFind<Control>("Tool.Breakpoints.ToolBar.breakpoints.enableAll"));

        // Sorting by Location: file name, then line numerically; back to creation order.
        h.Click("Tool.Breakpoints.Column.Location");
        Assert.Equal(["Hello.pas:2", "Hello.pas:3", "Hello.pas:4", "Unit1.pas:5", "Unit1.pas:5@2", "Unit1.pas:6"], view.Rows.Select(r => r.Key));
        h.Click("Tool.Breakpoints.Column.Location");
        h.Click("Tool.Breakpoints.Column.Location");
        Assert.Equal(["Unit1.pas:5", "Unit1.pas:6", "Hello.pas:3", "Unit1.pas:5@2", "Hello.pas:4", "Hello.pas:2"], view.Rows.Select(r => r.Key));

        // Disable All / Enable All.
        await h.ExecuteAsync("breakpoints.disableAll");
        Assert.All(breakpoints.All, b => Assert.False(b.Enabled));
        Assert.False(commands.CanExecute("breakpoints.disableAll"));
        await h.ExecuteAsync("breakpoints.enableAll");
        Assert.All(breakpoints.All, b => Assert.True(b.Enabled));
        Assert.False(commands.CanExecute("breakpoints.enableAll"));

        // UI-TW-801: the default action opens the file at the breakpoint's line, the caret on the first non-blank
        // character, and focuses the editor.
        Show();
        h.DoubleClick("Tool.Breakpoints.Row.Unit1.pas:6.Location");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unitPath, "the breakpoint's file shown");
        Dispatcher.UIThread.RunJobs();
        var caret = ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret;
        Assert.Equal((6, 3), (caret.Line, caret.Column)); // "  Foo := 1;"
        Assert.Equal("Editor.Unit1.pas", h.FocusedAutomationId);
        Show();
        h.Click("Tool.Breakpoints.Row.Hello.pas:3.Location");
        h.Press("Enter"); // breakpoints.goToSource
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == mainPath, "the breakpoint's file shown");
        Assert.Equal(3, ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret.Line);

        // UI-TW-802: multi-selection; Delete deletes the selected rows without confirmation and selects the next row.
        Show();
        Assert.True(h.Find<ListBox>("Tool.Breakpoints.List").SelectionMode.HasFlag(SelectionMode.Multiple));
        h.Click("Tool.Breakpoints.Row.Unit1.pas:5@2.Location");
        view.SelectedKeys = ["Unit1.pas:5@2", "Hello.pas:4"];
        h.Press("Delete");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(h.Messages.Requests);
        Assert.Equal(["Unit1.pas:5", "Unit1.pas:6", "Hello.pas:3", "Hello.pas:2"], view.Rows.Select(r => r.Key));
        Assert.Equal(["Hello.pas:2"], view.SelectedKeys);
        h.Press("Delete"); // the last row: the previous one is selected
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Hello.pas:3"], view.SelectedKeys);
        Assert.Equal(3, breakpoints.All.Count);

        // A session: state, hits and the row the program is paused at (UI-TW-803).
        h.DebugSessions.UseScenario($$"""
            { "steps": [
              { "on": "setBreakpoints", "expectArguments": { "source": { "path": {{DebugScenarios.Json(mainPath)}} } },
                "respond": { "breakpoints": [ { "id": 13, "verified": true, "line": 3 } ] } },
              { "on": "setBreakpoints", "expectArguments": { "source": { "path": {{DebugScenarios.Json(unitPath)}} } },
                "respond": { "breakpoints": [ { "id": 11, "verified": true, "line": 5 }, { "id": 12, "verified": true, "line": 6 } ] } },
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent(more: """, "hitBreakpointIds": [11]""")}} ] },
              {{DebugScenarios.AtStop([("Run", unitPath, 5)])}},
              { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 11, "verified": true, "line": 5 } ] } },
              {{DebugScenarios.Continue()}} ] }
            """);
        h.Press("F5");
        await DebugScenario.WaitForStopAsync(h, 5);
        var current = view.Rows.Single(r => r.Key == "Unit1.pas:5");
        Assert.True(current.IsHighlighted);
        Assert.Equal("Unit1.pas:5, current", current.AutomationName);
        Assert.Equal("1", current.Cells[5].Text); // BreakpointStatus.HitCount
        Assert.Equal("Unit1.pas:5, current", AutomationProperties.GetName(h.Find<Control>("Tool.Breakpoints.Row.Unit1.pas:5")));
        var grid = h.Find<Grid>("Tool.Breakpoints.Row.Unit1.pas:5");
        Assert.True(grid.TryFindResource("FxcStatusWarningBgBrush", grid.ActualThemeVariant, out var warningBackground));
        Assert.Same(warningBackground, grid.Background);
        Assert.Equal(Avalonia.Media.FontWeight.SemiBold, h.Find<TextBlock>("Tool.Breakpoints.Row.Unit1.pas:5.Location").FontWeight);
        Assert.All(view.Rows.Where(r => r.Key != "Unit1.pas:5"), r => Assert.False(r.IsHighlighted));
        Assert.Equal("0", view.Rows.Single(r => r.Key == "Unit1.pas:6").Cells[5].Text);

        // A condition the debugger rejects: the unverified ring with the debugger's message as tooltip.
        var conditional = breakpoints.Find(unitPath, 6)!;
        breakpoints.Update(conditional, enabled: true, "Foo1 >", conditional.HitCount);
        await h.WaitForAsync(() => conditional.State == BreakpointState.Invalid, "the breakpoint's status");
        var invalid = view.Rows.Single(r => r.Key == "Unit1.pas:6");
        Assert.Equal((LineGlyphKind.BreakpointUnverified, conditional.Message), (invalid.Cells[1].Glyph, invalid.Cells[1].ToolTip));
        Assert.False(string.IsNullOrEmpty(conditional.Message));

        // Running: no row is the current one.
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(view.Rows, r => r.IsHighlighted);
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        await DebugScenario.WaitForScenarioCompleteAsync(h); // I-301: the line may be read after the session ended
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
        Assert.All(view.Rows, r => Assert.Equal("0", r.Cells[5].Text)); // 0 outside a session

        // run.deleteAllBreakpoints asks when more than one exists.
        h.Messages.Answer("Msg.Debug.DeleteAllBreakpoints", "Cancel");
        await h.ExecuteAsync("run.deleteAllBreakpoints");
        Assert.Equal(["3"], h.Messages.Requests[^1].Args);
        Assert.Equal(3, view.Rows.Count);
        h.Messages.Answer("Msg.Debug.DeleteAllBreakpoints", "Delete");
        await h.ExecuteAsync("run.deleteAllBreakpoints");
        Assert.Empty(view.Rows);
        Assert.Equal(Strings.Msg_Debug_BreakpointsEmpty, view.EmptyHeading);
        Assert.False(commands.CanExecute("breakpoints.delete"));
    }
}
