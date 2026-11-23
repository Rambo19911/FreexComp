using FreexComp.Build;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

public sealed partial class BuildServiceTests
{
    /// <summary>IDE-P3-020…022: all / prompt / none; non-members never saved; Clean saves nothing; a failed save aborts.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-020")]
    [Trait("Req", "IDE-P3-021")]
    [Trait("Req", "IDE-P3-022")]
    [Trait("Req", "IDE-P3-023")]
    public Task SaveBeforeBuildModes() => TestUiThread.RunAsync(() => SaveBeforeBuildModesOnUiThread()); // I-209

    private async Task SaveBeforeBuildModesOnUiThread()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var outsider = bed.File(@"C:\other\Notes.pas", "unit Notes;\n");
        var unit = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        var notes = await bed.OpenDocumentAsync(outsider);
        var build = bed.Services.Build;

        // all (default): the member is saved, the non-member is not.
        BuildTestBed.Type(unit, "{a}");
        BuildTestBed.Type(notes, "{n}");
        Assert.NotNull(await build.BuildAsync(BuildKind.Build));
        Assert.StartsWith("{a}", bed.Text(BuildTestBed.UnitPath), StringComparison.Ordinal);
        Assert.False(unit.IsModified);
        Assert.True(notes.IsModified);
        Assert.Equal("unit Notes;\n", bed.Text(outsider));

        // IDE-P3-023: an untitled document is never a member, so no Save As picker appears; it stays unsaved.
        var untitled = bed.Services.Documents.NewUntitledUnit();
        BuildTestBed.Type(untitled, "{u}");
        Assert.NotNull(await build.BuildAsync(BuildKind.Build));
        Assert.True(untitled.IsModified);
        Assert.Null(untitled.FilePath);
        Assert.Empty(bed.Dialogs.Requests);

        // Clean saves nothing.
        BuildTestBed.Type(unit, "{b}");
        Assert.NotNull(await build.BuildAsync(BuildKind.Clean));
        Assert.True(unit.IsModified);

        // prompt: Cancel aborts the build; Save saves and builds.
        bed.Services.Settings.Set(SettingsCatalog.EnvironmentAutoSaveBeforeBuild, "prompt");
        bed.Dialogs.Enqueue("Msg.File.SaveChangesOne", "Cancel", "Save");
        var requests = bed.Engine.Requests.Count;
        Assert.Null(await build.BuildAsync(BuildKind.Rebuild));
        Assert.Equal(requests, bed.Engine.Requests.Count);
        Assert.True(unit.IsModified);
        Assert.NotNull(await build.BuildAsync(BuildKind.Rebuild));
        Assert.False(unit.IsModified);
        Assert.StartsWith("{b}{a}", bed.Text(BuildTestBed.UnitPath), StringComparison.Ordinal);
        Assert.Equal(["Msg.File.SaveChangesOne", "Msg.File.SaveChangesOne"],
            bed.Dialogs.Requests.Select(r => r.MessageId).Where(id => id.StartsWith("Msg.File.", StringComparison.Ordinal)));

        // none: nothing saved, the build uses the files on disk.
        bed.Services.Settings.Set(SettingsCatalog.EnvironmentAutoSaveBeforeBuild, "none");
        BuildTestBed.Type(unit, "{c}");
        Assert.NotNull(await build.BuildAsync(BuildKind.Build));
        Assert.True(unit.IsModified);
        Assert.StartsWith("{b}{a}", bed.Text(BuildTestBed.UnitPath), StringComparison.Ordinal);
        Assert.Empty(bed.Engine.Requests[^1].OpenFiles);

        // all + a save that fails and is canceled: no build, Msg.Build.SaveFailedAbort.
        bed.Services.Settings.Set(SettingsCatalog.EnvironmentAutoSaveBeforeBuild, "all");
        bed.Fs.SetReadOnly(BuildTestBed.UnitPath, readOnly: true);
        bed.Dialogs.Answer("Msg.File.SaveFailed", "Cancel");
        var status = new List<string?>();
        bed.Services.Status.Changed += (_, _) => status.Add(bed.Services.Status.Message);
        requests = bed.Engine.Requests.Count;
        Assert.Null(await build.BuildAsync(BuildKind.Build));
        Assert.Equal(requests, bed.Engine.Requests.Count);
        Assert.Contains(Strings.Msg_Build_SaveFailedAbort, status);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
    }

    /// <summary>
    /// IDE-P3-050/051/052: Cancel keeps Building with CancelRequested until the engine returns; an engine that does not
    /// return within 10 s is abandoned, its later events discarded, and the state forced to Idle.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-050")]
    [Trait("Req", "IDE-P3-051")]
    [Trait("Req", "IDE-P3-052")]
    public Task CancelWaitsThenForcesIdle() => TestUiThread.RunAsync(() => CancelWaitsThenForcesIdleOnUiThread()); // I-209

    private async Task CancelWaitsThenForcesIdleOnUiThread()
    {
        var bed = new BuildTestBed();
        await bed.OpenHelloAsync();
        var state = bed.Services.State;
        var build = bed.Services.Build;
        var foo = Error("E2003", "Undeclared identifier 'Foo'", BuildTestBed.UnitPath, 12, 5, 3);

        // An engine that honors cancellation: the reported row stays, the summary is Canceled.
        var held = bed.Engine.Enqueue(new FakeBuild([new BuildEvent.Diagnostic(DateTime.UtcNow, BuildTestBed.ProjectPath, foo)],
            FakeBuildEngine.Result(BuildTestBed.ProjectPath, true)).Held());
        var running = build.BuildAsync(BuildKind.Build);
        await held.Reached;
        Assert.Equal(IdeState.Building, state.Current.State);
        Assert.True(bed.Services.Commands.CanExecute("project.cancelBuild"));
        await bed.Services.Commands.ExecuteAsync("project.cancelBuild");
        Assert.True(held.Token.IsCancellationRequested);
        var result = await running;
        Assert.True(result!.Canceled);
        Assert.Equal(IdeState.Idle, state.Current.State);
        Assert.Equal(["E2003", ""], bed.Services.Messages.Items.Select(i => i.Code));
        Assert.Equal(Strings.Msg_Build_Canceled, bed.Services.Messages.Items[^1].Text);

        // A hung engine: Building with CancelRequested for 10 s, then forced Idle.
        var hung = bed.Engine.Enqueue(new FakeBuild([], FakeBuildEngine.Result(BuildTestBed.ProjectPath, true)).Held(honorsCancel: false));
        running = build.BuildAsync(BuildKind.Build);
        await hung.Reached;
        build.Cancel();
        Assert.Equal(new IdeStateInfo(IdeState.Building, BuildKind.Build, true, null, null), state.Current);
        bed.Clock.Advance(TimeSpan.FromSeconds(9.9));
        Assert.False(running.IsCompleted);
        Assert.Equal(IdeState.Building, state.Current.State);
        bed.Clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Null(await running);
        Assert.Equal(IdeState.Idle, state.Current.State);
        Assert.Equal(Strings.Msg_Build_Canceled, build.ResultText);
        Assert.Contains(bed.Log.Lines, l => l.StartsWith("ERROR build:", StringComparison.Ordinal));

        // The abandoned engine's late events are discarded.
        var rows = bed.Services.Messages.Items.Count;
        hung.Release();
        bed.FlushOutput();
        Assert.Equal(rows, bed.Services.Messages.Items.Count);
    }
}
