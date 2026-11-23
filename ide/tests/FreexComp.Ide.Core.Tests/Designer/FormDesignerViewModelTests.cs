using System.Globalization;
using System.Text;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// The form designer's document side (designer README §3, code-sync.md §9, form-document.md §6/§8; D-072): two buffers,
/// three views, the switching rules, dirty tracking over both buffers and the save pipeline.
/// </summary>
[Trait("Phase", "5")]
public sealed class FormDesignerViewModelTests
{
    private const string Dir = @"C:\p";
    private const string UnitPath = Dir + @"\MainForm.pas";
    private const string FormPath = Dir + @"\MainForm.form";

    private readonly InMemoryFileSystem _memory = new();
    private readonly RecordingFileSystem _fs;
    private readonly IdeServices _services;

    public FormDesignerViewModelTests()
    {
        _fs = new RecordingFileSystem(_memory);
        _services = TestComposition.Build(o => o.FileSystem = _fs);
        ((DocumentService)_services.Documents).DesignCatalog = CodeSyncTests.Catalog;
    }

    private void File(string path, string text)
    {
        _memory.CreateDirectory(Path.GetDirectoryName(path)!);
        _memory.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
    }

    private string Disk(string path) => Encoding.UTF8.GetString(_memory.ReadAllBytes(path));

    private async Task<FormDocumentViewModel> OpenAsync(string? unit = null, string? form = null)
    {
        File(UnitPath, unit ?? TestUnits.Read("MainForm.pas"));
        File(FormPath, form ?? TestUnits.Read("MainForm.form"));
        var result = await _services.Documents.OpenAsync(UnitPath);
        Assert.True(result.Success, result.MessageId);
        return Assert.IsType<FormDocumentViewModel>(result.Value);
    }

    /// <summary>A designer interaction: applied through the editor and pushed as one undo unit.</summary>
    private static void Edit(FormDesignerViewModel designer, FormChange change)
    {
        var inverse = designer.Editor!.Apply([change]);
        designer.Undo.Push(new DesignerUndoUnit("Undo_Designer_Property", [change], inverse, [], SelectionSnapshot.Empty, SelectionSnapshot.Empty));
    }

    private static void UndoDesigner(FormDesignerViewModel designer) => designer.Editor!.Apply(designer.Undo.Undo()!.Undo);

    private static string Caption(FormDesignerViewModel designer) =>
        ((StringValue)designer.Model!.Root.FindProperty("Caption")!.Value).Value;

    private static void Replace(ITextBuffer buffer, string oldText, string newText)
    {
        var offset = buffer.Text.IndexOf(oldText, StringComparison.Ordinal);
        Assert.True(offset >= 0, oldText);
        buffer.ApplyEdits([new TextEdit(offset, oldText.Length, newText)]);
    }

