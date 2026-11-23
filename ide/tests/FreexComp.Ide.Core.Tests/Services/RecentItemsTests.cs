using System.Text;
using System.Text.Json.Nodes;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Services;

/// <summary><c>IRecentItemsService</c> and <c>recent.json</c> (settings.md §6, phase1-shell.md §13).</summary>
[Trait("Phase", "1")]
public sealed class RecentItemsTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly RecordingLog _log = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 13, 9, 41, 7, 250, TimeSpan.Zero));
    private IdeServices _services;

    public RecentItemsTests()
    {
        _services = Build();
    }

    private IdeServices Build() => TestComposition.Build(o =>
    {
        o.FileSystem = _fs;
        o.Log = _log;
        o.Clock = _clock;
        o.UserDir = @"C:\user";
    });

    private IRecentItemsService Recent => _services.RecentItems;

    private FakeDialogService Dialogs => (FakeDialogService)_services.Dialogs;

    private const string RecentPath = @"C:\user\roaming\recent.json";

    private string File(string path, string text)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
        return path;
    }

    private JsonObject Saved() => JsonNode.Parse(Encoding.UTF8.GetString(_fs.ReadAllBytes(RecentPath)))!.AsObject();

    private static string Project(int n) => $@"C:\p\P{n}\P{n}.pproj";

    private void Tick() => _clock.Advance(TimeSpan.FromSeconds(1));

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-072")]
    [Trait("Req", "IDE-P1-130")]
    public void CapsAtTwentyAndKeepsPinned()
    {
        for (var n = 1; n <= 3; n++)
        {
            Recent.Add(RecentKind.Project, Project(n));
            Recent.SetPinned(RecentKind.Project, Project(n), true);
            Tick();
        }

        for (var n = 4; n <= 25; n++)
        {
            Recent.Add(RecentKind.Project, Project(n));
            Tick();
        }

        // 20 entries: the three pinned ones (the oldest) stay, unpinned ones are dropped oldest first.
        Assert.Equal(20, Recent.Projects.Count);
        Assert.Equal([Project(3), Project(2), Project(1)], Recent.Projects.Take(3).Select(i => i.Path));
        Assert.Equal(Enumerable.Range(9, 17).Reverse().Select(Project), Recent.Projects.Skip(3).Select(i => i.Path));
        Assert.Equal(20, Saved()["projects"]!.AsArray().Count);
        Assert.Empty(Recent.Files); // independent lists

        // 20 pinned entries: a newly opened unpinned item is not recorded; a pinned one moves.
        foreach (var item in Recent.Projects.ToList())
            Recent.SetPinned(RecentKind.Project, item.Path, true);
        Recent.Add(RecentKind.Project, Project(99));
        Assert.DoesNotContain(Recent.Projects, i => i.Path == Project(99));
        Tick();
        Recent.Add(RecentKind.Project, Project(1));
        Assert.Equal(Project(1), Recent.Projects[0].Path);
        Assert.Equal(20, Recent.Projects.Count);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-070")]
    [Trait("Req", "IDE-SET-071")]
    [Trait("Req", "IDE-SET-075")]
    public void OrderIsPinnedThenMostRecent()
    {
        Recent.Add(RecentKind.File, @"C:\w\a.pas");
        Tick();
        Recent.Add(RecentKind.File, @"C:\w\sub\..\b.pas");
        Tick();
        Recent.Add(RecentKind.File, @"C:\w\c.pas");
        Tick();
        Recent.SetPinned(RecentKind.File, @"C:\W\A.PAS", true);
        Assert.Equal([@"C:\w\a.pas", @"C:\w\c.pas", @"C:\w\b.pas"], Recent.Files.Select(i => i.Path));

        // Opening again moves to the front of its group (case-insensitive path, normalized).
        Recent.Add(RecentKind.File, @"c:\w\B.pas");
        Assert.Equal([@"C:\w\a.pas", @"c:\w\B.pas", @"C:\w\c.pas"], Recent.Files.Select(i => i.Path));

        // The file: same order, UTC seconds with Z, saved at once (no debounce), canonical layout.
        var text = Encoding.UTF8.GetString(_fs.ReadAllBytes(RecentPath));
        Assert.Equal(
            """
            {
              "formatVersion": 1,
              "projects": [],
              "files": [
                {
                  "path": "C:\\w\\a.pas",
                  "lastOpened": "2026-11-13T09:41:07Z",
                  "pinned": true
                },
                {
                  "path": "c:\\w\\B.pas",
                  "lastOpened": "2026-11-13T09:41:10Z",
                  "pinned": false
                },
                {
                  "path": "C:\\w\\c.pas",
                  "lastOpened": "2026-11-13T09:41:09Z",
                  "pinned": false
                }
              ]
            }

            """.ReplaceLineEndings("\n"), text);

        // A new instance reads the same lists.
        _services = Build();
        Assert.Equal([@"C:\w\a.pas", @"c:\w\B.pas", @"C:\w\c.pas"], Recent.Files.Select(i => i.Path));
        Assert.True(Recent.Files[0].Pinned);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 9, 41, 10, TimeSpan.Zero), Recent.Files[1].LastOpened);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-075")]
    public void MergesEntriesOfAnotherInstanceAndToleratesGarbage()
    {
        Recent.Add(RecentKind.Project, Project(1));
        Tick();

        // Another instance wrote P2 (newer) and P1 (older than ours) meanwhile.
        File(RecentPath, $$"""
            { "formatVersion": 1,
              "projects": [ { "path": "{{Project(2).Replace(@"\", @"\\", StringComparison.Ordinal)}}", "lastOpened": "2026-11-13T10:00:00Z", "pinned": true },
                            { "path": "{{Project(1).Replace(@"\", @"\\", StringComparison.Ordinal)}}", "lastOpened": "2026-01-01T00:00:00Z", "pinned": true } ],
              "files": [] }
            """);
        Recent.Add(RecentKind.Project, Project(3));

        Assert.Equal([Project(2), Project(3), Project(1)], Recent.Projects.Select(i => i.Path));
        Assert.False(Recent.Projects[2].Pinned); // ours is newer
        Assert.Equal(3, Saved()["projects"]!.AsArray().Count);

        // Unreadable file: empty lists, a warning, overwritten at the next save.
        File(RecentPath, "{ nope");
        _services = Build();
        Assert.Empty(Recent.Projects);
        Assert.Contains(_log.Lines, l => l.Contains("WARN", StringComparison.Ordinal) && l.Contains("recent.json", StringComparison.Ordinal));
        Recent.Add(RecentKind.File, @"C:\w\a.pas");
        Assert.Single(Saved()["files"]!.AsArray());
        Assert.Empty(Dialogs.Requests);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-074")]
    [Trait("Req", "IDE-P1-133")]
    public Task ClearRemovesUnpinnedEntriesOfBothLists() => TestUiThread.RunAsync(() => ClearRemovesUnpinnedEntriesOfBothListsOnUiThread()); // I-209

    private async Task ClearRemovesUnpinnedEntriesOfBothListsOnUiThread()
    {
        Assert.False(_services.Commands.CanExecute("file.clearRecent"));
        Recent.Add(RecentKind.Project, Project(1));
        Recent.Add(RecentKind.Project, Project(2));
        Recent.SetPinned(RecentKind.Project, Project(2), true);
        Recent.Add(RecentKind.File, @"C:\w\a.pas");
        var changes = 0;
        Recent.Changed += (_, _) => changes++;
        Assert.True(_services.Commands.CanExecute("file.clearRecent"));

        await _services.Commands.ExecuteAsync("file.clearRecent");

        Assert.Equal([Project(2)], Recent.Projects.Select(i => i.Path));
        Assert.Empty(Recent.Files);
        Assert.Equal(1, changes);
        Assert.Empty(Dialogs.Requests); // without confirmation
        Assert.False(_services.Commands.CanExecute("file.clearRecent")); // only pinned left
        Assert.Single(Saved()["projects"]!.AsArray());

        // A removed entry does not come back through the merge.
        Recent.Add(RecentKind.File, @"C:\w\b.pas");
        Assert.Equal([@"C:\w\b.pas"], Recent.Files.Select(i => i.Path));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-130")]
    [Trait("Req", "IDE-P1-132")]
    [Trait("Req", "IDE-SET-073")]
    public Task OpeningRecordsEntriesAndMissingOnesAskToRemove() => TestUiThread.RunAsync(() => OpeningRecordsEntriesAndMissingOnesAskToRemoveOnUiThread()); // I-209

    private async Task OpeningRecordsEntriesAndMissingOnesAskToRemoveOnUiThread()
    {
        const string dir = @"C:\p\Hello";
        File(dir + @"\Hello.pproj", """{ "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas" }""");
        File(dir + @"\Hello.pas", "program Hello;\nbegin\nend.\n");
        var loose = File(@"C:\w\Loose.pas", "unit Loose;\n");

        // A project and a non-member file are recorded; the project's own files are not.
        Assert.True(await _services.Projects.OpenAsync(dir + @"\Hello.pproj"));
        Assert.True((await _services.Documents.OpenAsync(loose)).Success);
        Assert.Equal([dir + @"\Hello.pproj"], Recent.Projects.Select(i => i.Path));
        Assert.Equal([loose], Recent.Files.Select(i => i.Path));
        Assert.True(await _services.Projects.CloseAsync());

        // Activating entries.
        foreach (var document in _services.Documents.Documents.ToList())
            _services.Documents.Close(document);
        await _services.Commands.ExecuteAsync("file.openRecentFile", loose);
        Assert.Equal(loose, _services.Documents.ActiveDocument!.FilePath);
        await _services.Commands.ExecuteAsync("file.openRecentProject", dir + @"\Hello.pproj");
        Assert.Equal("Hello", _services.Projects.Project!.Name);

        // Missing: Keep leaves the entry, Remove deletes it.
        Recent.Add(RecentKind.File, @"C:\w\Gone.pas");
        Dialogs.Enqueue("Msg.File.RecentNotFound", "Keep", "Remove");
        await _services.Commands.ExecuteAsync("file.openRecentFile", @"C:\w\Gone.pas");
        Assert.Contains(Recent.Files, i => i.Path == @"C:\w\Gone.pas");
        await _services.Commands.ExecuteAsync("file.openRecentFile", @"C:\w\Gone.pas");
        Assert.DoesNotContain(Recent.Files, i => i.Path == @"C:\w\Gone.pas");
        Assert.Equal([@"C:\w\Gone.pas", @"C:\w\Gone.pas"], Dialogs.Requests.Select(r => r.Args[0]));
        Assert.False(_services.Commands.CanExecute("file.openRecentFile", null));
    }
}
