using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>Find in Files (I-122, editor-features.md §12).</summary>
[Trait("Phase", "3")]
public sealed class FindInFilesTests
{
    private const string Dir = @"C:\p\Hello";

    private readonly InMemoryFileSystem _fs = new();
    private readonly ManualClock _clock = new();
    private readonly IdeServices _services;

    public FindInFilesTests()
    {
        _services = TestComposition.Build(o =>
        {
            o.FileSystem = _fs;
            o.Clock = _clock;
        });
    }

    private FindInFilesService Service(int maxParallelism = 0) =>
        new(_services.Documents, _services.Projects, _fs, _services.Settings, _clock, maxParallelism);

    private void File(string path, string text) => Bytes(path, Encoding.UTF8.GetBytes(text));

    private void Bytes(string path, byte[] bytes)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        _fs.WriteAllBytes(path, bytes);
    }

    private static FindInFilesRequest Request(string text, FindInFilesScope scope, string? folder = null, string masks = FindInFilesRequest.DefaultMasks) =>
        new(new FindQuery(text, MatchCase: false, WholeWord: false, Regex: false), scope, folder, Subfolders: true, masks);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-120")]
    [Trait("Req", "IDE-ED-121")]
    [Trait("Req", "IDE-ED-122")]
    public Task ScopesUseBuffersForOpenFiles() => TestUiThread.RunAsync(() => ScopesUseBuffersForOpenFilesOnUiThread()); // I-209

    private async Task ScopesUseBuffersForOpenFilesOnUiThread()
    {
        File(Dir + @"\Hello.pproj", """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas",
              "files": [ { "path": "Hello.pas" }, { "path": "Unit1.pas" }, { "path": "sub/Unit2.pas" }, { "path": "Gone.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\n  Needle;\nend.\n");
        File(Dir + @"\Unit1.pas", "unit Unit1;\ninterface\nimplementation\nend.\n");
        File(Dir + @"\sub\Unit2.pas", "unit Unit2; // needle needle\r\nend.\r\n");
        File(Dir + @"\Loose.pas", "needle outside the project\n");
        File(Dir + @"\notes.txt", "needle in a text file\n");
        File(Dir + @"\obj\Debug\Gen.pas", "needle in obj\n");
        File(Dir + @"\bin\X.inc", "needle in bin\n");
        File(Dir + @"\.freexcomp\S.pas", "needle in session\n");
        Bytes(Dir + @"\Bin.pas", [.. Encoding.UTF8.GetBytes("needle"), 0, 1, 2]); // binary (IDE-P1-072)
        Assert.True(await _services.Projects.OpenAsync(Dir + @"\Hello.pproj"));

        // Unit1 is open with an unsaved needle: the buffer is searched, not the disk.
        var unit1 = Assert.IsType<TextDocumentViewModel>((await _services.Documents.OpenAsync(Dir + @"\Unit1.pas")).Value);
        unit1.Buffer.ApplyEdits([new TextEdit(unit1.Buffer.GetOffset(3, 1), 0, "{needle}")]);

        var project = await Service().SearchAsync(Request("needle", FindInFilesScope.Project), null, CancellationToken.None);
        Assert.Equal(
            [
                (Dir + @"\Hello.pas", 3, 3, "  Needle;"),
                (Dir + @"\Unit1.pas", 3, 2, "{needle}implementation"),
                (Dir + @"\sub\Unit2.pas", 1, 16, "unit Unit2; // needle needle"),
                (Dir + @"\sub\Unit2.pas", 1, 23, "unit Unit2; // needle needle"),
            ],
            project.Matches.Select(m => (m.Path, m.Line, m.Column, m.LineText)));
        Assert.All(project.Matches, m => Assert.Equal(6, m.Length));
        Assert.Equal(3, project.FileCount);
        Assert.False(project.IsCapped);

        // Open files: the open documents only (Hello.pas is open as the main source).
        var open = await Service().SearchAsync(Request("needle", FindInFilesScope.OpenFiles), null, CancellationToken.None);
        Assert.Equal([Dir + @"\Hello.pas", Dir + @"\Unit1.pas"], open.Matches.Select(m => m.Path));

        // Folder: masks, recursion, skipped folders, binary files; open files still come from their buffers.
        var folder = await Service().SearchAsync(Request("needle", FindInFilesScope.Folder, Dir), null, CancellationToken.None);
        Assert.Equal(
            [Dir + @"\Hello.pas", Dir + @"\Loose.pas", Dir + @"\sub\Unit2.pas", Dir + @"\sub\Unit2.pas", Dir + @"\Unit1.pas"],
            folder.Matches.Select(m => m.Path));
        var texts = await Service().SearchAsync(Request("needle", FindInFilesScope.Folder, Dir, "*.txt"), null, CancellationToken.None);
        Assert.Equal([Dir + @"\notes.txt"], texts.Matches.Select(m => m.Path));
        var flat = await Service().SearchAsync(Request("needle", FindInFilesScope.Folder, Dir) with { Subfolders = false }, null, CancellationToken.None);
        Assert.DoesNotContain(flat.Matches, m => m.Path.Contains(@"\sub\", StringComparison.Ordinal));

        // Files over files.largeFileThresholdMb are skipped.
        _services.Settings.Set(Core.Settings.SettingsCatalog.FilesLargeFileThresholdMb, 1);
        File(Dir + @"\Big.pas", "needle\n" + new string('x', 1024 * 1024 + 10));
        var large = await Service().SearchAsync(Request("needle", FindInFilesScope.Folder, Dir), null, CancellationToken.None);
        Assert.DoesNotContain(large.Matches, m => m.Path.EndsWith("Big.pas", StringComparison.Ordinal));

        // Matching follows the in-editor find (whole word, match case, regex line by line).
        var whole = await Service().SearchAsync(new FindInFilesRequest(new FindQuery("Needle", true, true, false), FindInFilesScope.Project), null, CancellationToken.None);
        Assert.Equal([(Dir + @"\Hello.pas", 3)], whole.Matches.Select(m => (m.Path, m.Line)));
        var regex = await Service().SearchAsync(new FindInFilesRequest(new FindQuery(@"end\.\s*$", false, false, true), FindInFilesScope.OpenFiles), null, CancellationToken.None);
        Assert.Equal([4, 4], regex.Matches.Select(m => m.Line));
        var bad = await Service().SearchAsync(new FindInFilesRequest(new FindQuery("(", false, false, true), FindInFilesScope.OpenFiles), null, CancellationToken.None);
        Assert.NotNull(bad.Error);
        Assert.Empty(bad.Matches);
    }

    /// <summary>IDE-ED-120 (I-187): files with the hidden or system attribute are not read from disk in any scope.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-120")]
    public Task HiddenAndSystemFilesAreSkipped() => TestUiThread.RunAsync(() => HiddenAndSystemFilesAreSkippedOnUiThread()); // I-209

    private async Task HiddenAndSystemFilesAreSkippedOnUiThread()
    {
        File(Dir + @"\Hello.pproj", """
            { "formatVersion": 1, "name": "Hello", "mainSource": "Hello.pas", "files": [ { "path": "Hidden.pas" }, { "path": "System.pas" } ] }
            """);
        File(Dir + @"\Hello.pas", "program Hello;\nbegin\n  Needle;\nend.\n");
        File(Dir + @"\Hidden.pas", "needle hidden\n");
        File(Dir + @"\System.pas", "needle system\n");
        File(Dir + @"\sub\Deep.pas", "needle hidden too\n");
        _fs.SetAttributes(Dir + @"\Hidden.pas", FileAttributes.Hidden);
        _fs.SetAttributes(Dir + @"\System.pas", FileAttributes.System | FileAttributes.ReadOnly);
        _fs.SetAttributes(Dir + @"\sub\Deep.pas", FileAttributes.Hidden | FileAttributes.Archive);
        Assert.Equal(FileAttributes.Normal, _fs.GetAttributes(Dir + @"\Hello.pas"));
        Assert.True(await _services.Projects.OpenAsync(Dir + @"\Hello.pproj"));

        var project = await Service().SearchAsync(Request("needle", FindInFilesScope.Project), null, CancellationToken.None);
        Assert.Equal([Dir + @"\Hello.pas"], project.Matches.Select(m => m.Path));
        var folder = await Service().SearchAsync(Request("needle", FindInFilesScope.Folder, Dir), null, CancellationToken.None);
        Assert.Equal([Dir + @"\Hello.pas"], folder.Matches.Select(m => m.Path));
        Assert.Equal(1, folder.FileCount);

        // The physical file system reports the real attributes.
        var temp = Path.Combine(Path.GetTempPath(), "fxc-i187-" + Guid.NewGuid().ToString("N") + ".pas");
        System.IO.File.WriteAllText(temp, "x");
        try
        {
            var physical = new Core.Platform.PhysicalFileSystem();
            Assert.False(physical.GetAttributes(temp).HasFlag(FileAttributes.Hidden));
            System.IO.File.SetAttributes(temp, FileAttributes.Hidden);
            Assert.True(physical.GetAttributes(temp).HasFlag(FileAttributes.Hidden));
        }
        finally
        {
            System.IO.File.SetAttributes(temp, FileAttributes.Normal);
            System.IO.File.Delete(temp);
        }
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-ED-123")]
    [Trait("Req", "IDE-ED-124")]
    public Task OrderCapAndCancel() => TestUiThread.RunAsync(() => OrderCapAndCancelOnUiThread()); // I-209

    private async Task OrderCapAndCancelOnUiThread()
    {
        // 40 files × 200 matches: 8 000 matches, capped at 5 000 in file order (ordinal-ignore-case path order).
        var names = Enumerable.Range(0, 40).Select(i => (i % 2 == 0 ? "a" : "B") + i.ToString("D2", CultureInfo.InvariantCulture) + ".pas").ToList();
        foreach (var name in names)
            File(Path.Combine(Dir, name), string.Concat(Enumerable.Repeat("x needle\n", 200)));
        var expectedOrder = names.Order(StringComparer.OrdinalIgnoreCase).Select(n => Path.Combine(Dir, n)).ToList();

        // Parallel workers, no time passes: the result is in file order and capped; one final batch holds it all.
        var all = new List<IReadOnlyList<FileMatch>>();
        var result = await Service().SearchAsync(Request("needle", FindInFilesScope.Folder, Dir), new SyncProgress(all.Add), CancellationToken.None);
        Assert.True(result.IsCapped);
        Assert.False(result.IsCanceled);
        Assert.Equal(FindInFilesService.MatchCap, result.Matches.Count);
        Assert.Equal(expectedOrder.Take(25), result.Matches.Select(m => m.Path).Distinct());
        Assert.Equal(Enumerable.Range(1, 200), result.Matches.Take(200).Select(m => m.Line));
        Assert.Equal(result.Matches, all.SelectMany(b => b));

        // I-189: one worker and 100 ms pass on every file read (the gate), so each batch is exactly the file finished
        // before it (IDE-ED-123: batches every 100 ms) — streamed in order, nothing twice, the last one at the end.
        _fs.BeforeRead = _ => _clock.Advance(TimeSpan.FromMilliseconds(100));
        var batches = new List<IReadOnlyList<FileMatch>>();
        var streamed = await Service(maxParallelism: 1).SearchAsync(Request("needle", FindInFilesScope.Folder, Dir), new SyncProgress(batches.Add),
            CancellationToken.None);
        Assert.True(streamed.IsCapped);
        Assert.Equal(result.Matches, streamed.Matches);
        Assert.Equal(Enumerable.Repeat(200, 25), batches.Select(b => b.Count));
        Assert.Equal(expectedOrder.Take(25), batches.Select(b => b[0].Path));
        Assert.Equal(result.Matches, batches.SelectMany(b => b));

        // Canceled before it starts: no matches, reported as canceled.
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var none = await Service().SearchAsync(Request("needle", FindInFilesScope.Folder, Dir), null, canceled.Token);
        Assert.True(none.IsCanceled);
        Assert.Empty(none.Matches);

        // Canceled while running (from the first batch, reported when the second file is read): the batches stop and
        // the result holds exactly what was reported.
        using var stop = new CancellationTokenSource();
        var reports = new List<IReadOnlyList<FileMatch>>();
        var partial = await Service(maxParallelism: 1).SearchAsync(Request("needle", FindInFilesScope.Folder, Dir), new SyncProgress(b =>
        {
            reports.Add(b);
            stop.Cancel();
        }), stop.Token);
        Assert.True(partial.IsCanceled);
        Assert.False(partial.IsCapped);
        Assert.Single(reports);
        Assert.Equal(result.Matches.Take(200), partial.Matches);
        Assert.Equal(reports[0], partial.Matches);
    }

    /// <summary>An <see cref="IProgress{T}"/> that reports on the calling thread (no synchronization context).</summary>
    private sealed class SyncProgress(Action<IReadOnlyList<FileMatch>> report) : IProgress<IReadOnlyList<FileMatch>>
    {
        public void Report(IReadOnlyList<FileMatch> value) => report(value);
    }
}
