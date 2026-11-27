using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Documents;

/// <summary>Unsaved-file auto-recovery (phase1-shell.md §12) with the manual clock and the in-memory disk.</summary>
[Trait("Phase", "1")]
public sealed class RecoveryTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30); // environment.recoveryIntervalSeconds default
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private readonly ManualClock _clock = new();
    private readonly InMemoryFileSystem _disk;
    private readonly RecordingFileSystem _fs;
    private readonly FakeDialogService _dialogs = new();
    private readonly IdeServices _services;

    public RecoveryTests()
    {
        _disk = new InMemoryFileSystem(() => _clock.Now.UtcDateTime);
        _fs = new RecordingFileSystem(_disk);
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Clock = _clock;
            o.Dialogs = _dialogs;
        });
        _services.Settings.Set(SettingsCatalog.FilesEnsureFinalNewline, false);
    }

    private IRecoveryService Recovery => _services.Recovery;

    private string RecoveryRoot => Path.Combine(_services.UserPaths.LocalDirectory, "recovery");

    private string File(string path, string text)
    {
        _disk.CreateDirectory(Path.GetDirectoryName(path)!);
        _disk.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    private async Task<TextDocumentViewModel> OpenAsync(string path, string text)
    {
        var result = await _services.Documents.OpenAsync(File(path, text));
        Assert.True(result.Success, result.MessageId);
        return Assert.IsType<TextDocumentViewModel>(result.Value);
    }

    private string CopyPath(DocumentViewModel document, string extension) =>
        Path.Combine(Recovery.InstanceDirectory, document.Id.Value.ToString("D") + extension);

    private async Task TickAsync()
    {
        _clock.Advance(Interval);
        await Recovery.WhenIdle();
    }

    private static JsonElement Json(InMemoryFileSystem disk, string path) =>
        JsonDocument.Parse(disk.ReadAllBytes(path)).RootElement;

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-120")]
    [Trait("Req", "IDE-P1-121")]
    public Task WritesCopiesForModifiedDocuments() => TestUiThread.RunAsync(() => WritesCopiesForModifiedDocumentsOnUiThread()); // I-209

    private async Task WritesCopiesForModifiedDocumentsOnUiThread()
    {
        var clean = await OpenAsync(@"C:\p\Clean.pas", "clean");
        var dirty = await OpenAsync(@"C:\p\Dirty.pas", "dirty\r\n");
        dirty.Buffer.ApplyEdits([new TextEdit(0, 0, "très ")]);
        var edited = _clock.Now;
        var untitled = _services.Documents.NewUntitledUnit();

        _clock.Advance(Interval - TimeSpan.FromSeconds(1));
        await Recovery.WhenIdle();
        // Not before the interval. (The edited document has its idle copy by now, IDE-P1-126; the untitled one was never edited.)
        Assert.False(_disk.FileExists(CopyPath(untitled, ".txt")));
        Assert.False(_disk.FileExists(CopyPath(untitled, ".json")));

        _clock.Advance(TimeSpan.FromSeconds(1)); // the first interval ends now
        await Recovery.WhenIdle();
        Assert.Equal(RecoveryRoot, Path.GetDirectoryName(Recovery.InstanceDirectory));
        var owner = Json(_disk, Path.Combine(Recovery.InstanceDirectory, "owner.json"));
        using var self = Process.GetCurrentProcess();
        Assert.Equal(Environment.ProcessId, owner.GetProperty("processId").GetInt32());
        Assert.Equal(self.StartTime.ToUniversalTime().Ticks, owner.GetProperty("startTimeTicks").GetInt64());

        // Text: UTF-8 with BOM, as in the buffer. Metadata as IDE-P1-121.
        Assert.Equal([.. Utf8Bom, .. Encoding.UTF8.GetBytes("très dirty\r\n")], _disk.ReadAllBytes(CopyPath(dirty, ".txt")));
        var meta = Json(_disk, CopyPath(dirty, ".json"));
        Assert.Equal(1, meta.GetProperty("formatVersion").GetInt32());
        Assert.Equal(@"C:\p\Dirty.pas", meta.GetProperty("path").GetString());
        Assert.Equal("Dirty.pas", meta.GetProperty("title").GetString());
        Assert.Equal("utf-8", meta.GetProperty("encoding").GetString());
        Assert.Equal("crlf", meta.GetProperty("lineEnding").GetString());
        // The edited document was copied two seconds after its edit (IDE-P1-126), the untitled one at the interval.
        Assert.Equal(edited.UtcDateTime.AddSeconds(2), meta.GetProperty("savedUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(dirty.DiskStamp!.LastWriteUtc, meta.GetProperty("baseTimestampUtc").GetDateTime().ToUniversalTime());
        var untitledMeta = Json(_disk, CopyPath(untitled, ".json"));
        Assert.Equal(JsonValueKind.Null, untitledMeta.GetProperty("path").ValueKind);
        Assert.Equal(JsonValueKind.Null, untitledMeta.GetProperty("baseTimestampUtc").ValueKind);
        Assert.Equal(untitled.Title, untitledMeta.GetProperty("title").GetString());
        Assert.Equal(_clock.Now.UtcDateTime, untitledMeta.GetProperty("savedUtc").GetDateTime().ToUniversalTime());
        Assert.False(_disk.FileExists(CopyPath(clean, ".txt"))); // unmodified: no copy

        // Unchanged since the last copy: nothing is written again; a new edit is copied at the next interval.
        var calls = _fs.Calls.Count;
        await TickAsync();
        Assert.DoesNotContain(_fs.Calls.Skip(calls), c => c.Contains(Recovery.InstanceDirectory, StringComparison.OrdinalIgnoreCase));
        dirty.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        await TickAsync();
        Assert.Equal([.. Utf8Bom, .. Encoding.UTF8.GetBytes("xtrès dirty\r\n")], _disk.ReadAllBytes(CopyPath(dirty, ".txt")));

        // 0 switches the copies off.
        _services.Settings.Set(SettingsCatalog.EnvironmentRecoveryIntervalSeconds, 0);
        dirty.Buffer.ApplyEdits([new TextEdit(0, 0, "y")]);
        calls = _fs.Calls.Count;
        _clock.Advance(TimeSpan.FromMinutes(10));
        await Recovery.WhenIdle();
        Assert.DoesNotContain(_fs.Calls.Skip(calls), c => c.Contains(Recovery.InstanceDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-123")]
    [Trait("Req", "IDE-P1-124")]
    [Trait("Req", "IDE-SET-101")]
    public Task OrphanedCopiesAreOfferedOnStart() => TestUiThread.RunAsync(() => OrphanedCopiesAreOfferedOnStartOnUiThread()); // I-209

    private async Task OrphanedCopiesAreOfferedOnStartOnUiThread()
    {
        var saved = new DateTime(2025, 12, 31, 23, 15, 0, DateTimeKind.Utc);
        File(@"C:\p\A.pas", "disk text");
        var orphan = Path.Combine(RecoveryRoot, "dead-instance");
        WriteOwner(orphan, processId: int.MaxValue, startTicks: 1); // no such process
        var fileCopy = Guid.NewGuid();
        WriteCopy(orphan, fileCopy, @"C:\p\A.pas", "A.pas", "recovered text", saved);
        var untitledCopy = Guid.NewGuid();
        WriteCopy(orphan, untitledCopy, null, "Unit3.pas", "unit Unit3;\r\nend.\r\n", saved);
        var goneCopy = Guid.NewGuid();
        WriteCopy(orphan, goneCopy, @"C:\p\Gone.pas", "Gone.pas", "the file is gone", saved);

        // A folder whose owner still runs (this process) is left alone.
        var live = Path.Combine(RecoveryRoot, "live-instance");
        using (var self = Process.GetCurrentProcess())
            WriteOwner(live, Environment.ProcessId, self.StartTime.ToUniversalTime().Ticks);
        WriteCopy(live, Guid.NewGuid(), @"C:\p\A.pas", "A.pas", "someone else's", saved);

        await Recovery.RecoverAsync();
        await Recovery.WhenIdle();

        var a = Assert.Single(_services.Documents.Documents.OfType<TextDocumentViewModel>(), d => d.FilePath == @"C:\p\A.pas");
        Assert.Equal("recovered text", a.Buffer.Text);
        Assert.True(a.IsModified); // an unsaved modification of the original file
        var bar = Assert.Single(a.InfoBars, b => b.Kind == "Recovered");
        Assert.Equal(InfoBarSeverity.Info, bar.Severity);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_Recovered, saved.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)), bar.Text);
        Assert.Equal(["KeepRecovered", "ShowDifferences", "DiscardAndReload"], bar.Actions.Select(x => x.Name)); // IDE-P1-125
        Assert.Equal([Strings.Button_KeepRecovered, Strings.Button_ShowDifferences, Strings.Button_DiscardAndReload], bar.Actions.Select(x => x.Label));

        var u = Assert.Single(_services.Documents.Documents.OfType<TextDocumentViewModel>(), d => d.Title == "Unit3.pas");
        Assert.Null(u.FilePath);
        Assert.Equal("unit Unit3;\r\nend.\r\n", u.Buffer.Text);
        Assert.NotNull(Assert.Single(u.InfoBars, b => b.Kind == "Recovered"));
        var gone = Assert.Single(_services.Documents.Documents.OfType<TextDocumentViewModel>(), d => d.Title == "Gone.pas");
        Assert.Null(gone.FilePath); // the original file no longer exists: untitled
        Assert.Equal("the file is gone", gone.Buffer.Text);

        // The orphaned folder is gone once adopted; the new instance holds its own copies at once.
        Assert.False(_disk.DirectoryExists(orphan));
        Assert.True(_disk.FileExists(Path.Combine(live, "owner.json")));
        Assert.Equal([.. Utf8Bom, .. Encoding.UTF8.GetBytes("recovered text")], _disk.ReadAllBytes(CopyPath(a, ".txt")));
        Assert.True(_disk.FileExists(CopyPath(u, ".txt")));

        // Discard and Reload from Disk: the disk version, unmodified, and its copy goes.
        await bar.Actions.Single(x => x.Name == "DiscardAndReload").ExecuteAsync();
        await Recovery.WhenIdle();
        Assert.Equal("disk text", a.Buffer.Text);
        Assert.False(a.IsModified);
        Assert.DoesNotContain(a.InfoBars, b => b.Kind == "Recovered");
        Assert.False(_disk.FileExists(CopyPath(a, ".txt")));
        Assert.False(_disk.FileExists(CopyPath(a, ".json")));

        // Keep Recovered Version: the bar goes, the document stays modified; Discard on an untitled one closes it.
        await u.InfoBars.Single(b => b.Kind == "Recovered").Actions.Single(x => x.Name == "KeepRecovered").ExecuteAsync();
        Assert.Empty(u.InfoBars);
        Assert.True(u.IsModified);
        await gone.InfoBars.Single(b => b.Kind == "Recovered").Actions.Single(x => x.Name == "DiscardAndReload").ExecuteAsync();
        await Recovery.WhenIdle();
        Assert.DoesNotContain(gone, _services.Documents.Documents);
        Assert.Empty(_dialogs.Requests); // no prompts, never an I/O message (IDE-P1-124)
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-122")]
    [Trait("Req", "IDE-P1-035")]
    [Trait("Req", "IDE-SET-101")]
    public Task CopyDeletedOnSaveAndClose() => TestUiThread.RunAsync(() => CopyDeletedOnSaveAndCloseOnUiThread()); // I-209

    private async Task CopyDeletedOnSaveAndCloseOnUiThread()
    {
        var saved = await OpenAsync(@"C:\p\Saved.pas", "s");
        var discarded = await OpenAsync(@"C:\p\Discarded.pas", "d");
        var reverted = await OpenAsync(@"C:\p\Reverted.pas", "r");
        var kept = await OpenAsync(@"C:\p\Kept.pas", "k");
        foreach (var document in (TextDocumentViewModel[])[saved, discarded, reverted, kept])
            document.Buffer.ApplyEdits([new TextEdit(0, 0, "edit ")]);
        await TickAsync();
        foreach (var document in (TextDocumentViewModel[])[saved, discarded, reverted, kept])
            Assert.True(_disk.FileExists(CopyPath(document, ".txt")), document.Title);

        Assert.True(await _services.Documents.SaveAsync(saved));
        _dialogs.Answer("Msg.File.SaveChangesOne", "DontSave");
        Assert.True(await _services.Documents.CloseAsync(discarded));
        _dialogs.Answer("Msg.File.RevertConfirm", "Revert");
        Assert.True(await _services.Documents.RevertAsync(reverted));
        await Recovery.WhenIdle();
        foreach (var document in (TextDocumentViewModel[])[saved, discarded, reverted])
        {
            Assert.False(_disk.FileExists(CopyPath(document, ".txt")), document.Title);
            Assert.False(_disk.FileExists(CopyPath(document, ".json")), document.Title);
        }

        Assert.True(_disk.FileExists(CopyPath(kept, ".txt")));
        Assert.Equal("edit s", Encoding.UTF8.GetString(_disk.ReadAllBytes(@"C:\p\Saved.pas")));

        // Normal exit removes the instance's folder.
        await Recovery.DeleteInstanceDirectoryAsync();
        Assert.False(_disk.DirectoryExists(Recovery.InstanceDirectory));
    }

    private void WriteOwner(string folder, int processId, long startTicks)
    {
        _disk.CreateDirectory(folder);
        _disk.WriteAllBytes(Path.Combine(folder, "owner.json"),
            JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, processId, startTimeTicks = startTicks }));
    }

    /// <summary>A copy as an earlier instance left it; without <paramref name="baseSha256"/> the manifest has no such field (I-330).</summary>
    private void WriteCopy(string folder, Guid id, string? path, string title, string text, DateTime savedUtc,
        DateTime? baseTimestampUtc = null, string? baseSha256 = null)
    {
        _disk.WriteAllBytes(Path.Combine(folder, id.ToString("D") + ".txt"), [.. Utf8Bom, .. Encoding.UTF8.GetBytes(text)]);
        var manifest = new Dictionary<string, object?>
        {
            ["formatVersion"] = 1,
            ["path"] = path,
            ["title"] = title,
            ["encoding"] = "utf-8",
            ["lineEnding"] = "crlf",
            ["savedUtc"] = savedUtc,
            ["baseTimestampUtc"] = baseTimestampUtc,
        };
        if (baseSha256 is not null)
            manifest["baseSha256"] = baseSha256;
        _disk.WriteAllBytes(Path.Combine(folder, id.ToString("D") + ".json"), JsonSerializer.SerializeToUtf8Bytes(manifest));
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private string Orphan(string name = "dead-instance")
    {
        var orphan = Path.Combine(RecoveryRoot, name);
        WriteOwner(orphan, processId: int.MaxValue, startTicks: 1); // no such process
        return orphan;
    }

    private TextDocumentViewModel Document(string title) =>
        Assert.Single(_services.Documents.Documents.OfType<TextDocumentViewModel>(), d => d.Title == title);

    private static InfoBarViewModel RecoveredBar(TextDocumentViewModel document) => Assert.Single(document.InfoBars, b => b.Kind == "Recovered");

    private bool WroteToInstanceDirectory(int sinceCall) =>
        _fs.Calls.Skip(sinceCall).Any(c => c.Contains(Recovery.InstanceDirectory, StringComparison.OrdinalIgnoreCase));

    private async Task AdvanceAsync(TimeSpan by)
    {
        _clock.Advance(by);
        await Recovery.WhenIdle();
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-125")]
    public Task ShowDifferencesOpensDiff() => TestUiThread.RunAsync(() => ShowDifferencesOpensDiffOnUiThread());

    private async Task ShowDifferencesOpensDiffOnUiThread()
    {
        var saved = new DateTime(2025, 12, 31, 23, 15, 0, DateTimeKind.Utc);
        File(@"C:\p\A.pas", "disk text\r\nsame\r\n");
        var orphan = Orphan();
        WriteCopy(orphan, Guid.NewGuid(), @"C:\p\A.pas", "A.pas", "recovered text\r\nsame\r\n", saved);
        WriteCopy(orphan, Guid.NewGuid(), null, "Unit3.pas", "unit Unit3;\r\nend.\r\n", saved);
        WriteCopy(orphan, Guid.NewGuid(), @"C:\p\Gone.pas", "Gone.pas", "the file is gone", saved);
        await Recovery.RecoverAsync();
        await Recovery.WhenIdle();

        // Keep, Show Differences, Discard; not for an untitled document or one whose file no longer exists.
        var a = Document("A.pas");
        var bar = RecoveredBar(a);
        Assert.Equal(["KeepRecovered", "ShowDifferences", "DiscardAndReload"], bar.Actions.Select(x => x.Name));
        Assert.Equal("Show Differences", bar.Actions[1].Label);
        Assert.Equal(Strings.Button_ShowDifferences, bar.Actions[1].Label);
        Assert.Equal(["KeepRecovered", "DiscardAndReload"], RecoveredBar(Document("Unit3.pas")).Actions.Select(x => x.Name));
        Assert.Equal(["KeepRecovered", "DiscardAndReload"], RecoveredBar(Document("Gone.pas")).Actions.Select(x => x.Name));

        // Left = the file on disk, right = the document's buffer (live); the bar stays, the document stays modified.
        await bar.Actions[1].ExecuteAsync();
        var diff = Assert.Single(_services.Documents.Documents.OfType<DiffDocumentViewModel>());
        Assert.Same(diff, _services.Documents.ActiveDocument);
        Assert.Equal("A.pas (on disk) ↔ A.pas (recovered)", diff.Title);
        Assert.Equal(("A.pas (on disk)", "A.pas (recovered)"), (diff.LeftLabel, diff.RightLabel));
        Assert.Equal((@"C:\p\A.pas", @"C:\p\A.pas"), (diff.LeftToolTip, diff.RightToolTip));
        Assert.Equal("disk text\r\nsame\r\n", (await diff.LeftSource.ReadAsync())!.Text);
        Assert.Equal("recovered text\r\nsame\r\n", (await diff.RightSource.ReadAsync())!.Text);
        Assert.Same(a.Buffer, diff.RightSource.LiveBuffer);
        Assert.False(diff.IsIdentical);
        Assert.Same(bar, RecoveredBar(a));
        Assert.True(a.IsModified);
        Assert.Equal("recovered text\r\nsame\r\n", a.Buffer.Text);

        // Once more: the open diff is activated and shows the buffer as it is now, no second tab.
        _services.Documents.Activate(a);
        a.Buffer.ApplyEdits([new TextEdit(0, 0, "newer ")]);
        await bar.Actions[1].ExecuteAsync();
        Assert.Same(diff, Assert.Single(_services.Documents.Documents.OfType<DiffDocumentViewModel>()));
        Assert.Same(diff, _services.Documents.ActiveDocument);
        Assert.Equal("newer recovered text\r\nsame\r\n", (await diff.RightSource.ReadAsync())!.Text);

        // Equal texts (the recovered change undone): the status message, no tab.
        _services.Documents.Close(diff);
        a.Buffer.Undo();
        a.Buffer.Undo();
        Assert.Equal("disk text\r\nsame\r\n", a.Buffer.Text);
        await bar.Actions[1].ExecuteAsync();
        Assert.Equal(Strings.Msg_Diff_Identical, _services.Status.Message);
        Assert.Empty(_services.Documents.Documents.OfType<DiffDocumentViewModel>());
        Assert.Empty(_dialogs.Requests);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-126")]
    public Task IdleSnapshotAfterTwoSeconds() => TestUiThread.RunAsync(() => IdleSnapshotAfterTwoSecondsOnUiThread());

    private async Task IdleSnapshotAfterTwoSecondsOnUiThread()
    {
        var document = await OpenAsync(@"C:\p\Idle.pas", "idle");
        var undone = await OpenAsync(@"C:\p\Undone.pas", "undone");
        string Copy() => Encoding.UTF8.GetString(_disk.ReadAllBytes(CopyPath(document, ".txt")).AsSpan(Utf8Bom.Length));

        // Two seconds without an edit: the copy is written then, long before the interval.
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "a")]);
        undone.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        undone.Buffer.Undo(); // back at the saved text: nothing to copy
        await AdvanceAsync(TimeSpan.FromMilliseconds(1999));
        Assert.False(_disk.DirectoryExists(Recovery.InstanceDirectory));
        await AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Equal("aidle", Copy());
        Assert.Equal(@"C:\p\Idle.pas", Json(_disk, CopyPath(document, ".json")).GetProperty("path").GetString());
        Assert.False(_disk.FileExists(CopyPath(undone, ".txt")));

        // An edit starts the two seconds again: no copy while the edits follow each other faster.
        var calls = _fs.Calls.Count;
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "b")]);
        await AdvanceAsync(TimeSpan.FromMilliseconds(1500));
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "c")]);
        await AdvanceAsync(TimeSpan.FromMilliseconds(1500));
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "d")]);
        await AdvanceAsync(TimeSpan.FromMilliseconds(1999));
        Assert.False(WroteToInstanceDirectory(calls));
        Assert.Equal("aidle", Copy());
        await AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Equal("dcbaidle", Copy());

        // One write per idle period: nothing more while the document stays as it is, also not at the interval.
        calls = _fs.Calls.Count;
        await AdvanceAsync(Interval + Interval);
        Assert.False(WroteToInstanceDirectory(calls));

        // Switched off together with the interval (0), and on again with it.
        _services.Settings.Set(SettingsCatalog.EnvironmentRecoveryIntervalSeconds, 0);
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "e")]);
        calls = _fs.Calls.Count;
        await AdvanceAsync(TimeSpan.FromMinutes(10));
        Assert.False(WroteToInstanceDirectory(calls));
        _services.Settings.Set(SettingsCatalog.EnvironmentRecoveryIntervalSeconds, 30);
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "f")]);
        await AdvanceAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("fedcbaidle", Copy());

        // A closed document has no pending idle copy.
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "g")]);
        _dialogs.Answer("Msg.File.SaveChangesOne", "DontSave");
        Assert.True(await _services.Documents.CloseAsync(document));
        await Recovery.WhenIdle();
        calls = _fs.Calls.Count;
        await AdvanceAsync(TimeSpan.FromSeconds(2));
        Assert.False(WroteToInstanceDirectory(calls));
        Assert.False(_disk.FileExists(CopyPath(document, ".txt")));

        // Nor does an edit made just before a normal exit bring the deleted folder back.
        undone.Buffer.ApplyEdits([new TextEdit(0, 0, "h")]);
        await Recovery.DeleteInstanceDirectoryAsync();
        await AdvanceAsync(TimeSpan.FromSeconds(2));
        Assert.True(undone.IsModified);
        Assert.False(_disk.DirectoryExists(Recovery.InstanceDirectory));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-127")]
    public Task ManifestHasBaseHash() => TestUiThread.RunAsync(() => ManifestHasBaseHashOnUiThread());

    private async Task ManifestHasBaseHashOnUiThread()
    {
        const string path = @"C:\p\Hash.pas";
        var document = await OpenAsync(path, "as opened\r\n");
        var untitled = _services.Documents.NewUntitledUnit();
        string? BaseHash(DocumentViewModel d) => Json(_disk, CopyPath(d, ".json")).GetProperty("baseSha256").GetString();

        // The bytes read at open: lowercase hex SHA-256. An untitled document has none.
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        await TickAsync();
        Assert.Equal(Sha256("as opened\r\n"), BaseHash(document));
        Assert.Matches("^[0-9a-f]{64}$", BaseHash(document));
        Assert.Equal(JsonValueKind.Null, Json(_disk, CopyPath(untitled, ".json")).GetProperty("baseSha256").ValueKind);

        // The bytes written at the last save.
        Assert.True(await _services.Documents.SaveAsync(document));
        Assert.Equal("xas opened\r\n", Encoding.UTF8.GetString(_disk.ReadAllBytes(path)));
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "y")]);
        await TickAsync();
        Assert.Equal(Sha256("xas opened\r\n"), BaseHash(document));

        // Never by reading the file again for the copy: another program's change is not what the edits were based on.
        _disk.WriteAllBytes(path, Encoding.UTF8.GetBytes("changed behind the editor"));
        var calls = _fs.Calls.Count;
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "z")]);
        await TickAsync();
        Assert.Equal("zyxas opened\r\n", Encoding.UTF8.GetString(_disk.ReadAllBytes(CopyPath(document, ".txt")).AsSpan(Utf8Bom.Length)));
        Assert.Equal(Sha256("xas opened\r\n"), BaseHash(document));
        Assert.DoesNotContain("ReadAllBytes " + path, _fs.Calls.Skip(calls));

        // The bytes read at a revert.
        _dialogs.Answer("Msg.File.RevertConfirm", "Revert");
        Assert.True(await _services.Documents.RevertAsync(document));
        document.Buffer.ApplyEdits([new TextEdit(0, 0, "r")]);
        await TickAsync();
        Assert.Equal(Sha256("changed behind the editor"), BaseHash(document));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-127")]
    public Task DiskChangedSinceSnapshotUsesWarningBar() => TestUiThread.RunAsync(() => DiskChangedSinceSnapshotUsesWarningBarOnUiThread());

    private async Task DiskChangedSinceSnapshotUsesWarningBarOnUiThread()
    {
        var saved = new DateTime(2025, 12, 31, 23, 15, 0, DateTimeKind.Utc);
        var when = saved.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var changedStamp = _disk.GetLastWriteTimeUtc(File(@"C:\p\Changed.pas", "replaced on disk"));
        var sameStamp = _disk.GetLastWriteTimeUtc(File(@"C:\p\Same.pas", "as it was"));
        var orphan = Orphan();
        // Changed.pas: other bytes with the same time (a copy tool); Same.pas: the same bytes with another time.
        WriteCopy(orphan, Guid.NewGuid(), @"C:\p\Changed.pas", "Changed.pas", "my edits", saved, changedStamp, Sha256("the text my edits started from"));
        WriteCopy(orphan, Guid.NewGuid(), @"C:\p\Same.pas", "Same.pas", "my other edits", saved, sameStamp.AddHours(-1),
            Sha256("as it was").ToUpperInvariant());
        WriteCopy(orphan, Guid.NewGuid(), null, "Unit3.pas", "unit Unit3;", saved);

        await Recovery.RecoverAsync();
        await Recovery.WhenIdle();

        var changed = Document("Changed.pas");
        Assert.Equal("my edits", changed.Buffer.Text);
        Assert.True(changed.IsModified);
        var bar = RecoveredBar(changed);
        Assert.Equal(InfoBarSeverity.Warning, bar.Severity);
        Assert.Equal(
            "Unsaved changes from {0} were recovered, but the file on disk has changed since. Compare before keeping them.",
            Strings.Msg_File_RecoveredDiskChanged);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_RecoveredDiskChanged, when), bar.Text);
        Assert.Equal(["KeepRecovered", "ShowDifferences", "DiscardAndReload"], bar.Actions.Select(x => x.Name));
        Assert.True(bar.CanDismiss);

        var same = RecoveredBar(Document("Same.pas"));
        Assert.Equal(InfoBarSeverity.Info, same.Severity);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_Recovered, when), same.Text);
        Assert.Equal(InfoBarSeverity.Info, RecoveredBar(Document("Unit3.pas")).Severity);

        // This instance's own copy keeps what the edits were based on, so a second unexpected end warns again.
        var meta = Json(_disk, CopyPath(changed, ".json"));
        Assert.Equal(Sha256("the text my edits started from"), meta.GetProperty("baseSha256").GetString());
        Assert.Equal(changedStamp, meta.GetProperty("baseTimestampUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(Sha256("as it was"), Json(_disk, CopyPath(Document("Same.pas"), ".json")).GetProperty("baseSha256").GetString());

        // Keep Recovered Version and a save: the edits are based on what is on disk now.
        await bar.Actions[0].ExecuteAsync();
        Assert.Empty(changed.InfoBars);
        Assert.True(await _services.Documents.SaveAsync(changed));
        changed.Buffer.ApplyEdits([new TextEdit(0, 0, "more ")]);
        await TickAsync();
        Assert.Equal(Sha256("my edits"), Json(_disk, CopyPath(changed, ".json")).GetProperty("baseSha256").GetString());
        Assert.Empty(_dialogs.Requests);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-127")]
    public Task OldManifestWithoutHashUsesTimestamp() => TestUiThread.RunAsync(() => OldManifestWithoutHashUsesTimestampOnUiThread());

    private async Task OldManifestWithoutHashUsesTimestampOnUiThread()
    {
        var saved = new DateTime(2025, 12, 31, 23, 15, 0, DateTimeKind.Utc);
        var when = saved.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var newerStamp = _disk.GetLastWriteTimeUtc(File(@"C:\p\Newer.pas", "written again"));
        var sameStamp = _disk.GetLastWriteTimeUtc(File(@"C:\p\Same.pas", "as it was"));
        File(@"C:\p\Unknown.pas", "no base at all");
        var orphan = Orphan();
        WriteCopy(orphan, Guid.NewGuid(), @"C:\p\Newer.pas", "Newer.pas", "my edits", saved, newerStamp.AddMinutes(-1));
        WriteCopy(orphan, Guid.NewGuid(), @"C:\p\Same.pas", "Same.pas", "my other edits", saved, sameStamp);
        WriteCopy(orphan, Guid.NewGuid(), @"C:\p\Unknown.pas", "Unknown.pas", "edits", saved); // neither a hash nor a time: nothing to compare

        await Recovery.RecoverAsync();
        await Recovery.WhenIdle();

        var newer = RecoveredBar(Document("Newer.pas"));
        Assert.Equal(InfoBarSeverity.Warning, newer.Severity);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_RecoveredDiskChanged, when), newer.Text);
        Assert.Equal(["KeepRecovered", "ShowDifferences", "DiscardAndReload"], newer.Actions.Select(x => x.Name));
        foreach (var title in (string[])["Same.pas", "Unknown.pas"])
        {
            var bar = RecoveredBar(Document(title));
            Assert.Equal(InfoBarSeverity.Info, bar.Severity);
            Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_Recovered, when), bar.Text);
        }

        // The base stays the old manifest's time, without a hash, until the document is saved or reloaded.
        var meta = Json(_disk, CopyPath(Document("Newer.pas"), ".json"));
        Assert.Equal(JsonValueKind.Null, meta.GetProperty("baseSha256").ValueKind);
        Assert.Equal(newerStamp.AddMinutes(-1), meta.GetProperty("baseTimestampUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(Sha256("as it was"), Json(_disk, CopyPath(Document("Same.pas"), ".json")).GetProperty("baseSha256").GetString());
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-129")]
    public Task DiscardDeletesSnapshot() => TestUiThread.RunAsync(() => DiscardDeletesSnapshotOnUiThread());

    private async Task DiscardDeletesSnapshotOnUiThread()
    {
        var saved = new DateTime(2025, 12, 31, 23, 15, 0, DateTimeKind.Utc);
        File(@"C:\p\A.pas", "disk a");
        File(@"C:\p\B.pas", "disk b");
        var orphan = Orphan();
        var (a, b, untitled) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        WriteCopy(orphan, a, @"C:\p\A.pas", "A.pas", "recovered a", saved);
        WriteCopy(orphan, b, @"C:\p\B.pas", "B.pas", "recovered b", saved);
        WriteCopy(orphan, untitled, null, "Unit3.pas", "unit Unit3;", saved);
        // B's copy cannot be read now, so the folder stays for the next start (IDE-P1-124) with the adopted copies in it.
        var unreadable = Path.Combine(orphan, b.ToString("D") + ".txt");
        _fs.FailWhen = call => call == "ReadAllBytes " + unreadable;

        await Recovery.RecoverAsync();
        await Recovery.WhenIdle();
        _fs.FailWhen = null;
        var documentA = Document("A.pas");
        var documentU = Document("Unit3.pas");
        Assert.DoesNotContain(_services.Documents.Documents, d => d.Title == "B.pas");
        Assert.True(_disk.FileExists(Path.Combine(orphan, a.ToString("D") + ".txt")));
        Assert.True(_disk.FileExists(CopyPath(documentA, ".txt")));
        Assert.True(_disk.FileExists(CopyPath(documentU, ".txt")));

        // Discard and Reload from Disk: this instance's copy and the adopted one are deleted at once.
        await RecoveredBar(documentA).Actions.Single(x => x.Name == "DiscardAndReload").ExecuteAsync();
        await RecoveredBar(documentU).Actions.Single(x => x.Name == "DiscardAndReload").ExecuteAsync();
        await Recovery.WhenIdle();
        Assert.Equal("disk a", documentA.Buffer.Text);
        Assert.False(documentA.IsModified);
        Assert.Empty(documentA.InfoBars);
        Assert.DoesNotContain(documentU, _services.Documents.Documents);
        foreach (var gone in (string[])[CopyPath(documentA, ".txt"), CopyPath(documentA, ".json"), CopyPath(documentU, ".txt"), CopyPath(documentU, ".json"),
                     Path.Combine(orphan, a.ToString("D") + ".txt"), Path.Combine(orphan, a.ToString("D") + ".json"),
                     Path.Combine(orphan, untitled.ToString("D") + ".txt"), Path.Combine(orphan, untitled.ToString("D") + ".json")])
        {
            Assert.False(_disk.FileExists(gone), gone);
        }

        Assert.True(_disk.FileExists(unreadable)); // not adopted: still there

        // After a restart (this instance ended unexpectedly too) nothing of the discarded copies is offered again.
        var next = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Clock = _clock;
            o.Dialogs = _dialogs;
            o.UserDir = _services.Options.UserDir!;
        });
        var restarted = new RecoveryService(next.Documents, _fs, next.UserPaths, _clock, next.Settings, next.Log, safeMode: false,
            self: new ProcessIdentity(1, 1), isRunning: _ => false);
        await restarted.RecoverAsync();
        await restarted.WhenIdle();
        var offered = Assert.Single(next.Documents.Documents.OfType<TextDocumentViewModel>());
        Assert.Equal((@"C:\p\B.pas", "recovered b"), (offered.FilePath, offered.Buffer.Text));
        Assert.Empty(_dialogs.Requests);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-091")]
    public Task FatalErrorWritesCopiesSynchronously() => TestUiThread.RunAsync(() => FatalErrorWritesCopiesSynchronouslyOnUiThread()); // I-209

    private async Task FatalErrorWritesCopiesSynchronouslyOnUiThread()
    {
        var dirty = await OpenAsync(@"C:\p\Dirty.pas", "dirty");
        dirty.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        await OpenAsync(@"C:\p\Clean.pas", "clean");

        // Before any interval: the copy is on disk when the call returns (no queue, no timer).
        _services.Application.ReportFatal(new InvalidOperationException("fatal"));
        Assert.Equal([.. Utf8Bom, .. "xdirty"u8], _disk.ReadAllBytes(CopyPath(dirty, ".txt")));
        Assert.Equal(@"C:\p\Dirty.pas", Json(_disk, CopyPath(dirty, ".json")).GetProperty("path").GetString());
        Assert.True(_disk.FileExists(Path.Combine(Recovery.InstanceDirectory, "owner.json")));
        Assert.Equal(3, _disk.EnumerateFiles(Recovery.InstanceDirectory, "*").Count()); // owner + one copy
    }
}
