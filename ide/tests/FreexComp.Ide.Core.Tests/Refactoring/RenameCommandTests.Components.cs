using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Refactoring;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Designer;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using static FreexComp.Ide.Core.Tests.Designer.ComponentRenameProject;

namespace FreexComp.Ide.Core.Tests.Refactoring;

/// <summary>
/// IDE-REFAC-059, 060 (I-384): a component renamed from code. A component whose designer rename changes the project's forms
/// in no more than the object header goes through the engine with preview; one with component-reference values or
/// descendant forms is renamed by the form designer (one designer undo unit, no preview); a form that cannot be opened
/// in the designer refuses. Each test has its own component project (<see cref="ComponentRenameProject"/>).
/// </summary>
public sealed partial class RenameCommandTests
{
    /// <summary>The form of the component tests with <c>ActiveControl = OkButton</c> on its root (DSG-FD-082).</summary>
    private static readonly string FormWithActiveControl = DesignerFixture.Form.Replace("  PixelsPerInch = 96\n",
        "  PixelsPerInch = 96\n  ActiveControl = OkButton\n", StringComparison.Ordinal);

    /// <summary>
    /// Rename Symbol of <paramref name="p"/>'s services, with the designer's component rename, run at the caret of the active
    /// document with a name entry that plans <paramref name="newName"/> and answers it.
    /// </summary>
    private static Task<RefactoringResult> RenameComponentAsync(ComponentRenameProject p, string newName)
    {
        var services = p.Services;
        var renamer = new RenameRefactoring(services.Refactoring, services.RenamePlanner, services.CodeModel, services.Projects,
            services.LibrarySource, services.Toolchain, async (info, source, position) =>
            {
                var plan = await services.RenamePlanner.PlanAsync(new RenameRequest(source, position, info.CurrentName, newName, false, true), CancellationToken.None);
                return new RenameDialogResult(newName, false, plan);
            }, components: ((DocumentService)services.Documents).DesignComponentRename);
        return services.Refactoring.RunAsync(renamer);
    }

    /// <summary>B open and active, the caret on its <c>OkButton</c>.</summary>
    private static async Task<TextDocumentViewModel> OpenCallerAtOkButtonAsync(ComponentRenameProject p)
    {
        var caller = await p.OpenAsync(CallerPath);
        Caret(caller, "OkButton");
        return caller;
    }

