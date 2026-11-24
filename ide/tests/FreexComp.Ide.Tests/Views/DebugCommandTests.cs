using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// What a stop of the debugged program does in the IDE (I-141; the debug-session interface §6.1): the editor shows
/// <c>StopInfo.Location</c> with the execution glyph before <c>Stopped</c> is published, and the Debug profile's
/// windows are back — with a session over the scripted adapter.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class DebugCommandTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-ED-020")]
    [Trait("Req", "UI-WIN-012")]
    public async Task StopNavigatesAndMarksExecutionLine()
    {
        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var mainPath = DebugScenario.MainPath(h);
        var layout = h.Services.Layout.Layout;
        var debug = h.Services.Debug!;
        // Stops in Hello.pas (not open yet); after Continue, Pause stops in Unit1.pas.
        h.DebugSessions.UseScenario($$"""
            { "steps": [ { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("entry")}} ] },
                         {{DebugScenarios.AtStop([("Hello", mainPath, 3)])}},
                         {{DebugScenarios.Continue()}},
                         { "on": "pause", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("pause")}} ] },
                         {{DebugScenarios.AtStop([("Run", unitPath, 5), ("Hello", mainPath, 3)])}} ] }
            """);
        Assert.True(await h.Services.Documents.CloseAllAsync());
        var (unit, _, _) = await DebugScenario.OpenAsync(h, unitPath);
        Assert.DoesNotContain(h.Services.Documents.Documents, d => d.FilePath == mainPath);

        // When Stopped is published the editor already shows the stop: file, caret and execution glyph.
        var seen = new List<(string? File, int Line, int Column, StopReason Reason)>();
        debug.Stopped += (_, stop) =>
        {
            var active = h.Services.Documents.ActiveDocument as TextDocumentViewModel;
            seen.Add((active?.FilePath, active?.ViewState.Caret.Line ?? 0, active?.ViewState.Caret.Column ?? 0, stop.Reason));
        };

        h.Press("F11"); // Step Into without a session: start and stop at the first line
        await DebugScenario.WaitForStopAsync(h, 3);
        Assert.Equal([(mainPath, 3, 1, StopReason.Entry)], seen);
        Assert.Equal(new SourceLocation(mainPath, 3, 1), debug.CurrentStop!.Location);
        Assert.Equal(mainPath, h.Services.Documents.ActiveDocument?.FilePath);
        var probe = DebugScenario.ProbeOf(h, mainPath);
        Assert.Equal("Execution", probe.GlyphAt(3));
        Assert.Equal("editor.execution.line.bg", probe.LineBackgroundAt(3));
        Assert.Equal("Paused at Hello.pas:3", h.Status.State);

        // The Debug profile's windows show the stop: Call Stack (left) and Locals (upper right) are the active tabs.
        Assert.Equal(LayoutProfiles.Debug, layout.ActiveProfile);
        Assert.Equal(("CallStack", "Locals", "Breakpoints"), (layout.Left.ActiveTool, layout.RightUpper.ActiveTool, layout.Bottom.ActiveTool));
        Assert.True(h.Find<Control>("Tool.CallStack").IsEffectivelyVisible);
        Assert.Equal("Hello", DebugScenario.Cell(h, "CallStack", "0", "Routine"));

        // The user leaves the profile while the program runs; the next stop brings the Debug profile back (layout.autoSwitch).
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        await h.ExecuteAsync("window.layout.edit");
        Assert.Equal(LayoutProfiles.Edit, layout.ActiveProfile);
        Assert.Null(h.TryFind<Control>("Tool.CallStack"));
        await h.Services.Documents.OpenAsync(mainPath);
        h.Press("Ctrl+Alt+Break");
        await DebugScenario.WaitForStopAsync(h, 5);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((unitPath, 5, 1, StopReason.Pause), seen[^1]);
        Assert.Same(unit, h.Services.Documents.ActiveDocument);
        Assert.Equal("Execution", DebugScenario.ProbeOf(h, unitPath).GlyphAt(5));
        var main = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.Documents.Single(d => d.FilePath == mainPath));
        Assert.DoesNotContain(main.Decorations.GlyphsAt(3), g => g.Kind == Core.Editing.LineGlyphKind.Execution);
        Assert.Equal(LayoutProfiles.Debug, layout.ActiveProfile);
        Assert.True(h.Find<Control>("Tool.CallStack").IsEffectivelyVisible);
        Assert.Equal("Run", DebugScenario.Cell(h, "CallStack", "0", "Routine"));

        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());
    }

    /// <summary>
    /// IDE-P3-006 (from Phase 6): a file edited while a debug session is active gets the info bar
    /// <c>Msg.Debug.SourceChanged</c> with <c>Rebuild and Restart</c>; it goes with the session.
    /// </summary>
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-006")]
    [Trait("Req", "UI-ED-093")]
    public async Task EditDuringSessionShowsSourceChanged()
    {
        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var debug = h.Services.Debug!;
        h.DebugSessions.UseScenario("{}"); // every session of the test: the program runs until it is stopped
        var (unit, editor, _) = await DebugScenario.OpenAsync(h, unitPath);
        var main = Assert.IsType<TextDocumentViewModel>(h.Services.Documents.Documents.Single(d => d.FilePath == DebugScenario.MainPath(h)));
        void TypeInUnit(string text)
        {
            editor.TextArea.Focus();
            Dispatcher.UIThread.RunJobs();
            h.Type(text);
        }

        // Editing without a session shows nothing.
        TypeInUnit("{a}");
        Assert.Null(h.TryFind<Control>("InfoBar.SourceChanged"));

        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        await h.WaitForAsync(() => debug.Session?.State == DebugState.Running, "the program runs");
        await DebugScenario.OpenAsync(h, unitPath);
        Assert.Null(h.TryFind<Control>("InfoBar.SourceChanged"));
        var builds = h.Build.Requests.Count;

        // The first edit of the session shows the warning bar in that file only, once.
        TypeInUnit("{b}");
        var bar = Assert.Single(unit.InfoBars, b => b.Kind == Core.Debugging.DebugService.SourceChangedKind);
        Assert.Equal((InfoBarSeverity.Warning, Core.Resources.Strings.Msg_Debug_SourceChanged), (bar.Severity, bar.Text));
        Assert.Equal("This file changed since the program was built. Breakpoints and the execution line may not match.", h.Find<TextBlock>("InfoBar.SourceChanged.Text").Text);
        Assert.Equal(["RebuildAndRestart"], bar.Actions.Select(a => a.Name));
        Assert.Equal("Rebuild and Restart", bar.Actions[0].Label);
        TypeInUnit("{c}");
        Assert.Same(bar, Assert.Single(unit.InfoBars, b => b.Kind == Core.Debugging.DebugService.SourceChangedKind));
        Assert.Empty(main.InfoBars);

        // Rebuild and Restart: the session ends, the project is built again, a new session starts; the bar is gone.
        h.Click("InfoBar.SourceChanged.Action.RebuildAndRestart");
        await h.WaitForAsync(() => h.DebugSessions.Sessions.Count == 2 && debug.Session?.State == DebugState.Running, "the new session");
        Assert.Equal(builds + 1, h.Build.Requests.Count);
        Assert.Equal(DebugState.Terminated, h.DebugSessions.Sessions[0].State);
        Assert.Null(h.TryFind<Control>("InfoBar.SourceChanged"));
        Assert.Empty(unit.InfoBars);

        // In the new session the next edit shows it again; closing it (×) keeps it away for the rest of the session.
        await DebugScenario.OpenAsync(h, unitPath);
        TypeInUnit("{d}");
        Assert.NotNull(h.TryFind<Control>("InfoBar.SourceChanged"));
        h.Click("InfoBar.SourceChanged.Dismiss");
        Assert.Null(h.TryFind<Control>("InfoBar.SourceChanged"));
        TypeInUnit("{e}");
        Assert.Null(h.TryFind<Control>("InfoBar.SourceChanged"));

        // The end of the session takes the bars away.
        await h.Services.Documents.OpenAsync(DebugScenario.MainPath(h));
        main.Buffer.ApplyEdits([new Core.Text.TextEdit(0, 0, "{f}")]);
        Assert.Single(main.InfoBars);
        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(main.InfoBars);
        Assert.Empty(unit.InfoBars);
    }
}
