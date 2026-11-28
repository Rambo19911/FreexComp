using System.Globalization;
using System.Text;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using static FreexComp.Ide.Core.Tests.Designer.ComponentRenameProject;
using DesignCommandIds = FreexComp.Designer.Surface.Commands;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// DSG-CS-130…134 (I-383): a component renamed in the designer updates its references in code — in the form's unit and in
/// every other source file — as one designer undo unit with the form and the field; undo and redo by text.
/// </summary>
[Trait("Phase", "8")]
public sealed class ComponentRenameTests
{
    private readonly ComponentRenameProject _p = new();

    private static string Format(string resource, params object[] args) => string.Format(CultureInfo.CurrentCulture, resource, args);

    private static string? Caption(FormDocumentViewModel document, string name) =>
        document.Designer.Model!.FindByName(name)?.FindProperty("Caption")?.Value is StringValue { Value: var text } ? text : null;

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-132")]
    [Trait("Req", "DSG-CS-133")]
    public Task UndoRestoresFormFieldAndEveryReference() => TestUiThread.RunAsync(async () =>
    {
        var document = await _p.OpenAsync();
        var caller = await _p.OpenAsync(CallerPath); // B open: its buffer is edited, not saved
        var formText = document.Designer.CurrentFormText();

        Assert.True(await _p.RenameAsync(document, "OkButton", "YesButton"));

        Assert.Equal(Renamed(UnitText, "OkButton", "YesButton"), document.Buffer.Text);
        Assert.Equal(Renamed(Caller, "OkButton", "YesButton"), caller.Buffer.Text);
        Assert.True(caller.IsModified);
        Assert.Equal(Caller, _p.Disk(CallerPath));
        Assert.Equal("YesButton", Caption(document, "YesButton"));
        Assert.Equal((1, UndoTitles.Rename), (document.Designer.Undo.UndoCount, document.Designer.Undo.UndoTitleKey));

        Undo(document);

        Assert.Equal(UnitText, document.Buffer.Text);
        Assert.Equal(Caller, caller.Buffer.Text);
        Assert.Equal(formText, document.Designer.CurrentFormText());
        Assert.NotNull(document.Designer.Model!.FindByName("OkButton"));
        Assert.Equal((0, 1), (document.Designer.Undo.UndoCount, document.Designer.Undo.RedoCount));
        Assert.Equal(Caller, _p.Disk(CallerPath));
        Assert.Empty(_p.Dialogs.Requests);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-133")]
    public Task RedoAppliesAgain() => TestUiThread.RunAsync(async () =>
    {
        var document = await _p.OpenAsync();
        var formText = document.Designer.CurrentFormText();
        Assert.True(await _p.RenameAsync(document, "OkButton", "YesButton"));
        var renamedForm = document.Designer.CurrentFormText();

        Undo(document);
        Assert.Equal((UnitText, Caller), (document.Buffer.Text, _p.Disk(CallerPath)));

        // Redo: the references first, then code sync renames the declaration again (DSG-CS-102).
        Redo(document);
        Assert.Equal(Renamed(UnitText, "OkButton", "YesButton"), document.Buffer.Text);
        Assert.Equal(Renamed(Caller, "OkButton", "YesButton"), _p.Disk(CallerPath));
        Assert.Equal(renamedForm, document.Designer.CurrentFormText());
        Assert.Equal((1, 0), (document.Designer.Undo.UndoCount, document.Designer.Undo.RedoCount));

        // And back once more.
        Undo(document);
        Assert.Equal((UnitText, Caller, formText), (document.Buffer.Text, _p.Disk(CallerPath), document.Designer.CurrentFormText()));
        Assert.Empty(_p.Dialogs.Requests);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-131")]
    public Task ConflictRefusesAndChangesNothing() => TestUiThread.RunAsync(async () =>
    {
        // A local of the new name in a method that uses the component: the reference would be captured.
        var unit = UnitText.Replace("begin\r\n  OkButton := nil;", "var\r\n  YesButton: Integer;\r\nbegin\r\n  YesButton := 1;\r\n  OkButton := nil;",
            StringComparison.Ordinal);
        var document = await _p.OpenAsync((UnitPath, unit));
        _p.Dialogs.Answer("Msg.Designer.RenameRefused", "OK");
        var formText = document.Designer.CurrentFormText();
        var before = _p.Snapshot();

        Assert.False(await _p.RenameAsync(document, "OkButton", "YesButton"));

        var request = Assert.Single(_p.Dialogs.Requests);
        Assert.Equal("Msg.Designer.RenameRefused", request.MessageId);
        Assert.Equal(new[] { "OkButton", "YesButton" }, request.Args.Take(2));
        Assert.Contains("YesButton", request.Args[2], StringComparison.Ordinal);
        Assert.Equal(before, _p.Snapshot());
        Assert.Equal(formText, document.Designer.CurrentFormText());
        Assert.Equal(0, document.Designer.Undo.UndoCount);
        await TakeDialogAsync(document);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-131")]
    public Task ReadOnlyReferencingFileRefuses() => TestUiThread.RunAsync(async () =>
    {
        var document = await _p.OpenAsync();
        _p.SetReadOnly(CallerPath);
        _p.Dialogs.Answer("Msg.Refactoring.Blocked", "OK");
        var formText = document.Designer.CurrentFormText();
        var before = _p.Snapshot();

        Assert.False(await _p.RenameAsync(document, "OkButton", "YesButton"));

        var request = Assert.Single(_p.Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.Blocked", request.MessageId);
        Assert.Equal("Rename 'OkButton' to 'YesButton'", request.Args[0]);
        Assert.Equal(CallerPath + " — " + Strings.Reason_Refactoring_NotWritable, request.Args[1]);
        Assert.Equal(before, _p.Snapshot());
        Assert.Equal(formText, document.Designer.CurrentFormText());
        Assert.Equal(0, document.Designer.Undo.UndoCount);
        await TakeDialogAsync(document);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-132")]
    [Trait("Req", "DSG-CS-133")]
    public Task ClosedReferencingUnitIsWrittenAndPutBackOnUndo() => TestUiThread.RunAsync(async () =>
    {
        // B with a byte-order mark and CRLF: written in its encoding, and put back byte for byte.
        var document = await _p.OpenAsync();
        var crlf = Caller.Replace("\n", "\r\n", StringComparison.Ordinal);
        var original = Utf8WithBom(crlf);
        _p.PutBytes(CallerPath, original);

        Assert.True(await _p.RenameAsync(document, "OkButton", "YesButton"));

        Assert.Equal(Utf8WithBom(Renamed(crlf, "OkButton", "YesButton")), _p.DiskBytes(CallerPath));
        Assert.DoesNotContain(_p.Services.Documents.Documents, d => d is TextDocumentViewModel { FilePath: CallerPath });

        Undo(document);

        Assert.Equal(original, _p.DiskBytes(CallerPath));
        Assert.Equal(UnitText, document.Buffer.Text);
        Assert.Empty(_p.Dialogs.Requests);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-133")]
    public Task FileChangedSinceBlocksTheUndoStep() => TestUiThread.RunAsync(async () =>
    {
        var document = await _p.OpenAsync();
        Assert.True(await _p.RenameAsync(document, "OkButton", "YesButton"));
        var renamedForm = document.Designer.CurrentFormText();
        var changed = Renamed(Caller, "OkButton", "YesButton") + "{ edited elsewhere }\n";
        _p.Put(CallerPath, changed);
        _p.Dialogs.Answer("Msg.Refactoring.UndoBlocked", "OK");

        Undo(document);

        // The step is not taken: nothing changes, the unit stays on the undo stack.
        var request = Assert.Single(_p.Dialogs.Requests);
        Assert.Equal("Msg.Refactoring.UndoBlocked", request.MessageId);
        Assert.Equal(new[] { "Rename 'OkButton' to 'YesButton'", CallerPath + " — " + Strings.Reason_Refactoring_ChangedAfterwards }, request.Args);
        Assert.Equal(Renamed(UnitText, "OkButton", "YesButton"), document.Buffer.Text);
        Assert.Equal(changed, _p.Disk(CallerPath));
        Assert.Equal(renamedForm, document.Designer.CurrentFormText());
        Assert.Equal((1, 0), (document.Designer.Undo.UndoCount, document.Designer.Undo.RedoCount));
        await TakeDialogAsync(document);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-130")]
    public Task UnitWithErrorsFallsBackToTheDeclaration() => TestUiThread.RunAsync(async () =>
    {
        // The unit parses (code sync is Ok) but the field's type is unknown to the code model: the field does not bind,
        // so only the declaration is renamed, as before (DSG-CS-045); the references stay.
        var unit = UnitText.Replace("    OkButton: TButton;", "    OkButton: TButtonX;", StringComparison.Ordinal);
        var document = await _p.OpenAsync((UnitPath, unit));

        Assert.True(await _p.RenameAsync(document, "OkButton", "YesButton"));

        Assert.Equal(unit.Replace("    OkButton: TButtonX;", "    YesButton: TButtonX;", StringComparison.Ordinal), document.Buffer.Text);
        Assert.Contains("  OkButton := nil;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(Caller, _p.Disk(CallerPath));
        Assert.NotNull(document.Designer.Model!.FindByName("YesButton"));
        Assert.Equal(1, document.Designer.Undo.UndoCount);
        Assert.DoesNotContain("reference", _p.Status ?? "", StringComparison.Ordinal);
        Assert.Empty(_p.Dialogs.Requests);

        Undo(document);
        Assert.Equal(unit, document.Buffer.Text);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-131")]
    public Task ChangeDuringThePrepareDropsTheRename() => TestUiThread.RunAsync(async () =>
    {
        var document = await _p.OpenAsync();
        _p.RenameInInspector(document, "OkButton", "YesButton");
        var work = document.Designer.RenameWork!;
        Assert.False(work.IsCompleted); // accepted; the prepare runs

        // A further rename is not started while it runs; the unit changes meanwhile.
        _p.RenameInInspector(document, "Panel1", "BottomPanel");
        Assert.Same(work, document.Designer.RenameWork);
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "{ typed }")]);
        var edited = document.Buffer.Text;

        Assert.False(await work);

        Assert.Equal(Format(Strings.Msg_Designer_RenameDropped, "OkButton"), _p.Status);
        Assert.Equal(edited, document.Buffer.Text);
        Assert.Equal(Caller, _p.Disk(CallerPath));
        Assert.NotNull(document.Designer.Model!.FindByName("OkButton"));
        Assert.NotNull(document.Designer.Model!.FindByName("Panel1"));
        Assert.Equal(0, document.Designer.Undo.UndoCount);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-130")]
    public Task MenuDesignerRenameFollowsToo() => TestUiThread.RunAsync(async () =>
    {
        const string MenuUnit = "unit MainForm;\r\n\r\ninterface\r\n\r\nuses\r\n  Classes, Controls, Forms, Menus;\r\n\r\ntype\r\n"
            + "  TMainForm = class(TForm)\r\n    MainMenu1: TMainMenu;\r\n    MenuItem1: TMenuItem;\r\n    procedure FormCreate(Sender: TObject);\r\n"
            + "  private\r\n  public\r\n  end;\r\n\r\nvar\r\n  MainForm: TMainForm;\r\n\r\nimplementation\r\n\r\n{$FORM MainForm.form}\r\n\r\n"
            + "procedure TMainForm.FormCreate(Sender: TObject);\r\nbegin\r\n  MenuItem1 := nil;\r\nend;\r\n\r\nend.\r\n";
        const string MenuForm = "object MainForm: TMainForm\n  Left = 200\n  Top = 120\n  Caption = 'Hello Form'\n  ClientWidth = 400\n"
            + "  ClientHeight = 300\n  Menu = MainMenu1\n  PixelsPerInch = 96\n  OnCreate = FormCreate\n  object MainMenu1: TMainMenu\n"
            + "    DesignLeft = 320\n    DesignTop = 120\n    object MenuItem1: TMenuItem\n      Caption = 'MenuItem1'\n    end\n  end\nend\n";
        const string MenuCaller = "unit B;\n\ninterface\n\nuses MainForm;\n\nprocedure Run(F: TMainForm);\n\nimplementation\n\n"
            + "procedure Run(F: TMainForm);\nbegin\n  F.MenuItem1 := nil;\nend;\n\nend.\n";
        var document = await _p.OpenAsync((UnitPath, MenuUnit), (FormFile, MenuForm), (CallerPath, MenuCaller));
        var designer = document.Designer;

        MenuDesignerViewModel? shown = null;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        _p.Dialogs.Script(MenuDesignerViewModel.DialogName, async dialog =>
        {
            shown = Assert.IsType<MenuDesignerViewModel>(dialog);
            try
            {
                shown.Select("MenuItem1");
                shown.CaptionText = "&Edit";
                Assert.True(shown.CommitCaption()); // accepted: the caption and the name follow after the prepare
                Assert.True(await designer.RenameWork!);
                Assert.Equal("Edit", shown.Selected!.Name);
                Assert.Equal("&Edit", shown.Nodes[0].Caption);
            }
            catch (Exception e)
            {
                failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e);
            }

            await shown.PressCancelAsync();
        });
        DesignerFixture.Select(designer, "MainMenu1");
        await _p.Services.Commands.ExecuteAsync(DesignCommandIds.EditComponent);
        failure?.Throw();
        Assert.NotNull(shown);

        Assert.Equal(Renamed(MenuUnit, "MenuItem1", "Edit"), document.Buffer.Text);
        Assert.Equal(Renamed(MenuCaller, "MenuItem1", "Edit"), _p.Disk(CallerPath));
        Assert.Equal((1, UndoTitles.Property), (designer.Undo.UndoCount, designer.Undo.UndoTitleKey));
        Assert.Equal(Format(Strings.Msg_Designer_RenameCodeRefsMany, "MenuItem1", "Edit", 2, 2), _p.Status);

        Undo(document);
        Assert.Equal((MenuUnit, MenuCaller), (document.Buffer.Text, _p.Disk(CallerPath)));
        Assert.Equal("MenuItem1", Caption(document, "MenuItem1"));
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-132")]
    [Trait("Req", "DSG-FD-134")]
    public Task AncestorComponentRenameUpdatesDescendantFormsAndCode() => TestUiThread.RunAsync(async () =>
    {
        // The base dialog (its unit open in the designer) and MainForm inheriting from it; MainForm's code uses the
        // inherited OK button. D-137 renames the descendant's form, DSG-CS-132 the code — one undo takes back both.
        const string Project = """
            { "formatVersion": 1, "name": "App", "appType": "console", "mainSource": "App.pas",
              "files": [ { "path": "App.pas" }, { "path": "B.pas" }, { "path": "BaseDialogUnit.pas", "form": "BaseDialogUnit.form" },
                         { "path": "MainForm.pas", "form": "MainForm.form" } ] }
            """;
        var mainUnit = InheritedDesign.Unit.Replace("{$FORM MainForm.form}\r\n",
            "{$FORM MainForm.form}\r\n\r\nprocedure Touch(F: TMainForm);\r\nbegin\r\n  F.OkButton := nil;\r\nend;\r\n", StringComparison.Ordinal);
        const string BaseUnitPath = @"C:\p\App\BaseDialogUnit.pas";
        const string BaseFormPath = @"C:\p\App\BaseDialogUnit.form";
        await _p.OpenComponentProjectAsync(Project, FrameDesign.Catalog,
            (BaseUnitPath, InheritedDesign.BaseUnit), (BaseFormPath, InheritedDesign.BaseDialog), (UnitPath, mainUnit), (FormFile, InheritedDesign.Form));
        var document = await _p.OpenFormAsync(BaseUnitPath);

        Assert.True(await _p.RenameAsync(document, "OkButton", "YesButton"));

        Assert.Equal(Renamed(InheritedDesign.BaseUnit, "    OkButton: TButton;", "    YesButton: TButton;"), document.Buffer.Text);
        Assert.Equal(Renamed(mainUnit, "F.OkButton", "F.YesButton"), _p.Disk(UnitPath));
        Assert.Equal(Renamed(InheritedDesign.Form, "inherited OkButton:", "inherited YesButton:"), _p.Disk(FormFile));
        Assert.Equal(Renamed(Caller, "OkButton", "YesButton"), _p.Disk(CallerPath)); // B uses MainForm's inherited field
        Assert.Equal(Format(Strings.Msg_Designer_RenameCodeRefsMany, "OkButton", "YesButton", 2, 2), _p.Status);

        Undo(document);
        Assert.Equal(InheritedDesign.BaseUnit, document.Buffer.Text);
        Assert.Equal(mainUnit, _p.Disk(UnitPath));
        Assert.Equal(InheritedDesign.Form, _p.Disk(FormFile));
        Assert.Equal(Caller, _p.Disk(CallerPath));
        Assert.Empty(_p.Dialogs.Requests);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "DSG-CS-134")]
    public Task StatusCountsTheReferences() => TestUiThread.RunAsync(async () =>
    {
        var document = await _p.OpenAsync();

        // No identifier of the project spells the name: renamed at once (the plan would edit the declaration alone), no
        // status text of its own.
        var status = _p.Status;
        _p.RenameInInspector(document, "InfoLabel", "HintLabel");
        Assert.Null(document.Designer.RenameWork);
        Assert.Contains("    HintLabel: TLabel;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.NotNull(document.Designer.Model!.FindByName("HintLabel"));
        Assert.Equal(status, _p.Status);

        Assert.True(await _p.RenameAsync(document, "NameEdit", "NameBox"));
        Assert.Equal(Format(Strings.Msg_Designer_RenameCodeRefsOne, "NameEdit", "NameBox"), _p.Status);
        Assert.Equal("Renamed NameEdit to NameBox; 1 reference in the code was updated", _p.Status);

        Assert.True(await _p.RenameAsync(document, "Panel1", "BottomPanel"));
        Assert.Equal("Renamed Panel1 to BottomPanel; 2 references in 1 file were updated", _p.Status);

        Assert.True(await _p.RenameAsync(document, "OkButton", "YesButton"));
        Assert.Equal("Renamed OkButton to YesButton; 2 references in 2 files were updated", _p.Status);
        Assert.Equal(4, document.Designer.Undo.UndoCount);
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-REFAC-061")]
    [Trait("Req", "DSG-CS-134")]
    public Task UnrenamedBecomeRows() => TestUiThread.RunAsync(async () =>
    {
        // B refers to OkButton in active code and, behind it on the same line, in inactive code.
        var caller = Caller.Replace("  F.OkButton := nil;\n", "  F.OkButton := nil; {$IFDEF NEVER_DEFINED} F.OkButton := nil; {$ENDIF}\n", StringComparison.Ordinal);
        var document = await _p.OpenAsync((CallerPath, caller));
        var layout = string.Join(" | ", _p.Services.Layout.Layout.Groups.Select(g => string.Join(",", g.Tools) + ">" + g.ActiveTool));

        Assert.True(await _p.RenameAsync(document, "OkButton", "YesButton"));

        // The designer's commit wrote B (closed): one row at the inactive OkButton's place in the written text.
        var written = _p.Disk(CallerPath);
        Assert.Contains("  F.YesButton := nil; {$IFDEF NEVER_DEFINED} F.OkButton := nil; {$ENDIF}\n", written, StringComparison.Ordinal);
        var (line, column) = Refactoring.RenameCommandTests.LineColumn(written, "{$IFDEF NEVER_DEFINED} F.OkButton", "{$IFDEF NEVER_DEFINED} F.".Length);
        var row = Assert.Single(_p.Services.Messages.Items, i => !i.IsSummary);
        Assert.Equal((Core.Build.MessageSeverity.Warning, "OkButton in inactive code was not renamed to YesButton", line, column, column + 8),
            (row.Severity, row.Text, row.Location!.Line, row.Location.Column, row.Location.EndColumn));
        Assert.Equal(CallerPath, row.Location.FilePath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Rename 'OkButton' to 'YesButton': occurrences that were not changed are listed in Messages (1)", _p.Status);
        Assert.Equal(layout, string.Join(" | ", _p.Services.Layout.Layout.Groups.Select(g => string.Join(",", g.Tools) + ">" + g.ActiveTool)));
        Assert.Empty(_p.Dialogs.Requests);
    });

    /// <summary>The error dialog the designer showed (its pending editor) has ended (I-210).</summary>
    private static async Task TakeDialogAsync(FormDocumentViewModel document)
    {
        if (document.Designer.TakePendingEditor() is { } pending)
            await pending;
    }
}
