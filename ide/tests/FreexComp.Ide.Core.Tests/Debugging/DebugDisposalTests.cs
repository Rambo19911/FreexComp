using System.Globalization;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Debugging;

/// <summary>
/// I-222: requests of the Evaluate/Modify dialog and of the Breakpoint dialog's condition check that reach a session
/// which is being disposed (the program ended under the open dialog; the IDE applies that with the session's next
/// event) end as "not paused" — none of them faults.
/// </summary>
[Trait("Phase", "6")]
[Trait("Category", "Integration")]
public sealed class DebugDisposalTests
{
    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "UI-DLG-210")]
    [Trait("Req", "UI-DLG-190")]
    public Task EvaluateModifyAndConditionCheckSurviveADisposedSession() => TestUiThread.RunAsync(EvaluateModifyAndConditionCheckSurviveADisposedSessionOnUiThread);

    private static async Task EvaluateModifyAndConditionCheckSurviveADisposedSessionOnUiThread()
    {
        using var bed = new DebugTestBed();
        await bed.OpenHelloAsync();
        var demo = bed.File(Path.Combine(bed.Dir, "DbgDemo.pas"), DebugDemo.SourceText);
        bed.File(Path.ChangeExtension(bed.Exe, ".pdi"), DebugDemo.TypeInfo(demo));
        bed.Sessions.UseScenario($$"""
            { {{DebugDemo.Data()}}, "steps": [
              { "on": "configurationDone", "respond": {}, "thenEvents": [ {{DebugScenarios.StoppedEvent("pause")}} ] },
              {{DebugDemo.AtStop(demo)}} ] }
            """);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Debug.Stopped += (_, _) => stopped.TrySetResult();
        Assert.True(await bed.Debug.StartAsync());
        await DebugTestBed.WaitAsync(stopped.Task, "the stop");
        await DebugTestBed.WaitAsync(bed.Debug.WhenViewsSettledAsync(), "the debug views' requests");

        // The dialog has a modifiable value while the session lives.
        var modifying = new EvaluateViewModel(bed.Debug, null, "Total");
        await DebugTestBed.WaitAsync(modifying.Evaluation, "the first evaluation");
        Assert.Equal(("24", false, true), (modifying.ResultText, modifying.ResultIsError, modifying.CanEditNewValue));
        modifying.NewValue = "5";
        var evaluating = new EvaluateViewModel(bed.Debug, null, "");
        evaluating.Expression = "Total + 1";

        // The session is being disposed; the requests come in the same turn of the UI thread, before the IDE can apply
        // the end of the session (its events are posted to this thread).
        var gone = bed.Sessions.Last.DisposeAsync();
        Assert.True(bed.Debug.IsPaused);
        var modify = modifying.ModifyAsync();
        var evaluate = evaluating.EvaluateAsync();
        var condition = bed.Debug.ConditionProblemAsync("Total > 1");
        await gone;
        await DebugTestBed.WaitAsync(Task.WhenAny(Task.WhenAll(modify, evaluate, condition)), "the requests ended");

        Assert.True(modify.IsCompletedSuccessfully, modify.Exception?.ToString());
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_ModifyFailed, Strings.Msg_Debug_NotPaused), modifying.ModifyError);
        Assert.Equal("24", modifying.ResultText); // not evaluated again
        Assert.True(evaluate.IsCompletedSuccessfully, evaluate.Exception?.ToString());
        Assert.Equal((string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_EvalFailed, Strings.Msg_Debug_NotPaused), true),
            (evaluating.ResultText, evaluating.ResultIsError));
        Assert.True(condition.IsCompletedSuccessfully, condition.Exception?.ToString());
        Assert.Null(await condition); // cannot be checked now: not a problem of the condition

        await bed.Debug.ShutdownAsync();
        Assert.Equal(IdeState.Idle, bed.State);
    }
}
