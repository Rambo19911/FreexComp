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
}
