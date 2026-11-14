using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Documents;

/// <summary>External file changes (phase1-shell.md §11) with the manual watcher and clock.</summary>
[Trait("Phase", "1")]
public sealed class FileChangeTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300); // IDE-P1-111

    private readonly ManualClock _clock = new();
    private readonly InMemoryFileSystem _disk;
    private readonly RecordingFileSystem _fs;
    private readonly ManualFileWatcherFactory _watchers = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly IdeServices _services;

    public FileChangeTests()
    {
        _disk = new InMemoryFileSystem(() => _clock.Now.UtcDateTime); // writes carry the clock's time
        _fs = new RecordingFileSystem(_disk);
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Clock = _clock;
            o.FileWatchers = _watchers;
            o.Dialogs = _dialogs;
        });
        _services.Settings.Set(SettingsCatalog.FilesEnsureFinalNewline, false);
        _services.Settings.Set(SettingsCatalog.FilesTrimTrailingWhitespaceOnSave, false);
    }

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

    /// <summary>Another program writes the file one second later; the watcher reports it.</summary>
    private void WriteExternally(string path, string text)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        var existed = _disk.FileExists(path);
        _disk.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        _watchers.Raise(new FileChange(existed ? FileChangeKind.Changed : FileChangeKind.Created, path));
    }

    private void DeleteExternally(string path)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        _disk.DeleteFile(path);
        _watchers.Raise(new FileChange(FileChangeKind.Deleted, path));
    }

    /// <summary>Lets the debounce run out and waits for the checks it started.</summary>
    private async Task SettleAsync()
    {
        _clock.Advance(Debounce);
        await _services.FileChanges.WhenIdle();
    }

    private static InfoBarViewModel? Bar(DocumentViewModel document, string kind) =>
        document.InfoBars.FirstOrDefault(b => b.Kind == kind);

    private static Task Press(InfoBarViewModel bar, string action) => bar.Actions.Single(a => a.Name == action).ExecuteAsync();

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-110")]
    [Trait("Req", "IDE-P1-111")]
    [Trait("Req", "IDE-P1-113")]
    [Trait("Req", "IDE-P1-118")]
    public async Task UnmodifiedReloadsSilently()
    {
        var doc = await OpenAsync(@"C:\p\A.pas", "line1\r\nline2\r\nline3\r\n");
        doc.Buffer.ApplyEdits([new TextEdit(0, 0, "x")]);
        doc.Buffer.Undo(); // a redo step that the reload clears
        doc.ViewState.Caret = new TextPosition(3, 4);
        Assert.Equal([@"C:\p"], _watchers.ActiveDirectories); // one watcher per directory

        // Debounced per file: nothing happens until 300 ms after the last event.
        WriteExternally(doc.FilePath!, "LINE1\r\nline2 changed\r\nline3 too\r\nline4\r\n");
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        _watchers.Raise(new FileChange(FileChangeKind.Changed, doc.FilePath!));
        _clock.Advance(TimeSpan.FromMilliseconds(200));
        await _services.FileChanges.WhenIdle();
        Assert.Equal("line1\r\nline2\r\nline3\r\n", doc.Buffer.Text);

        await SettleAsync();
        Assert.Equal("LINE1\r\nline2 changed\r\nline3 too\r\nline4\r\n", doc.Buffer.Text);
        Assert.False(doc.IsModified);
        Assert.False(doc.Buffer.CanUndo);
        Assert.False(doc.Buffer.CanRedo); // the undo history is cleared
        Assert.Equal(new TextPosition(3, 4), doc.ViewState.Caret);
        Assert.Empty(doc.InfoBars);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_ReloadedSilently, "A.pas"), _services.Status.Message);

        // A watcher error triggers a full re-check (IDE-P1-118).
        WriteExternally(doc.FilePath!, "after error\r\n");
        _watchers.RaiseError(@"C:\p", new InternalBufferOverflowException());
        await _services.FileChanges.WhenIdle();
        Assert.Equal("after error\r\n", doc.Buffer.Text);
        // I-173: the Changed event of that write left a debounce pending; let it run out here (it finds nothing new),
        // or it fires inside the next WriteExternally's clock advance and races the disk write and the next check
        // (two awaited checks resuming on two pool threads could both add an ExternalChange bar).
        await SettleAsync();
        Assert.Equal("after error\r\n", doc.Buffer.Text);

        // With environment.autoReloadUnmodified off: the ExternalChange bar (Reload / Keep My Version).
        _services.Settings.Set(SettingsCatalog.EnvironmentAutoReloadUnmodified, false);
        WriteExternally(doc.FilePath!, "third\r\n");
        await SettleAsync();
        Assert.Equal("after error\r\n", doc.Buffer.Text);
        var bar = Assert.IsType<InfoBarViewModel>(Bar(doc, "ExternalChange"));
        Assert.Equal(InfoBarSeverity.Warning, bar.Severity);
        Assert.Equal(Strings.Msg_File_ExternalChange, bar.Text);
        Assert.Equal(["Reload", "KeepMyVersion"], bar.Actions.Select(a => a.Name));
        await Press(bar, "Reload");
        await _services.FileChanges.WhenIdle();
        Assert.Equal("third\r\n", doc.Buffer.Text);
        Assert.Null(Bar(doc, "ExternalChange"));
        Assert.False(doc.IsModified);

        // The watcher goes when the last document of its directory closes, and with detection switched off.
        Assert.True(_services.Documents.Close(doc));
        Assert.Empty(_watchers.ActiveDirectories);
        var b = await OpenAsync(@"C:\q\B.pas", "b");
        Assert.Equal([@"C:\q"], _watchers.ActiveDirectories);
        _services.Settings.Set(SettingsCatalog.FilesDetectExternalChanges, false);
        Assert.Empty(_watchers.ActiveDirectories);
        WriteExternally(b.FilePath!, "bb");
        _services.FileChanges.CheckAll();
        await SettleAsync();
        Assert.Equal("b", b.Buffer.Text);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-110")]
    [Trait("Req", "IDE-P1-114")]
    [Trait("Req", "IDE-P1-116")]
    public async Task ModifiedShowsInfoBarAndKeepRemembersTimestamp()
    {
        var doc = await OpenAsync(@"C:\p\A.pas", "disk 1");
        doc.Buffer.ApplyEdits([new TextEdit(0, 0, "mine ")]);

        WriteExternally(doc.FilePath!, "disk 2");
        await SettleAsync();
        var bar = Assert.IsType<InfoBarViewModel>(Bar(doc, "ExternalChange"));
        Assert.Equal(Strings.Msg_File_ExternalChangeDirty, bar.Text);
        Assert.Equal(["ReloadAndDiscard", "KeepMyVersion"], bar.Actions.Select(a => a.Name));
        Assert.Equal([Strings.Button_ReloadAndDiscard, Strings.Button_KeepMyVersion], bar.Actions.Select(a => a.Label));
        Assert.Equal("mine disk 1", doc.Buffer.Text);
        Assert.True(doc.IsModified);

        // Keep My Version: the bar goes, the document stays modified, the same change is not reported again — neither
        // by the watcher nor by the activation re-check (IDE-P1-110 b).
        await Press(bar, "KeepMyVersion");
        Assert.Empty(doc.InfoBars);
        Assert.True(doc.IsModified);
        _watchers.Raise(new FileChange(FileChangeKind.Changed, doc.FilePath!));
        await SettleAsync();
        _services.FileChanges.CheckAll();
        await _services.FileChanges.WhenIdle();
        Assert.Empty(doc.InfoBars);

        // The next save overwrites the disk version without a question (the fake dialogs fail on any prompt).
        Assert.True(await _services.Documents.SaveAsync(doc));
        Assert.Equal("mine disk 1", Encoding.UTF8.GetString(_disk.ReadAllBytes(doc.FilePath!)));

        // A newer change found by the activation re-check; Reload and Discard My Changes takes the disk version.
        doc.Buffer.ApplyEdits([new TextEdit(0, 0, "again ")]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _disk.WriteAllBytes(doc.FilePath!, Encoding.UTF8.GetBytes("disk 3"));
        _services.FileChanges.CheckAll();
        await _services.FileChanges.WhenIdle();
        bar = Assert.IsType<InfoBarViewModel>(Bar(doc, "ExternalChange"));
        Assert.Single(doc.InfoBars, b => b.Kind is "ExternalChange" or "ExternalDelete"); // one per document
        await Press(bar, "ReloadAndDiscard");
        await _services.FileChanges.WhenIdle();
        Assert.Equal("disk 3", doc.Buffer.Text);
        Assert.False(doc.IsModified);
        Assert.False(doc.Buffer.CanUndo);
        Assert.Empty(doc.InfoBars);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-115")]
    [Trait("Req", "IDE-P1-116")]
    [Trait("Req", "UI-ED-074")]
    public async Task DeletedFileOffersKeepOrClose()
    {
        var area = new DocumentAreaViewModel(_services.Commands, _services.Keymap, _services.Documents);
        var a = await OpenAsync(@"C:\p\A.pas", "a");
        DeleteExternally(a.FilePath!);
        await SettleAsync();
        var bar = Assert.IsType<InfoBarViewModel>(Bar(a, "ExternalDelete"));
        Assert.Equal(Strings.Msg_File_ExternalDelete, bar.Text);
        Assert.Equal(["KeepInEditor", "Close"], bar.Actions.Select(x => x.Name));
        Assert.True(a.IsDeletedOnDisk);
        Assert.True(area.Tabs.Single(t => t.Document == a).IsDeletedOnDisk); // struck-through title (UI-ED-074)

        // Keep in Editor: the document stays and becomes modified; saving recreates the file.
        await Press(bar, "KeepInEditor");
        Assert.Empty(a.InfoBars);
        Assert.True(a.IsModified);
        Assert.True(a.IsDeletedOnDisk);
        Assert.True(await _services.Documents.SaveAsync(a));
        Assert.Equal("a", Encoding.UTF8.GetString(_disk.ReadAllBytes(a.FilePath!)));
        Assert.False(a.IsDeletedOnDisk);
        Assert.False(area.Tabs.Single(t => t.Document == a).IsDeletedOnDisk);

        // Close on an unmodified document: no prompt (unscripted prompts fail).
        var b = await OpenAsync(@"C:\p\B.pas", "b");
        DeleteExternally(b.FilePath!);
        await SettleAsync();
        await Press(Bar(b, "ExternalDelete")!, "Close");
        Assert.DoesNotContain(b, _services.Documents.Documents);
        Assert.Empty(_dialogs.Requests);

        // Close on a modified document: the normal prompt.
        var c = await OpenAsync(@"C:\p\C.pas", "c");
        c.Buffer.ApplyEdits([new TextEdit(1, 0, "!")]);
        DeleteExternally(c.FilePath!);
        await SettleAsync();
        _dialogs.Answer("Msg.File.SaveChangesOne", "DontSave");
        await Press(Bar(c, "ExternalDelete")!, "Close");
        Assert.DoesNotContain(c, _services.Documents.Documents);
        Assert.Equal("Msg.File.SaveChangesOne", Assert.Single(_dialogs.Requests).MessageId);

        // The file comes back before the user answers: identical content removes the bar ...
        var d = await OpenAsync(@"C:\p\D.pas", "d");
        DeleteExternally(d.FilePath!);
        await SettleAsync();
        Assert.NotNull(Bar(d, "ExternalDelete"));
        WriteExternally(d.FilePath!, "d");
        await SettleAsync();
        Assert.Empty(d.InfoBars);
        Assert.False(d.IsDeletedOnDisk);
        Assert.False(d.IsModified);

        // ... changed content gets the changed bar in its place (here: modified document).
        d.Buffer.ApplyEdits([new TextEdit(1, 0, "?")]);
        DeleteExternally(d.FilePath!);
        await SettleAsync();
        WriteExternally(d.FilePath!, "new d");
        await SettleAsync();
        Assert.Null(Bar(d, "ExternalDelete"));
        Assert.Equal(Strings.Msg_File_ExternalChangeDirty, Bar(d, "ExternalChange")?.Text);
        Assert.False(d.IsDeletedOnDisk);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-086")]
    [Trait("Req", "IDE-P1-112")]
    public async Task OwnSaveIsIgnored()
    {
        var doc = await OpenAsync(@"C:\p\A.pas", "a");
        doc.Buffer.ApplyEdits([new TextEdit(1, 0, "b")]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await _services.Documents.SaveAsync(doc));
        var saved = _services.Status.Message;

        // The watcher reports our own write: for 500 ms after it the path is not even looked at.
        var calls = _fs.Calls.Count;
        _watchers.Raise(new FileChange(FileChangeKind.Changed, doc.FilePath!));
        await SettleAsync();
        _services.FileChanges.CheckAll();
        await _services.FileChanges.WhenIdle();
        Assert.DoesNotContain(_fs.Calls.Skip(calls), c => c.Contains(doc.FilePath!, StringComparison.OrdinalIgnoreCase));

        // Later the recorded timestamp/size match the disk: nothing to report, nothing re-read.
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        calls = _fs.Calls.Count;
        _watchers.Raise(new FileChange(FileChangeKind.Changed, doc.FilePath!));
        await SettleAsync();
        Assert.Contains(_fs.Calls.Skip(calls), c => c.StartsWith("GetLastWriteTimeUtc", StringComparison.Ordinal));
        Assert.DoesNotContain(_fs.Calls.Skip(calls), c => c.StartsWith("ReadAllBytes", StringComparison.Ordinal));
        Assert.Empty(doc.InfoBars);
        Assert.Equal("ab", doc.Buffer.Text);
        Assert.False(doc.IsModified);
        Assert.Equal(saved, _services.Status.Message);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-112")]
    public async Task IdenticalContentIsSilent()
    {
        var unmodified = await OpenAsync(@"C:\p\A.pas", "same");
        var modified = await OpenAsync(@"C:\p\B.pas", "same too");
        modified.Buffer.ApplyEdits([new TextEdit(0, 0, "edit ")]);

        // Rewritten with the same bytes (new timestamp): only the recorded timestamp changes.
        WriteExternally(unmodified.FilePath!, "same");
        WriteExternally(modified.FilePath!, "same too");
        await SettleAsync();
        Assert.Empty(unmodified.InfoBars);
        Assert.Empty(modified.InfoBars);
        Assert.Null(_services.Status.Message);
        Assert.Equal("same", unmodified.Buffer.Text);
        Assert.Equal("edit same too", modified.Buffer.Text);
        Assert.True(modified.Buffer.CanUndo);

        // The new timestamp was recorded: a re-check does not read the files again.
        var calls = _fs.Calls.Count;
        _services.FileChanges.CheckAll();
        await _services.FileChanges.WhenIdle();
        Assert.DoesNotContain(_fs.Calls.Skip(calls), c => c.StartsWith("ReadAllBytes", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-117")]
    [Trait("Req", "IDE-P1-110")]
    [Trait("Req", "IDE-P1-086")]
    public async Task ProjectFileChangePromptsReload()
    {
        const string dir = @"C:\p\Hello";
        const string project = dir + @"\Hello.pproj";
        static string Pproj(params string[] files) =>
            $$"""{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "files": [ {{string.Join(", ", files.Select(f => $$"""{ "path": "{{f}}" }"""))}} ] }""";
        File(project, Pproj("Hello.pas"));
        File(dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        File(dir + @"\Unit1.pas", "unit Unit1;\ninterface\nimplementation\nend.\n");
        File(dir + @"\Unit2.pas", "unit Unit2;\ninterface\nimplementation\nend.\n");
        _services.Settings.Set(SettingsCatalog.EnvironmentRestoreOpenFiles, false);
        Assert.True(await _services.Projects.OpenAsync(project));

        // The project file is watched by itself: its folder keeps a watcher with no document open there.
        Assert.True(_services.Documents.Close(_services.Documents.ActiveDocument!));
        Assert.Empty(_services.Documents.Documents);
        Assert.Equal([dir], _watchers.ActiveDirectories);
        string[] Files() => [.. _services.Projects.Project!.Files.Select(f => f.Path)];
        int Prompts() => _dialogs.Requests.Count(r => r.MessageId == "Msg.Project.ChangedOnDisk");

        // The IDE's own write of the project file (adding a unit) is not reported (IDE-P1-086).
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await _services.Projects.AddExistingFilesAsync([dir + @"\Unit1.pas"]));
        _watchers.Raise(new FileChange(FileChangeKind.Changed, project));
        await SettleAsync();
        _clock.Advance(TimeSpan.FromSeconds(1));
        _services.FileChanges.CheckAll();
        await _services.FileChanges.WhenIdle();
        Assert.Equal(0, Prompts());
        Assert.Equal(["Hello.pas", "Unit1.pas"], Files());

        // Changed by another program, reported by the watcher: the dialog; Keep records the new timestamp.
        WriteExternally(project, Pproj("Hello.pas", "Unit2.pas"));
        _dialogs.Enqueue("Msg.Project.ChangedOnDisk", "Keep");
        await SettleAsync();
        Assert.Equal(1, Prompts());
        Assert.Equal(["Hello.pas", "Unit1.pas"], Files()); // kept
        _watchers.Raise(new FileChange(FileChangeKind.Changed, project));
        await SettleAsync();
        _services.FileChanges.CheckAll();
        await _services.FileChanges.WhenIdle();
        Assert.Equal(1, Prompts()); // the same change is not reported again

        // A rewrite with identical bytes is silent.
        WriteExternally(project, Pproj("Hello.pas", "Unit2.pas"));
        await SettleAsync();
        Assert.Equal(1, Prompts());

        // A newer change found by the activation re-check: Reload re-reads the project; open documents stay open.
        var main = await OpenAsync(dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        main.Buffer.ApplyEdits([new TextEdit(0, 0, "{ mine }")]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _disk.WriteAllBytes(project, Encoding.UTF8.GetBytes(Pproj("Hello.pas", "Unit1.pas", "Unit2.pas")));
        _dialogs.Enqueue("Msg.Project.ChangedOnDisk", "Reload");
        _services.FileChanges.CheckAll();
        await _services.FileChanges.WhenIdle();
        Assert.Equal(2, Prompts());
        Assert.Equal(["Hello.pas", "Unit1.pas", "Unit2.pas"], Files());
        Assert.True(_services.Projects.IsMember(dir + @"\Unit2.pas"));
        Assert.Contains(main, _services.Documents.Documents);
        Assert.Equal("{ mine }program Hello;\nbegin\nend.\n", main.Buffer.Text);
        Assert.True(main.IsModified);
        Assert.Equal([dir], _watchers.ActiveDirectories);

        // After Close Project the project file is no longer watched (no document left in its folder).
        _dialogs.Answer("Msg.File.SaveChangesOne", "DontSave");
        Assert.True(await _services.Projects.CloseAsync());
        Assert.Empty(_watchers.ActiveDirectories);
        WriteExternally(project, Pproj("Hello.pas"));
        _services.FileChanges.CheckAll();
        await SettleAsync();
        Assert.Equal(2, Prompts());
    }
}
