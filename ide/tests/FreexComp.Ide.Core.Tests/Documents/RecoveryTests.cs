using System.Diagnostics;
using System.Globalization;
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
    public async Task WritesCopiesForModifiedDocuments()
    {
        var clean = await OpenAsync(@"C:\p\Clean.pas", "clean");
        var dirty = await OpenAsync(@"C:\p\Dirty.pas", "dirty\r\n");
        dirty.Buffer.ApplyEdits([new TextEdit(0, 0, "très ")]);
        var untitled = _services.Documents.NewUntitledUnit();

        _clock.Advance(Interval - TimeSpan.FromSeconds(1));
        await Recovery.WhenIdle();
        Assert.False(_disk.DirectoryExists(Recovery.InstanceDirectory)); // not before the interval

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
        Assert.Equal(_clock.Now.UtcDateTime, meta.GetProperty("savedUtc").GetDateTime().ToUniversalTime());
        Assert.Equal(dirty.DiskStamp!.LastWriteUtc, meta.GetProperty("baseTimestampUtc").GetDateTime().ToUniversalTime());
        var untitledMeta = Json(_disk, CopyPath(untitled, ".json"));
        Assert.Equal(JsonValueKind.Null, untitledMeta.GetProperty("path").ValueKind);
        Assert.Equal(JsonValueKind.Null, untitledMeta.GetProperty("baseTimestampUtc").ValueKind);
        Assert.Equal(untitled.Title, untitledMeta.GetProperty("title").GetString());
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
    public async Task OrphanedCopiesAreOfferedOnStart()
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
        Assert.Equal(["KeepRecovered", "DiscardAndReload"], bar.Actions.Select(x => x.Name));
        Assert.Equal([Strings.Button_KeepRecovered, Strings.Button_DiscardAndReload], bar.Actions.Select(x => x.Label));

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
    public async Task CopyDeletedOnSaveAndClose()
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

    private void WriteCopy(string folder, Guid id, string? path, string title, string text, DateTime savedUtc)
    {
        _disk.WriteAllBytes(Path.Combine(folder, id.ToString("D") + ".txt"), [.. Utf8Bom, .. Encoding.UTF8.GetBytes(text)]);
        _disk.WriteAllBytes(Path.Combine(folder, id.ToString("D") + ".json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            path,
            title,
            encoding = "utf-8",
            lineEnding = "crlf",
            savedUtc,
            baseTimestampUtc = (DateTime?)null,
        }));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ARCH-091")]
    public async Task FatalErrorWritesCopiesSynchronously()
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
