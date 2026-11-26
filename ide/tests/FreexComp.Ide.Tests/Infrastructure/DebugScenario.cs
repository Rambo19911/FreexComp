using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Editor;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>
/// Phase 6 headless set-up (testing.md §5.4): <see cref="BuildScenario"/> with the Phase 6 tool windows and commands; the
/// debug sessions run over the scripted adapter once the test sets a scenario (<see cref="HeadlessHarness.DebugSessions"/>).
/// </summary>
public static class DebugScenario
{
    public static Task<HeadlessHarness> StartAsync(Action<HarnessOptions>? configure = null) =>
        BuildScenario.StartAsync(configure, implementedPhase: 6);

    public static string UnitPath(HeadlessHarness h) => BuildScenario.UnitPath(h);

    public static string MainPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"Hello\Hello.pas");

    /// <summary>Opens (or activates) a file of the project and returns its document, editor control and probe.</summary>
    public static async Task<(TextDocumentViewModel Document, TextEditor Editor, IEditorTestProbe Probe)> OpenAsync(HeadlessHarness h, string path)
    {
        var result = await h.Services.Documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        Dispatcher.UIThread.RunJobs();
        return (Assert.IsType<TextDocumentViewModel>(result.Value), EditorOf(h, path), ProbeOf(h, path));
    }

    public static TextEditor EditorOf(HeadlessHarness h, string path) => h.Find<TextEditor>(AutomationIds.Editor(Path.GetFileName(path)));

    public static IEditorTestProbe ProbeOf(HeadlessHarness h, string path) =>
        EditorOf(h, path).FindAncestorOfType<CodeEditorView>() ?? throw new InvalidOperationException("The editor has no code editor view.");

    /// <summary>
    /// Writes the program's type information file (<c>Hello.pdi</c> beside the executable, as a build with debug
    /// information does): the session then evaluates expressions while stopped in <c>Run</c> of Unit1.pas.
    /// </summary>
    public static void WriteTypeInfo(HeadlessHarness h) =>
        File.WriteAllText(Path.ChangeExtension(BuildScenario.ExePath(h), ".pdi"), FreexComp.Ide.TestSupport.DebugScenarios.TypeInfo("Hello", UnitPath(h)));

    public static Task WaitForStateAsync(HeadlessHarness h, IdeState state) =>
        h.WaitForAsync(() => h.Services.State.Current.State == state, $"IDE state {state}");

    /// <summary>
    /// I-301: waits, up to the test's own timeout, until the scripted adapter has reported on its error stream that every
    /// step of the scenario was consumed (<c>SCENARIO-COMPLETE</c>). The session ends after fixed 2 s waits for the
    /// adapter's exit and for the end of its error stream, so under load the IDE can be idle before the line is read.
    /// </summary>
    public static Task WaitForScenarioCompleteAsync(HeadlessHarness h) =>
        h.Deadline.WaitAsync(h.DebugSessions.WaitForStderrLineAsync("SCENARIO-COMPLETE"), "the scripted adapter's SCENARIO-COMPLETE");

    /// <summary>The demo program of <see cref="FreexComp.Ide.TestSupport.DebugDemo"/> in the project folder (its frames are there).</summary>
    public static string DemoPath(HeadlessHarness h) => Path.Combine(h.TempDir, @"Hello\DbgDemo.pas");

    /// <summary>
    /// Phase 6 set-up with the demo program: its source in the project folder and its type information beside the
    /// executable, so a session over the scripted adapter reads the locals of <c>Run</c>.
    /// </summary>
    public static async Task<HeadlessHarness> StartDemoAsync(Action<HarnessOptions>? configure = null)
    {
        var h = await StartAsync(o =>
        {
            o.InitialFiles[@"Hello\DbgDemo.pas"] = FreexComp.Ide.TestSupport.DebugDemo.SourceText;
            configure?.Invoke(o);
        });
        File.WriteAllText(Path.ChangeExtension(BuildScenario.ExePath(h), ".pdi"), FreexComp.Ide.TestSupport.DebugDemo.TypeInfo(DemoPath(h)));
        return h;
    }

    /// <summary>Waits until the program is paused at <paramref name="line"/>, the editor shows the stop and the debug views have what they asked for.</summary>
    public static async Task WaitForStopAsync(HeadlessHarness h, int line)
    {
        await h.WaitForAsync(() => h.Services.State.Current is { State: IdeState.Paused } state && state.PausedLine == line, $"the stop at line {line}");
        await h.WaitForAsync(() => h.Services.Documents.ActiveDocument?.FilePath == h.Services.State.Current.PausedFile, "the editor shows the stop");
        await SettleAsync(h);
    }

    /// <summary>Waits until the debug views have the answers to everything they asked the session.</summary>
    public static async Task SettleAsync(HeadlessHarness h)
    {
        await h.WaitForAsync(() => !h.Services.Debug!.HasViewWork, "the debug views' requests");
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The text of a cell of a debug tool window (<c>Tool.&lt;tool&gt;.Row.&lt;key&gt;.&lt;column&gt;</c>), as the view shows it.</summary>
    public static string Cell(HeadlessHarness h, string tool, string key, string column) =>
        h.Find<Avalonia.Controls.TextBlock>($"Tool.{tool}.Row.{key}.{column}").Text ?? "";
}