    /// <summary>The paths of the project files written (atomically, through a temporary file), in order.</summary>
    private List<string> Writes() =>
        [.. _fs.Calls.Where(c => c.StartsWith("WriteAllBytesDurable " + Dir + @"\", StringComparison.Ordinal))
            .Select(c => c["WriteAllBytesDurable ".Length..])
            .Select(p => p[..p.IndexOf(".~tmp", StringComparison.Ordinal)])];

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-091")]
    [Trait("Req", "DSG-FD-093")]
    public Task ViewSwitchingRules() => TestUiThread.RunAsync(() => ViewSwitchingRulesOnUiThread()); // I-209

    private async Task ViewSwitchingRulesOnUiThread()
    {
        var document = await OpenAsync();
        var designer = document.Designer;
        var formFile = TestUnits.Read("MainForm.form");
        Assert.Equal(DocumentView.Code, document.ActiveView);
        var model = designer.Model;
        Assert.NotNull(model);
        Assert.Equal("MainForm", Caption(designer));

        // Code → Text with the model unedited: the text is the file's own text.
        Assert.True(document.ShowView(DocumentView.Text));
        Assert.Equal(formFile, document.FormBuffer.Text);
        Assert.Same(document.FormText, document.CommandTarget);

        // Text → Design unchanged: same model; a designer edit; Design → Text serializes the model (FormWriter).
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.Same(model, designer.Model);
        Edit(designer, new SetProperty("MainForm", "Caption", new StringValue("Hello")));
        Assert.True(document.ShowView(DocumentView.Text));
        Assert.Equal(FormWriter.Write(model), document.FormBuffer.Text);
        Assert.Contains("Caption = 'Hello'", document.FormBuffer.Text, StringComparison.Ordinal);

        // Leaving the Text view unchanged keeps the model, the selection and the undo stack.
        document.ToggleFormCode(); // F7 from the Text view goes to the Design view
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Same(model, designer.Model);
        Assert.Equal(1, designer.Undo.UndoCount);

        // A syntax error refuses Text → Design and Text → Code: the Text view stays, InfoBar.FormSyntax, caret at the error.
        document.ShowFormAsText();
        var broken = document.FormBuffer.Text.Replace("  Left = 200", "  Left = = 200", StringComparison.Ordinal);
        document.FormBuffer.ReplaceAll(broken, resetUndo: false);
        var error = FormParser.Parse(broken, FormPath, CodeSyncTests.Catalog).Diagnostics.Single();
        var replaced = 0;
        designer.ModelReplaced += (_, _) => replaced++;
        Assert.False(document.ShowView(DocumentView.Design));
        Assert.False(document.ShowView(DocumentView.Code));
        document.ToggleFormCode();
        Assert.Equal(DocumentView.Text, document.ActiveView);
        var bar = Assert.Single(document.FormText.InfoBars, b => b.Kind == FormDesignerViewModel.FormSyntaxInfoBarKind);
        Assert.Equal(InfoBarSeverity.Error, bar.Severity);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_FormSyntaxError, error.Message, error.Line, error.Col), bar.Text);
        Assert.Equal(new TextPosition(error.Line, error.Col), document.FormText.ViewState.Caret);
        Assert.Same(model, designer.Model);
        Assert.Equal(0, replaced);

        // Fixed and changed: the model is replaced, the designer undo stack cleared, the info bar removed.
        document.FormBuffer.ReplaceAll(broken.Replace("Left = = 200", "Left = 200", StringComparison.Ordinal)
            .Replace("Caption = 'Hello'", "Caption = 'World'", StringComparison.Ordinal), resetUndo: false);
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.NotSame(model, designer.Model);
        Assert.Equal("World", Caption(designer));
        Assert.Equal(0, designer.Undo.UndoCount);
        Assert.Equal(1, replaced);
        Assert.DoesNotContain(document.FormText.InfoBars, b => b.Kind == FormDesignerViewModel.FormSyntaxInfoBarKind);
        Assert.Null(document.CommandTarget); // the Design view has its own targets

        // F7: Design → Code. Code → Design with a modified unit (state Ok) repairs the form class (DSG-CS-091 c).
        document.ToggleFormCode();
        Assert.Equal(DocumentView.Code, document.ActiveView);
        Replace(document.Buffer, "    NameEdit: TEdit;\r\n", "");
        Assert.DoesNotContain("NameEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.Contains("NameEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-091")]
    public Task DirtyTrackingAcrossBothBuffers() => TestUiThread.RunAsync(() => DirtyTrackingAcrossBothBuffersOnUiThread()); // I-209

    private async Task DirtyTrackingAcrossBothBuffersOnUiThread()
    {
        var document = await OpenAsync();
        var designer = document.Designer;
        Assert.False(document.IsModified);
        var notified = 0;
        document.PropertyChanged += (_, e) => notified += e.PropertyName == nameof(DocumentViewModel.IsModified) ? 1 : 0;

        // The form buffer: a designer edit marks the document modified; undo back to the save point clears it.
        Edit(designer, new SetProperty("MainForm", "Caption", new StringValue("Hello")));
        Assert.True(document.IsModified);
        Assert.True(designer.IsFormModified);
        Assert.True(notified > 0);
        UndoDesigner(designer);
        Assert.False(document.IsModified);

        // The unit buffer alone.
        Replace(document.Buffer, "  public\r\n", "  public\r\n    { comment }\r\n");
        Assert.True(document.IsModified);
        Assert.False(designer.IsFormModified);
        document.Buffer.Undo();
        Assert.False(document.IsModified);

        // A pending Text-view edit; typed back to the original it is no edit at all.
        Assert.True(document.ShowView(DocumentView.Text));
        var original = document.FormBuffer.Text;
        Replace(document.FormBuffer, "Left = 200", "Left = 208");
        Assert.True(document.IsModified);
        Replace(document.FormBuffer, "Left = 208", "Left = 200");
        Assert.False(document.IsModified);
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.False(document.IsModified);

        // A Text-view edit that replaces the model: the designer save point is gone (DSG-FD-093), so it stays modified.
        Assert.True(document.ShowView(DocumentView.Text));
        Replace(document.FormBuffer, "Left = 200", "Left = 208");
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.True(document.IsModified);
        Assert.True(designer.IsFormModified);

        // Saving makes both buffers clean.
        Assert.True(await _services.Documents.SaveAsync(document));
        Assert.False(document.IsModified);
        Assert.NotEqual(original, Disk(FormPath));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-091")]
    [Trait("Req", "DSG-CS-072")]
    [Trait("Req", "DSG-CS-074")]
    public Task SaveRunsRepairThenRemovesHandlersThenWrites() => TestUiThread.RunAsync(() => SaveRunsRepairThenRemovesHandlersThenWritesOnUiThread()); // I-209

    private async Task SaveRunsRepairThenRemovesHandlersThenWritesOnUiThread()
    {
        // The unit lacks the NameEdit field; the session creates a handler that stays empty and unreferenced.
        var document = await OpenAsync(TestUnits.Read("MainForm.pas").Replace("    NameEdit: TEdit;\r\n", "", StringComparison.Ordinal));
        var designer = document.Designer;
        designer.CodeSync.CreateHandler("NameEditChange", "TNotifyEvent");
        Assert.Contains("procedure TMainForm.NameEditChange(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Edit(designer, new SetProperty("MainForm", "Caption", new StringValue("Hello")));

        Assert.True(await _services.Documents.SaveAsync(document));

        // Written: the repaired unit without the empty handler, first; then the canonical form.
        var unit = Disk(UnitPath);
        Assert.Contains("    NameEdit: TEdit;", unit, StringComparison.Ordinal);
        Assert.DoesNotContain("NameEditChange", unit, StringComparison.Ordinal);
        Assert.Equal(document.Buffer.Text, unit);
        Assert.Equal(FormWriter.Write(designer.Model!), Disk(FormPath));
        Assert.Equal([UnitPath, FormPath], Writes());
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_HandlersRemovedOne), _services.Status.Message);
        Assert.False(document.IsModified);

        // The order on the code editor's undo stack: removal last, repair before it, each one group (DSG-CS-074).
        document.Buffer.Undo();
        Assert.Contains("NameEditChange", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("    NameEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);
        document.Buffer.Undo();
        Assert.Contains("NameEditChange", document.Buffer.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("NameEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-051")]
    public Task UnmodifiedFormIsNotRewritten() => TestUiThread.RunAsync(() => UnmodifiedFormIsNotRewrittenOnUiThread()); // I-209

    private async Task UnmodifiedFormIsNotRewrittenOnUiThread()
    {
        // Valid but not canonical: CRLF, a blank line, different indentation.
        const string form = "object MainForm: TMainForm\r\n\r\n    Left = 200\r\n  Top = 120\r\n  Caption = 'MainForm'\r\n"
            + "  ClientWidth = 400\r\n  ClientHeight = 300\r\n  PixelsPerInch = 96\r\n  object NameEdit: TEdit\r\n    Left = 16\r\n"
            + "    Top = 16\r\n    Width = 200\r\n    Height = 23\r\n    TabOrder = 0\r\n  end\r\n  object GreetButton: TButton\r\n"
            + "    Left = 232\r\n    Top = 15\r\n    Width = 75\r\n    Height = 25\r\n    Caption = '&Greet'\r\n    TabOrder = 1\r\n"
            + "    OnClick = GreetButtonClick\r\n  end\r\nend\r\n";
        var document = await OpenAsync(form: form);
        Assert.NotNull(document.Designer.Model);

        // A unit edit and a look at the Text view (unchanged): only the unit is written.
        Replace(document.Buffer, "  public\r\n", "  public\r\n    { comment }\r\n");
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.True(document.ShowView(DocumentView.Text));
        Assert.Equal(form, document.FormBuffer.Text);
        Assert.True(document.ShowView(DocumentView.Code));
        Assert.True(await _services.Documents.SaveAsync(document));

        Assert.Equal([UnitPath], Writes());
        Assert.Equal(form, Disk(FormPath));
        Assert.Contains("{ comment }", Disk(UnitPath), StringComparison.Ordinal);
        Assert.False(document.IsModified);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-080")]
    public Task OpeningTheFormFileOpensItsUnitInDesignView() => TestUiThread.RunAsync(() => OpeningTheFormFileOpensItsUnitInDesignViewOnUiThread()); // I-209

    private async Task OpeningTheFormFileOpensItsUnitInDesignViewOnUiThread()
    {
        File(UnitPath, TestUnits.Read("MainForm.pas"));
        File(FormPath, TestUnits.Read("MainForm.form"));

        var document = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(FormPath)).Value);
        Assert.Equal(UnitPath, document.FilePath);
        Assert.Equal(DocumentView.Design, document.ActiveView);

        // Opened again from the Code view: the same document, shown in the Design view.
        Assert.True(document.ShowView(DocumentView.Code));
        Assert.Same(document, (await _services.Documents.OpenAsync(FormPath)).Value);
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Single(_services.Documents.Documents);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-080")]
    public Task FormWithoutUnitOpensDesignWithoutCodeSync() => TestUiThread.RunAsync(() => FormWithoutUnitOpensDesignWithoutCodeSyncOnUiThread()); // I-209

    private async Task FormWithoutUnitOpensDesignWithoutCodeSyncOnUiThread()
    {
        const string lonely = Dir + @"\Lonely.form";
        File(lonely, "object Lonely: TForm\n  Left = 200\n  Top = 120\n  Caption = 'Lonely'\n  ClientWidth = 400\n  ClientHeight = 300\n"
            + "  PixelsPerInch = 96\n  object Button1: TButton\n    Left = 8\n    Top = 8\n    Width = 75\n    Height = 25\n    TabOrder = 0\n  end\nend\n");

        // The NoUnit mode: an editable Design view without code sync and without a Code view.
        var document = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(lonely)).Value);
        var designer = document.Designer;
        Assert.Equal(lonely, document.FilePath);
        Assert.Equal("Lonely.form", document.Title);
        Assert.False(document.HasUnit);
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Equal(FreexComp.Ide.Core.Designer.CodeSync.CodeSyncState.NoUnit, designer.CodeSync.State);
        Assert.False(designer.Surface!.IsReadOnly);
        Assert.False(document.ShowView(DocumentView.Code));
        Assert.Equal(DocumentView.Design, document.ActiveView);
        Assert.Same(document, (await _services.Documents.OpenAsync(lonely)).Value);

        // Edits work; the events page is read-only and the default handler does nothing.
        var inspector = new ObjectInspectorViewModel(_services.Status);
        inspector.Bind(designer);
        designer.Surface.Selection.Set(["Button1"]);
        inspector.CurrentPath = "Caption";
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "Press";
        Assert.True(inspector.Commit());
        inspector.Page = InspectorPage.Events;
        Assert.True(inspector.IsEventsReadOnly);
        Assert.False(inspector.CommitEvent("OnClick", "Button1Click"));
        Assert.False(designer.Surface.CanExecute(FreexComp.Designer.Surface.Commands.CreateDefaultHandler));
        inspector.CurrentPath = "Name";
        inspector.Page = InspectorPage.Properties;
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "PressButton";
        Assert.True(inspector.Commit());
        Assert.True(document.IsModified);

        // Save writes only the form.
        Assert.True(await _services.Documents.SaveAsync(document));
        Assert.Equal([lonely], Writes());
        Assert.Contains("Caption = 'Press'", Disk(lonely), StringComparison.Ordinal);
        Assert.Contains("object PressButton: TButton", Disk(lonely), StringComparison.Ordinal);
        Assert.False(_memory.FileExists(Dir + @"\Lonely.pas"));
        Assert.False(document.IsModified);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-091")]
    public Task RepairRunsBeforeFirstEditInSameUnit() => TestUiThread.RunAsync(() => RepairRunsBeforeFirstEditInSameUnitOnUiThread()); // I-209

    private async Task RepairRunsBeforeFirstEditInSameUnitOnUiThread()
    {
        // The unit lacks the NameEdit field: opening reports it and does not edit (DSG-CS-090).
        var unitText = TestUnits.Read("MainForm.pas").Replace("    NameEdit: TEdit;\r\n", "", StringComparison.Ordinal);
        var document = await OpenAsync(unitText);
        var designer = document.Designer;
        Assert.Equal(unitText, document.Buffer.Text);
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.Equal(unitText, document.Buffer.Text);
        var surface = designer.Surface!;

        // The first mutating action repairs the class first, inside its own undo unit.
        surface.Selection.Set(["GreetButton"]);
        Assert.True(surface.OnKey(new FreexComp.Designer.Surface.KeyInput(FreexComp.Designer.Surface.DesignKey.Right, FreexComp.Designer.Surface.InputModifiers.Ctrl)));
        Assert.Contains("    NameEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(1, designer.Undo.UndoCount);
        Assert.NotEmpty(designer.Undo.Peek!.CodeEdits);

        // Undo reverts the action and the repair together; redo repeats both.
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(unitText, document.Buffer.Text);
        surface.Execute(FreexComp.Designer.Surface.Commands.Redo);
        Assert.Contains("    NameEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);

        // Later actions do not repair again (the user's code edits stand until save).
        Replace(document.Buffer, "    NameEdit: TEdit;\r\n", "");
        surface.Execute(FreexComp.Designer.Surface.Commands.SendToBack);
        Assert.DoesNotContain("NameEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Empty(designer.Undo.Peek!.CodeEdits);

        // After a Text-view replacement of the model the next action repairs again (also from the Object Inspector).
        Assert.True(document.ShowView(DocumentView.Text));
        Replace(document.FormBuffer, "Caption = 'MainForm'", "Caption = 'Main'");
        Assert.True(document.ShowView(DocumentView.Design));
        Assert.DoesNotContain("NameEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);
        var inspector = new ObjectInspectorViewModel(_services.Status);
        inspector.Bind(designer);
        inspector.CurrentPath = "Caption";
        Assert.True(inspector.BeginEdit());
        inspector.EditText = "Hello";
        Assert.True(inspector.Commit());
        Assert.Contains("    NameEdit: TEdit;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.NotEmpty(designer.Undo.Peek!.CodeEdits);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-FD-100")]
    [Trait("Req", "DSG-FD-093")]
    public Task ExternalFormChangeReloads() => TestUiThread.RunAsync(() => ExternalFormChangeReloadsOnUiThread()); // I-209

    private async Task ExternalFormChangeReloadsOnUiThread()
    {
        var document = await OpenAsync();
        var designer = document.Designer;
        Assert.True(document.ShowView(DocumentView.Design));
        designer.Surface!.Selection.Set(["GreetButton"]);
        var model = designer.Model;

        // Unmodified: the changed form is reloaded silently, the model replaced, the selection kept by name.
        var changed = TestUnits.Read("MainForm.form").Replace("Caption = 'MainForm'", "Caption = 'Outside'", StringComparison.Ordinal);
        File(FormPath, changed);
        _services.FileChanges.CheckAll();
        await _services.FileChanges.WhenIdle();
        Assert.NotSame(model, designer.Model);
        Assert.Equal("Outside", Caption(designer));
        Assert.Equal(["GreetButton"], designer.Surface!.Selection.Items.Select(o => o.Name));
        Assert.Equal(changed, document.FormBuffer.Text);
        Assert.False(document.IsModified);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_ReloadedSilently, document.Title), _services.Status.Message);

        // Modified: the document's external-change bar; Reload and Discard replaces the model and clears the undo stack.
        Edit(designer, new SetProperty("MainForm", "Caption", new StringValue("Mine")));
        File(FormPath, changed.Replace("'Outside'", "'Again'", StringComparison.Ordinal));
        _services.FileChanges.CheckAll();
        await _services.FileChanges.WhenIdle();
        Assert.Equal("Mine", Caption(designer));
        var bar = Assert.Single(document.InfoBars, b => b.Kind == TextDocumentViewModel.ExternalChangeInfoBarKind);
        Assert.Equal(Strings.Msg_File_ExternalChangeDirty, bar.Text);
        await bar.Actions.Single(a => a.Name == "ReloadAndDiscard").ExecuteAsync();
        await _services.FileChanges.WhenIdle();
        Assert.Equal("Again", Caption(designer));
        Assert.Equal(0, designer.Undo.UndoCount);
        Assert.False(document.IsModified);
        Assert.DoesNotContain(document.InfoBars, b => b.Kind == TextDocumentViewModel.ExternalChangeInfoBarKind);

        // A form in another folder is watched as well (the watcher of its folder reports it).
        Assert.Equal(TestUnits.Read("MainForm.pas"), document.Buffer.Text);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-022")]
    [Trait("Req", "IDE-PM-071")]
    public Task FormFollowsUnitRenameAndSaveAs() => TestUiThread.RunAsync(() => FormFollowsUnitRenameAndSaveAsOnUiThread()); // I-209

    private async Task FormFollowsUnitRenameAndSaveAsOnUiThread()
    {
        // Save As: the form goes with the unit under the new base name; the {$FORM} directive follows.
        var document = await OpenAsync();
        Assert.True((await _services.Documents.SaveAsAsync(document, Dir + @"\Other.pas")).Success);
        Assert.Equal(Dir + @"\Other.pas", document.FilePath);
        Assert.Equal(Dir + @"\Other.form", document.FormText.FilePath);
        Assert.Equal("Other.form", document.Designer.CodeSync.FormFileName);
        Assert.Contains("{$FORM Other.form}", Disk(Dir + @"\Other.pas"), StringComparison.Ordinal);
        Assert.Equal(TestUnits.Read("MainForm.form"), Disk(Dir + @"\Other.form"));
        Assert.Equal(TestUnits.Read("MainForm.form"), Disk(FormPath)); // the original stays
        Assert.False(document.IsModified);

        // A later designer edit saves into the new form file.
        Edit(document.Designer, new SetProperty("MainForm", "Caption", new StringValue("Moved")));
        Assert.True(await _services.Documents.SaveAsync(document));
        Assert.Contains("Caption = 'Moved'", Disk(Dir + @"\Other.form"), StringComparison.Ordinal);
        Assert.DoesNotContain("Moved", Disk(FormPath), StringComparison.Ordinal);
        Assert.True(_services.Documents.Close(document));

        // Rename in the project: the form file is renamed with it and the open document follows it.
        File(Dir + @"\App.pproj", """
            { "formatVersion": 1, "name": "App", "mainSource": "App.pas",
              "files": [ { "path": "App.pas" }, { "path": "MainForm.pas", "form": "MainForm.form" } ] }
            """);
        File(Dir + @"\App.pas", "program App;\nbegin\nend.\n");
        Assert.True(await _services.Projects.OpenAsync(Dir + @"\App.pproj"));
        var opened = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(UnitPath)).Value);
        Assert.True(await _services.Projects.RenameFileAsync("MainForm.pas", "Renamed.pas"));
        Assert.Equal(Dir + @"\Renamed.pas", opened.FilePath);
        Assert.Equal(Dir + @"\Renamed.form", opened.FormText.FilePath);
        Assert.Equal("Renamed.form", opened.Designer.CodeSync.FormFileName);
        Assert.False(_memory.FileExists(FormPath));
        Assert.Equal(TestUnits.Read("MainForm.form"), Disk(Dir + @"\Renamed.form"));
        Assert.Contains("{$FORM Renamed.form}", Disk(Dir + @"\Renamed.pas"), StringComparison.Ordinal);
        Edit(opened.Designer, new SetProperty("MainForm", "Caption", new StringValue("Renamed")));
        Assert.True(await _services.Documents.SaveAsync(opened));
        Assert.Contains("Caption = 'Renamed'", Disk(Dir + @"\Renamed.form"), StringComparison.Ordinal);
        Assert.False(_memory.FileExists(FormPath));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-008")]
    public Task ReloadMetadataRebindsOpenForms() => TestUiThread.RunAsync(() => ReloadMetadataRebindsOpenFormsOnUiThread()); // I-209

    private async Task ReloadMetadataRebindsOpenFormsOnUiThread()
    {
        const string meta = @"C:\meta";
        var f = new DesignerFixture();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "metadata"), "*.json"))
        {
            f.File(Path.Combine(meta, Path.GetFileName(file)), System.IO.File.ReadAllText(file));
        }

        f.Services.Settings.Set(FreexComp.Ide.Core.Settings.SettingsCatalog.ToolchainFrameworkDesignDir, meta);
        var document = await f.OpenAsync();
        var designer = document.Designer;
        var palette = new ToolPaletteViewModel(CodeSyncTests.Catalog);
        f.Services.Metadata!.CatalogChanged += (_, catalog) => palette.SetCatalog(catalog);
        designer.Surface!.Selection.Set(["NameEdit", "GreetButton"]);
        Assert.True(designer.Surface.OnKey(new FreexComp.Designer.Surface.KeyInput(FreexComp.Designer.Surface.DesignKey.Right, FreexComp.Designer.Surface.InputModifiers.Ctrl)));
        Assert.Equal(1, designer.Undo.UndoCount);
        var model = designer.Model;
        var replaced = 0;
        designer.ModelReplaced += (_, _) => replaced++;

        // Same structure: the model is re-read against the new catalog, selection kept by name, undo stack kept.
        await f.Services.Commands.ExecuteAsync("component.reloadMetadata");
        Assert.NotSame(model, designer.Model);
        Assert.Equal(1, replaced);
        Assert.Equal(["NameEdit", "GreetButton"], designer.Surface!.Selection.Items.Select(o => o.Name));
        Assert.Equal(1, designer.Undo.UndoCount);
        var count = f.Services.Metadata.Catalog.All.Count(c => c.ClassName != "TComponent");
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_MetadataReloaded, count), f.Services.Status.Message);
        Assert.Same(f.Services.Metadata.Catalog, designer.Catalog);
        Assert.Same(f.Services.Metadata.Catalog, ((DocumentService)f.Services.Documents).DesignCatalog);
        Assert.Contains(palette.Pages, p => p.Name == "Standard");
        designer.Surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(232, ((IntegerValue)designer.Model!.FindByName("GreetButton")!.FindProperty("Left")!.Value).Value);

        // A catalog without the StdCtrls classes: the structure differs (preserved components), the stack is cleared.
        f.File(Path.Combine(meta, "index.json"), System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "metadata", "index.json"))
            .Replace("\"StdCtrls.components.json\", ", "", StringComparison.Ordinal));
        designer.Surface.Execute(FreexComp.Designer.Surface.Commands.Redo);
        await f.Services.Commands.ExecuteAsync("component.reloadMetadata");
        Assert.True(designer.Model!.FindByName("GreetButton")!.IsPreserved);
        Assert.Equal(0, designer.Undo.UndoCount);
        Assert.Equal(["NameEdit", "GreetButton"], designer.Surface!.Selection.Items.Select(o => o.Name));
        Assert.DoesNotContain(palette.Pages.SelectMany(p => p.Items), i => i.ClassName == "TButton");
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-TP-053")]
    [Trait("Req", "DSG-CS-051")]
    public Task PlacementAddsFieldAndUses() => TestUiThread.RunAsync(() => PlacementAddsFieldAndUsesOnUiThread()); // I-209

