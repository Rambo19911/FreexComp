using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Editor;

/// <summary>
/// The execution line and the selected call-frame line in the editor, and the paused state in title and status bar
/// (I-135; editor.md §3, UI-ED-020, main-window.md §4, states-and-messages.md §9) — with a session over the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class ExecutionLineTests
{
    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-ED-020")]
    [Trait("Req", "UI-WIN-033")]
    [Trait("Req", "UI-WIN-041")]
    public async Task HighlightFollowsPausedFrame()
    {
        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var mainPath = DebugScenario.MainPath(h);
        // Stops at Unit1.pas:5, a step over stops at line 6, then the program runs on.
        h.DebugSessions.UseScenario($$"""
            { "steps": [ {{DebugScenarios.StopAtStart(unitPath, 5)}},
                         { "on": "setBreakpoints", "respond": { "breakpoints": [ { "id": 1, "verified": true, "line": 5 } ] } },
                         {{DebugScenarios.StepTo("next", unitPath, 6)}}, {{DebugScenarios.Continue()}} ] }
            """);
        Assert.Equal("Hello – FreexComp", h.Window.Title);

        // Unit1.pas of the scenario has an undeclared identifier on line 6 (`Foo := 1;`): the live diagnostics mark it once
        // their delay has passed on the test clock, which every harness wait advances. They are settled first, so each glyph
        // below is seen with the error marker present, whatever the machine's load.
        var (_, _, unitProbe) = await DebugScenario.OpenAsync(h, unitPath);
        h.Clock.Advance(TimeSpan.FromMilliseconds(h.Services.Settings.Get(Core.Settings.SettingsCatalog.EditorLiveDiagnosticsDelayMs)));
        await h.WaitForAsync(() => unitProbe.GlyphAt(6) == "Error", "the error marker of line 6");

        await h.ExecuteAsync("run.start");
        await DebugScenario.WaitForStateAsync(h, IdeState.Paused);
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unitPath, "the editor shows the stop");
        Dispatcher.UIThread.RunJobs();

        // Paused: arrow and amber background on the stop line; the caret is there; title and status bar say Paused.
        var probe = DebugScenario.ProbeOf(h, unitPath);
        Assert.Equal("Execution", probe.GlyphAt(5));
        Assert.Equal("editor.execution.line.bg", probe.LineBackgroundAt(5));
        Assert.Equal("Error", probe.GlyphAt(6)); // no debug glyph there: the line's error marker shows
        Assert.Equal(5, ((Core.ViewModels.TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret.Line);
        Assert.Equal("Hello (Paused) – FreexComp", h.Window.Title);
        Assert.Equal("Paused at Unit1.pas:5", h.Status.State);
        Assert.Equal(Core.ViewModels.StatusBarState.Debugging, ((Core.ViewModels.MainWindowViewModel)h.Window.DataContext!).StatusBar.StateKind);

        // editor.md §3: on a breakpoint line the arrow wins; the breakpoint is still there underneath.
        h.Services.Breakpoints!.Toggle(unitPath, 5);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Execution", probe.GlyphAt(5));
        Assert.Equal("editor.execution.line.bg", probe.LineBackgroundAt(5));

        // A selected call frame that is not the top frame: hollow arrow and its own background in its file.
        var (_, _, mainProbe) = await DebugScenario.OpenAsync(h, mainPath);
        // Hello.pas: its own diagnostics are not settled here, so only the frame marker is asserted on its lines.
        Assert.NotEqual("Frame", mainProbe.GlyphAt(3));
        var caller = new StackFrameInfo(101, 1, "Hello", new SourceLocation(mainPath, 3, 1), true, 0, "Hello.exe");
        h.Services.Debug!.SelectFrame(caller);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Frame", mainProbe.GlyphAt(3));
        Assert.Equal("editor.callFrame.line.bg", mainProbe.LineBackgroundAt(3));
        Assert.Same(caller, h.Services.Debug.SelectedFrame);
        // Frame 0 returns to the execution point: no frame marker.
        h.Services.Debug.SelectFrame(caller with { Index = 0, Location = new SourceLocation(unitPath, 5, 1) });
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual("Frame", mainProbe.GlyphAt(3));
        Assert.NotEqual("editor.callFrame.line.bg", mainProbe.LineBackgroundAt(3));
        Assert.Null(h.Services.Debug.SelectedFrame);
        h.Services.Debug.SelectFrame(caller);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Frame", mainProbe.GlyphAt(3));

        // F10 steps: the highlight follows to the new stop; the old line shows its breakpoint again; no frame marker.
        h.Press("F10");
        await h.WaitForAsync(() => h.Services.State.Current is { State: IdeState.Paused, PausedLine: 6 }, "the stop after the step");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unitPath, "the editor shows the stop");
        Dispatcher.UIThread.RunJobs();
        probe = DebugScenario.ProbeOf(h, unitPath);
        Assert.Equal("Execution", probe.GlyphAt(6)); // editor.md §3: the arrow has priority over the error marker
        Assert.Equal("editor.execution.line.bg", probe.LineBackgroundAt(6));
        Assert.Equal("Breakpoint", probe.GlyphAt(5));
        Assert.Equal("editor.breakpoint.line.bg", probe.LineBackgroundAt(5));
        Assert.Null(h.Services.Debug.SelectedFrame);
        Assert.Equal("Paused at Unit1.pas:6", h.Status.State);
        await DebugScenario.OpenAsync(h, mainPath);
        Assert.NotEqual("Frame", DebugScenario.ProbeOf(h, mainPath).GlyphAt(3));

        // Alt+Multiply shows the execution point again.
        await h.ExecuteAsync("run.showExecutionPoint");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == unitPath, "the execution point shown");
        Assert.Equal(6, ((Core.ViewModels.TextDocumentViewModel)h.Services.Documents.ActiveDocument!).ViewState.Caret.Line);

        // Continue: the program runs, nothing is highlighted.
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        Dispatcher.UIThread.RunJobs();
        probe = DebugScenario.ProbeOf(h, unitPath);
        Assert.Equal("Error", probe.GlyphAt(6)); // the arrow is gone: the line's error marker shows again
        Assert.NotEqual("editor.execution.line.bg", probe.LineBackgroundAt(6));
        Assert.Equal("Hello (Debugging) – FreexComp", h.Window.Title);
        Assert.Equal("Debugging Hello.exe", h.Status.State);

        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Hello – FreexComp", h.Window.Title);
        Assert.Equal("Breakpoint", probe.GlyphAt(5));
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
    }
}
