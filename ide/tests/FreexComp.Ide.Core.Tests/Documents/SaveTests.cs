using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Documents;

/// <summary>The save pipeline (phase1-shell.md §8, IDE-P1-080…085).</summary>
[Trait("Phase", "1")]
public sealed class SaveTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly IdeServices _services;

    public SaveTests()
    {
        _services = TestComposition.Build(o => o.FileSystem = _fs);
    }

    private IDocumentService Documents => _services.Documents;

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private FakeFileDialogService Pickers => (FakeFileDialogService)_services.FileDialogs;

    private string File(string path, string text) => File(path, Encoding.UTF8.GetBytes(text));

    private string File(string path, byte[] bytes)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, bytes);
        return path;
    }

    private string Disk(string path) => Encoding.UTF8.GetString(_fs.ReadAllBytes(path));

    private static async Task<TextDocumentViewModel> OpenAsync(IDocumentService documents, string path)
    {
        var result = await documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    private Task<TextDocumentViewModel> OpenAsync(string path) => OpenAsync(Documents, path);

    private static void Append(TextDocumentViewModel document, string text) =>
        document.Buffer.ApplyEdits([new TextEdit(document.Buffer.Length, 0, text)]);

    [Theory(Timeout = 30000)]
    [InlineData("memory")]
    [InlineData("disk")]
    [Trait("Category", "Integration")]
    [Trait("Req", "IDE-P1-081")]
    public async Task AtomicWriteLeavesNoTempFile(string kind)
    {
        using var temp = new TempDirectory();
        IFileSystem inner = kind == "disk" ? new PhysicalFileSystem() : new InMemoryFileSystem();
        var fs = new RecordingFileSystem(inner);
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.UserDir = temp.Combine("user");
        });
        var documents = services.Documents;
        var dialogs = (FakeDialogService)services.Dialogs;
        var dir = kind == "disk" ? temp.Combine("p") : @"C:\p";
        var path = Path.Combine(dir, "A.pas");
        inner.CreateDirectory(dir);
        inner.WriteAllBytes(path, Encoding.UTF8.GetBytes("a\r\n"));
        var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        if (kind == "disk")
        {
            System.IO.File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.Archive); // every write sets Archive
            System.IO.File.SetCreationTimeUtc(path, created);
        }

        string[] TempFiles() => [.. inner.EnumerateFiles(dir, "*.~tmp*")];

        void AssertAttributesKept()
        {
            if (kind != "disk")
                return;
            Assert.Equal(FileAttributes.Hidden | FileAttributes.Archive, System.IO.File.GetAttributes(path));
            Assert.Equal(created, System.IO.File.GetCreationTimeUtc(path));
        }

        // An existing file: write <name>.~tmp<6 hex> in the same folder, then replace the target.
        var doc = await OpenAsync(documents, path);
        Append(doc, "b\r\n");
        Assert.True(await documents.SaveAsync(doc));
        Assert.Equal("a\r\nb\r\n", Encoding.UTF8.GetString(inner.ReadAllBytes(path)));
        Assert.Empty(TempFiles());
        // Calls in the document's folder only: opening the file also writes the user's recent.json (I-074).
        var documentCalls = fs.Calls.Where(c => c.Contains(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList();
        var write = Assert.Single(documentCalls, c => c.StartsWith("WriteAllBytesDurable ", StringComparison.Ordinal));
        var tempPath = write["WriteAllBytesDurable ".Length..];
        Assert.DoesNotContain(fs.Calls, c => c.StartsWith("WriteAllBytes ", StringComparison.Ordinal)); // flushed to disk before the replace
        Assert.Equal(dir, Path.GetDirectoryName(tempPath));
        Assert.Matches(@"^A\.pas\.~tmp[0-9a-f]{6}$", Path.GetFileName(tempPath));
        Assert.Contains($"ReplaceFile {tempPath} -> {path}", fs.Calls);
        AssertAttributesKept();

        // A failing replace: the temp file is deleted, the target is untouched.
        fs.FailWhen = c => c.StartsWith("ReplaceFile ", StringComparison.Ordinal);
        Append(doc, "c\r\n");
        dialogs.Answer("Msg.File.SaveFailed", "Cancel");
        Assert.False(await documents.SaveAsync(doc));
        Assert.Equal("a\r\nb\r\n", Encoding.UTF8.GetString(inner.ReadAllBytes(path)));
        Assert.Empty(TempFiles());
        Assert.True(doc.IsModified);

        // Folder not writable but the file is (access denied): overwrite in place.
        fs.FailWhen = null;
        fs.FailWith = c => c.StartsWith("WriteAllBytesDurable ", StringComparison.Ordinal) && c.Contains(".~tmp", StringComparison.Ordinal)
            ? new UnauthorizedAccessException("Access denied.")
            : null;
        Assert.True(await documents.SaveAsync(doc));
        Assert.Equal("a\r\nb\r\nc\r\n", Encoding.UTF8.GetString(inner.ReadAllBytes(path)));
        Assert.Contains($"OverwriteFile {path}", fs.Calls);
        Assert.Empty(TempFiles());
        AssertAttributesKept();

        // A read-only file fails in place too; nothing is left behind.
        fs.FailWith = null;
        if (kind == "disk")
            System.IO.File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.ReadOnly);
        else
            ((InMemoryFileSystem)inner).SetReadOnly(path, true);
        Append(doc, "d");
        Assert.False(await documents.SaveAsync(doc));
        Assert.Equal("a\r\nb\r\nc\r\n", Encoding.UTF8.GetString(inner.ReadAllBytes(path)));
        Assert.Empty(TempFiles());

        // A new file: the temp file is moved to the target name.
        var unit = Path.Combine(dir, "New.pas");
        Assert.True((await documents.SaveAsAsync(doc, unit)).Success);
        Assert.Contains(fs.Calls, c => c.StartsWith("MoveFile ", StringComparison.Ordinal) && c.EndsWith(" -> " + unit, StringComparison.Ordinal));
        Assert.Equal("a\r\nb\r\nc\r\nd\r\n", Encoding.UTF8.GetString(inner.ReadAllBytes(unit)));
        Assert.Empty(TempFiles());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-084")]
    public async Task PreservesEncodingAndBom()
    {
        (FileEncoding Encoding, byte[] Bom)[] cases =
        [
            (FileEncoding.Utf8, []),
            (FileEncoding.Utf8Bom, [0xEF, 0xBB, 0xBF]),
            (FileEncoding.Utf16Le, [0xFF, 0xFE]),
            (FileEncoding.Utf16Be, [0xFE, 0xFF]),
            (FileEncoding.Windows1252, []),
        ];
        foreach (var (encoding, bom) in cases)
        {
            var text = encoding == FileEncoding.Windows1252 ? "x := 'Grüße';\r\n" : "x := 'Grüße – ņ';\r\n";
            var path = File($@"C:\p\{FileEncodings.ToId(encoding)}.pas", FileEncodings.Encode(text, encoding));
            var doc = await OpenAsync(path);
            Assert.Equal(encoding, doc.Encoding);

            Append(doc, "y;\r\n");
            Assert.True(await Documents.SaveAsync(doc));
            var bytes = _fs.ReadAllBytes(path);
            Assert.Equal([.. bom], bytes.Take(bom.Length));
            Assert.Equal(FileEncodings.Encode(text + "y;\r\n", encoding), bytes);
            if (bom.Length == 0)
                Assert.NotEqual(0xEF, bytes[0]);
            Assert.Equal(encoding, doc.Encoding);
            Assert.False(doc.IsModified);
        }

        // A changed encoding is used for the next save, with its BOM.
        var utf8 = await OpenAsync(@"C:\p\utf-8.pas");
        utf8.SetEncoding(FileEncoding.Utf16Le);
        Assert.True(await Documents.SaveAsync(utf8));
        Assert.Equal(FileEncodings.Encode(utf8.Buffer.Text, FileEncoding.Utf16Le), _fs.ReadAllBytes(@"C:\p\utf-8.pas"));
        Assert.False(utf8.IsModified);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-083")]
    public async Task LossyEncodingOffersUtf8()
    {
        var path = File(@"C:\p\Latin.pas", [0x41, 0xFC, 0x0D, 0x0A]); // "Aü" in Windows-1252
        var doc = await OpenAsync(path);
        Assert.Equal(FileEncoding.Windows1252, doc.Encoding);
        Append(doc, "\u0146"); // ņ: not in Windows-1252

        // Cancel aborts the save: nothing written, encoding unchanged.
        Dialogs.Enqueue("Msg.File.EncodingLossy", "Cancel");
        Assert.False(await Documents.SaveAsync(doc));
        var request = Assert.Single(Dialogs.Requests);
        Assert.Equal("Msg.File.EncodingLossy", request.MessageId);
        Assert.Equal([Strings.Encoding_windows_1252], request.Args);
        Assert.Equal([0x41, 0xFC, 0x0D, 0x0A], _fs.ReadAllBytes(path));
        Assert.Equal(FileEncoding.Windows1252, doc.Encoding);
        Assert.True(doc.IsModified);

        // Save as UTF-8 switches the encoding and saves.
        Dialogs.Enqueue("Msg.File.EncodingLossy", "SaveAsUTF8");
        Assert.True(await Documents.SaveAsync(doc));
        Assert.Equal(FileEncoding.Utf8, doc.Encoding);
        Assert.Equal("A\u00FC\r\n\u0146\r\n", Disk(path));
        Assert.False(doc.IsModified);
        Assert.Equal(2, Dialogs.Requests.Count);

        // Representable text in Windows-1252 saves without a question.
        var other = await OpenAsync(File(@"C:\p\Latin2.pas", [0x41, 0xFC]));
        Append(other, "\u00E9\r\n");
        Assert.True(await Documents.SaveAsync(other));
        Assert.Equal([0x41, 0xFC, 0xE9, 0x0D, 0x0A], _fs.ReadAllBytes(@"C:\p\Latin2.pas"));
        Assert.Equal(2, Dialogs.Requests.Count);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-080")]
    public async Task TrimAndFinalNewlineOptions()
    {
        var settings = _services.Settings;

        // Defaults: no trimming; a final line break (the document's) is added as one undo step.
        Assert.False(settings.Get(SettingsCatalog.FilesTrimTrailingWhitespaceOnSave));
        Assert.True(settings.Get(SettingsCatalog.FilesEnsureFinalNewline));
        var a = await OpenAsync(File(@"C:\p\A.pas", "a  \r\nb"));
        Assert.True(await Documents.SaveAsync(a));
        Assert.Equal("a  \r\nb\r\n", Disk(@"C:\p\A.pas"));
        Assert.Equal("a  \r\nb\r\n", a.Buffer.Text);
        Assert.False(a.IsModified);
        a.Buffer.Undo();
        Assert.Equal("a  \r\nb", a.Buffer.Text);
        Assert.True(a.IsModified);

        // An LF document gets an LF; text already ending with a line break and empty text get none.
        var lf = await OpenAsync(File(@"C:\p\Lf.pas", "a\nb"));
        Assert.True(await Documents.SaveAsync(lf));
        Assert.Equal("a\nb\n", Disk(@"C:\p\Lf.pas"));
        Assert.True(await Documents.SaveAsync(lf)); // nothing to change: no new undo step
        lf.Buffer.Undo();
        Assert.Equal("a\nb", lf.Buffer.Text);
        var empty = await OpenAsync(File(@"C:\p\Empty.pas", ""));
        empty.SetEncoding(FileEncoding.Utf8Bom);
        Assert.True(await Documents.SaveAsync(empty));
        Assert.Equal("", empty.Buffer.Text);
        Assert.False(empty.Buffer.CanUndo);

        // Trimming: spaces and tabs before each line break and at the end, except on the caret line when the caret is
        // in its trailing whitespace; trim and final line break form one undo step.
        settings.Set(SettingsCatalog.FilesTrimTrailingWhitespaceOnSave, true);
        var t = await OpenAsync(File(@"C:\p\T.pas", "a \t\r\nb  \r\n\t\r\n  c  "));
        t.ViewState.Caret = new TextPosition(2, 3); // inside "b  "'s trailing whitespace
        Assert.True(await Documents.SaveAsync(t));
        Assert.Equal("a\r\nb  \r\n\r\n  c\r\n", Disk(@"C:\p\T.pas"));
        t.Buffer.Undo();
        Assert.Equal("a \t\r\nb  \r\n\t\r\n  c  ", t.Buffer.Text);
        t.ViewState.Caret = new TextPosition(2, 2);   // before the whitespace: the line is trimmed
        Assert.True(await Documents.SaveAsync(t));
        Assert.Equal("a\r\nb\r\n\r\n  c\r\n", Disk(@"C:\p\T.pas"));

        // Whitespace only: trimmed to empty, no line break added.
        var blank = await OpenAsync(File(@"C:\p\Blank.pas", "  \t"));
        blank.ViewState.Caret = new TextPosition(1, 1);
        Assert.True(await Documents.SaveAsync(blank));
        Assert.Equal("", Disk(@"C:\p\Blank.pas"));

        // Form and JSON documents are saved exactly as they are.
        var json = await OpenAsync(File(@"C:\p\Data.json", "{ }  "));
        Append(json, " ");
        Assert.True(await Documents.SaveAsync(json));
        Assert.Equal("{ }   ", Disk(@"C:\p\Data.json"));
        // A .form without unit opens as a form document (DSG-CS-080); its form text is the file.
        var formResult = await Documents.OpenAsync(File(@"C:\p\Main.form", "object Form1  "));
        Assert.True(formResult.Success, formResult.MessageId);
        var form = Assert.IsType<FormDocumentViewModel>(formResult.Value);
        Append(form.FormText, " ");
        Assert.True(await Documents.SaveAsync(form));
        Assert.Equal("object Form1   ", Disk(@"C:\p\Main.form"));

        // Without ensureFinalNewline the end stays as it is.
        settings.Set(SettingsCatalog.FilesEnsureFinalNewline, false);
        var n = await OpenAsync(File(@"C:\p\N.pas", "x  "));
        n.ViewState.Caret = new TextPosition(1, 1);
        Append(n, "y");
        Assert.True(await Documents.SaveAsync(n));
        Assert.Equal("x  y", Disk(@"C:\p\N.pas"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-082")]
    [Trait("Req", "IDE-P1-080")]
    [Trait("Req", "UI-MSG-002")]
    public async Task FailedSaveKeepsDocumentModified()
    {
        var path = File(@"C:\p\A.pas", "a\r\n");
        var doc = await OpenAsync(path);
        Append(doc, "b");
        _fs.SetReadOnly(path, true);
        var saved = new List<DocumentViewModel>();
        Documents.DocumentSaved += (_, e) => saved.Add(e.Document);

        // Retry repeats the write; Cancel gives up. Only the final line break (step 2) changed the buffer.
        Dialogs.Enqueue("Msg.File.SaveFailed", "Retry", "Cancel");
        Assert.False(await Documents.SaveAsync(doc));
        Assert.Equal(["Msg.File.SaveFailed", "Msg.File.SaveFailed"], Dialogs.Requests.Select(r => r.MessageId));
        Assert.Equal(path, Dialogs.Requests[0].Args[0]);
        Assert.Equal(2, Dialogs.Requests[0].Args.Count);
        Assert.Equal("a\r\nb\r\n", doc.Buffer.Text);
        Assert.Equal("a\r\n", Disk(path));
        Assert.True(doc.IsModified);
        Assert.Empty(saved);
        Assert.Null(_services.Status.Message);

        // Save As… opens the Save As picker; a canceled picker keeps the document modified.
        Dialogs.Enqueue("Msg.File.SaveFailed", "SaveAs");
        Pickers.NextCancel();
        Assert.False(await Documents.SaveAsync(doc));
        Assert.Equal("saveAs", Pickers.Requests[^1].PromptId);
        Assert.True(doc.IsModified);
        Assert.Equal(path, doc.FilePath);

        // A Retry after the cause is gone succeeds: saved, unmodified, DocumentSaved and the status message.
        Dialogs.Enqueue("Msg.File.SaveFailed", _ =>
        {
            _fs.SetReadOnly(path, false);
            return "Retry";
        });
        await _services.Commands.ExecuteAsync("file.save");
        Assert.Equal("a\r\nb\r\n", Disk(path));
        Assert.False(doc.IsModified);
        Assert.Equal([doc], saved);
        Assert.Equal(string.Format(Strings.Msg_File_Saved, "A.pas"), _services.Status.Message);

        // file.save needs a modified active document.
        Assert.False(_services.Commands.CanExecute("file.save"));
        Append(doc, "c");
        Assert.True(_services.Commands.CanExecute("file.save"));

        // Save As… with a chosen path writes there and switches the document to it; the old file is untouched.
        _fs.SetReadOnly(path, true);
        Dialogs.Enqueue("Msg.File.SaveFailed", "SaveAs");
        Pickers.NextSave(@"C:\p\A2.pas");
        Assert.True(await Documents.SaveAsync(doc));
        Assert.Equal(@"C:\p\A2.pas", doc.FilePath);
        Assert.Equal("a\r\nb\r\nc\r\n", Disk(@"C:\p\A2.pas"));
        Assert.Equal("a\r\nb\r\n", Disk(path));
        Assert.False(doc.IsModified);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-085")]
    public async Task SaveAllCountsAndOrder()
    {
        var commands = _services.Commands;
        var a = await OpenAsync(File(@"C:\p\A.pas", "a\r\n"));
        var b = await OpenAsync(File(@"C:\p\B.pas", "b\r\n"));
        var c = await OpenAsync(File(@"C:\p\C.pas", "c\r\n"));
        Assert.False(commands.CanExecute("file.saveAll"));   // nothing modified

        Documents.Activate(a);
        var unit = Documents.NewUntitledUnit();              // opens right of A: tab order A, Unit1, B, C
        Append(c, "3\r\n");
        Append(a, "1\r\n");
        Assert.True(commands.CanExecute("file.saveAll"));
        var saved = new List<DocumentViewModel>();
        Documents.DocumentSaved += (_, e) => saved.Add(e.Document);

        // Tab order; the untitled document's canceled picker skips it; unmodified B is not written.
        Pickers.NextCancel();
        await commands.ExecuteAsync("file.saveAll");
        Assert.Equal(("saveAs", "Unit1.pas"), (Pickers.Requests.Single().PromptId, Pickers.Requests.Single().SuggestedName));
        Assert.Equal([a, c], saved);
        Assert.Equal("a\r\n1\r\n", Disk(@"C:\p\A.pas"));
        Assert.Equal("c\r\n3\r\n", Disk(@"C:\p\C.pas"));
        Assert.True(unit.IsModified);
        Assert.Equal(string.Format(Strings.Msg_File_SavedAll, 2), _services.Status.Message);
        Assert.Empty(Dialogs.Requests);

        // Nothing written: NothingToSave.
        Documents.Close(unit);
        Assert.True(await Documents.SaveAllAsync());
        Assert.Equal(Strings.Msg_File_NothingToSave, _services.Status.Message);

        // A failure answered with Cancel stops; one answered with Retry goes on.
        Append(a, "!");
        Append(b, "!");
        Append(c, "!");
        _fs.SetReadOnly(@"C:\p\B.pas", true);
        Dialogs.Enqueue("Msg.File.SaveFailed", "Cancel");
        saved.Clear();
        Assert.False(await Documents.SaveAllAsync());
        Assert.Equal([a], saved);
        Assert.True(b.IsModified && c.IsModified);

        Dialogs.Enqueue("Msg.File.SaveFailed", _ =>
        {
            _fs.SetReadOnly(@"C:\p\B.pas", false);
            return "Retry";
        });
        Assert.True(await Documents.SaveAllAsync());
        Assert.Equal([a, b, c], saved);
        Assert.Equal(string.Format(Strings.Msg_File_SavedAll, 2), _services.Status.Message);
        Assert.False(commands.CanExecute("file.saveAll"));
    }
}
