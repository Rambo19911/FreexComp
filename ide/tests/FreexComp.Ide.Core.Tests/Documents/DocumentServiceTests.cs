using System.Text;
using FreexComp.Ide.Core.Composition;
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
    [Trait("Req", "UI-WIN-040")]
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
