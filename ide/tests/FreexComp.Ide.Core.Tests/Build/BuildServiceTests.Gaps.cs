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
    public async Task ResultTextClearsOnNextEdit()
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

    /// <summary>IDE-P3-022 in <c>prompt</c> mode: Save is chosen, the save fails and its error dialog ends with Cancel → no build.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P3-022")]
    public async Task PromptSaveFailureAborts()
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
