using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Documents;

[Trait("Phase", "1")]
public sealed class DocumentServiceTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly IdeServices _services;

    public DocumentServiceTests()
    {
        _services = TestComposition.Build(o => o.FileSystem = _fs);
        // These tests check file contents byte for byte; the final-line-break rule is SaveTests' (IDE-P1-080).
        _services.Settings.Set(SettingsCatalog.FilesEnsureFinalNewline, false);
    }

    private IDocumentService Documents => _services.Documents;

    private string File(string path, string text) => File(path, Encoding.UTF8.GetBytes(text));

    private string File(string path, byte[] bytes)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, bytes);
        return path;
    }

    private async Task<TextDocumentViewModel> OpenAsync(string path)
    {
        var result = await Documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-020")]
    [Trait("Req", "IDE-ARCH-021")]
    [Trait("Req", "IDE-P1-032")]
    public async Task OpenSameFileActivatesExistingTab()
    {
        var a = File(@"C:\p\A.pas", "unit A;\r\n");
        File(@"C:\p\B.pas", "unit B;\r\n");
        var opened = new List<DocumentViewModel>();
        Documents.DocumentOpened += (_, e) => opened.Add(e.Document);

        var docA = await OpenAsync(a);
        var docB = await OpenAsync(@"C:\p\B.pas");
        Assert.Same(docB, Documents.ActiveDocument);

        // Same file through another spelling: case and a non-normalized path.
        var again = await OpenAsync(@"c:\P\sub\..\a.PAS");
        Assert.Same(docA, again);
        Assert.Same(docA, Documents.ActiveDocument);
        Assert.Equal([docA, docB], Documents.Documents);
        Assert.Equal([docA, docB], opened);
        Assert.Equal(@"C:\p\A.pas", docA.FilePath);

        // Two concurrent opens of a file not yet open give one document.
        File(@"C:\p\C.pas", "unit C;\r\n");
        var both = await Task.WhenAll(Documents.OpenAsync(@"C:\p\C.pas"), Documents.OpenAsync(@"C:\P\c.pas"));
        Assert.Same(both[0].Value, both[1].Value);
        Assert.Equal(3, Documents.Documents.Count);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-037")]
    [Trait("Req", "UI-ED-071")]
    public async Task DuplicateFileNamesAreDisambiguated()
    {
        var other = await OpenAsync(File(@"C:\p\Other.pas", ""));
        var lib = await OpenAsync(File(@"C:\p\lib\Unit1.pas", ""));
        Assert.Equal("Unit1.pas", lib.Title);
        Assert.Null(lib.TitleSuffix);

        var src = await OpenAsync(File(@"C:\p\src\Unit1.pas", ""));
        Assert.Equal("Unit1.pas", src.Title);
        Assert.Equal("lib", lib.TitleSuffix);
        Assert.Equal("src", src.TitleSuffix);

        // A third one shares the parent "lib": those two need a further folder, "src" stays unique on its own.
        var qlib = await OpenAsync(File(@"C:\q\lib\Unit1.pas", ""));
        Assert.Equal(@"p\lib", lib.TitleSuffix);
        Assert.Equal(@"q\lib", qlib.TitleSuffix);
        Assert.Equal("src", src.TitleSuffix);
        Assert.Null(other.TitleSuffix);

        // Names compare case-insensitively.
        var upper = await OpenAsync(File(@"C:\r\OTHER.pas", ""));
        Assert.Equal("p", other.TitleSuffix);
        Assert.Equal("r", upper.TitleSuffix);

        // Closing recomputes the suffixes; a name left alone has none.
        Assert.True(Documents.Close(qlib));
        Assert.Equal("lib", lib.TitleSuffix);
        Assert.True(Documents.Close(src));
        Assert.Null(lib.TitleSuffix);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-033")]
    [Trait("Req", "IDE-P1-034")]
    [Trait("Req", "IDE-P1-079")]
    public async Task UntitledUnitNamingAndSaveAs()
    {
        _services.Settings.Set(SettingsCatalog.FilesDefaultLineEnding, "lf");
        _services.Settings.Set(SettingsCatalog.FilesDefaultEncoding, "utf-8-bom");

        await _services.Commands.ExecuteAsync("file.newUnit"); // no project open: an untitled unit
        var unit1 = Assert.IsType<TextDocumentViewModel>(Documents.ActiveDocument);
        var unit2 = Documents.NewUntitledUnit();

        Assert.Equal("Unit1.pas", unit1.Title);
        Assert.Equal("Unit2.pas", unit2.Title);
        Assert.Null(unit1.FilePath);
        Assert.True(unit1.IsModified);
        Assert.Equal("pascal", unit1.Language.Id);
        Assert.Equal(FileEncoding.Utf8Bom, unit1.Encoding);
        Assert.Equal(LineEndingKind.Lf, unit1.LineEnding.Kind);
        Assert.Equal("unit Unit2;\n\ninterface\n\nimplementation\n\nend.\n", unit2.Buffer.Text);
        Assert.Same(unit2, Documents.ActiveDocument);

        // Undoing everything does not make an unsaved new document unmodified.
        Assert.False(unit1.Buffer.CanUndo);
        Assert.True(unit1.IsModified);

        // N = smallest positive integer not used by an open untitled document.
        Assert.True(Documents.Close(unit1));
        Assert.Equal("Unit1.pas", Documents.NewUntitledUnit().Title);

        // Save As: path, title and language change; the document is saved with its encoding.
        _fs.CreateDirectory(@"C:\p");
        var result = await Documents.SaveAsAsync(unit2, @"C:\p\Hello.pas");
        Assert.True(result.Success, result.MessageId);
        Assert.Equal(@"C:\p\Hello.pas", unit2.FilePath);
        Assert.Equal("Hello.pas", unit2.Title);
        Assert.False(unit2.IsModified);
        Assert.Equal(FileEncodings.Encode(unit2.Buffer.Text, FileEncoding.Utf8Bom), _fs.ReadAllBytes(@"C:\p\Hello.pas"));
        Assert.Equal("Unit2.pas", Documents.NewUntitledUnit().Title); // its untitled name is free again

        // The saved document is now found by its path.
        Assert.Same(unit2, (await Documents.OpenAsync(@"C:\P\hello.pas")).Value);

        // Save As to another extension switches the language.
        Assert.True((await Documents.SaveAsAsync(unit2, @"C:\p\Hello.json")).Success);
        Assert.Equal("json", unit2.Language.Id);
        Assert.True(_fs.FileExists(@"C:\p\Hello.pas")); // the old file is untouched

        // A path open in another document is refused.
        var other = await OpenAsync(File(@"C:\p\Other.pas", "x"));
        var refused = await Documents.SaveAsAsync(unit2, @"c:\p\other.PAS");
        Assert.False(refused.Success);
        Assert.Equal("Msg.File.SaveFailed", refused.MessageId);
        Assert.Equal([@"C:\p\Other.pas", Strings.Reason_OpenInAnotherTab], refused.Args);
        Assert.Equal(@"C:\p\Hello.json", unit2.FilePath);
        Assert.Equal("x", Encoding.UTF8.GetString(_fs.ReadAllBytes(other.FilePath!)));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-038")]
    [Trait("Req", "IDE-P1-035")]
    [Trait("Req", "UI-ED-077")]
    public async Task MruAndVisualOrderSwitching()
    {
        var a = await OpenAsync(File(@"C:\p\A.pas", ""));
        var b = await OpenAsync(File(@"C:\p\B.pas", ""));
        var c = await OpenAsync(File(@"C:\p\C.pas", ""));
        Assert.Equal([a, b, c], Documents.Documents);           // each opens right of the active tab

        Documents.Activate(a);
        var d = await OpenAsync(File(@"C:\p\D.pas", ""));
        Assert.Equal([a, d, b, c], Documents.Documents);
        Assert.Equal([d, a, c, b], Documents.MostRecentlyUsed);

        var changes = 0;
        Documents.ActiveDocumentChanged += (_, _) => changes++;
        var commands = _services.Commands;

        // Visual order (Ctrl+PageDown/PageUp; the default from the menu) wraps around the tabs.
        await commands.ExecuteAsync("window.nextDocument", "visual");
        Assert.Same(b, Documents.ActiveDocument);
        await commands.ExecuteAsync("window.nextDocument");
        Assert.Same(c, Documents.ActiveDocument);
        await commands.ExecuteAsync("window.nextDocument", "visual");
        Assert.Same(a, Documents.ActiveDocument);
        await commands.ExecuteAsync("window.previousDocument", "visual");
        Assert.Same(c, Documents.ActiveDocument);
        Assert.Equal([c, a, b, d], Documents.MostRecentlyUsed);

        // MRU order (Ctrl+Tab): next = the previously used document, previous = the least recently used one.
        await commands.ExecuteAsync("window.nextDocument", "mru");
        Assert.Same(a, Documents.ActiveDocument);
        await commands.ExecuteAsync("window.nextDocument", "mru");
        Assert.Same(c, Documents.ActiveDocument);
        await commands.ExecuteAsync("window.previousDocument", "mru");
        Assert.Same(d, Documents.ActiveDocument);
        Assert.Equal(7, changes);

        // window.activateDocument takes the document id.
        await commands.ExecuteAsync("window.activateDocument", b.Id.ToString());
        Assert.Same(b, Documents.ActiveDocument);
        Assert.False(commands.CanExecute("window.activateDocument", Guid.NewGuid().ToString()));

        // Closing the active document activates the most recently used remaining one.
        Assert.Equal([b, d, c, a], Documents.MostRecentlyUsed);
        Assert.True(Documents.Close(b));
        Assert.Same(d, Documents.ActiveDocument);
        Assert.True(Documents.Close(a)); // not active: the active document stays
        Assert.Same(d, Documents.ActiveDocument);
        Assert.Equal([d, c], Documents.Documents);

        // With one document there is nothing to switch to.
        Assert.True(Documents.Close(c));
        Assert.False(commands.CanExecute("window.nextDocument", "mru"));
        Assert.False(commands.CanExecute("window.previousDocument"));
        Assert.True(Documents.Close(d));
        Assert.Null(Documents.ActiveDocument);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-021")]
    public async Task ClosingCanBeCanceled()
    {
        var a = await OpenAsync(File(@"C:\p\A.pas", ""));
        Documents.DocumentClosing += (_, e) => e.Cancel = true;

        Assert.False(Documents.Close(a));
        Assert.Equal([a], Documents.Documents);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-071")]
    [Trait("Req", "IDE-P1-072")]
    [Trait("Req", "IDE-ARCH-092")]
    public async Task OpenDetectsEncodingAndReportsFailures()
    {
        var latin = await OpenAsync(File(@"C:\p\Latin.pas", [0x41, 0xE9, 0x0D, 0x0A]));
        Assert.Equal(FileEncoding.Windows1252, latin.Encoding);
        Assert.Equal("A\u00E9\r\n", latin.Buffer.Text);
        Assert.False(latin.IsModified);
        var bar = Assert.Single(latin.InfoBars);
        Assert.Equal("Encoding", bar.Kind);
        Assert.Equal(string.Format(Strings.Msg_File_EncodingFallback, Strings.Encoding_windows_1252), bar.Text);

        // Convert to UTF-8 changes only the encoding of the next save: the document becomes modified (IDE-P1-074).
        await bar.Actions.Single(x => x.Name == "ConvertToUtf8").ExecuteAsync();
        Assert.Equal(FileEncoding.Utf8, latin.Encoding);
        Assert.True(latin.IsModified);
        Assert.Empty(latin.InfoBars);

        var bom = await OpenAsync(File(@"C:\p\Data.json", [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']));
        Assert.Equal(FileEncoding.Utf8Bom, bom.Encoding);
        Assert.Equal("{}", bom.Buffer.Text);
        Assert.Equal("json", bom.Language.Id);
        Assert.Empty(bom.InfoBars);

        var binary = await Documents.OpenAsync(File(@"C:\p\Tool.exe", [0x4D, 0x5A, 0x00, 0x01]));
        Assert.False(binary.Success);
        Assert.Equal("Msg.File.Binary", binary.MessageId);
        Assert.Equal([@"C:\p\Tool.exe"], binary.Args);

        var missing = await Documents.OpenAsync(@"C:\p\Missing.pas");
        Assert.Equal("Msg.File.NotFound", missing.MessageId);
        Assert.Equal(2, Documents.Documents.Count);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-075")]
    [Trait("Req", "IDE-P1-077")]
    [Trait("Req", "IDE-ARCH-022")]
    public async Task LineEndingStateDrivesInfoBarAndModifiedFlag()
    {
        var mixed = await OpenAsync(File(@"C:\p\Mixed.pas", "a\r\nb\nc\r\n"));
        Assert.Equal(LineEndingKind.Mixed, mixed.LineEnding.Kind);
        var bar = Assert.Single(mixed.InfoBars);
        Assert.Equal("MixedLineEndings", bar.Kind);
        Assert.Equal(string.Format(Strings.Msg_File_MixedLineEndings, 2, 1), bar.Text);
        Assert.Equal(["ConvertToCrlf", "ConvertToLf", "LeaveAsIs"], bar.Actions.Select(x => x.Name));

        await bar.Actions.Single(x => x.Name == "ConvertToLf").ExecuteAsync();
        Assert.Equal("a\nb\nc\n", mixed.Buffer.Text);
        Assert.True(mixed.IsModified);
        Assert.Empty(mixed.InfoBars);                       // removed when the kind is no longer Mixed
        mixed.Buffer.Undo();
        Assert.False(mixed.IsModified);

        // Leave As Is only dismisses the bar.
        var second = await OpenAsync(File(@"C:\p\Mixed2.pas", "a\r\nb\n"));
        await second.InfoBars.Single().Actions.Single(x => x.Name == "LeaveAsIs").ExecuteAsync();
        Assert.Empty(second.InfoBars);
        Assert.Equal(LineEndingKind.Mixed, second.LineEnding.Kind);
        Assert.False(second.IsModified);

        // A kind-only conversion (no line break in the text) changes no text but marks the document modified.
        var single = await OpenAsync(File(@"C:\p\One.pas", "x"));
        Assert.Equal(LineEndingKind.CrLf, single.LineEnding.Kind);    // files.defaultLineEnding
        Assert.True(await single.CommandTarget!.ExecuteAsync("edit.convertLineEndings", "lf"));
        Assert.Equal("x", single.Buffer.Text);
        Assert.True(single.Buffer.IsAtSavePoint);
        Assert.True(single.IsModified);
        Assert.True(await single.CommandTarget.ExecuteAsync("edit.convertLineEndings", "crlf"));
        Assert.False(single.IsModified);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-022")]
    [Trait("Req", "IDE-P1-094")]
    public async Task DirtyFlagFollowsSavePoint()
    {
        var doc = await OpenAsync(File(@"C:\p\A.pas", "unit A;"));
        var changes = 0;
        doc.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(DocumentViewModel.IsModified) ? 1 : 0;
        Assert.False(doc.IsModified);

        doc.Buffer.ApplyEdits([new TextEdit(7, 0, "\r\n")]);
        Assert.True(doc.IsModified);
        Assert.Equal(1, changes);
        doc.Buffer.Undo();                                  // back at the save point (the load)
        Assert.False(doc.IsModified);
        doc.Buffer.Redo();
        Assert.True(doc.IsModified);

        // Saving moves the save point; it does not clear the undo stack (IDE-P1-094).
        Assert.True(await Documents.SaveAsync(doc));
        Assert.False(doc.IsModified);
        Assert.Equal("unit A;\r\n", Encoding.UTF8.GetString(_fs.ReadAllBytes(doc.FilePath!)));
        Assert.True(doc.Buffer.CanUndo);
        doc.Buffer.Undo();                                  // below the save point
        Assert.True(doc.IsModified);
        doc.Buffer.Redo();
        Assert.False(doc.IsModified);

        // Undo below the save point followed by a new edit: the save point is lost until the next save.
        doc.Buffer.Undo();
        doc.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        doc.Buffer.Undo();
        Assert.Equal("unit A;", doc.Buffer.Text);
        Assert.True(doc.IsModified);
        Assert.True(await Documents.SaveAsync(doc));
        Assert.False(doc.IsModified);

        // The encoding of the next save is part of the state; changing it back restores "unmodified".
        doc.SetEncoding(FileEncoding.Utf16Le);
        Assert.True(doc.IsModified);
        doc.SetEncoding(FileEncoding.Utf8);
        Assert.False(doc.IsModified);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-035")]
    [Trait("Req", "UI-DLG-060")]
    public async Task CloseModifiedPromptsSaveDontSaveCancel()
    {
        var dialogs = (FakeDialogService)_services.Dialogs;
        var commands = _services.Commands;
        Assert.False(commands.CanExecute("file.close"));    // nothing open

        // Cancel: the document stays open and modified.
        var a = await OpenAsync(File(@"C:\p\A.pas", "a"));
        a.Buffer.ApplyEdits([new TextEdit(1, 0, "1")]);
        dialogs.Answer("Msg.File.SaveChangesOne", "Cancel");
        await commands.ExecuteAsync("file.close");
        var request = Assert.Single(dialogs.Requests);
        Assert.Equal("Msg.File.SaveChangesOne", request.MessageId);
        Assert.Equal(["A.pas"], request.Args);
        Assert.Equal([a], Documents.Documents);
        Assert.True(a.IsModified);

        // Save: written to disk, then closed.
        dialogs.Answer("Msg.File.SaveChangesOne", "Save");
        await commands.ExecuteAsync("file.close");
        Assert.Empty(Documents.Documents);
        Assert.Equal("a1", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\p\A.pas")));

        // Don't Save: closed, the file is untouched.
        var b = await OpenAsync(File(@"C:\p\B.pas", "b"));
        b.Buffer.ApplyEdits([new TextEdit(1, 0, "2")]);
        dialogs.Answer("Msg.File.SaveChangesOne", "DontSave");
        Assert.True(await Documents.CloseAsync(b));
        Assert.Empty(Documents.Documents);
        Assert.Equal("b", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\p\B.pas")));

        // An unmodified document closes without a prompt.
        var count = dialogs.Requests.Count;
        await OpenAsync(File(@"C:\p\C.pas", "c"));
        await commands.ExecuteAsync("file.close");
        Assert.Empty(Documents.Documents);
        Assert.Equal(count, dialogs.Requests.Count);

        // Save that fails: the failure is reported and the document stays open and modified.
        var d = await OpenAsync(File(@"C:\p\D.pas", "d"));
        d.Buffer.ApplyEdits([new TextEdit(1, 0, "3")]);
        _fs.SetReadOnly(@"C:\p\D.pas", true);
        dialogs.Answer("Msg.File.SaveChangesOne", "Save");
        dialogs.Answer("Msg.File.SaveFailed", "Cancel");
        Assert.False(await Documents.CloseAsync(d));
        Assert.Equal([d], Documents.Documents);
        Assert.True(d.IsModified);
        var failed = dialogs.Requests[^1];
        Assert.Equal("Msg.File.SaveFailed", failed.MessageId);
        Assert.Equal(@"C:\p\D.pas", failed.Args[0]);
        Assert.Equal("d", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\p\D.pas")));

        // Once the file is writable again the same close saves and closes.
        _fs.SetReadOnly(@"C:\p\D.pas", false);
        Assert.True(await Documents.CloseAsync(d));
        Assert.Equal("d3", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\p\D.pas")));

        // An untitled document's Save needs the Save As picker; a canceled picker keeps it open.
        var unit = Documents.NewUntitledUnit();
        ((FakeFileDialogService)_services.FileDialogs).NextCancel();
        Assert.False(await Documents.CloseAsync(unit));
        Assert.Equal([unit], Documents.Documents);
        Assert.Equal(["Unit1.pas"], dialogs.Requests[^1].Args);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-036")]
    [Trait("Req", "UI-DLG-061")]
    [Trait("Req", "UI-DLG-062")]
    [Trait("Req", "UI-DLG-063")]
    public async Task CloseAllAsksOnceWithList()
    {
        var dialogs = (FakeDialogService)_services.Dialogs;
        var commands = _services.Commands;
        Assert.False(commands.CanExecute("file.closeAll"));
        Assert.False(commands.CanExecute("file.closeOthers"));

        var a = await OpenAsync(File(@"C:\p\A.pas", "a"));
        var b = await OpenAsync(File(@"C:\p\B.pas", "b"));
        var c = await OpenAsync(File(@"C:\p\C.pas", "c"));
        a.Buffer.ApplyEdits([new TextEdit(1, 0, "1")]);
        c.Buffer.ApplyEdits([new TextEdit(1, 0, "3")]);

        // Cancel: one dialog listing the modified documents in tab order; nothing is closed or saved.
        UnsavedChangesViewModel? shown = null;
        dialogs.Script(UnsavedChangesViewModel.DialogName, vm =>
        {
            shown = (UnsavedChangesViewModel)vm;
            return vm.PressCancelAsync();
        });
        await commands.ExecuteAsync("file.closeAll");
        Assert.Single(dialogs.Dialogs);
        Assert.Empty(dialogs.Requests);
        Assert.Equal(string.Format(Strings.Msg_File_SaveChangesMany, 2), shown!.Heading);
        Assert.Equal(Strings.Msg_File_SaveChangesMany_Title, shown.Title);
        Assert.Equal([a, c], shown.Files.Select(f => f.Document));
        Assert.Equal([("A.pas", @"C:\p"), ("C.pas", @"C:\p")], shown.Files.Select(f => (f.FileName, f.Folder!)));
        Assert.All(shown.Files, f => Assert.True(f.IsChecked));
        Assert.Equal([a, b, c], Documents.Documents);

        // Save Selected with C unchecked: A is saved, C is discarded, everything is closed.
        dialogs.Script(UnsavedChangesViewModel.DialogName, vm =>
        {
            var u = (UnsavedChangesViewModel)vm;
            u.Files[1].IsChecked = false;
            return u.DefaultButton!.PressAsync();
        });
        await commands.ExecuteAsync("file.closeAll");
        Assert.Empty(Documents.Documents);
        Assert.Equal("a1", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\p\A.pas")));
        Assert.Equal("c", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\p\C.pas")));
        Assert.Equal(2, dialogs.Dialogs.Count);

        // Close Others with one modified other document asks the single-file question.
        a = await OpenAsync(@"C:\p\A.pas");
        b = await OpenAsync(@"C:\p\B.pas");
        c = await OpenAsync(@"C:\p\C.pas");
        a.Buffer.ApplyEdits([new TextEdit(2, 0, "!")]);
        Documents.Activate(b);
        dialogs.Answer("Msg.File.SaveChangesOne", "DontSave");
        await commands.ExecuteAsync("file.closeOthers");
        Assert.Equal([b], Documents.Documents);
        Assert.Equal(["A.pas"], Assert.Single(dialogs.Requests).Args);
        Assert.Equal("a1", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\p\A.pas")));
        Assert.False(commands.CanExecute("file.closeOthers")); // nothing else open

        // Don't Save discards every listed document.
        a = await OpenAsync(@"C:\p\A.pas");
        a.Buffer.ApplyEdits([new TextEdit(2, 0, "!")]);
        b.Buffer.ApplyEdits([new TextEdit(1, 0, "2")]);
        dialogs.Script(UnsavedChangesViewModel.DialogName, vm => vm.Buttons.Single(x => x.Id == "DontSave").PressAsync());
        await commands.ExecuteAsync("file.closeAll");
        Assert.Empty(Documents.Documents);
        Assert.Equal("b", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\p\B.pas")));

        // A failed save stops the operation: nothing is closed; what was saved stays open and unmodified.
        a = await OpenAsync(@"C:\p\A.pas");
        b = await OpenAsync(@"C:\p\B.pas");
        a.Buffer.ApplyEdits([new TextEdit(2, 0, "!")]);
        b.Buffer.ApplyEdits([new TextEdit(1, 0, "2")]);
        _fs.SetReadOnly(@"C:\p\B.pas", true);
        dialogs.Answer("Msg.File.SaveFailed", "Cancel");
        dialogs.Script(UnsavedChangesViewModel.DialogName, vm => vm.PressDefaultAsync());
        await commands.ExecuteAsync("file.closeAll");
        Assert.Equal([a, b], Documents.Documents);
        Assert.False(a.IsModified);
        Assert.True(b.IsModified);
        Assert.Equal("Msg.File.SaveFailed", dialogs.Requests[^1].MessageId);

        // An untitled document whose Save As is canceled cancels the whole operation.
        _fs.SetReadOnly(@"C:\p\B.pas", false);
        ((FakeFileDialogService)_services.FileDialogs).NextCancel();
        var unit = Documents.NewUntitledUnit();
        dialogs.Script(UnsavedChangesViewModel.DialogName, vm =>
        {
            var u = (UnsavedChangesViewModel)vm;
            Assert.Equal([("B.pas", (string?)@"C:\p"), ("Unit1.pas", null)], u.Files.Select(f => (f.FileName, f.Folder)));
            return u.PressDefaultAsync();
        });
        await commands.ExecuteAsync("file.closeAll");
        Assert.Equal([a, b, unit], Documents.Documents);
        Assert.False(b.IsModified);                         // saved before the untitled one
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-061")]
    public async Task UnsavedChangesSelectionRules()
    {
        var a = await OpenAsync(File(@"C:\p\A.pas", "a"));
        var b = Documents.NewUntitledUnit();
        var vm = new UnsavedChangesViewModel([a, b]);
        Assert.Equal(["Ok", "DontSave", "Cancel"], vm.Buttons.Select(x => x.Id));
        Assert.Equal(("_Save Selected", "Do_n't Save"), (vm.Buttons[0].AccessLabel, vm.Buttons[1].AccessLabel));
        Assert.Same(vm.Buttons[0], vm.DefaultButton);
        Assert.Same(vm.Buttons[2], vm.CancelButton);
        Assert.True(vm.CanResize);
        Assert.Equal((520.0, (double?)320.0), (vm.Width, vm.Height));

        vm.Files[0].IsChecked = false;
        vm.Files[1].IsChecked = false;
        Assert.False(vm.DefaultButton!.IsEnabled);          // nothing checked
        vm.CheckAll();
        Assert.True(vm.DefaultButton.IsEnabled);
        vm.Files[0].IsChecked = false;
        await vm.PressDefaultAsync();
        Assert.Equal([b], vm.Result);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-034")]
    [Trait("Req", "IDE-PM-080")]
    [Trait("Req", "UI-DLG-050")]
    public async Task SaveAsSwitchesDocumentPath()
    {
        var commands = _services.Commands;
        var pickers = (FakeFileDialogService)_services.FileDialogs;
        var dialogs = (FakeDialogService)_services.Dialogs;
        Assert.False(commands.CanExecute("file.saveAs"));   // no document

        var doc = await OpenAsync(File(@"C:\p\A.pas", "unit A;"));
        _fs.CreateDirectory(@"C:\q");
        Assert.True(commands.CanExecute("file.saveAs"));    // also when unmodified

        // A canceled picker changes nothing.
        pickers.NextCancel();
        await commands.ExecuteAsync("file.saveAs");
        var request = Assert.Single(pickers.Requests);
        Assert.Equal(("saveAs", "A.pas", @"C:\p"), (request.PromptId, request.SuggestedName, request.InitialFolder));
        Assert.Equal(@"C:\p\A.pas", doc.FilePath);

        // Save As writes the new file and switches the document to it; the old file is untouched.
        doc.Buffer.ApplyEdits([new TextEdit(7, 0, " { b }")]);
        var saved = new List<DocumentViewModel>();
        Documents.DocumentSaved += (_, e) => saved.Add(e.Document);
        pickers.NextSave(@"C:\q\B.pas");
        await commands.ExecuteAsync("file.saveAs");
        Assert.Equal(@"C:\q\B.pas", doc.FilePath);
        Assert.Equal("B.pas", doc.Title);
        Assert.False(doc.IsModified);
        Assert.Equal([doc], saved);
        Assert.Equal("unit A; { b }", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\q\B.pas")));
        Assert.Equal("unit A;", Encoding.UTF8.GetString(_fs.ReadAllBytes(@"C:\p\A.pas")));

        // The old path is free again: opening it gives a new document.
        var old = await OpenAsync(@"C:\p\A.pas");
        Assert.NotSame(doc, old);

        // A path open in another document is refused (SaveFailed with the reason); Cancel keeps the path.
        Documents.Activate(doc);
        pickers.NextSave(@"c:\P\a.pas");
        dialogs.Answer("Msg.File.SaveFailed", "Cancel");
        await commands.ExecuteAsync("file.saveAs");
        Assert.Equal([@"C:\p\A.pas", Strings.Reason_OpenInAnotherTab], dialogs.Requests[^1].Args);
        Assert.Equal(@"C:\q\B.pas", doc.FilePath);

        // Another extension switches the language; an untitled document's Save As names it.
        pickers.NextSave(@"C:\q\B.json");
        await commands.ExecuteAsync("file.saveAs");
        Assert.Equal("json", doc.Language.Id);
        var unit = Documents.NewUntitledUnit();
        pickers.NextSave(@"C:\q\Unit9.pas");
        await commands.ExecuteAsync("file.saveAs");
        Assert.Equal((@"C:\q\Unit9.pas", "Unit9.pas", false), (unit.FilePath, unit.Title, unit.IsModified));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-039")]
    [Trait("Req", "IDE-P1-090")]
    public async Task RevertReloadsAndClearsUndo()
    {
        var commands = _services.Commands;
        var dialogs = (FakeDialogService)_services.Dialogs;
        Assert.False(commands.CanExecute("file.revert"));   // no document

        var doc = await OpenAsync(File(@"C:\p\A.pas", "line1\r\nline2\r\nline3"));
        Assert.False(commands.CanExecute("file.revert"));   // unmodified
        doc.Buffer.ApplyEdits([new TextEdit(19, 0, " changed")]);
        doc.ViewState.Caret = new TextPosition(3, 14);
        doc.ViewState.SelectionAnchor = new TextPosition(3, 1);
        Assert.True(commands.CanExecute("file.revert"));

        // Cancel keeps the edits.
        dialogs.Answer("Msg.File.RevertConfirm", "Cancel");
        await commands.ExecuteAsync("file.revert");
        Assert.Equal(["A.pas"], Assert.Single(dialogs.Requests).Args);
        Assert.True(doc.IsModified);
        Assert.Equal("line1\r\nline2\r\nline3 changed", doc.Buffer.Text);

        // Revert reloads what is on disk now, clears the undo history and keeps the caret (clamped).
        File(@"C:\p\A.pas", "one\r\ntwo");
        dialogs.Answer("Msg.File.RevertConfirm", "Revert");
        await commands.ExecuteAsync("file.revert");
        Assert.Equal("one\r\ntwo", doc.Buffer.Text);
        Assert.False(doc.IsModified);
        Assert.False(doc.Buffer.CanUndo);
        Assert.False(doc.Buffer.CanRedo);
        Assert.Equal(new TextPosition(2, 4), doc.ViewState.Caret);
        Assert.Null(doc.ViewState.SelectionAnchor);
        Assert.False(commands.CanExecute("file.revert"));

        // The document's current encoding is used for the reload.
        File(@"C:\p\A.pas", [0x41, 0xC3, 0xA9]);            // "Aé" in UTF-8
        doc.SetEncoding(FileEncoding.Windows1252);
        await commands.ExecuteAsync("file.revert");
        Assert.Equal("A\u00C3\u00A9", doc.Buffer.Text);
        Assert.Equal(FileEncoding.Windows1252, doc.Encoding);
        Assert.False(doc.IsModified);

        // Not possible when the file is gone or the document is untitled.
        doc.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        _fs.DeleteFile(@"C:\p\A.pas");
        Assert.False(commands.CanExecute("file.revert"));
        Documents.NewUntitledUnit();
        Assert.False(commands.CanExecute("file.revert"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-040")]
    [Trait("Req", "UI-ED-093")]
    public async Task ReadOnlyFileShowsInfoBarAndBlocksEdits()
    {
        var path = File(@"C:\p\A.pas", "unit A;");
        _fs.SetReadOnly(path, true);
        var doc = await OpenAsync(path);
        Assert.True(doc.IsReadOnly);
        Assert.True(doc.Buffer.IsReadOnly);
        var bar = Assert.Single(doc.InfoBars);
        Assert.Equal(("ReadOnly", InfoBarSeverity.Info, Strings.Msg_File_ReadOnly, true), (bar.Kind, bar.Severity, bar.Text, bar.CanDismiss));
        Assert.Equal(["MakeWritable"], bar.Actions.Select(a => a.Name));
        Assert.Equal(Strings.Button_MakeWritable, bar.Actions[0].Label);

        // Typing is refused with a status message; the text is unchanged.
        Assert.False(doc.TryEdit([new TextEdit(0, 0, "x")]));
        Assert.Equal("unit A;", doc.Buffer.Text);
        Assert.Equal(Strings.Msg_Editor_ReadOnlyEdit, _services.Status.Message);
        Assert.False(doc.IsModified);

        // Dismissing the bar keeps the document read-only.
        bar.Dismiss();
        Assert.Empty(doc.InfoBars);
        Assert.True(doc.IsReadOnly);

        // Make Writable clears the attribute and the buffer becomes writable.
        Assert.True(Documents.Close(doc));
        doc = await OpenAsync(path);
        await doc.InfoBars.Single().Actions.Single().ExecuteAsync();
        Assert.False(_fs.IsReadOnly(path));
        Assert.False(doc.IsReadOnly);
        Assert.Empty(doc.InfoBars);
        Assert.True(doc.TryEdit([new TextEdit(7, 0, "x")]));
        Assert.Equal("unit A;x", doc.Buffer.Text);

        // A writable file opens without the bar.
        var writable = await OpenAsync(File(@"C:\p\B.pas", "b"));
        Assert.False(writable.IsReadOnly);
        Assert.Empty(writable.InfoBars);

        // Save As of a read-only document to a writable file makes it writable.
        var c = File(@"C:\p\C.pas", "c");
        _fs.SetReadOnly(c, true);
        var readOnly = await OpenAsync(c);
        ((FakeFileDialogService)_services.FileDialogs).NextSave(@"C:\p\D.pas");
        await _services.Commands.ExecuteAsync("file.saveAs");
        Assert.Equal(@"C:\p\D.pas", readOnly.FilePath);
        Assert.False(readOnly.IsReadOnly);
        Assert.Empty(readOnly.InfoBars);

        // Failing to clear the attribute reports Msg.File.MakeWritableFailed and leaves the document read-only.
        var failing = new RecordingFileSystem(_fs) { FailWith = call => call.StartsWith("SetReadOnly", StringComparison.Ordinal) ? new UnauthorizedAccessException("denied") : null };
        var services = TestComposition.Build(o => o.FileSystem = failing);
        var dialogs = (FakeDialogService)services.Dialogs;
        dialogs.Answer("Msg.File.MakeWritableFailed", "OK");
        var locked = Assert.IsType<TextDocumentViewModel>((await services.Documents.OpenAsync(c)).Value);
        await locked.InfoBars.Single().Actions.Single().ExecuteAsync();
        Assert.Equal([c, "denied"], Assert.Single(dialogs.Requests).Args);
        Assert.True(locked.IsReadOnly);
        Assert.Single(locked.InfoBars);
        Assert.True(_fs.IsReadOnly(c));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-WIN-040")]
    [Trait("Req", "IDE-ED-012")]
    public async Task ActiveTextDocumentFeedsStatusBar()
    {
        var status = _services.Status;
        Assert.Null(status.Editor);

        var pas = await OpenAsync(File(@"C:\p\A.pas", "unit A;\r\n"));
        Assert.Equal(new EditorStatus(1, 1, 0, 0, false, true, 2, LineEndingKind.CrLf, "utf-8", "Object Pascal"), status.Editor);

        var txt = await OpenAsync(File(@"C:\p\notes.txt", "a\nb"));
        Assert.Equal(new EditorStatus(1, 1, 0, 0, false, true, 2, LineEndingKind.Lf, "utf-8", "Plain Text"), status.Editor);

        // Changes of the active document are followed; those of other documents are not shown.
        txt.ViewState.SelectionAnchor = new TextPosition(1, 1);
        txt.ViewState.Caret = new TextPosition(2, 2);
        await txt.CommandTarget!.ExecuteAsync("edit.convertLineEndings", "crlf");
        Assert.Equal(new EditorStatus(2, 2, 4, 2, false, true, 2, LineEndingKind.CrLf, "utf-8", "Plain Text"), status.Editor);
        pas.ViewState.Caret = new TextPosition(1, 5);
        Assert.Equal(2, status.Editor!.Line);

        _services.Settings.Set(SettingsCatalog.EditorTabWidth, 4);
        _services.Settings.Set(SettingsCatalog.EditorInsertSpaces, false);
        Assert.Equal((false, 4), (status.Editor!.InsertSpaces, status.Editor.IndentSize));

        Documents.Activate(pas);
        Assert.Equal((1, 5, "Object Pascal"), (status.Editor!.Line, status.Editor.Column, status.Editor.Language));

        Documents.Close(pas);
        Documents.Close(txt);
        Assert.Null(status.Editor);
    }
}
