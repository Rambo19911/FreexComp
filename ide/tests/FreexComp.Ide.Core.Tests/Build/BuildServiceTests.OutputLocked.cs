using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>I-212: a locked output exe (E4008).</summary>
public sealed partial class BuildServiceTests
{
    /// <summary>
    /// IDE-P3-035: an <c>E4008</c> result shows <c>Msg.Build.OutputLocked</c> with the exe file name; <c>Stop and Build</c>
    /// terminates the program holding the exe and repeats the build; <c>Cancel</c> leaves the failed build as it is.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-035")]
    public Task OutputLockedOffersStopAndBuild() => TestUiThread.RunAsync(() => OutputLockedOffersStopAndBuildOnUiThread());

    private static async Task OutputLockedOffersStopAndBuildOnUiThread()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var exe = BuildTestBed.Dir + @"\Hello.exe";
        FakeBuild Locked() => FakeBuildEngine.Failure(BuildTestBed.ProjectPath, Diagnostic.Create("E4008", Location.ForFile(exe), exe));

        // Stop and Build: the holder is terminated, then the build runs again (the default fake build succeeds).
        bed.Engine.Enqueue(Locked());
        bed.Dialogs.Enqueue("Msg.Build.OutputLocked", request =>
        {
            Assert.Equal(IdeState.Idle, bed.Services.State.Current.State); // the failed build has ended
            Assert.Empty(bed.Processes.Terminated);
            return "StopAndBuild";
        });
        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);
        var shown = Assert.Single(bed.Dialogs.Requests, r => r.MessageId == "Msg.Build.OutputLocked");
        Assert.Equal(["Hello.exe"], shown.Args);
        Assert.Equal([exe], bed.Processes.Terminated);
        Assert.Equal(2, bed.Engine.Requests.Count);
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        // Cancel: nothing is terminated and the build is not repeated.
        bed.Engine.Enqueue(Locked());
        bed.Dialogs.Enqueue("Msg.Build.OutputLocked", "Cancel");
        result = await bed.Services.Build.BuildAsync(BuildKind.Build);
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(3, bed.Engine.Requests.Count);
        Assert.Single(bed.Processes.Terminated);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        // Any other failure asks nothing.
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath,
            FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier: 'x'", BuildTestBed.MainPath, 3, 1, 1)));
        await bed.Services.Build.BuildAsync(BuildKind.Build);
        Assert.Equal(2, bed.Dialogs.Requests.Count(r => r.MessageId == "Msg.Build.OutputLocked"));
    }
}
