using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using FreexComp.Ide.Views;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Call Stack tool window (I-142; tool-windows.md §11, the debug-session interface §6.1) from
/// <c>StackTraceResult</c>: frames, markers, the selected frame, paging and copy — with a session over the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class CallStackViewTests
{
    private const int Total = 450;

    /// <summary>
    /// A <c>stackTrace</c> step with frames <paramref name="start"/> … of a stack of <see cref="Total"/> frames: Run in
    /// Unit1.pas, its caller in Hello.pas, a frame without symbol and source, then <c>Deep&lt;n&gt;</c> routines.
    /// </summary>
    private static string Frames(HeadlessHarness h, int start, int count)
    {
        string Frame(int index) => index switch
        {
            0 => $$"""{ "id": 1000, "name": "Run", "line": 5, "column": 1, "source": { "path": {{DebugScenarios.Json(DebugScenario.UnitPath(h))}} } }""",
            1 => $$"""{ "id": 1001, "name": "Hello", "line": 3, "column": 1, "source": { "path": {{DebugScenarios.Json(DebugScenario.MainPath(h))}} } }""",
            2 => """{ "id": 1002, "name": "", "line": 0, "column": 0, "instructionPointerReference": "0x7FF6A1B21C40" }""",
            _ => $$"""{ "id": {{1000 + index}}, "name": "Deep{{index}}", "line": 7, "column": 1, "source": { "path": {{DebugScenarios.Json(DebugScenario.UnitPath(h))}} } }""",
        };
        return $$"""{ "on": "stackTrace", "respond": { "totalFrames": {{Total}}, "stackFrames": [ {{string.Join(", ", Enumerable.Range(start, count).Select(Frame))}} ] } }""";
    }

    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-TW-900")]
    [Trait("Req", "UI-TW-901")]
    [Trait("Req", "UI-TW-902")]
    [Trait("Req", "UI-TW-903")]
    [Trait("Req", "UI-TW-904")]
    public async Task FramesSelectionAndPaging()
    {
        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var mainPath = DebugScenario.MainPath(h);
        var debug = h.Services.Debug!;
        var stack = h.Services.DebugShell!.CallStack;
        var clipboard = (FakeClipboard)h.Services.Clipboard;
        h.DebugSessions.UseScenario($$"""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("entry")}} ] },
                         {{DebugScenarios.Threads}},
                         {{Frames(h, 0, 20)}}, {{Frames(h, 0, 200)}},
                         {{DebugScenarios.NoScopes}}, {{DebugScenarios.NoScopes}},
                         {{Frames(h, 200, 200)}}, {{Frames(h, 400, 50)}},
                         {{DebugScenarios.Continue()}} ] }
            """);

        // No session: the empty state, no rows.
        Assert.True(h.Services.ToolWindows.Show(CallStackViewModel.ToolName, focus: false));
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Find<Control>("Tool.CallStack.Empty").IsEffectivelyVisible);
        Assert.False(h.Find<ListBox>("Tool.CallStack.List").IsEffectivelyVisible);
        Assert.Equal(Strings.Msg_Debug_NoSession, stack.EmptyHeading);

        h.Press("F11");
        await DebugScenario.WaitForStopAsync(h, 5);

        // UI-TW-900 / UI-TW-902: the frames of the current thread, innermost first; at most 200 are fetched, a last row
        // offers the rest; frame 0 is selected.
        var list = h.Find<ListBox>("Tool.CallStack.List");
        Assert.True(list.IsEffectivelyVisible);
        Assert.Equal(SelectionMode.Single, list.SelectionMode);
        Assert.Equal(["Marker", "Routine", "File", "Line"], stack.Columns.Select(c => c.Id));
        Assert.Equal(201, stack.Rows.Count);
        Assert.Equal(Enumerable.Range(0, 200).Select(i => i.ToString(CultureInfo.InvariantCulture)).Append(CallStackViewModel.MoreKey), stack.Rows.Select(r => r.Key)); // UI-TW-904
        Assert.Equal(["0"], stack.SelectedKeys);
        Assert.Equal("0", (list.SelectedItem as DebugGridRow)?.Key);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_MoreFrames, 250), stack.Rows[^1].Cells[1].Text);
        Assert.Equal("… 250 more frames (double-click to load)", stack.Rows[^1].Cells[1].Text);

        // The cells as the view shows them: routine verbatim, file name with the full path as tooltip, line; the arrow on frame 0.
        Assert.Equal(("Run", "Unit1.pas", "5"), (DebugScenario.Cell(h, "CallStack", "0", "Routine"), DebugScenario.Cell(h, "CallStack", "0", "File"), DebugScenario.Cell(h, "CallStack", "0", "Line")));
        Assert.Equal(unitPath, ToolTip.GetTip(h.Find<TextBlock>("Tool.CallStack.Row.0.File")));
        Assert.Equal(LineGlyphKind.Execution, h.Find<DebugGlyph>("Tool.CallStack.Row.0.Marker").Kind);
        Assert.Equal(("Hello", "Hello.pas", "3"), (DebugScenario.Cell(h, "CallStack", "1", "Routine"), DebugScenario.Cell(h, "CallStack", "1", "File"), DebugScenario.Cell(h, "CallStack", "1", "Line")));
        Assert.Null(stack.Rows[1].Cells[0].Glyph);

        // A frame without symbol and source: the address in Pascal notation, text.tertiary, empty cells, no default action.
        Assert.Equal(("$00007FF6A1B21C40", "", ""), (DebugScenario.Cell(h, "CallStack", "2", "Routine"), stack.Rows[2].Cells[2].Text, stack.Rows[2].Cells[3].Text));
        Assert.Equal(DebugCellStyle.Tertiary, stack.Rows[2].Cells[1].Style);
        h.Click("Tool.CallStack.Row.2.Routine");
        Assert.Equal(["2"], stack.SelectedKeys);
        Assert.False(h.Services.Commands.CanExecute("callStack.goToFrame"));
        h.DoubleClick("Tool.CallStack.Row.2.Routine");
        await DebugScenario.SettleAsync(h);
        Assert.Null(debug.SelectedFrame);

        // UI-TW-901: a single click only selects; the default action makes the frame the selected frame — the editor shows
        // its line with the call-frame marker, the row gets the hollow arrow, Locals asks for that frame's scopes.
        h.Click("Tool.CallStack.Row.1.Routine");
        Assert.Equal(["1"], stack.SelectedKeys);
        Assert.Null(debug.SelectedFrame);
        Assert.Equal(unitPath, h.Services.Documents.ActiveDocument?.FilePath);
        h.DoubleClick("Tool.CallStack.Row.1.Routine");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == mainPath, "the editor shows the frame");
        await DebugScenario.SettleAsync(h);
        Assert.Equal((1, "Hello"), (debug.SelectedFrame!.Index, debug.SelectedFrame.Name));
        Assert.Equal(3, ((TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret.Line);
        Assert.Equal("Frame", DebugScenario.ProbeOf(h, mainPath).GlyphAt(3));
        Assert.Equal("editor.callFrame.line.bg", DebugScenario.ProbeOf(h, mainPath).LineBackgroundAt(3));
        Assert.Equal(LineGlyphKind.Frame, h.Find<DebugGlyph>("Tool.CallStack.Row.1.Marker").Kind);
        Assert.Equal(LineGlyphKind.Execution, h.Find<DebugGlyph>("Tool.CallStack.Row.0.Marker").Kind);
        Assert.Equal(2, h.DebugSessions.SentRequests("scopes").Count);
        Assert.Contains("\"frameId\":1001", h.DebugSessions.SentRequests("scopes")[1], StringComparison.Ordinal);

        // Frame 0 (Enter = callStack.goToFrame) returns to the execution point.
        h.Click("Tool.CallStack.Row.0.Routine");
        Assert.True(h.Services.Commands.CanExecute("callStack.goToFrame"));
        h.Press("Enter");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unitPath, "the editor shows the execution point");
        await DebugScenario.SettleAsync(h);
        Assert.Null(debug.SelectedFrame);
        Assert.Null(stack.Rows[1].Cells[0].Glyph);
        Assert.Equal("Execution", DebugScenario.ProbeOf(h, unitPath).GlyphAt(5));

        // UI-TW-903: Copy Call Stack — every frame, one per line, with <file name>:<line> or the address alone.
        await h.ExecuteAsync("callStack.copy");
        var copied = clipboard.Text!.Split("\r\n");
        Assert.Equal(200, copied.Length);
        Assert.Equal(["Run (Unit1.pas:5)", "Hello (Hello.pas:3)", "$00007FF6A1B21C40", "Deep3 (Unit1.pas:7)"], copied[..4]);

        // UI-TW-902: the last row loads the next 200 frames, then the rest; no row is left to load.
        await stack.ActivateAsync(stack.Rows[^1], null);
        await DebugScenario.SettleAsync(h);
        Assert.Equal(401, stack.Rows.Count);
        Assert.Equal("… 50 more frames (double-click to load)", stack.Rows[^1].Cells[1].Text);
        Assert.Equal("Deep399", stack.Rows[399].Cells[1].Text);
        await stack.ActivateAsync(stack.Rows[^1], null);
        await DebugScenario.SettleAsync(h);
        Assert.Equal(Total, stack.Rows.Count);
        Assert.Equal(("449", "Deep449"), (stack.Rows[^1].Key, stack.Rows[^1].Cells[1].Text));
        Assert.Equal(
            ["0+20", "0+200", "200+200", "400+200"],
            h.DebugSessions.SentArguments("stackTrace").Select(a => JsonDocument.Parse(a).RootElement)
                .Select(a => $"{a.GetProperty("startFrame").GetInt32()}+{a.GetProperty("levels").GetInt32()}"));

        // The context menu of tool-windows.md §11.
        Assert.Equal(["callStack.goToFrame", "run.showExecutionPoint", null, "callStack.copy"], stack.ContextCommands);

        // Running: "Available while the program is paused"; no session: "No debug session".
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(stack.Rows);
        Assert.Equal(Strings.Msg_Debug_NotPaused, stack.EmptyHeading);
        Assert.True(h.Find<Control>("Tool.CallStack.Empty").IsEffectivelyVisible);
        Assert.False(h.Services.Commands.CanExecute("callStack.copy"));
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Assert.Equal(Strings.Msg_Debug_NoSession, stack.EmptyHeading);
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
    }
}