    private async Task PlacementAddsFieldAndUsesOnUiThread()
    {
        var document = await OpenAsync();
        var designer = document.Designer;
        Assert.True(document.ShowView(DocumentView.Design));
        var surface = designer.Surface!;
        var original = document.Buffer.Text;

        // A panel (unit ExtCtrls, not used yet): the field and the uses entry in the same "Add" undo unit.
        surface.PlaceAtDefaultPosition("TPanel");
        Assert.NotNull(designer.Model!.FindByName("Panel1"));
        var text = document.Buffer.Text;
        Assert.Contains("    GreetButton: TButton;\r\n    Panel1: TPanel;\r\n", text, StringComparison.Ordinal);
        Assert.Contains("  Classes, Controls, Forms, StdCtrls, ExtCtrls;", text, StringComparison.Ordinal);
        Assert.Equal(1, designer.Undo.UndoCount);
        Assert.NotEmpty(designer.Undo.Peek!.CodeEdits);

        // A button (StdCtrls, used already): only the field.
        surface.PlaceAtDefaultPosition("TButton");
        Assert.Contains("    Panel1: TPanel;\r\n    Button1: TButton;\r\n", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("StdCtrls, ExtCtrls;", document.Buffer.Text, StringComparison.Ordinal);

        // Undo removes the object, its field and the uses entry together; redo adds them again.
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(original, document.Buffer.Text);
        Assert.Null(designer.Model.FindByName("Panel1"));
        surface.Execute(FreexComp.Designer.Surface.Commands.Redo);
        Assert.Equal(text, document.Buffer.Text);
        Assert.NotNull(designer.Model.FindByName("Panel1"));

        // A name the form class uses for another member is not taken (DSG-TP-050).
        Replace(document.Buffer, "    procedure GreetButtonClick", "    procedure Edit1;\r\n    procedure GreetButtonClick");
        surface.PlaceAtDefaultPosition("TEdit");
        Assert.NotNull(designer.Model.FindByName("Edit2"));
        Assert.Contains("    Edit2: TEdit;\r\n", document.Buffer.Text, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-127")]
    [Trait("Req", "DSG-SUR-057")]
    [Trait("Req", "DSG-CS-102")]
    public Task PasteAddsFieldsAndUndoRemovesThem() => TestUiThread.RunAsync(() => PasteAddsFieldsAndUndoRemovesThemOnUiThread()); // I-209

    private async Task PasteAddsFieldsAndUndoRemovesThemOnUiThread()
    {
        var document = await OpenAsync();
        var designer = document.Designer;
        Assert.True(document.ShowView(DocumentView.Design));
        var surface = designer.Surface!;
        var original = document.Buffer.Text;

        // A panel with a child: copied and pasted, every pasted component gets its field (in pre-order), its unit in uses.
        surface.PlaceAtDefaultPosition("TPanel");
        surface.PlaceAtDefaultPosition("TButton"); // the panel is selected: the button goes into it
        Assert.Equal("Panel1", designer.Model!.FindByName("Button1")!.Parent!.Name);
        var placed = document.Buffer.Text;
        surface.Selection.Set(["Panel1"]);
        surface.Execute(FreexComp.Designer.Surface.Commands.Copy);
        surface.Selection.Set(["MainForm"]);
        surface.Execute(FreexComp.Designer.Surface.Commands.Paste);
        Assert.NotNull(designer.Model.FindByName("Panel2"));
        Assert.NotNull(designer.Model.FindByName("Button2"));
        Assert.Contains("    Button1: TButton;\r\n    Panel2: TPanel;\r\n    Button2: TButton;\r\n", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(document.Buffer.Text, "ExtCtrls"));
        Assert.Equal(3, designer.Undo.UndoCount);

        // Undo removes the pasted objects and their fields in one step; redo re-runs the field edits.
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(placed, document.Buffer.Text);
        Assert.Null(designer.Model.FindByName("Panel2"));
        surface.Execute(FreexComp.Designer.Surface.Commands.Redo);
        Assert.Contains("    Panel2: TPanel;\r\n    Button2: TButton;\r\n", document.Buffer.Text, StringComparison.Ordinal);
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);

        // Ctrl-drag copy of GreetButton (client 232, 15 → surface + (17, 47)): the copy gets its field in the "Copy" unit.
        surface.Selection.Set(["GreetButton"]);
        static FreexComp.Designer.Surface.PointerInput Pointer(FreexComp.Designer.Surface.PointerKind kind, double x, double y,
            FreexComp.Designer.Surface.InputModifiers modifiers = FreexComp.Designer.Surface.InputModifiers.None) =>
            new(kind, x, y, kind == FreexComp.Designer.Surface.PointerKind.Move ? FreexComp.Designer.Surface.PointerButton.None
                : FreexComp.Designer.Surface.PointerButton.Left, modifiers, 1);
        surface.OnPointer(Pointer(FreexComp.Designer.Surface.PointerKind.Down, 259, 72));
        surface.OnPointer(Pointer(FreexComp.Designer.Surface.PointerKind.Move, 259, 152, FreexComp.Designer.Surface.InputModifiers.Ctrl));
        surface.OnPointer(Pointer(FreexComp.Designer.Surface.PointerKind.Up, 259, 152, FreexComp.Designer.Surface.InputModifiers.Ctrl));
        Assert.NotNull(designer.Model.FindByName("GreetButton1"));
        Assert.Contains("    GreetButton1: TButton;\r\n", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal("Undo_Designer_Copy", designer.Undo.Peek!.TitleKey);
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(placed, document.Buffer.Text);
        Assert.Null(designer.Model.FindByName("GreetButton1"));

        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(original, document.Buffer.Text);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-SUR-131")]
    [Trait("Req", "DSG-SUR-113")]
    [Trait("Req", "DSG-CS-040")]
    [Trait("Req", "DSG-CS-101")]
    [Trait("Req", "DSG-CS-102")]
    public Task DeleteRemovesFieldsAndUndoRestoresThem() => TestUiThread.RunAsync(() => DeleteRemovesFieldsAndUndoRestoresThemOnUiThread()); // I-209

    private async Task DeleteRemovesFieldsAndUndoRestoresThemOnUiThread()
    {
        var document = await OpenAsync();
        var designer = document.Designer;
        Assert.True(document.ShowView(DocumentView.Design));
        var surface = designer.Surface!;
        var original = document.Buffer.Text;
        Assert.Contains("    GreetButton: TButton;\r\n", original, StringComparison.Ordinal);

        // Delete: the component and its field go in one "Delete" unit; the handler stays (DSG-CS-070).
        surface.Selection.Set(["GreetButton"]);
        surface.Execute(FreexComp.Designer.Surface.Commands.Delete);
        Assert.Null(designer.Model!.FindByName("GreetButton"));
        Assert.DoesNotContain("GreetButton: TButton;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Contains("procedure GreetButtonClick(Sender: TObject);", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(1, designer.Undo.UndoCount);
        Assert.Equal("Undo_Designer_Delete", designer.Undo.Peek!.TitleKey);

        // Undo restores the component and the field text exactly; redo re-runs the removal; undo again.
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(original, document.Buffer.Text);
        Assert.NotNull(designer.Model.FindByName("GreetButton"));
        surface.Execute(FreexComp.Designer.Surface.Commands.Redo);
        Assert.DoesNotContain("GreetButton: TButton;", document.Buffer.Text, StringComparison.Ordinal);
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(original, document.Buffer.Text);

        // Cut of a container removes the fields of its whole subtree in the "Cut" unit.
        surface.PlaceAtDefaultPosition("TPanel");
        surface.PlaceAtDefaultPosition("TButton"); // into the selected panel
        var placed = document.Buffer.Text;
        Assert.Contains("    Panel1: TPanel;\r\n    Button1: TButton;\r\n", placed, StringComparison.Ordinal);
        surface.Selection.Set(["Panel1"]);
        surface.Execute(FreexComp.Designer.Surface.Commands.Cut);
        Assert.Null(designer.Model.FindByName("Button1"));
        Assert.DoesNotContain("Panel1: TPanel;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Button1: TButton;", document.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal("Undo_Designer_Cut", designer.Undo.Peek!.TitleKey);
        surface.Execute(FreexComp.Designer.Surface.Commands.Undo);
        Assert.Equal(placed, document.Buffer.Text);
        Assert.Equal("Panel1", designer.Model.FindByName("Button1")!.Parent!.Name);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-080")]
    public Task ProjectFormEntryNamesTheCompanion() => TestUiThread.RunAsync(() => ProjectFormEntryNamesTheCompanionOnUiThread()); // I-209

    private async Task ProjectFormEntryNamesTheCompanionOnUiThread()
    {
        File(Dir + @"\App.pproj", """
            { "formatVersion": 1, "name": "App", "mainSource": "App.pas",
              "files": [ { "path": "App.pas" }, { "path": "MainForm.pas", "form": "forms/MainForm.form" } ] }
            """);
        File(Dir + @"\App.pas", "program App;\nbegin\nend.\n");
        File(UnitPath, TestUnits.Read("MainForm.pas"));
        File(Dir + @"\forms\MainForm.form", TestUnits.Read("MainForm.form"));
        Assert.True(await _services.Projects.OpenAsync(Dir + @"\App.pproj"));

        var document = Assert.IsType<FormDocumentViewModel>((await _services.Documents.OpenAsync(UnitPath)).Value);
        Assert.Equal(Dir + @"\forms\MainForm.form", document.FormText.FilePath);
        Assert.Equal("MainForm", Caption(document.Designer));
    }
}
