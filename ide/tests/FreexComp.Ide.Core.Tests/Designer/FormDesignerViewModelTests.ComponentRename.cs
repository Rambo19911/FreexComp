using System.Globalization;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using static FreexComp.Ide.Core.Tests.Designer.ComponentRenameProject;

namespace FreexComp.Ide.Core.Tests.Designer;

public sealed partial class FormDesignerViewModelTests
{
    /// <summary>
    /// F13-001 triage acceptance (DSG-CS-130, 132; I-383): the Object Inspector's <c>Name</c> renames a component with its
    /// field, its references in the form's unit and in a second, closed unit, and the form — one designer undo unit.
    /// </summary>
    [Fact(Timeout = 120000)]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-CS-130")]
    [Trait("Req", "DSG-CS-132")]
    public Task ComponentRenameUpdatesCodeReferences() => TestUiThread.RunAsync(async () =>
    {
        var p = new ComponentRenameProject();
        var document = await p.OpenAsync();

        p.RenameInInspector(document, "OkButton", "YesButton");
        var work = document.Designer.RenameWork;
        Assert.NotNull(work);
        Assert.NotNull(document.Designer.Model!.FindByName("OkButton")); // accepted; the prepare runs
        Assert.True(await work);

        Assert.Null(document.Designer.Model!.FindByName("OkButton"));
        var button = document.Designer.Model!.FindByName("YesButton");
        Assert.NotNull(button);
        Assert.Equal(new StringValue("YesButton"), button.FindProperty("Caption")?.Value); // the caption equal to the old name follows
        Assert.Contains("  object YesButton: TButton", document.Designer.CurrentFormText(), StringComparison.Ordinal);
        Assert.Equal(Renamed(UnitText, "OkButton", "YesButton"), document.Buffer.Text); // field and reference
        Assert.Equal(Renamed(Caller, "OkButton", "YesButton"), p.Disk(CallerPath)); // the closed unit is written
        Assert.Equal((1, UndoTitles.Rename), (document.Designer.Undo.UndoCount, document.Designer.Undo.UndoTitleKey));
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_RenameCodeRefsMany, "OkButton", "YesButton", 2, 2), p.Status);
        Assert.Empty(p.Dialogs.Requests);
    });
}
