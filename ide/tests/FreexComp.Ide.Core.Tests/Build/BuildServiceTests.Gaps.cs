using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>Phase 3 gaps left by I-099…I-106 (I-190).</summary>
public sealed partial class BuildServiceTests
{
    /// <summary>IDE-P3-005: the build result stays in the state field until the next state change or the next edit of any document.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-005")]
    public Task ResultTextClearsOnNextEdit() => TestUiThread.RunAsync(() => ResultTextClearsOnNextEditOnUiThread()); // I-209

    private async Task ResultTextClearsOnNextEditOnUiThread()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var outsider = bed.File(@"C:\other\Notes.pas", "unit Notes;\n");
        var notes = await bed.OpenDocumentAsync(outsider); // not a project member: any document counts
        var bar = new StatusBarViewModel(bed.Services.Status, bed.Services.Commands, bed.Services.Settings, bed.Services.Documents,
            bed.Services.State, bed.Services.Projects, bed.Services.Build, bed.Services.Messages);

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        var result = bed.Services.Build.ResultText;
        Assert.NotNull(result);
        Assert.Equal(result, bar.StateText);
        var changes = 0;
        bar.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(StatusBarViewModel.StateText) ? 1 : 0;

        BuildTestBed.Type(notes, "{x}");
        Assert.Equal(Strings.Msg_State_Idle, bar.StateText);
        Assert.Equal(1, changes);
        BuildTestBed.Type(notes, "{y}"); // nothing left to clear
        Assert.Equal(1, changes);

        // A document opened after the build counts too.
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Equal(result, bar.StateText);
        var unit = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        Assert.Equal(result, bar.StateText); // opening is not an edit
        BuildTestBed.Type(unit, "{z}");
        Assert.Equal(Strings.Msg_State_Idle, bar.StateText);
    }

    /// <summary>
    /// IDE-P3-003 (footnote of the availability table): <c>file.exit</c> while Building cancels the build after the
    /// unsaved-changes prompt and waits at most 5 s for the cancellation before closing.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-003")]
    public Task ExitDuringBuildCancelsAndWaits() => TestUiThread.RunAsync(() => ExitDuringBuildCancelsAndWaitsOnUiThread()); // I-209

    private async Task ExitDuringBuildCancelsAndWaitsOnUiThread()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var unit = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        bed.Services.Settings.Set(SettingsCatalog.EnvironmentAutoSaveBeforeBuild, "none");
        BuildTestBed.Type(unit, "{a}");
        var shutdowns = 0;
        bed.Services.Application.Shutdown = () => shutdowns++;
        var held = bed.Engine.Enqueue(new FakeBuild([], FakeBuildEngine.Result(BuildTestBed.ProjectPath, true)).Held());
        var running = bed.Services.Build.BuildAsync(BuildKind.Build);
        await held.Reached;

        // Cancel at the unsaved-changes prompt: the build goes on.
        bed.Dialogs.Enqueue("Msg.File.SaveChangesOne", "Cancel", "DontSave");
        Assert.False(await bed.Services.Application.ExitAsync());
        Assert.False(held.Token.IsCancellationRequested);
        Assert.Equal(IdeState.Building, bed.Services.State.Current.State);

        // Don't Save: the build is canceled, its end awaited, then the IDE closes.
        Assert.True(await bed.Services.Application.ExitAsync());
        Assert.True(held.Token.IsCancellationRequested);
        Assert.NotEqual(IdeState.Building, bed.Services.State.Current.State); // the build ended before the close
        Assert.True((await running)!.Canceled);
        Assert.Equal(Strings.Msg_Build_Canceled, bed.Services.Build.ResultText);
        Assert.Equal(1, shutdowns);

        // A hung engine: the exit waits 5 s, then closes anyway.
        var bed2 = new BuildTestBed();
        await bed2.OpenHelloAsync();
        var closed = 0;
        bed2.Services.Application.Shutdown = () => closed++;
        var hung = bed2.Engine.Enqueue(new FakeBuild([], FakeBuildEngine.Result(BuildTestBed.ProjectPath, true)).Held(honorsCancel: false));
        var hungBuild = bed2.Services.Build.BuildAsync(BuildKind.Build);
        await hung.Reached;
        var exiting = bed2.Services.Application.ExitAsync();
        Assert.True(hung.Token.IsCancellationRequested);
        bed2.Clock.Advance(TimeSpan.FromSeconds(4.9));
        Assert.False(exiting.IsCompleted);
        Assert.Equal(0, closed);
        bed2.Clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.True(await exiting);
        Assert.Equal(1, closed);
        hung.Release();
        await hungBuild; // I-210: the released build ends within the test, not after it
    }

    /// <summary>IDE-P3-022 in <c>prompt</c> mode: Save is chosen, the save fails and its error dialog ends with Cancel → no build.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-022")]
    public Task PromptSaveFailureAborts() => TestUiThread.RunAsync(() => PromptSaveFailureAbortsOnUiThread()); // I-209

    private async Task PromptSaveFailureAbortsOnUiThread()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var unit = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        BuildTestBed.Type(unit, "{a}");
        bed.Services.Settings.Set(SettingsCatalog.EnvironmentAutoSaveBeforeBuild, "prompt");
        bed.Fs.SetReadOnly(BuildTestBed.UnitPath, readOnly: true);
        bed.Dialogs.Answer("Msg.File.SaveChangesOne", "Save");
        bed.Dialogs.Answer("Msg.File.SaveFailed", "Cancel");
        var status = new List<string?>();
        bed.Services.Status.Changed += (_, _) => status.Add(bed.Services.Status.Message);
        var requests = bed.Engine.Requests.Count;

        Assert.Null(await bed.Services.Build.BuildAsync(BuildKind.Build));

        Assert.Equal(requests, bed.Engine.Requests.Count);
        Assert.Contains(bed.Dialogs.Requests, r => r.MessageId == "Msg.File.SaveFailed");
        Assert.Contains(Strings.Msg_Build_SaveFailedAbort, status);
        Assert.True(unit.IsModified);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
    }
}
