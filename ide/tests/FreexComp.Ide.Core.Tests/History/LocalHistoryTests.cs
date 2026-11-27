using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.History;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.History;

/// <summary>
/// The local history store (local-history.md §1, IDE-HIST-001…008; F11-059): revisions on save under
/// <c>.freexcomp/history/</c>, the <c>original</c> before the first save, retention, isolation from the save.
/// </summary>
[Trait("Phase", "8")]
public sealed class LocalHistoryTests
{
    private const string Dir = @"C:\p\App";
    private const string HistoryDir = Dir + @"\.freexcomp\history";

    private readonly InMemoryFileSystem _memory = new();
    private readonly RecordingFileSystem _fs;
    private readonly ManualClock _clock = new();
    private readonly IdeServices _services;

    public LocalHistoryTests()
    {
        _fs = new RecordingFileSystem(_memory);
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Clock = _clock;
        });
        _services.Settings.Set(SettingsCatalog.FilesEnsureFinalNewline, false); // contents are compared byte for byte
        ((DocumentService)_services.Documents).DesignCatalog = CodeSyncTests.Catalog;
    }

    private LocalHistoryService History => _services.History;

    private IDocumentService Documents => _services.Documents;

    private string File(string path, string text)
    {
        _memory.CreateDirectory(Path.GetDirectoryName(path)!);
        _memory.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    private string Disk(string path) => Encoding.UTF8.GetString(_memory.ReadAllBytes(path));

    private async Task OpenProjectAsync()
    {
        File(Dir + @"\App.pproj", """
            { "formatVersion": 1, "name": "App", "mainSource": "App.pas", "files": [ { "path": "App.pas" } ] }
            """);
        File(Dir + @"\App.pas", "program App;\nbegin\nend.\n");
        Assert.True(await _services.Projects.OpenAsync(Dir + @"\App.pproj"));
    }

    private async Task<TextDocumentViewModel> OpenAsync(string path)
    {
        var result = await Documents.OpenAsync(path);
        Assert.True(result.Success, result.MessageId);
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    /// <summary>A document whose file is gone before its first save (so that save has no original).</summary>
    private async Task<TextDocumentViewModel> OpenNewAsync(string path)
    {
        var document = await OpenAsync(File(path, ""));
        _memory.DeleteFile(path);
        return document;
    }

    /// <summary>Sets the text, saves (to <paramref name="path"/> when given), waits for the recording, then one second passes.</summary>
    private async Task SaveAsync(TextDocumentViewModel document, string text, string? path = null)
    {
        document.Buffer.ReplaceAll(text, resetUndo: false);
        if (path is null)
        {
            Assert.True(await Documents.SaveAsync(document));
        }
        else
        {
            var result = await Documents.SaveAsAsync(document, path);
            Assert.True(result.Success, result.MessageId);
        }

        await History.Pending;
        _clock.Advance(TimeSpan.FromSeconds(1));
    }

    private string Text(HistoryRevision revision, HistoryRole role = HistoryRole.Text) =>
        Encoding.UTF8.GetString(History.ReadEntry(revision, role));

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Sha(string text) => Sha(Encoding.UTF8.GetBytes(text));

    private static string BlobPath(string hash) => HistoryDir + @"\objects\" + hash[..2] + @"\" + hash[2..] + ".gz";

    private static string ListPath(string relativePath) =>
        HistoryDir + @"\files\" + Sha(relativePath.ToUpperInvariant()) + ".json";

    private List<string> Blobs() =>
        [.. _memory.AllFiles.Where(f => f.StartsWith(HistoryDir + @"\objects\", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];

    private void DeleteTree(string directory)
    {
        foreach (var sub in _memory.EnumerateDirectories(directory))
            DeleteTree(sub);
        foreach (var file in _memory.EnumerateFiles(directory, "*"))
            _memory.DeleteFile(file);
        _memory.DeleteDirectory(directory);
    }

    private static string RandomText(int seed, int length)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var random = new Random(seed);
        var text = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            text.Append(i % 100 == 99 ? '\n' : Alphabet[random.Next(Alphabet.Length)]);
        return text.ToString();
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-001")]
    [Trait("Req", "IDE-HIST-003")]
    [Trait("Req", "IDE-HIST-004")]
    [Trait("Req", "IDE-HIST-008")]
    public Task ThreeSavesThreeRevisions() => TestUiThread.RunAsync(ThreeSavesThreeRevisionsOnUiThread);

    private async Task ThreeSavesThreeRevisionsOnUiThread()
    {
        await OpenProjectAsync();
        var path = Dir + @"\Src\New.pas";
        _memory.CreateDirectory(Dir + @"\Src");
        var document = Documents.NewUntitledUnit();
        var changed = new List<string>();
        History.Changed += (_, e) => changed.Add(e.DocumentPath);
        var start = _clock.Now;

        await SaveAsync(document, "unit New; // 1", path);
        await SaveAsync(document, "unit New; // 2");
        await SaveAsync(document, "unit New; // 3");

        // F11-059 acceptance 1: three revisions, newest first, contents read back equal; ids 1, 2, 3 per key.
        var revisions = History.GetRevisions(path);
        Assert.Equal([3, 2, 1], revisions.Select(r => r.Id));
        Assert.All(revisions, r => Assert.Equal(HistoryKind.Save, r.Kind));
        Assert.All(revisions, r => Assert.Null(r.Label));
        Assert.Equal(["unit New; // 3", "unit New; // 2", "unit New; // 1"], revisions.Select(r => Text(r)));
        Assert.Equal([start.AddSeconds(2), start.AddSeconds(1), start], revisions.Select(r => r.TimeUtc));
        Assert.Equal([path, path, path], changed);
        var entry = Assert.Single(revisions[0].Entries);
        Assert.Equal((HistoryRole.Text, Sha("unit New; // 3"), 14L), (entry.Role, entry.Hash, entry.Size));

        // IDE-HIST-001: one gzip blob per content, named by the SHA-256 of the bytes; one list per key.
        foreach (var n in new[] { 1, 2, 3 })
        {
            var bytes = Encoding.UTF8.GetBytes("unit New; // " + n.ToString(CultureInfo.InvariantCulture));
            using var gzip = new GZipStream(new MemoryStream(_memory.ReadAllBytes(BlobPath(Sha(bytes)))), CompressionMode.Decompress);
            using var raw = new MemoryStream();
            gzip.CopyTo(raw);
            Assert.Equal(bytes, raw.ToArray());
        }

        Assert.Equal(3, Blobs().Count);

        // IDE-HIST-003: the list format.
        var list = JsonNode.Parse(Disk(ListPath("Src/New.pas")))!;
        Assert.Equal(1, (int)list["formatVersion"]!);
        Assert.Equal("Src/New.pas", (string)list["path"]!);
        var first = list["revisions"]![0]!;
        Assert.Equal(1, (int)first["id"]!);
        Assert.Equal("2026-01-01T12:00:00.000Z", (string)first["timeUtc"]!);
        Assert.Equal("save", (string)first["kind"]!);
        Assert.Null(first["label"]);
        Assert.Equal("text", (string)first["entries"]![0]!["role"]!);
        Assert.Equal(Sha("unit New; // 1"), (string)first["entries"]![0]!["hash"]!);
        Assert.Equal(14, (int)first["entries"]![0]!["size"]!);
        Assert.Equal("Saved New.pas", _services.Status.Message);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-006")]
    [Trait("Req", "IDE-HIST-007")]
    public Task RetentionOfTwoKeepsTwoNewest() => TestUiThread.RunAsync(RetentionOfTwoKeepsTwoNewestOnUiThread);

    private async Task RetentionOfTwoKeepsTwoNewestOnUiThread()
    {
        await OpenProjectAsync();
        _services.Settings.Set(SettingsCatalog.HistoryMaxRevisions, 2);
        var path = Dir + @"\Keep.pas";
        var document = await OpenNewAsync(path);

        await SaveAsync(document, "one");
        await SaveAsync(document, "two");
        await SaveAsync(document, "three");

        // F11-059 acceptance 2: with a limit of 2, three saves keep the two newest.
        var revisions = History.GetRevisions(path);
        Assert.Equal(["three", "two"], revisions.Select(r => Text(r)));
        Assert.Equal([3, 2], revisions.Select(r => r.Id));
        Assert.False(_memory.FileExists(BlobPath(Sha("one"))));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-005")]
    public Task DeletedFolderNoErrorOnNextSave() => TestUiThread.RunAsync(DeletedFolderNoErrorOnNextSaveOnUiThread);

    private async Task DeletedFolderNoErrorOnNextSaveOnUiThread()
    {
        await OpenProjectAsync();
        var path = File(Dir + @"\A.pas", "a0");
        var document = await OpenAsync(path);
        await SaveAsync(document, "a1");
        Assert.Equal(2, History.GetRevisions(path).Count); // the original and the save

        // F11-059 acceptance 3: the whole folder deleted while the IDE runs; the next save creates it again.
        DeleteTree(HistoryDir);
        Assert.Empty(History.GetRevisions(path));
        await SaveAsync(document, "a2");

        Assert.Equal("a2", Disk(path));
        Assert.Equal("Saved A.pas", _services.Status.Message);
        var revisions = History.GetRevisions(path);
        Assert.Equal([(HistoryKind.Save, "a2"), (HistoryKind.Original, "a1")], revisions.Select(r => (r.Kind, Text(r))));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-004")]
    public Task IdenticalSaveRecordsNothing() => TestUiThread.RunAsync(IdenticalSaveRecordsNothingOnUiThread);

    private async Task IdenticalSaveRecordsNothingOnUiThread()
    {
        await OpenProjectAsync();
        var path = Dir + @"\Same.pas";
        var document = Documents.NewUntitledUnit();
        await SaveAsync(document, "same", path);
        var changed = 0;
        History.Changed += (_, _) => changed++;
        var listBefore = Disk(ListPath("Same.pas"));

        document.Buffer.ReplaceAll("other", resetUndo: false);
        await SaveAsync(document, "same"); // modified, but the bytes written equal the newest revision's

        Assert.True(Assert.Single(History.GetRevisions(path)) is { Id: 1, Kind: HistoryKind.Save });
        Assert.Equal(listBefore, Disk(ListPath("Same.pas")));
        Assert.Equal(0, changed);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-004")]
    public Task FirstSaveOfExistingFileRecordsOriginal() => TestUiThread.RunAsync(FirstSaveOfExistingFileRecordsOriginalOnUiThread);

    private async Task FirstSaveOfExistingFileRecordsOriginalOnUiThread()
    {
        await OpenProjectAsync();
        var path = File(Dir + @"\Old.pas", "old text");
        var document = await OpenAsync(path);
        var saveTime = _clock.Now;
        await SaveAsync(document, "new text");

        var revisions = History.GetRevisions(path);
        Assert.Equal([(2, HistoryKind.Save, "new text"), (1, HistoryKind.Original, "old text")], revisions.Select(r => (r.Id, r.Kind, Text(r))));
        Assert.Equal([saveTime, saveTime.AddMilliseconds(-1)], revisions.Select(r => r.TimeUtc));
        Assert.Equal("original", (string)JsonNode.Parse(Disk(ListPath("Old.pas")))!["revisions"]![0]!["kind"]!);

        // Later saves record no further original.
        await SaveAsync(document, "newer text");
        Assert.Equal([HistoryKind.Save, HistoryKind.Save, HistoryKind.Original], History.GetRevisions(path).Select(r => r.Kind));

        // Save As to a new path records under the new key (the old key keeps its revisions); an existing target's old
        // content is its original.
        var other = File(Dir + @"\Other.pas", "other old");
        await SaveAsync(document, "moved", other);
        Assert.Equal(3, History.GetRevisions(path).Count);
        Assert.Equal([(HistoryKind.Save, "moved"), (HistoryKind.Original, "other old")], History.GetRevisions(other).Select(r => (r.Kind, Text(r))));

        // A first save that writes the bytes already on disk records only the save.
        var same = File(Dir + @"\Same.pas", "same");
        var sameDocument = await OpenAsync(same);
        sameDocument.Buffer.ReplaceAll("x", resetUndo: false);
        await SaveAsync(sameDocument, "same");
        Assert.Equal([HistoryKind.Save], History.GetRevisions(same).Select(r => r.Kind));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-002")]
    [Trait("Req", "IDE-HIST-004")]
    public Task FormDocumentUnitAndFormOneRevision() => TestUiThread.RunAsync(FormDocumentUnitAndFormOneRevisionOnUiThread);

    private async Task FormDocumentUnitAndFormOneRevisionOnUiThread()
    {
        await OpenProjectAsync();
        var unitPath = File(Dir + @"\MainForm.pas", TestUnits.Read("MainForm.pas"));
        var formPath = File(Dir + @"\MainForm.form", TestUnits.Read("MainForm.form"));
        var originalUnit = Disk(unitPath);
        var originalForm = Disk(formPath);
        var document = Assert.IsType<FormDocumentViewModel>((await Documents.OpenAsync(unitPath)).Value);

        // A designer change rewrites only the form; the unit's bytes are read from disk for the revision.
        var change = new SetProperty("MainForm", "Caption", new StringValue("Changed"));
        var inverse = document.Designer.Editor!.Apply([change]);
        document.Designer.Undo.Push(new DesignerUndoUnit("Undo_Designer_Property", [change], inverse, [], SelectionSnapshot.Empty, SelectionSnapshot.Empty));
        Assert.True(await Documents.SaveAsync(document));
        await History.Pending;

        // One key (the unit); each revision has a unit and a form entry; the form path has no list of its own.
        var revisions = History.GetRevisions(unitPath);
        Assert.Equal([HistoryKind.Save, HistoryKind.Original], revisions.Select(r => r.Kind));
        Assert.All(revisions, r => Assert.Equal([HistoryRole.Unit, HistoryRole.Form], r.Entries.Select(e => e.Role)));
        Assert.Equal(Disk(unitPath), Text(revisions[0], HistoryRole.Unit));
        Assert.Equal(Disk(formPath), Text(revisions[0], HistoryRole.Form));
        Assert.Contains("Caption = 'Changed'", Text(revisions[0], HistoryRole.Form), StringComparison.Ordinal);
        Assert.Equal(originalUnit, Text(revisions[1], HistoryRole.Unit));
        Assert.Equal(originalForm, Text(revisions[1], HistoryRole.Form));
        Assert.Empty(History.GetRevisions(formPath));
        Assert.False(_memory.FileExists(ListPath("MainForm.form")));
        Assert.Equal("MainForm.pas", (string)JsonNode.Parse(Disk(ListPath("MainForm.pas")))!["path"]!);

        // A form without unit is one text entry under its own path.
        var lonely = File(Dir + @"\Lonely.form", "object Lonely: TForm\r\nend\r\n");
        var lonelyDocument = Assert.IsType<FormDocumentViewModel>((await Documents.OpenAsync(lonely)).Value);
        Assert.False(lonelyDocument.HasUnit);
        lonelyDocument.FormText.Buffer.ReplaceAll("object Lonely: TForm\r\n  Left = 8\r\nend\r\n", resetUndo: false);
        Assert.True(await Documents.SaveAsync(lonelyDocument));
        await History.Pending;
        var newest = History.GetRevisions(lonely)[0];
        Assert.Equal([HistoryRole.Text], newest.Entries.Select(e => e.Role));
        Assert.Equal(Disk(lonely), Text(newest));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-006")]
    [Trait("Req", "IDE-HIST-008")]
    public Task LabelledExemptFromCountAndAge() => TestUiThread.RunAsync(LabelledExemptFromCountAndAgeOnUiThread);

    private async Task LabelledExemptFromCountAndAgeOnUiThread()
    {
        await OpenProjectAsync();
        _services.Settings.Set(SettingsCatalog.HistoryMaxRevisions, 2);
        var path = Dir + @"\L.pas";
        var document = Documents.NewUntitledUnit();
        await SaveAsync(document, "v1", path);
        var changed = 0;
        History.Changed += (_, _) => changed++;

        // SetLabel trims, keeps at most 80 characters, and an empty label removes it.
        History.SetLabel(History.GetRevisions(path)[0], "  temp  ");
        Assert.Equal("temp", History.GetRevisions(path)[0].Label);
        History.SetLabel(History.GetRevisions(path)[0], "   ");
        Assert.Null(History.GetRevisions(path)[0].Label);
        History.SetLabel(History.GetRevisions(path)[0], new string('x', 100));
        Assert.Equal(new string('x', 80), History.GetRevisions(path)[0].Label);
        History.SetLabel(History.GetRevisions(path)[0], "  before refactor ");
        Assert.Equal(4, changed);
        Assert.Equal("before refactor", (string)JsonNode.Parse(Disk(ListPath("L.pas")))!["revisions"]![0]!["label"]!);

        // Count: the labelled revision does not count and is not removed.
        await SaveAsync(document, "v2");
        await SaveAsync(document, "v3");
        await SaveAsync(document, "v4");
        Assert.Equal(["v4", "v3", "v1"], History.GetRevisions(path).Select(r => Text(r)));

        // Age: unlabelled revisions older than the limit go, the labelled one stays; the newest is never removed.
        _services.Settings.Set(SettingsCatalog.HistoryMaxAgeDays, 1);
        _clock.Advance(TimeSpan.FromDays(2));
        await SaveAsync(document, "v5");
        Assert.Equal([("v5", (string?)null), ("v1", "before refactor")], History.GetRevisions(path).Select(r => (Text(r), r.Label)));
        _clock.Advance(TimeSpan.FromDays(2));
        await SaveAsync(document, "v5"); // nothing recorded, nothing removed
        Assert.Equal(["v5", "v1"], History.GetRevisions(path).Select(r => Text(r)));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-006")]
    public Task SizeLimitRemovesOldestUnlabelledFirst() => TestUiThread.RunAsync(SizeLimitRemovesOldestUnlabelledFirstOnUiThread);

    private async Task SizeLimitRemovesOldestUnlabelledFirstOnUiThread()
    {
        await OpenProjectAsync();
        _services.Settings.Set(SettingsCatalog.HistoryMaxSizeMb, 10);
        const int Length = 4_000_000; // about 3 MB compressed each: three blobs fit in 10 MB, four do not
        var a = Dir + @"\A.txt"; // plain text: no language service over the large random texts
        var b = Dir + @"\B.txt";
        var documentA = await OpenNewAsync(a);
        var documentB = await OpenNewAsync(b);
        await SaveAsync(documentA, RandomText(1, Length));
        History.SetLabel(History.GetRevisions(a)[0], "first");
        await SaveAsync(documentA, RandomText(2, Length));
        await SaveAsync(documentA, RandomText(3, Length));
        Assert.Equal(3, History.GetRevisions(a).Count);

        // The fourth blob exceeds the limit: the oldest unlabelled revision goes (A's second), not the older labelled one.
        await SaveAsync(documentB, RandomText(4, Length));
        Assert.Equal([3, 1], History.GetRevisions(a).Select(r => r.Id));
        Assert.Equal("first", History.GetRevisions(a)[1].Label);
        Assert.Equal([1], History.GetRevisions(b).Select(r => r.Id));
        Assert.Equal(3, Blobs().Count);

        // Only labelled and newest revisions left: a labelled one goes next; the newest of each key stays.
        var c = Dir + @"\C.txt";
        var documentC = await OpenNewAsync(c);
        await SaveAsync(documentC, RandomText(5, Length));
        Assert.Equal([3], History.GetRevisions(a).Select(r => r.Id));
        Assert.Single(History.GetRevisions(b));
        Assert.Single(History.GetRevisions(c));
        var documentD = await OpenNewAsync(Dir + @"\D.txt");
        await SaveAsync(documentD, RandomText(6, Length));
        Assert.Equal(4, Blobs().Count); // nothing else is removable

        foreach (var document in new[] { documentA, documentB, documentC, documentD })
            await document.Tokens.GetAsync(); // the background token scans of the large buffers end within the test
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-006")]
    public Task UnreferencedBlobsDeleted() => TestUiThread.RunAsync(UnreferencedBlobsDeletedOnUiThread);

    private async Task UnreferencedBlobsDeletedOnUiThread()
    {
        await OpenProjectAsync();
        _services.Settings.Set(SettingsCatalog.HistoryMaxRevisions, 1);
        var a = Dir + @"\A.pas";
        var b = Dir + @"\B.pas";
        var documentA = Documents.NewUntitledUnit();
        var documentB = Documents.NewUntitledUnit();
        await SaveAsync(documentA, "shared", a);
        await SaveAsync(documentB, "shared", b); // one blob for both keys
        Assert.Equal([BlobPath(Sha("shared"))], Blobs());

        // A's next revision removes A's first; the blob stays while B references it.
        await SaveAsync(documentA, "a2");
        Assert.Equal(new[] { BlobPath(Sha("shared")), BlobPath(Sha("a2")) }.Order(StringComparer.Ordinal), Blobs());
        await SaveAsync(documentB, "b2");
        Assert.Equal(new[] { BlobPath(Sha("a2")), BlobPath(Sha("b2")) }.Order(StringComparer.Ordinal), Blobs());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-005")]
    public Task CorruptListRenamedAndIgnored() => TestUiThread.RunAsync(CorruptListRenamedAndIgnoredOnUiThread);

    private async Task CorruptListRenamedAndIgnoredOnUiThread()
    {
        await OpenProjectAsync();
        var path = Dir + @"\C.pas";
        var document = Documents.NewUntitledUnit();
        await SaveAsync(document, "c1", path);
        var list = ListPath("C.pas");
        File(list, "{ not json");
        File(list + ".corrupt", "an older corrupt list");
        Assert.Empty(History.GetRevisions(path)); // read: corrupt counts as empty

        await SaveAsync(document, "c2");

        Assert.Equal("c2", Disk(path));
        Assert.Equal("{ not json", Disk(list + ".corrupt"));
        Assert.Equal([(1, "c2")], History.GetRevisions(path).Select(r => (r.Id, Text(r))));
        Assert.Equal([BlobPath(Sha("c2"))], Blobs());
        Assert.Equal("Saved C.pas", _services.Status.Message);

        // A list of the right shape with a bad entry is corrupt too.
        File(list, """{ "formatVersion": 1, "path": "C.pas", "revisions": [ { "id": "x" } ] }""");
        await SaveAsync(document, "c3");
        Assert.Equal([(1, "c3")], History.GetRevisions(path).Select(r => (r.Id, Text(r))));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-005")]
    public Task WriteFailureDoesNotFailSave() => TestUiThread.RunAsync(WriteFailureDoesNotFailSaveOnUiThread);

    private async Task WriteFailureDoesNotFailSaveOnUiThread()
    {
        await OpenProjectAsync();
        var path = File(Dir + @"\W.pas", "w0");
        var document = await OpenAsync(path);
        _fs.FailWith = call => call.Contains(@"\.freexcomp\history\", StringComparison.OrdinalIgnoreCase)
            && (call.StartsWith("WriteAllBytes", StringComparison.Ordinal) || call.StartsWith("CreateDirectory", StringComparison.Ordinal))
            ? new IOException("There is not enough space on the disk.")
            : null;

        await SaveAsync(document, "w1");
        Assert.Equal("w1", Disk(path));
        Assert.False(document.IsModified);
        Assert.Equal("Local history could not be written: There is not enough space on the disk.", _services.Status.Message);

        // Once per IDE session.
        await SaveAsync(document, "w2");
        Assert.Equal("w2", Disk(path));
        Assert.Equal("Saved W.pas", _services.Status.Message);

        // Reads fail too (a blob that cannot be read back): the save still succeeds.
        _fs.FailWith = null;
        await SaveAsync(document, "w3");
        Assert.Equal(["w3", "w2"], History.GetRevisions(path).Select(r => Text(r))); // nothing was kept of w1, w2: w2 is the original
        _fs.FailWith = call => call.StartsWith("ReadAllBytes " + HistoryDir, StringComparison.OrdinalIgnoreCase) ? new IOException("Access is denied.") : null;
        await SaveAsync(document, "w4");
        Assert.Equal("w4", Disk(path));
        Assert.Equal("Saved W.pas", _services.Status.Message);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-004")]
    public Task OutsideProjectNotRecorded() => TestUiThread.RunAsync(OutsideProjectNotRecordedOnUiThread);

    private async Task OutsideProjectNotRecordedOnUiThread()
    {
        // No project open: nothing is recorded anywhere.
        var loose = File(Dir + @"\Loose.pas", "l0");
        var looseDocument = await OpenAsync(loose);
        await SaveAsync(looseDocument, "l1");
        Assert.False(_memory.DirectoryExists(HistoryDir));
        Assert.Empty(History.GetRevisions(loose));
        Assert.True(Documents.Close(looseDocument));

        // A project open: a file outside its folder and a file inside .freexcomp record nothing.
        await OpenProjectAsync();
        var outside = File(@"C:\p\Other\X.pas", "x0");
        var outsideDocument = await OpenAsync(outside);
        await SaveAsync(outsideDocument, "x1");
        var state = File(Dir + @"\.freexcomp\notes.txt", "n0");
        var stateDocument = await OpenAsync(state);
        await SaveAsync(stateDocument, "n1");
        Assert.False(_memory.DirectoryExists(HistoryDir));
        Assert.False(_memory.DirectoryExists(@"C:\p\Other\.freexcomp"));
        Assert.Empty(History.GetRevisions(outside));
        Assert.Empty(History.GetRevisions(state));

        // A sibling folder whose name starts with the project folder's is outside too.
        var sibling = File(@"C:\p\AppX\S.pas", "s0");
        await SaveAsync(await OpenAsync(sibling), "s1");
        Assert.False(_memory.DirectoryExists(HistoryDir));

        // Inside the folder: recorded.
        await SaveAsync(await OpenAsync(Dir + @"\App.pas"), "program App;\nbegin\n  WriteLn;\nend.\n");
        Assert.Equal(2, History.GetRevisions(Dir + @"\App.pas").Count);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-HIST-004")]
    [Trait("Req", "IDE-HIST-007")]
    public Task DisabledRecordsNothing() => TestUiThread.RunAsync(DisabledRecordsNothingOnUiThread);

    private async Task DisabledRecordsNothingOnUiThread()
    {
        await OpenProjectAsync();
        _services.Settings.Set(SettingsCatalog.HistoryEnabled, false);
        var path = File(Dir + @"\D.pas", "d0");
        var document = await OpenAsync(path);
        await SaveAsync(document, "d1");
        await SaveAsync(document, "d2");
        Assert.DoesNotContain(_fs.Calls, c => c.Contains(@"\.freexcomp\history", StringComparison.OrdinalIgnoreCase));
        Assert.False(_memory.DirectoryExists(HistoryDir));
        Assert.Empty(History.GetRevisions(path));

        // Turned on again: the next save records (with the original, the content on disk before it).
        _services.Settings.Set(SettingsCatalog.HistoryEnabled, true);
        await SaveAsync(document, "d3");
        Assert.Equal(["d3", "d2"], History.GetRevisions(path).Select(r => Text(r)));
    }
}
