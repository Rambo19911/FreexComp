using System.Globalization;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using static FreexComp.Ide.Core.Tests.Designer.DesignerFixture;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>The Object Inspector's Properties page (object-inspector.md §1, §4, §5, §7, §9; D-074, D-075).</summary>
[Trait("Phase", "5")]
public sealed class ObjectInspectorViewModelTests
{
    private readonly DesignerFixture _f = new();

    private async Task<(FormDocumentViewModel Document, FormDesignerViewModel Designer, ObjectInspectorViewModel Inspector)> OpenAsync(bool design = true)
    {
        var document = await _f.OpenAsync(design: design);
        var inspector = new ObjectInspectorViewModel(_f.Services.Status);
        inspector.Bind(document.Designer);
        return (document, document.Designer, inspector);
    }

    /// <summary>Edit the current row: enter edit mode, type, commit (Enter).</summary>
    private static bool Enter(ObjectInspectorViewModel inspector, string path, string text)
    {
        inspector.CurrentPath = path;
        Assert.True(inspector.BeginEdit());
        inspector.EditText = text;
        return inspector.Commit();
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-001")]
    [Trait("Req", "DSG-OI-031")]
    public Task FollowsSelectionAndKeepsCurrentRow() => TestUiThread.RunAsync(() => FollowsSelectionAndKeepsCurrentRowOnUiThread()); // I-209

    private async Task FollowsSelectionAndKeepsCurrentRowOnUiThread()
    {
        var (document, designer, inspector) = await OpenAsync(design: false);

        // DSG-OI-001: the Code view is not a Design view: the empty state.
        Assert.True(inspector.IsEmpty);
        Assert.Equal(Strings.Msg_Inspector_NoSelection, inspector.EmptyText);
        Assert.Empty(inspector.Rows);

        Assert.True(document.ShowView(DocumentView.Design));
        Assert.False(inspector.IsEmpty);
        Assert.Equal(["MainForm"], inspector.SelectedNames);

        // DSG-OI-031: the current row keeps its path when the new selection has it, else the first property row.
        Select(designer, "GreetButton");
        Assert.Equal(["GreetButton"], inspector.SelectedNames);
        inspector.CurrentPath = "Caption";
        Select(designer, "OkButton");
        Assert.Equal("Caption", inspector.CurrentPath);
        Assert.Equal("OkButton", inspector.Rows.Single(r => r.Path == "Caption").Display);
        Select(designer, "InfoLabel");
        Assert.Equal("Caption", inspector.CurrentPath);
        Select(designer, "NameEdit");
        Assert.Equal(inspector.Rows.First(r => r.Kind == PropertyRowKind.Property).Path, inspector.CurrentPath);

        // Back to the Code view: no state of the form is kept.
        Assert.True(document.ShowView(DocumentView.Code));
        Assert.True(inspector.IsEmpty);
        Assert.Empty(inspector.Rows);
        Assert.Empty(inspector.SelectedNames);
        inspector.Bind(null);
        Assert.True(inspector.IsEmpty);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-092")]
    [Trait("Req", "DSG-OI-093")]
    [Trait("Req", "DSG-OI-034")]
    [Trait("Req", "DSG-OI-041")]
    public Task CommitCreatesOneUndoUnitForAllSelected() => TestUiThread.RunAsync(() => CommitCreatesOneUndoUnitForAllSelectedOnUiThread()); // I-209

    private async Task CommitCreatesOneUndoUnitForAllSelectedOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();
        Select(designer, "NameEdit", "GreetButton");
        var before = designer.Undo.UndoCount;

        // A relative edit computed per component, all in one unit.
        Assert.True(Enter(inspector, "Width", "+= 10"));
        Assert.Equal("210", Stored(designer, "NameEdit", "Width"));
        Assert.Equal("85", Stored(designer, "GreetButton", "Width"));
        Assert.Equal(before + 1, designer.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Property", designer.Undo.UndoTitleKey);
        Assert.False(inspector.IsEditing);

        // DSG-OI-034: unchanged text → no unit. The display is blank (values differ): typing nothing changes nothing.
        Assert.Equal("", inspector.Rows.Single(r => r.Path == "Width").Display);
        inspector.CurrentPath = "Top";
        Assert.True(inspector.BeginEdit());
        Assert.True(inspector.Commit());
        Assert.Equal(before + 1, designer.Undo.UndoCount);

        // DSG-OI-092: invalid for any component → nothing is applied.
        Assert.False(Enter(inspector, "Width", "abc"));
        Assert.Equal("210", Stored(designer, "NameEdit", "Width"));
        inspector.Cancel();

        // An absolute value for both, then one designer undo reverts both.
        Assert.True(Enter(inspector, "Top", "40"));
        Assert.Equal("40", Stored(designer, "NameEdit", "Top"));
        Assert.Equal("40", Stored(designer, "GreetButton", "Top"));
        Assert.Equal("40", inspector.Rows.Single(r => r.Path == "Top").Display);
        designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal("16", Stored(designer, "NameEdit", "Top"));
        Assert.Equal("15", Stored(designer, "GreetButton", "Top"));
        Assert.Equal("", inspector.Rows.Single(r => r.Path == "Top").Display); // DSG-OI-094: rows re-read
        Assert.Empty(designer.Surface.Layout.Differences());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-035")]
    public Task InvalidEntryKeepsEditorOpenWithMessage() => TestUiThread.RunAsync(() => InvalidEntryKeepsEditorOpenWithMessageOnUiThread()); // I-209

    private async Task InvalidEntryKeepsEditorOpenWithMessageOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();
        Select(designer, "NameEdit");
        var before = designer.Undo.UndoCount;

        // Enter with an invalid value: edit mode stays, the type's message is shown; Escape cancels.
        Assert.False(Enter(inspector, "MaxLength", "-5"));
        Assert.True(inspector.IsEditing);
        Assert.Equal(Format(Strings.Msg_Designer_ValueInvalidInteger, 0, int.MaxValue), inspector.EditError);
        Assert.Equal("-5", inspector.EditText);
        inspector.Cancel();
        Assert.False(inspector.IsEditing);
        Assert.Null(inspector.EditError);
        Assert.Equal(before, designer.Undo.UndoCount);

        // Focus loss: the edit is canceled instead and the message goes to the status bar.
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "12x";
        Assert.False(inspector.Commit(InspectorCommitCause.FocusLost));
        Assert.False(inspector.IsEditing);
        Assert.Equal(Strings.Msg_Designer_ValueInvalidExpression, _f.Services.Status.Message);
        Assert.Equal(before, designer.Undo.UndoCount);

        // A valid entry after a failure commits.
        Assert.True(Enter(inspector, "MaxLength", "2 * (3 + 4)"));
        Assert.Equal("14", Stored(designer, "NameEdit", "MaxLength"));

        // Read-only rows never enter edit mode (DSG-OI-036).
        Select(designer, "MainForm");
        inspector.CurrentPath = "PixelsPerInch";
        Assert.False(inspector.BeginEdit());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-038")]
    public Task ResetToDefault() => TestUiThread.RunAsync(() => ResetToDefaultOnUiThread()); // I-209

    private async Task ResetToDefaultOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();
        Select(designer, "GreetButton", "OkButton");
        inspector.CurrentPath = "Caption";
        Assert.True(inspector.CanResetToDefault);
        inspector.ResetToDefault();
        Assert.Null(Stored(designer, "GreetButton", "Caption"));
        Assert.Null(Stored(designer, "OkButton", "Caption"));
        Assert.Equal("Undo_Designer_Property", designer.Undo.UndoTitleKey);
        Assert.False(inspector.CanResetToDefault); // nothing stored any more

        // Not for Name, the always-written geometry or a read-only row.
        inspector.CurrentPath = "Left";
        Assert.False(inspector.CanResetToDefault);
        Select(designer, "OkButton");
        inspector.CurrentPath = "Name";
        Assert.False(inspector.CanResetToDefault);
        Select(designer, "MainForm");
        inspector.CurrentPath = "PixelsPerInch";
        Assert.False(inspector.CanResetToDefault);

        // An object row removes all of its stored sub-properties in one unit.
        Select(designer, "OkButton");
        inspector.Expand("Font");
        Assert.True(Enter(inspector, "Font.Name", "Consolas"));
        Assert.True(Enter(inspector, "Font.Height", "-15"));
        inspector.CurrentPath = "Font";
        Assert.True(inspector.CanResetToDefault);
        var count = designer.Undo.UndoCount;
        inspector.ResetToDefault();
        Assert.Null(Stored(designer, "OkButton", "Font.Name"));
        Assert.Null(Stored(designer, "OkButton", "Font.Height"));
        Assert.Equal(count + 1, designer.Undo.UndoCount);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-093")]
    public Task AlignCommitStoresReflowedBounds() => TestUiThread.RunAsync(() => AlignCommitStoresReflowedBoundsOnUiThread()); // I-209

    private async Task AlignCommitStoresReflowedBoundsOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();
        Select(designer, "Panel1");
        Assert.True(Enter(inspector, "Align", "altop"));

        // §9.1: re-flow of the parent; the computed bounds are stored in the same unit.
        Assert.Equal("alTop", Stored(designer, "Panel1", "Align"));
        Assert.Equal(("0", "0", "400", "41"),
            (Stored(designer, "Panel1", "Left"), Stored(designer, "Panel1", "Top"), Stored(designer, "Panel1", "Width"), Stored(designer, "Panel1", "Height")));
        Assert.Empty(designer.Surface!.Layout.Differences());
        Assert.Equal("400", inspector.Rows.Single(r => r.Path == "Width").Display);

        // An aligned control honours only its free dimension: Width is re-flowed back, Height is kept.
        Assert.True(Enter(inspector, "Width", "100"));
        Assert.Equal("400", Stored(designer, "Panel1", "Width"));
        Assert.True(Enter(inspector, "Height", "60"));
        Assert.Equal("60", Stored(designer, "Panel1", "Height"));

        // Back to alNone keeps the current bounds; undo of the Align commit restores everything.
        Assert.True(Enter(inspector, "Align", "alNone"));
        Assert.Equal(("0", "400", "60"), (Stored(designer, "Panel1", "Top"), Stored(designer, "Panel1", "Width"), Stored(designer, "Panel1", "Height")));
        for (var i = 0; i < 4; i++)
        {
            designer.Surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        }

        Assert.Equal(("16", "100", "185", "41"),
            (Stored(designer, "Panel1", "Left"), Stored(designer, "Panel1", "Top"), Stored(designer, "Panel1", "Width"), Stored(designer, "Panel1", "Height")));
        Assert.Null(Stored(designer, "Panel1", "Align"));

        // TabOrder: the siblings are renumbered in the same unit (DSG-SUR-096).
        Select(designer, "Panel1");
        Assert.True(Enter(inspector, "TabOrder", "0"));
        Assert.Equal(("1", "2", "3", "0"), (Stored(designer, "NameEdit", "TabOrder"), Stored(designer, "GreetButton", "TabOrder"),
            Stored(designer, "OkButton", "TabOrder"), Stored(designer, "Panel1", "TabOrder")));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-050")]
    public Task SetSubRowsToggleElements() => TestUiThread.RunAsync(() => SetSubRowsToggleElementsOnUiThread()); // I-209

    private async Task SetSubRowsToggleElementsOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();
        Select(designer, "GreetButton");
        inspector.Expand("Anchors");
        Assert.Contains(inspector.Rows, r => r.Path == "Anchors.akRight" && r.Display == "False");

        // One sub-row toggle commits the new set at once.
        inspector.CurrentPath = "Anchors.akRight";
        inspector.ToggleBoolean();
        Assert.Equal("[akLeft, akTop, akRight]", Stored(designer, "GreetButton", "Anchors"));
        Assert.Equal("[akLeft, akTop, akRight]", inspector.Rows.Single(r => r.Path == "Anchors").Display);
        Assert.True(inspector.Rows.Single(r => r.Path == "Anchors.akRight").IsBold);

        // Multi-selection: each component's own set gets just that element, as one unit.
        Select(designer, "NameEdit", "GreetButton");
        inspector.Expand("Anchors");
        var count = designer.Undo.UndoCount;
        inspector.CurrentPath = "Anchors.akBottom";
        inspector.ToggleBoolean();
        Assert.Equal("[akLeft, akTop, akBottom]", Stored(designer, "NameEdit", "Anchors"));
        Assert.Equal("[akLeft, akTop, akRight, akBottom]", Stored(designer, "GreetButton", "Anchors"));
        Assert.Equal(count + 1, designer.Undo.UndoCount);

        // Text entry on the set row itself (brackets optional).
        Select(designer, "NameEdit");
        Assert.True(Enter(inspector, "Anchors", "akTop akLeft"));
        Assert.Null(Stored(designer, "NameEdit", "Anchors")); // the default set is not stored

        // Booleans toggle the same way (Space).
        inspector.CurrentPath = "ReadOnly";
        inspector.ToggleBoolean();
        Assert.Equal("True", Stored(designer, "NameEdit", "ReadOnly"));
    }

    [Fact(Timeout = 30000)]
    public Task ComponentReferenceListIsTypeFiltered() => TestUiThread.RunAsync(() => ComponentReferenceListIsTypeFilteredOnUiThread()); // I-209

    private async Task ComponentReferenceListIsTypeFilteredOnUiThread()
    {
        var (_, designer, inspector) = await OpenAsync();

        // §5.1: (none) + the form's components assignable to the property's class, without the component itself, by name.
        Select(designer, "MainForm");
        Assert.Equal(["(none)", "GreetButton", "NameEdit", "OkButton", "Panel1"], inspector.ValueList("ActiveControl"));
        Assert.Equal(["(none)"], inspector.ValueList("Menu"));

        Assert.False(Enter(inspector, "ActiveControl", "InfoLabel"));
        Assert.Equal(Format(Strings.Msg_Designer_ReferenceNotFound, "InfoLabel"), inspector.EditError);
        inspector.Cancel();
        Assert.True(Enter(inspector, "ActiveControl", "nameedit"));
        Assert.Equal("NameEdit", Stored(designer, "MainForm", "ActiveControl"));
        Assert.True(Enter(inspector, "ActiveControl", "(none)"));
        Assert.Null(Stored(designer, "MainForm", "ActiveControl"));

        Select(designer, "Panel1");
        Assert.DoesNotContain("Panel1", inspector.ValueList("PopupMenu"));
        Select(designer, "InfoLabel");
        Assert.Equal(["(none)", "GreetButton", "NameEdit", "OkButton", "Panel1"], inspector.ValueList("FocusControl"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-070")]
    [Trait("Req", "DSG-OI-072")]
    public Task NameValidationOrder() => TestUiThread.RunAsync(() => NameValidationOrderOnUiThread()); // I-209

    private async Task NameValidationOrderOnUiThread()
    {
        var (document, designer, inspector) = await OpenAsync();
        Select(designer, "GreetButton");
        var before = designer.Undo.UndoCount;

        void Fails(string text, string expected)
        {
            Assert.False(Enter(inspector, "Name", text));
            Assert.Equal(expected, inspector.EditError);
            inspector.Cancel();
        }

        // (1) shape and length, (2) reserved word, (3) another component, (4) a member of the class or its ancestors.
        Fails("1Button", Strings.Msg_Designer_NameInvalid);
        Fails("Greet Button", Strings.Msg_Designer_NameInvalid);
        Fails("", Strings.Msg_Designer_NameInvalid);
        Fails(new string('A', 128), Strings.Msg_Designer_NameInvalid);
        Fails("begin", Format(Strings.Msg_Designer_NameReserved, "begin"));
        Fails("nameedit", Format(Strings.Msg_Designer_NameDuplicate, "nameedit"));
        Fails("NameEdit", Format(Strings.Msg_Designer_NameDuplicate, "NameEdit")); // a duplicate before the field conflict
        Fails("FCount", Format(Strings.Msg_Designer_NameConflictsWithMember, "TMainForm", "FCount"));
        Fails("SharedClick", Format(Strings.Msg_Designer_NameConflictsWithMember, "TMainForm", "SharedClick"));
        Fails("Caption", Format(Strings.Msg_Designer_NameConflictsWithMember, "TMainForm", "Caption"));
        Assert.Equal(before, designer.Undo.UndoCount);

        // A change of casing only is allowed (the field is the one being renamed).
        Assert.True(Enter(inspector, "Name", "greetButton"));
        Assert.NotNull(designer.Model!.FindByName("greetButton"));
        Assert.Equal("greetButton", DesignerFixture.Object(designer, "greetButton").Name);
        Assert.Contains("    greetButton: TButton;", document.Buffer.Text, StringComparison.Ordinal);

        // DSG-OI-072: Name is blank and read-only in a multi-selection.
        Select(designer, "NameEdit", "OkButton");
        var name = inspector.Rows.Single(r => r.Path == "Name");
        Assert.Equal("", name.Display);
        Assert.True(name.IsReadOnly);
        inspector.CurrentPath = "Name";
        Assert.False(inspector.BeginEdit());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-OI-071")]
    [Trait("Req", "DSG-CS-046")]
    public Task RenameUpdatesCaptionWhenEqualToOldName() => TestUiThread.RunAsync(() => RenameUpdatesCaptionWhenEqualToOldNameOnUiThread()); // I-209

    private async Task RenameUpdatesCaptionWhenEqualToOldNameOnUiThread()
    {
        var (document, designer, inspector) = await OpenAsync();
        var before = designer.Undo.UndoCount;

        // Caption equal to the old name follows; field renamed; one unit; handlers are not renamed.
        Select(designer, "OkButton");
        Assert.True(Enter(inspector, "Name", "AcceptButton"));
        Assert.Equal("AcceptButton", Stored(designer, "AcceptButton", "Caption"));
        Assert.Contains("    AcceptButton: TButton;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("OkButton", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(before + 1, designer.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Rename", designer.Undo.UndoTitleKey);
        Assert.Equal(["AcceptButton"], inspector.SelectedNames);

        // Text likewise; a Caption that differs stays; the handler keeps its name.
        Select(designer, "NameEdit");
        Assert.True(Enter(inspector, "Name", "UserEdit"));
        Assert.Equal("UserEdit", Stored(designer, "UserEdit", "Text"));
        Select(designer, "GreetButton");
        Assert.True(Enter(inspector, "Name", "HelloButton"));
        Assert.Equal("&Greet", Stored(designer, "HelloButton", "Caption"));
        Assert.Equal("GreetButtonClick", Stored(designer, "HelloButton", "OnClick"));
        Assert.Contains("procedure TMainForm.GreetButtonClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);

        // The caption comparison is case-sensitive.
        Select(designer, "InfoLabel");
        Assert.True(Enter(inspector, "Caption", "infolabel"));
        Assert.True(Enter(inspector, "Name", "TitleLabel"));
        Assert.Equal("infolabel", Stored(designer, "TitleLabel", "Caption"));

        // Undo reverts the form and the field.
        designer.Surface!.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.NotNull(designer.Model!.FindByName("InfoLabel"));
        Assert.Contains("    InfoLabel: TLabel;", document.Buffer.Text, StringComparison.Ordinal);

        // The form root (DSG-CS-046): class, type name and variable follow; the caption equal to the old name too.
        Select(designer, "MainForm");
        Assert.True(Enter(inspector, "Name", "Main2"));
        Assert.Equal("Main2", designer.Model.Root.Name);
        Assert.Equal("TMain2", designer.Model.Root.TypeName);
        Assert.Equal("Main2", Stored(designer, "Main2", "Caption"));
        Assert.Contains("TMain2 = class(TForm)", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("  Main2: TMain2;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure TMain2.GreetButtonClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(FreexComp.Ide.Core.Designer.CodeSync.CodeSyncState.Ok, designer.CodeSync.State);
        designer.Surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal("TMainForm", designer.Model.Root.TypeName);
        Assert.Contains("TMainForm = class(TForm)", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(FreexComp.Ide.Core.Designer.CodeSync.CodeSyncState.Ok, designer.CodeSync.State);
    }
}
