using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Input;
using FreexComp.Ide.Tests.Infrastructure;
using FreexComp.Ide.TestSupport;
using CoreGesture = FreexComp.Ide.Core.Commands.KeyGesture;

namespace FreexComp.Ide.Tests.Input;

/// <summary>
/// Key bindings as the operating system delivers the keys (I-411): with <c>Ctrl</c> held, the Pause key sends the Break
/// scan code and arrives as virtual key <c>VK_CANCEL</c> (the toolkit's <see cref="Key.Cancel"/>), not <c>VK_PAUSE</c>.
/// </summary>
[Trait("Phase", "6")]
[Trait("Kind", "Ui")]
[Trait("Category", "Integration")]
public sealed class KeyBindingTests
{
    private const RawInputModifiers Ctrl = RawInputModifiers.Control;
    private const RawInputModifiers CtrlAlt = RawInputModifiers.Control | RawInputModifiers.Alt;

    /// <summary>Presses and releases the Pause key with <paramref name="modifiers"/> as Windows reports it: VK_CANCEL, no physical key the toolkit names.</summary>
    private static void PressBreak(Window window, RawInputModifiers modifiers)
    {
        window.KeyPress(Key.Cancel, modifiers, PhysicalKey.None, null);
        window.KeyRelease(Key.Cancel, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-KEY-001")]
    [Trait("Req", "UI-KEY-002")]
    public async Task CtrlPauseArrivesAsCancelAndStillPauses()
    {
        // The key normaliser: Cancel with Ctrl is the Break key; shortcut texts stay "Break".
        Assert.Equal(CoreGesture.Parse("Ctrl+Break"), KeyMapping.FromKeyEvent(Key.Cancel, KeyModifiers.Control, null));
        Assert.Equal(CoreGesture.Parse("Ctrl+Alt+Break"), KeyMapping.FromKeyEvent(Key.Cancel, KeyModifiers.Control | KeyModifiers.Alt, null));
        Assert.Equal(CoreGesture.Parse("Ctrl+Alt+Break"), KeyMapping.FromKeyEvent(Key.Pause, KeyModifiers.Control | KeyModifiers.Alt, null));

        await using var h = await DebugScenario.StartAsync();
        var unitPath = DebugScenario.UnitPath(h);
        var mainPath = DebugScenario.MainPath(h);
        var debug = h.Services.Debug!;

        // Ctrl+Pause during a build: project.cancelBuild (Ctrl+Break) cancels it.
        var held = h.Build.Enqueue(FakeBuildEngine.Success(BuildScenario.ProjectPath(h), BuildScenario.ExePath(h)).Held());
        h.Press("Ctrl+Shift+B");
        await h.WaitForAsync(() => held.Request is not null, "build started");
        PressBreak(h.Window, Ctrl);
        await h.WaitForAsync(() => held.Token.IsCancellationRequested, "the build cancelled by Ctrl+Break");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);

        // Ctrl+Alt+Pause while the program runs: run.pause (Ctrl+Alt+Break) stops it in Run of Unit1.pas. The pause asks for
        // the thread list first (no stop has listed it yet).
        h.DebugSessions.UseScenario($$"""
            { "steps": [ { "on": "configurationDone", "respond": {} },
                         {{DebugScenarios.Threads}},
                         { "on": "pause", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("pause")}} ] },
                         {{DebugScenarios.AtStop([("Run", unitPath, 5), ("Hello", mainPath, 3)])}} ] }
            """);
        var reasons = new List<StopReason>();
        debug.Stopped += (_, stop) => reasons.Add(stop.Reason);
        h.Press("F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.DebugRunning);
        // The IDE state is DebugRunning while the session still starts; a pause is taken once the launch completed.
        await h.WaitForAsync(() => h.Log.Lines.Any(l => l.Contains("Program started", StringComparison.Ordinal)), "the program started");
        PressBreak(h.Window, CtrlAlt);
        await DebugScenario.WaitForStopAsync(h, 5);
        Assert.Equal([StopReason.Pause], reasons);

        h.Press("Shift+F5");
        await DebugScenario.WaitForStateAsync(h, IdeState.Idle);
        await DebugScenario.WaitForScenarioCompleteAsync(h); // I-301: the line may be read after the session ended
        Assert.Contains("SCENARIO-COMPLETE", h.DebugSessions.AdapterStderr());

        // The key-chord recorder of the Key Mappings page records the same keys as Ctrl+Break and Ctrl+Alt+Break.
        var running = h.Services.Commands.ExecuteAsync("tools.keyMappings");
        var dlg = await h.WaitForDialogAsync("Options");
        var capture = dlg.Find<TextBox>("NewShortcut");
        capture.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        PressBreak(dlg.Window, Ctrl);
        Assert.Equal("Ctrl+Break", capture.Text);
        dlg.Press("Backspace"); // clears the box (a second key would make a chord)
        PressBreak(dlg.Window, CtrlAlt);
        Assert.Equal("Ctrl+Alt+Break", capture.Text);
        dlg.Click("Cancel");
        await running;
    }
}