    private static FormDocumentViewModel? FormDocument(ComponentRenameProject p, string unitPath) =>
        p.Services.Documents.Documents.OfType<FormDocumentViewModel>().FirstOrDefault(d => string.Equals(d.FilePath, unitPath, StringComparison.OrdinalIgnoreCase));

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-059")]
    public Task SimpleComponentGoesThroughTheEngine() => TestUiThread.RunAsync(async () =>
    {
        // OkButton: no line of a form refers to it and no form descends from MainForm — field, code and object header in one
        // operation of the engine, with preview; the designer is not opened.
        var p = new ComponentRenameProject();
        await p.OpenComponentProjectAsync();
        RefactoringPreviewViewModel? preview = null;
        p.Dialogs.Script(RefactoringPreviewViewModel.DialogName, dialog =>
        {
            preview = Assert.IsType<RefactoringPreviewViewModel>(dialog);
            return dialog.PressDefaultAsync();
        });
        var caller = await OpenCallerAtOkButtonAsync(p);

        var result = await RenameComponentAsync(p, "YesButton");

        Assert.Equal(RefactoringOutcome.Applied, result.Outcome);
        Assert.NotNull(preview);
        Assert.Null(FormDocument(p, UnitPath));
        Assert.Equal(ComponentRenameProject.Renamed(ComponentRenameProject.Caller, "OkButton", "YesButton"), caller.Buffer.Text);
        Assert.Equal(ComponentRenameProject.Renamed(UnitText, "OkButton", "YesButton"), p.Disk(UnitPath));
        Assert.Equal(DesignerFixture.Form.Replace("object OkButton: TButton", "object YesButton: TButton", StringComparison.Ordinal),
            p.Disk(FormFile)); // the header only: the caption is text and stays
        await preview!.DiffUpdate;
        if (preview.Diff is { } diff)
            await diff.Recomputing;
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-059")]
    [Trait("Req", "IDE-REFAC-060")]
    public Task ComponentReferencedByAPropertyIsRenamedByTheDesigner() => TestUiThread.RunAsync(async () =>
    {
        // MainForm's root names OkButton in ActiveControl: the designer renames the component — form (header, reference,
        // the caption that equals the name), field, code — as one designer undo unit; no preview.
        var p = new ComponentRenameProject();
        await p.OpenComponentProjectAsync(ComponentRenameProject.Project, null, (FormFile, FormWithActiveControl));
        var caller = await OpenCallerAtOkButtonAsync(p);

        var result = await RenameComponentAsync(p, "YesButton");

        Assert.Equal(RefactoringOutcome.Canceled, result.Outcome); // the engine applied nothing itself
        Assert.Empty(p.Dialogs.Dialogs.OfType<RefactoringPreviewViewModel>());
        var form = Assert.IsType<FormDocumentViewModel>(p.Services.Documents.ActiveDocument);
        Assert.Equal(UnitPath, form.FilePath, ignoreCase: true);
        Assert.Equal(DocumentView.Design, form.ActiveView);
        var designer = form.Designer;
        Assert.Equal("YesButton", Assert.Single(designer.Surface!.Selection.Items).Name);
        Assert.Equal("YesButton", ((FreexComp.Designer.Model.IdentValue)designer.Model!.Root.FindProperty("ActiveControl")!.Value).Name);
        Assert.Contains("    YesButton: TButton;", form.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("  YesButton := nil;", form.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(ComponentRenameProject.Renamed(ComponentRenameProject.Caller, "OkButton", "YesButton"), caller.Buffer.Text);
        Assert.Equal(1, designer.Undo.UndoCount);
        // I-410: the designer's own count of code references (DSG-CS-134), not the plan's.
        Assert.Equal(Format(Strings.Msg_Designer_RenameCodeRefsMany, "OkButton", "YesButton", 2, 2), p.Status);
        Assert.Empty(p.Dialogs.Requests);

        // One designer undo takes back every part.
        Undo(form);
        Assert.Equal(UnitText, form.Buffer.Text);
        Assert.Equal(ComponentRenameProject.Caller, caller.Buffer.Text);
        Assert.Equal("OkButton", ((FreexComp.Designer.Model.IdentValue)designer.Model!.Root.FindProperty("ActiveControl")!.Value).Name);
        Assert.NotNull(designer.Model.FindByName("OkButton"));
        Assert.Equal(UnitText, p.Disk(UnitPath));
        Assert.Equal(FormWithActiveControl, p.Disk(FormFile));
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-059")]
    [Trait("Req", "IDE-REFAC-060")]
    public Task ComponentWithADescendantFormIsRenamedByTheDesigner() => TestUiThread.RunAsync(async () =>
    {
        // OkButton of the base dialog, used through MainForm in B: the base dialog's designer renames it, MainForm's form
        // follows (D-137) and the code references with it (DSG-CS-132).
        const string InheritedProject = """
            { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
              "files": [ { "path": "App.pas" }, { "path": "B.pas" }, { "path": "BaseDialogUnit.pas", "form": "BaseDialogUnit.form" },
                         { "path": "MainForm.pas", "form": "MainForm.form" } ] }
            """;
        const string BaseUnitPath = @"C:\p\App\BaseDialogUnit.pas";
        const string BaseFormPath = @"C:\p\App\BaseDialogUnit.form";
        var p = new ComponentRenameProject();
        await p.OpenComponentProjectAsync(InheritedProject, FrameDesign.Catalog,
            (BaseUnitPath, InheritedDesign.BaseUnit), (BaseFormPath, InheritedDesign.BaseDialog), (UnitPath, InheritedDesign.Unit),
            (FormFile, InheritedDesign.Form));
        var caller = await OpenCallerAtOkButtonAsync(p);

        var result = await RenameComponentAsync(p, "YesButton");

        Assert.Equal(RefactoringOutcome.Canceled, result.Outcome);
        Assert.Empty(p.Dialogs.Dialogs.OfType<RefactoringPreviewViewModel>());
        var form = Assert.IsType<FormDocumentViewModel>(p.Services.Documents.ActiveDocument);
        Assert.Equal(BaseUnitPath, form.FilePath, ignoreCase: true);
        Assert.Equal(DocumentView.Design, form.ActiveView);
        Assert.Equal(ComponentRenameProject.Renamed(InheritedDesign.BaseUnit, "    OkButton: TButton;", "    YesButton: TButton;"), form.Buffer.Text);
        Assert.NotNull(form.Designer.Model!.FindByName("YesButton"));
        Assert.Equal(ComponentRenameProject.Renamed(InheritedDesign.Form, "inherited OkButton:", "inherited YesButton:"), p.Disk(FormFile));
        Assert.Equal(ComponentRenameProject.Renamed(ComponentRenameProject.Caller, "OkButton", "YesButton"), caller.Buffer.Text);
        Assert.Equal(Format(Strings.Msg_Designer_RenameCodeRefsOne, "OkButton", "YesButton"), p.Status); // I-410: the designer's count
        Assert.Empty(p.Dialogs.Requests);

        Undo(form);
        Assert.Equal(InheritedDesign.BaseUnit, form.Buffer.Text);
        Assert.Equal(InheritedDesign.Form, p.Disk(FormFile));
        Assert.Equal(ComponentRenameProject.Caller, caller.Buffer.Text);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-060")]
    public Task FormThatCannotOpenRefuses() => TestUiThread.RunAsync(async () =>
    {
        // The designer route is needed (ActiveControl), but MainForm's Design view waits for the component metadata:
        // Reason_Rename_FormNotLoaded with the reason, nothing changes.
        var p = new ComponentRenameProject();
        await p.OpenComponentProjectAsync(ComponentRenameProject.Project, null, (FormFile, FormWithActiveControl));
        var form = await p.OpenFormAsync();
        form.Designer.BeginMetadataWait();
        var caller = await OpenCallerAtOkButtonAsync(p);
        p.Dialogs.Answer("Msg.Refactoring.Refused", "OK");
        var before = p.Snapshot();

        var result = await RenameComponentAsync(p, "YesButton");

        var reason = Format(Strings.Reason_Rename_FormNotLoaded, "MainForm.form", Strings.Reason_Rename_MetadataLoading);
        Assert.Equal((RefactoringOutcome.Failed, reason), (result.Outcome, result.Error));
        Assert.Equal(reason, Assert.Single(p.Dialogs.Requests).Args[1]);
        Assert.Equal(before, p.Snapshot());
        Assert.Equal(ComponentRenameProject.Caller, caller.Buffer.Text);
        Assert.Equal(0, form.Designer.Undo.UndoCount);
    });
}
