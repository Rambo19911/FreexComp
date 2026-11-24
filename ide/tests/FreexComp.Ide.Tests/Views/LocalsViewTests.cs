using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Locals tool window (I-143; tool-windows.md §12, the debug-session interface §6.1): the scopes of the selected
/// frame, lazily fetched children in ranges of 100, the changed-value highlight, kept expansion and in-place
/// modification — with sessions over the scripted adapter that read the demo program's memory.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class LocalsViewTests
{
    private static string StepTo(string command, (string, string, int)[] frames, string scopes) => $$"""
        { "on": "{{command}}", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("step")}} ] },
        {{DebugScenarios.AtStop(frames, scopes)}}
        """;

    private static string Row(DebugGridRow row) => $"{row.Cells[0].Text} = {row.Cells[1].Text} : {row.Cells[2].Text}";

    private static DebugGridRow RowOf(DebugGridViewModel grid, string key) => grid.Rows.Single(r => r.Key == key);

    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-1000")]
    [Trait("Req", "UI-TW-1001")]
    [Trait("Req", "UI-TW-1002")]
    [Trait("Req", "UI-TW-1003")]
    [Trait("Req", "UI-TW-1006")]
    [Trait("Req", "UI-TW-1007")]
    [Trait("Req", "DBG-251")]
    [Trait("Req", "DBG-259")]
    public async Task ScopesLazyChildrenAndChangedHighlight()
    {
        await using var h = await DebugScenario.StartDemoAsync();
        var demo = DebugScenario.DemoPath(h);
        var debug = h.Services.Debug!;
        var locals = h.Services.DebugShell!.Locals;
        // Run's dynamic array claims 250 elements (its memory holds 60): ranges of 100, and elements that cannot be read.
        var numsLength = 0x1F3A2B40108ul;
        h.DebugSessions.UseScenario($$"""
            { {{DebugDemo.Data((numsLength, "FA00000000000000"))}}, "steps": [
              {{DebugDemo.FirstStep(demo)}},
              {{DebugDemo.AtStop(demo)}},
              {{StepTo("next", DebugDemo.Frames(demo, 72), DebugDemo.Scopes)}},
              {{StepTo("next", DebugDemo.Frames(demo, 73), DebugDemo.Scopes)}},
              {{StepTo("stepIn", DebugDemo.FactorialFrames(demo, 73), DebugDemo.FactorialScopes)}},
              {{DebugDemo.Scopes}},
              {{DebugScenarios.Continue()}} ] }
            """);

        // No session, then running: the empty states.
        Assert.Equal(Strings.Msg_Debug_NoSession, locals.EmptyHeading);
        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);

        // UI-TW-1000: the variables of the selected frame (the top frame) in declaration order — name, value and type
        // verbatim from the debugger.
        Assert.Null(locals.EmptyHeading);
        Assert.Equal(["Name", "Value", "Type"], locals.Columns.Select(c => c.Id));
        Assert.Equal(
        [
            "I = 3 : Integer", "Total = 24 : Integer", "Ch = 'A' : Char", "Ok = True : Boolean", "Color = clGreen : TColor",
            "Colors = [clRed, clBlue] : TColors", "S = 'Héllo' : string", "P = (X: 3; Y: 4) : TPoint",
            "Nums = (10, 20, 30, 0, 0, 0, 0, 0, …) (Length: 250) : array of Integer", "Pet = TDog($000001F3A2B41C60) : TDog",
            "PTotal = $0000007F3A6FFB04 : ^Integer",
        ], locals.Rows.Select(Row));
        Assert.Equal(["I", "Total", "Ch", "Ok", "Color", "Colors", "S", "P", "Nums", "Pet", "PTotal"], locals.Rows.Select(r => r.Key)); // UI-TW-1007
        Assert.Equal(("Total", "24", "Integer"), (DebugScenario.Cell(h, "Locals", "Total", "Name"), DebugScenario.Cell(h, "Locals", "Total", "Value"), DebugScenario.Cell(h, "Locals", "Total", "Type")));
        Assert.Contains("codeSmall", h.Find<TextBlock>("Tool.Locals.Row.Total.Value").Classes);

        // UI-TW-1001: structured values are expandable, scalars are not; children are fetched on expand, with a
        // "Loading…" child row meanwhile.
        Assert.Equal([null, null, null, null, null, null, null, false, false, false, false], locals.Rows.Select(r => r.IsExpanded));
        Assert.Null(h.TryFind<Control>("Tool.Locals.Row.Total.Expander"));
        var snapshots = new List<string>();
        locals.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DebugGridViewModel.Rows))
                snapshots.Add(string.Join(",", locals.Rows.Where(r => r.Level > 0).Select(r => r.Key)));
        };
        h.Click("Tool.Locals.Row.P.Expander");
        await DebugScenario.SettleAsync(h);
        Assert.Equal(["P.$loading", "P.X,P.Y"], snapshots.Distinct());
        Assert.True(RowOf(locals, "P").IsExpanded);
        Assert.Equal(["X = 3 : Integer", "Y = 4 : Integer"], locals.Rows.Where(r => r.Level == 1).Select(Row));
        Assert.Equal("3", DebugScenario.Cell(h, "Locals", "P.X", "Value"));
        var indentP = h.Find<TextBlock>("Tool.Locals.Row.P.Name").TranslatePoint(default, h.Window)!.Value.X;
        var indentX = h.Find<TextBlock>("Tool.Locals.Row.P.X.Name").TranslatePoint(default, h.Window)!.Value.X;
        Assert.Equal(16, indentX - indentP); // space.6 per level

        // Class instances and pointers: fields / the pointed-to value, with path keys.
        locals.Toggle(RowOf(locals, "Pet"));
        locals.Toggle(RowOf(locals, "PTotal"));
        await DebugScenario.SettleAsync(h);
        Assert.Equal(["Name = 'Rex' : string", "Legs = 4 : Integer"], locals.Rows.Where(r => r.Key.StartsWith("Pet.", StringComparison.Ordinal)).Select(Row));
        Assert.Equal("^ = 24 : Integer", Row(RowOf(locals, "PTotal.^")));

        // More than 100 elements: ranges of 100 as intermediate expandable rows; a range fetches its page on expand.
        locals.Toggle(RowOf(locals, "Nums"));
        await DebugScenario.SettleAsync(h);
        Assert.Equal(["[0..99]", "[100..199]", "[200..249]"], locals.Rows.Where(r => r.Key.StartsWith("Nums[", StringComparison.Ordinal)).Select(r => r.Cells[0].Text));
        Assert.All(locals.Rows.Where(r => r.Key.StartsWith("Nums[", StringComparison.Ordinal)), r => Assert.Equal((false, 1), (r.IsExpanded, r.Level)));
        locals.Toggle(RowOf(locals, "Nums[0..99]"));
        await DebugScenario.SettleAsync(h);
        var elements = locals.Rows.Where(r => r.Level == 2).ToList();
        Assert.Equal(100, elements.Count);
        Assert.Equal(["[0] = 10 : Integer", "[1] = 20 : Integer", "[2] = 30 : Integer", "[3] = 0 : Integer"], elements.Take(4).Select(Row));
        Assert.Equal(("Nums[0]", "Nums[99]"), (elements[0].Key, elements[^1].Key)); // UI-TW-1007: Items[3]

        // UI-TW-1006: a value that cannot be read shows <unavailable> in text.tertiary with the reason as tooltip.
        locals.Toggle(RowOf(locals, "Nums[100..199]"));
        await DebugScenario.SettleAsync(h);
        var unreadable = RowOf(locals, "Nums[100]");
        Assert.Equal((Strings.Msg_Debug_Unavailable, DebugCellStyle.Tertiary, "<unreadable memory at $000001F3A2B402A0>", ""),
            (unreadable.Cells[1].Text, unreadable.Cells[1].Style, unreadable.Cells[1].ToolTip, unreadable.Cells[2].Text));
        Assert.Equal("<unavailable>", unreadable.Cells[1].Text);
        locals.Toggle(RowOf(locals, "Nums[100..199]"));
        locals.Toggle(RowOf(locals, "Nums[0..99]")); // collapsed again: no request, the rows go
        Assert.DoesNotContain(locals.Rows, r => r.Level == 2);
        Assert.DoesNotContain(locals.Rows, r => r.Cells[1].Style == DebugCellStyle.Changed);

        // The program changes Total and P.X before the next stop (written to its memory behind the IDE's back).
        await debug.Session!.SetValueAsync("Total", "99", null);
        await debug.Session.SetValueAsync("P.X", "9", null);
        Assert.Equal("24", RowOf(locals, "Total").Cells[1].Text);

        // UI-TW-1002: at the next stop in the same frame the rows whose value text differs are highlighted — bold,
        // status.error, automation name ", changed". UI-TW-1003: the expansion is kept by variable path.
        h.Press("F10");
        await DebugScenario.WaitForStopAsync(h, 72);
        Assert.Equal(["Total = 99", "P = (X: 9; Y: 4)", "X = 9", "^ = 99"],
            locals.Rows.Where(r => r.Cells[1].Style == DebugCellStyle.Changed).Select(r => $"{r.Cells[0].Text} = {r.Cells[1].Text}"));
        Assert.Equal(["Total, changed", "P, changed", "X, changed", "^, changed"], locals.Rows.Where(r => r.AutomationName.EndsWith(", changed", StringComparison.Ordinal)).Select(r => r.AutomationName));
        Assert.Equal(DebugCellStyle.Code, RowOf(locals, "I").Cells[1].Style);
        var changed = h.Find<TextBlock>("Tool.Locals.Row.Total.Value");
        Assert.Equal(("99", FontWeight.SemiBold), (changed.Text, changed.FontWeight));
        Assert.True(changed.TryFindResource("FxcStatusErrorBrush", changed.ActualThemeVariant, out var errorBrush));
        Assert.Same(errorBrush, changed.Foreground);
        Assert.Equal("Total, changed", AutomationProperties.GetName(h.Find<Control>("Tool.Locals.Row.Total")));
        Assert.Equal("I", AutomationProperties.GetName(h.Find<Control>("Tool.Locals.Row.I")));
        Assert.Equal((true, true, true, true), (RowOf(locals, "P").IsExpanded, RowOf(locals, "Pet").IsExpanded, RowOf(locals, "PTotal").IsExpanded, RowOf(locals, "Nums").IsExpanded));
        Assert.Equal((false, false), (RowOf(locals, "Nums[0..99]").IsExpanded, RowOf(locals, "Nums[100..199]").IsExpanded));
        Assert.Equal("Y = 4 : Integer", Row(RowOf(locals, "P.Y")));

        // … until the next stop: nothing changed since, nothing is highlighted.
        h.Press("F10");
        await DebugScenario.WaitForStopAsync(h, 73);
        Assert.DoesNotContain(locals.Rows, r => r.Cells[1].Style == DebugCellStyle.Changed);
        Assert.Equal("99", DebugScenario.Cell(h, "Locals", "Total", "Value"));
        Assert.Equal(FontWeight.Normal, h.Find<TextBlock>("Tool.Locals.Row.Total.Value").FontWeight);
        Assert.True(RowOf(locals, "P").IsExpanded);

        // Another routine: its parameters (the Arguments scope) first, then its locals with Result last; highlight and
        // expansion belong to the frame's routine and do not carry over.
        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.FactorialLine);
        Assert.Equal(["N = 2 : Integer", "Result = 1 : Integer"], locals.Rows.Select(Row));
        Assert.DoesNotContain(locals.Rows, r => r.Cells[1].Style == DebugCellStyle.Changed);

        // The selected frame: Locals shows the caller's variables (the frame's scopes are asked for).
        var frames = (await debug.GetFramesAsync())!.Frames;
        await debug.GoToFrameAsync(frames[1]);
        await DebugScenario.SettleAsync(h);
        Assert.Equal("Run", debug.SelectedFrame?.Name);
        Assert.Equal(11, locals.Rows.Count);
        Assert.Equal("Total = 99 : Integer", Row(RowOf(locals, "Total")));
        Assert.All(locals.Rows, r => Assert.NotEqual(true, r.IsExpanded));
        Assert.Contains("\"frameId\":101", h.DebugSessions.SentArguments("scopes")[^1], StringComparison.Ordinal);

        // Running: "Available while the program is paused".
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(locals.Rows);
        Assert.Equal(Strings.Msg_Debug_NotPaused, locals.EmptyHeading);
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Assert.Equal(Strings.Msg_Debug_NoSession, locals.EmptyHeading);
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
    }

    /// <summary>
    /// I-222: Enter in the Value editor when the session is gone already (it ended under the open editor; the IDE applies
    /// that with the session's next event) — the commit ends without a failure of its own, the editor closes and nothing
    /// is left under way.
    /// </summary>
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-1004")]
    public async Task ModifyAfterTheSessionWentAwayClosesTheEditor()
    {
        await using var h = await DebugScenario.StartDemoAsync();
        var demo = DebugScenario.DemoPath(h);
        var locals = h.Services.DebugShell!.Locals;
        h.DebugSessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              {{DebugDemo.FirstStep(demo)}},
              {{DebugDemo.AtStop(demo)}} ] }
            """);
        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);
        h.Click("Tool.Locals.Row.Total.Name");
        h.Press("F2");
        Dispatcher.UIThread.RunJobs();
        var edit = locals.Edit!;
        var editor = h.Find<TextBox>("Tool.Locals.Row.Total.Editor");

        // The session is being disposed; Enter comes in the same turn of the UI thread, before the IDE can apply the end
        // of the session (its events are posted to this thread).
        editor.Text = "99";
        var gone = h.DebugSessions.Last.DisposeAsync();
        Assert.Same(edit, locals.Edit);
        h.Press("Enter");
        var commit = edit.Commit;
        await gone;
        await h.WaitForAsync(() => commit.IsCompleted, "the commit ended");

        Assert.True(commit.IsCompletedSuccessfully, commit.Exception?.ToString());
        Assert.Null(locals.Edit);
        Assert.Null(h.TryFind<TextBox>("Tool.Locals.Row.Total.Editor"));
        Assert.Empty(h.DebugSessions.SentRequests("writeMemory"));
        await DebugScenario.SettleAsync(h);
    }

    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-1004")]
    [Trait("Req", "UI-TW-1005")]
    public async Task ModifyOnlyWhenCanModify()
    {
        await using var h = await DebugScenario.StartDemoAsync();
        var demo = DebugScenario.DemoPath(h);
        var debug = h.Services.Debug!;
        var locals = h.Services.DebugShell!.Locals;
        var commands = h.Services.Commands;
        var clipboard = (FakeClipboard)h.Services.Clipboard;
        h.DebugSessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              {{DebugDemo.FirstStep(demo)}},
              {{DebugDemo.AtStop(demo)}},
              {{DebugDemo.Scopes}}, {{DebugDemo.Scopes}} ] }
            """); // after each assignment the session lists the frame's variables again
        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, DebugDemo.StopLine);
        locals.Toggle(RowOf(locals, "PTotal")); // an alias of Total's memory
        await DebugScenario.SettleAsync(h);
        TextBox Editor(string key) => h.Find<TextBox>($"Tool.Locals.Row.{key}.Editor");

        // F2 (locals.modifyValue) on a scalar row: a text input in the Value cell, pre-filled with the value text.
        h.Click("Tool.Locals.Row.Total.Name");
        Assert.Equal(["Total"], locals.SelectedKeys);
        Assert.True(commands.CanExecute("locals.modifyValue"));
        h.Press("F2");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("Total", "Value", "24"), (locals.Edit!.RowKey, locals.Edit.Column, locals.Edit.Text));
        var editor = Editor("Total");
        Assert.Equal("24", editor.Text);
        Assert.True(editor.IsFocused);
        Assert.Null(h.TryFind<TextBlock>("Tool.Locals.Row.Total.Value"));

        // A value that cannot be assigned: the input stays open with Msg.Debug.ModifyFailed under it; nothing is written.
        editor.Text = "abc";
        h.Press("Enter");
        await h.WaitForAsync(() => locals.Edit?.Error is not null, "the failure is shown");
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Msg_Debug_ModifyFailed, "Undeclared identifier 'abc'"), locals.Edit!.Error);
        Assert.Equal("The value cannot be changed: Undeclared identifier 'abc'", h.Find<TextBlock>("Tool.Locals.Row.Total.Error").Text);
        Assert.True(h.Find<TextBlock>("Tool.Locals.Row.Total.Error").IsVisible);
        Assert.Same(editor, Editor("Total"));
        Assert.Empty(h.DebugSessions.SentRequests("writeMemory"));

        // Enter commits: SetValueAsync with the row's EvaluateName; every visible value is read again (the alias too).
        editor.Text = "99";
        h.Press("Enter");
        await h.WaitForAsync(() => locals.Edit is null, "the edit committed");
        await DebugScenario.SettleAsync(h);
        Assert.Single(h.DebugSessions.SentRequests("writeMemory"));
        Assert.Equal("99", DebugScenario.Cell(h, "Locals", "Total", "Value"));
        Assert.Equal("99", RowOf(locals, "PTotal.^").Cells[1].Text);
        Assert.Null(h.TryFind<TextBox>("Tool.Locals.Row.Total.Editor"));
        Assert.Equal("99", (await debug.EvaluateAsync("Total", EvaluateContext.Watch)).Value.Value);

        // Escape cancels: nothing is assigned.
        h.Click("Tool.Locals.Row.Total.Name");
        h.Press("F2");
        Dispatcher.UIThread.RunJobs();
        Editor("Total").Text = "5";
        h.Press("Escape");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(locals.Edit);
        Assert.Single(h.DebugSessions.SentRequests("writeMemory"));
        Assert.Equal("99", DebugScenario.Cell(h, "Locals", "Total", "Value"));

        // Double-click on the Value cell edits too.
        h.DoubleClick("Tool.Locals.Row.Ok.Value");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("Ok", "True"), (locals.Edit!.RowKey, Editor("Ok").Text));
        Editor("Ok").Text = "False";
        h.Press("Enter");
        await h.WaitForAsync(() => locals.Edit is null, "the edit committed");
        await DebugScenario.SettleAsync(h);
        Assert.Equal("False", DebugScenario.Cell(h, "Locals", "Ok", "Value"));

        // Rows with CanModify = false (strings, structured values) do not enter edit mode.
        foreach (var key in (string[])["S", "P"])
        {
            h.Click($"Tool.Locals.Row.{key}.Name");
            Assert.Equal([key], locals.SelectedKeys);
            Assert.False(commands.CanExecute("locals.modifyValue"));
            h.Press("F2");
            h.DoubleClick($"Tool.Locals.Row.{key}.Value");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(locals.Edit);
        }

        Assert.Equal(2, h.DebugSessions.SentRequests("writeMemory").Count);

        // The default action (double-click on Name, Enter) toggles the expansion.
        Assert.False(RowOf(locals, "P").IsExpanded);
        h.DoubleClick("Tool.Locals.Row.P.Name");
        await DebugScenario.SettleAsync(h);
        Assert.True(RowOf(locals, "P").IsExpanded);
        h.Click("Tool.Locals.Row.P.Name");
        h.Press("Enter");
        await DebugScenario.SettleAsync(h);
        Assert.False(RowOf(locals, "P").IsExpanded);
        h.Press("Right");
        await DebugScenario.SettleAsync(h);
        Assert.True(RowOf(locals, "P").IsExpanded);

        // UI-TW-1005: Ctrl+C (and locals.copyValue) copies the Value text of the selected row.
        h.Click("Tool.Locals.Row.S.Name");
        h.Press("Ctrl+C");
        Assert.Equal("'Héllo'", clipboard.Text);
        h.Click("Tool.Locals.Row.P.X.Name");
        await h.ExecuteAsync("locals.copyValue");
        Assert.Equal("3", clipboard.Text);

        // The context menu; Evaluate/Modify opens with the row's path expression, Add Watch adds it (and shows Watches).
        Assert.Equal(["locals.modifyValue", "locals.copyValue", null, "run.addWatch", "run.evaluate"], locals.ContextCommands);
        Assert.Equal("P.X", locals.SelectedExpression);
        var opening = commands.ExecuteAsync("run.evaluate");
        var dialog = await h.WaitForDialogAsync("Evaluate");
        Assert.Equal("P.X", dialog.Find<TextBox>("Expression").Text);
        await h.WaitForAsync(() => dialog.Find<TextBox>("Result").Text == "3", "the row's expression evaluated");
        dialog.Click("Close");
        await h.WaitForAsync(() => !dialog.Window.IsVisible, "dialog closed");
        await opening;
        h.Click("Tool.Locals.Row.P.X.Name");
        await h.ExecuteAsync("run.addWatch");
        Assert.Equal(["P.X"], debug.Watches.Expressions);
        await DebugScenario.SettleAsync(h);

        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
    }
}
