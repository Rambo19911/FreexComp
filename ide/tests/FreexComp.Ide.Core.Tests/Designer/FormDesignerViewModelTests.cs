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
    public async Task ViewSwitchingRules()
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
    public async Task DirtyTrackingAcrossBothBuffers()
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
    public async Task SaveRunsRepairThenRemovesHandlersThenWrites()
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
    public async Task UnmodifiedFormIsNotRewritten()
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
    public async Task OpeningTheFormFileOpensItsUnitInDesignView()
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

        // A form without its unit opens as text (no design view without code sync yet).
        File(Dir + @"\Lonely.form", "object Lonely: TForm\nend\n");
        Assert.IsNotType<FormDocumentViewModel>((await _services.Documents.OpenAsync(Dir + @"\Lonely.form")).Value);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "DSG-CS-080")]
    public async Task ProjectFormEntryNamesTheCompanion()
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
