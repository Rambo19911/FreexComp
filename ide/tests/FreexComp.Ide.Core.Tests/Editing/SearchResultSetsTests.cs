using System.Collections.Concurrent;
using System.Globalization;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Editing;

/// <summary>
/// The result sets of Search Results (I-354, F13-016a; tool-windows.md §8, UI-TW-615…618): one current set and up to
/// ten pinned sets of either kind — <c>searchResults.pin</c>, a new search replaces only the current set, a repeat runs
/// in place, every set keeps its own state.
/// </summary>
[Trait("Phase", "8")]
public sealed class SearchResultSetsTests
{
    private const string Pin = "searchResults.pin";
    private const string Dir = BuildTestBed.Dir;
    private const string Unit2Path = Dir + @"\Unit2.pas";
    private const string Kept = @"C:\x\Kept";
    private const string Live = @"C:\x\Live";
    private const string Other = @"C:\x\Other";
    private const int LiveFiles = 12;

    private const string Main = "program Hello;\nuses Unit1, Unit2;\nvar\n  C: TCounter;\nbegin\n  C := TCounter.Create;\n  C.Step;\n  Twice(C);\n  Writeln(C.Count);\nend.\n";

    private const string Unit1 =
        "unit Unit1;\ninterface\ntype\n  TCounter = class\n  public\n    Count: Integer;\n    procedure Step;\n  end;\nimplementation\n"
        + "procedure TCounter.Step;\nbegin\n  Count := Count + 1;\nend;\nend.\n";

    private const string Unit2 =
        "unit Unit2;\ninterface\nuses Unit1;\nprocedure Twice(C: TCounter);\nimplementation\nprocedure Twice(C: TCounter);\nbegin\n"
        + "  C.Step;\n  C.Step;\n  if C.Count > 3 then\n    C.Count := 0;\nend;\nend.\n";

    /// <summary>
    /// The project Hello (program, Unit1, Unit2) over the real system unit, and three folders beside it for Find in
    /// Files searches whose reads a test can hold: <c>Kept</c> (one file), <c>Live</c> (twelve), <c>Other</c> (one).
    /// </summary>
    private static async Task<BuildTestBed> StartAsync()
    {
        var bed = new BuildTestBed();
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(File.Exists))
            bed.File(@"G:\rtl\" + Path.GetFileName(file), File.ReadAllText(file));
        bed.File(BuildTestBed.ProjectPath, "{ \"formatVersion\": 1, \"name\": \"Hello\", \"appType\": \"console\", \"mainSource\": \"Hello.pas\",\n"
            + "  \"files\": [ { \"path\": \"Hello.pas\" }, { \"path\": \"Unit1.pas\" }, { \"path\": \"Unit2.pas\" } ] }");
        bed.File(BuildTestBed.MainPath, Main);
        bed.File(BuildTestBed.UnitPath, Unit1);
        bed.File(Unit2Path, Unit2);
        bed.File(Kept + @"\a.pas", "needle\n");
        for (var i = 1; i <= LiveFiles; i++)
            bed.File(Live + @"\f" + i.ToString("00", CultureInfo.InvariantCulture) + ".pas", "needle\n");
        bed.File(Other + @"\o.pas", "needle\n");
        Assert.True(await bed.Services.Projects.OpenAsync(BuildTestBed.ProjectPath));
        Assert.True(await bed.Services.Documents.CloseAllAsync());
        return bed;
    }

    private static FindInFilesRequest InProject(string text) => new(new FindQuery(text, MatchCase: false, WholeWord: false, Regex: false), FindInFilesScope.Project);

    private static FindInFilesRequest InFolder(string folder) =>
        new(new FindQuery("needle", MatchCase: false, WholeWord: false, Regex: false), FindInFilesScope.Folder, folder);

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>The header of a Find in Files set over a folder with <paramref name="matches"/> matches in one file.</summary>
    private static string FolderHeader(string folder, int matches) => Format(Strings.SearchResults_Header, "needle", folder,
        matches == 1 ? Strings.Msg_FindInFiles_DoneOne : Format(Strings.Msg_FindInFiles_DoneMany, matches, 1));

    /// <summary>The offset of the <paramref name="occurrence"/>-th <paramref name="text"/> that is not part of a longer word.</summary>
    private static int Offset(TextDocumentViewModel doc, string text, int occurrence = 1)
    {
        var all = doc.Buffer.Text;
        static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_';
        var offset = -1;
        for (var found = 0; found < occurrence;)
        {
            offset = all.IndexOf(text, offset + 1, StringComparison.Ordinal);
            Assert.True(offset >= 0, text);
            var before = offset > 0 && Word(text[0]) && Word(all[offset - 1]);
            var after = offset + text.Length < all.Length && Word(text[^1]) && Word(all[offset + text.Length]);
            if (!before && !after)
                found++;
        }

        return offset;
    }

    /// <summary>Puts the caret into <paramref name="text"/> and runs the command of Shift+F12 (the query is not awaited).</summary>
    private static async Task StartFindAsync(BuildTestBed bed, TextDocumentViewModel doc, string text)
    {
        bed.Services.Documents.Activate(doc);
        doc.Selection = TextSelection.At(Offset(doc, text) + 1);
        Assert.True(bed.Services.Commands.CanExecute("search.findReferences"));
        await bed.Services.Commands.ExecuteAsync("search.findReferences");
    }

    private static async Task FindAsync(BuildTestBed bed, TextDocumentViewModel doc, string text)
    {
        await StartFindAsync(bed, doc, text);
        await bed.Services.FindReferences.Running;
    }

    /// <summary>One entry per tab of the set strip: title, <c>pinned</c> or <c>current</c>, and <c>shown</c> for the shown one.</summary>
    private static List<string> Strip(SearchResultsViewModel results) =>
        [.. results.Sets.Select(s => $"{s.Number} {s.Title} {(s.IsPinned ? "pinned" : "current")}{(s.IsShown ? " shown" : "")}")];

    private static List<string> Keys(SearchResultsViewModel results) => [.. results.Rows.Select(r => r.Key)];

    private static SearchResultRow Row(SearchResultsViewModel results, string key) => results.Rows.Single(r => r.Key == key);

    [Fact(Timeout = 60000)]
    [Trait("Req", "UI-TW-615")]
    [Trait("Req", "UI-TW-616")]
    public Task TenPinnedSetsLimit() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var results = bed.Services.SearchResults;
        var commands = bed.Services.Commands;
        bool CanPin() => commands.CanExecute(Pin);

        // Nothing was searched: no set, nothing to keep.
        Assert.True(results.IsEmpty);
        Assert.Empty(results.Sets);
        Assert.False(results.HasSetStrip);
        Assert.False(CanPin());

        // While the search of the current set runs (its read is held) it cannot be kept; when it has ended it can.
        using (var gate = new ManualResetEventSlim())
        {
            bed.Fs.BeforeRead = path =>
            {
                if (path.StartsWith(Kept, StringComparison.OrdinalIgnoreCase))
                    gate.Wait();
            };
            var running = results.RunAsync(InFolder(Kept));
            Assert.True(results.IsSearching);
            Assert.Equal(["1 \"needle\" current shown"], Strip(results));
            Assert.False(CanPin());
            gate.Set();
            await running;
            bed.Fs.BeforeRead = null;
        }

        Assert.False(results.IsSearching);
        Assert.True(results.HasMatches);
        Assert.False(results.HasSetStrip); // one unpinned set: no strip
        Assert.True(CanPin());

        // A set without rows cannot be kept.
        await results.RunAsync(InProject("Zork"));
        Assert.True(results.IsNoMatch);
        Assert.Equal(["1 \"Zork\" current shown"], Strip(results));
        Assert.False(CanPin());

        // Ten sets are kept, each from the current set; a pinned set cannot be pinned again (pinning is one way).
        for (var i = 1; i <= SearchResultsViewModel.MaxPinnedSets; i++)
        {
            await results.RunAsync(InProject("Step"));
            Assert.True(CanPin(), "set " + i);
            Assert.Equal(i, results.Sets.Count);
            Assert.False(results.Sets[^1].IsPinned);
            await commands.ExecuteAsync(Pin);
            Assert.True(results.HasSetStrip);
            Assert.Equal(i, results.Sets.Count);
            Assert.All(results.Sets, s => Assert.True(s.IsPinned));
            Assert.True(results.Sets[^1].IsShown); // the shown set stays the shown one
            Assert.False(CanPin(), "the pinned set " + i);
        }

        Assert.Equal(10, SearchResultsViewModel.MaxPinnedSets);

        // The eleventh: a finished current set with rows beside ten pinned ones cannot be kept.
        await results.RunAsync(InProject("Twice"));
        Assert.Equal(11, results.Sets.Count);
        Assert.Equal("11 \"Twice\" current shown", Strip(results)[^1]);
        Assert.True(results.HasMatches);
        Assert.False(CanPin());
        await commands.ExecuteAsync(Pin); // the disabled command changes nothing
        Assert.False(results.Sets[^1].IsPinned);

        // One pinned set is closed: now the current set can be kept, and the limit is reached again.
        results.CloseSet(3);
        Assert.Equal(10, results.Sets.Count);
        Assert.Equal("10 \"Twice\" current shown", Strip(results)[^1]);
        Assert.True(CanPin());
        await commands.ExecuteAsync(Pin);
        Assert.All(results.Sets, s => Assert.True(s.IsPinned));
        Assert.False(CanPin());
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "UI-TW-615")]
    [Trait("Req", "UI-TW-617")]
    [Trait("Req", "IDE-FREF-008")]
    public Task RepeatOnPinnedSetStaysPinned() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var results = bed.Services.SearchResults;
        var commands = bed.Services.Commands;
        var references = bed.Services.FindReferences;
        var unit2 = await bed.OpenDocumentAsync(Unit2Path);

        // Two kept sets — one of each kind — and a current one, in creation order.
        await results.RunAsync(InProject("Step"));
        await commands.ExecuteAsync(Pin);
        await FindAsync(bed, unit2, "Count");
        await commands.ExecuteAsync(Pin);
        await results.RunAsync(InProject("Twice"));
        Assert.Equal(["1 \"Step\" pinned", "2 Count pinned", "3 \"Twice\" current shown"], Strip(results));
        Assert.Equal(["find-in-files", "find", "find-in-files"], results.Sets.Select(s => s.IconName));
        Assert.Equal(
        [
            "Find \"Step\" in project — 5 matches in 3 files", "References to Count — 6 references in 3 files",
            "Find \"Twice\" in project — 3 matches in 2 files",
        ], results.Sets.Select(s => s.Tooltip));

        // An edit in the open unit: one more call of Step and one more write of Count.
        BuildTestBed.Type(unit2, "  C.Step;\n  C.Count := 1;\n", Offset(unit2, "  C.Step;"));

        // Repeat on the kept Find in Files set: its query again, in place — it stays pinned, where it was, and shown.
        results.ShowSet(1);
        Assert.Equal("Find \"Step\" in project — 5 matches in 3 files", results.Header);
        Assert.True(commands.CanExecute("searchResults.repeat"));
        await commands.ExecuteAsync("searchResults.repeat");
        await results.Running;
        Assert.Equal("Find \"Step\" in project — 6 matches in 3 files", results.Header);
        Assert.Equal(["1 \"Step\" pinned shown", "2 Count pinned", "3 \"Twice\" current"], Strip(results));
        Assert.False(commands.CanExecute(Pin));

        // Repeat on the kept References set: the same.
        results.ShowSet(2);
        Assert.Equal("References to Count — 6 references in 3 files", results.Header);
        await commands.ExecuteAsync("searchResults.repeat");
        await references.Running;
        Assert.Equal("References to Count — 7 references in 3 files", results.Header);
        Assert.Equal(["1 \"Step\" pinned", "2 Count pinned shown", "3 \"Twice\" current"], Strip(results));
        Assert.Equal("Unit2.pas:12:8", results.CurrentRow?.Key); // the start identifier, two lines further down

        // No other set was touched by either repeat.
        Assert.Equal("Find \"Step\" in project — 6 matches in 3 files", results.Sets[0].Tooltip);
        Assert.Equal("Find \"Twice\" in project — 3 matches in 2 files", results.Sets[2].Tooltip);

        // A repeat that ends while another set is shown: its set is still replaced in place, the shown set stays.
        using (var gate = new ManualResetEventSlim())
        {
            var blocker = bed.Services.CodeModel.Worker.RunAsync("test-block", _ =>
            {
                gate.Wait();
                return 0;
            });
            BuildTestBed.Type(unit2, "  C.Count := 2;\n", Offset(unit2, "  C.Step;"));
            await commands.ExecuteAsync("searchResults.repeat");
            Assert.True(results.IsSearching);
            Assert.True(commands.CanExecute("searchResults.cancel"));
            Assert.Equal("References to Count — " + Strings.SearchResults_Searching, results.Sets[1].Tooltip);
            results.ShowSet(3);
            Assert.False(results.IsSearching); // the shown set is not the one that is searched
            Assert.False(commands.CanExecute("searchResults.cancel")); // cancel acts on the shown set
            var twice = results.Rows;
            gate.Set();
            await blocker;
            await references.Running;
            Assert.Equal(["1 \"Step\" pinned", "2 Count pinned", "3 \"Twice\" current shown"], Strip(results));
            Assert.Same(twice, results.Rows);
            Assert.Equal("References to Count — 8 references in 3 files", results.Sets[1].Tooltip);
            results.ShowSet(2);
            Assert.Equal("References to Count — 8 references in 3 files", results.Header);
            Assert.Equal(8, results.Rows.Count(r => r.ReferenceKind is not null));
        }

        // Repeat on the current set: it stays the last one, unpinned.
        results.ShowSet(3);
        await commands.ExecuteAsync("searchResults.repeat");
        await results.Running;
        Assert.Equal(["1 \"Step\" pinned", "2 Count pinned", "3 \"Twice\" current shown"], Strip(results));
        Assert.Equal("Find \"Twice\" in project — 3 matches in 2 files", results.Header);
        Assert.True(commands.CanExecute(Pin));
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "UI-TW-617")]
    public Task NewSearchCancelsOnlyTheReplacedSet() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var results = bed.Services.SearchResults;
        var commands = bed.Services.Commands;
        var references = bed.Services.FindReferences;
        var worker = bed.Services.CodeModel.Worker;

        // The reads of the folders Kept and Live wait at this gate; every read is counted.
        var reads = new ConcurrentBag<string>();
        var liveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gate = new ManualResetEventSlim(initialState: true);
        bed.Fs.BeforeRead = path =>
        {
            var live = path.StartsWith(Live, StringComparison.OrdinalIgnoreCase);
            if (!live && !path.StartsWith(Kept, StringComparison.OrdinalIgnoreCase))
                return;
            reads.Add(path);
            if (live)
                liveStarted.TrySetResult();
            gate.Wait();
        };

        // A kept Find in Files set; its repeat is held at the read of its file, which has a second match now.
        await results.RunAsync(InFolder(Kept));
        await commands.ExecuteAsync(Pin);
        Assert.Equal(FolderHeader(Kept, 1), results.Header);
        bed.File(Kept + @"\a.pas", "needle\nneedle\n");
        gate.Reset();
        await commands.ExecuteAsync("searchResults.repeat");
        var keptRepeat = results.Running;
        Assert.True(results.IsSearching);
        Assert.False(keptRepeat.IsCompleted);

        // A new search becomes the current set and is shown; its first reads are held too.
        var live = results.RunAsync(InFolder(Live));
        Assert.Equal(["1 \"needle\" pinned", "2 \"needle\" current shown"], Strip(results));
        Assert.True(results.IsSearching);
        await liveStarted.Task;

        // Another new search replaces the current set: the running search of that set is cancelled — the repeat of the
        // kept set is not.
        await results.RunAsync(InFolder(Other));
        Assert.Equal(["1 \"needle\" pinned", "2 \"needle\" current shown"], Strip(results));
        Assert.Equal(FolderHeader(Other, 1), results.Header);
        Assert.False(results.IsSearching);
        Assert.Contains(Strings.SearchResults_Searching, results.Sets[0].Tooltip, StringComparison.Ordinal);
        Assert.False(keptRepeat.IsCompleted);
        Assert.False(live.IsCompleted);

        gate.Set();
        await live;
        await keptRepeat;

        // The replaced search stopped at the reads it was held in: the other files of its folder were never read.
        Assert.InRange(reads.Count(p => p.StartsWith(Live, StringComparison.OrdinalIgnoreCase)), 1, 4);
        Assert.True(LiveFiles > 4);

        // The repeat of the kept set ran to its end: in place, with the second match, and it says so.
        Assert.Equal(FolderHeader(Kept, 2), results.Sets[0].Tooltip);
        Assert.Equal(Format(Strings.Msg_FindInFiles_DoneMany, 2, 1), bed.Services.Status.Message);
        Assert.Equal(["1 \"needle\" pinned", "2 \"needle\" current shown"], Strip(results));
        Assert.Equal(FolderHeader(Other, 1), results.Header);
        bed.Fs.BeforeRead = null;

        // The same for reference queries. A kept References set, and the worker busy with something else: its repeat waits.
        var unit2 = await bed.OpenDocumentAsync(Unit2Path);
        await FindAsync(bed, unit2, "Count");
        await commands.ExecuteAsync(Pin);
        Assert.Equal(["1 \"needle\" pinned", "2 Count pinned shown"], Strip(results));
        using (var busy = new ManualResetEventSlim())
        {
            var blocker = worker.RunAsync("test-block", _ =>
            {
                busy.Wait();
                return 0;
            });
            BuildTestBed.Type(unit2, "  C.Count := 1;\n", Offset(unit2, "  C.Step;"));
            await commands.ExecuteAsync("searchResults.repeat");
            var keptQuery = references.Running;
            Assert.True(results.IsSearching);

            // A new reference query waits behind it; then a new Find in Files search runs. Neither cancels the repeat.
            await StartFindAsync(bed, unit2, "Step");
            var newer = references.Running;
            Assert.False(keptQuery.IsCompleted);
            await results.RunAsync(InFolder(Other));
            await newer; // replaced by the Find in Files search: it ends without a result and without a message
            Assert.Equal(["1 \"needle\" pinned", "2 Count pinned", "3 \"needle\" current shown"], Strip(results));
            Assert.False(keptQuery.IsCompleted);
            Assert.True(references.IsSearching);
            Assert.Equal("References to Count — " + Strings.SearchResults_Searching, results.Sets[1].Tooltip);

            busy.Set();
            await blocker;
            await keptQuery;
            Assert.False(references.IsSearching);
            Assert.Equal("References to Count — 7 references in 3 files", results.Sets[1].Tooltip);
            Assert.Equal(["1 \"needle\" pinned", "2 Count pinned", "3 \"needle\" current shown"], Strip(results));
        }

        // The repeat of the current set, by contrast, is cancelled by a new search: the set is replaced.
        await FindAsync(bed, unit2, "Twice");
        Assert.Equal(["1 \"needle\" pinned", "2 Count pinned", "3 Twice current shown"], Strip(results));
        using (var busy = new ManualResetEventSlim())
        {
            var blocker = worker.RunAsync("test-block", _ =>
            {
                busy.Wait();
                return 0;
            });
            await commands.ExecuteAsync("searchResults.repeat");
            var replaced = references.Running;
            Assert.True(results.IsSearching);
            await results.RunAsync(InFolder(Other));
            await replaced;
            Assert.False(references.IsSearching);
            Assert.Equal(Strings.Msg_FindInFiles_DoneOne, bed.Services.Status.Message); // no "canceled": it was replaced
            busy.Set();
            await blocker;
            Assert.Equal(["1 \"needle\" pinned", "2 Count pinned", "3 \"needle\" current shown"], Strip(results));
            Assert.Equal(FolderHeader(Other, 1), results.Header);
        }
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "UI-TW-618")]
    public Task SetsKeepTheirStateAndOutliveTheProject() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var results = bed.Services.SearchResults;
        var commands = bed.Services.Commands;
        var unit2 = await bed.OpenDocumentAsync(Unit2Path);

        // The first set (Find in Files) gets a state of its own: a collapsed file row, a current row, a selection, a
        // scroll position.
        await results.RunAsync(InProject("Step"));
        results.SetExpanded(Row(results, "Unit1.pas"), false);
        await results.GoToAsync(Row(results, "Unit2.pas:9:5"));
        results.SelectedRows = [Row(results, "Hello.pas:7:5"), Row(results, "Unit2.pas")];
        results.ScrollOffset = 44;
        List<string> first = ["Hello.pas", "Hello.pas:7:5", "Unit1.pas", "Unit2.pas", "Unit2.pas:8:5", "Unit2.pas:9:5"];
        Assert.Equal(first, Keys(results));
        await commands.ExecuteAsync(Pin);

        // The second set (References) is shown with its own state; the filter is typed for it.
        await FindAsync(bed, unit2, "Count");
        Assert.Equal(["1 \"Step\" pinned", "2 Count current shown"], Strip(results));
        Assert.True(results.IsReferences);
        Assert.Equal(13, results.Rows.Count);
        Assert.Empty(results.SelectedRows); // the selection of the first set did not come along
        Assert.Equal(0.0, results.ScrollOffset);
        Assert.Equal("Unit2.pas:10:8", results.CurrentRow?.Key);
        results.FilterText = "twice";
        bed.Clock.Advance(ToolWindowViewModel.FilterDelay);
        Assert.Equal(["Unit2.pas", "Unit2.pas#Twice", "Unit2.pas:10:8", "Unit2.pas:11:7"], Keys(results));
        results.SelectedRows = [Row(results, "Unit2.pas#Twice")];
        results.ScrollOffset = 22;

        // Back to the first: rows, expansion, selection, current row and scroll position are its own again, and the
        // filter text is cleared.
        results.ShowSet(1);
        Assert.Equal("", results.FilterText);
        Assert.False(results.IsReferences);
        Assert.Equal(first, Keys(results));
        Assert.False(Row(results, "Unit1.pas").IsExpanded);
        Assert.Equal(["Hello.pas:7:5", "Unit2.pas"], results.SelectedRows.Select(r => r.Key));
        Assert.Equal("Unit2.pas:9:5", results.CurrentRow?.Key);
        Assert.Equal(44.0, results.ScrollOffset);

        // And to the second: all its rows (the filter was cleared), its selection, its current row, its scroll position.
        results.ShowSet(2);
        Assert.True(results.IsReferences);
        Assert.Equal(13, results.Rows.Count);
        Assert.Equal(["Unit2.pas#Twice"], results.SelectedRows.Select(r => r.Key));
        Assert.Equal("Unit2.pas:10:8", results.CurrentRow?.Key);
        Assert.Equal(22.0, results.ScrollOffset);

        // F8, expand / collapse and the filter act on the shown set only.
        await results.NavigateNextAsync(forward: true);
        Assert.Equal("Unit2.pas:11:7", results.CurrentRow?.Key);
        await commands.ExecuteAsync("searchResults.collapseAll");
        Assert.Equal(["Hello.pas", "Unit1.pas", "Unit2.pas"], Keys(results));
        results.ShowSet(1);
        Assert.Equal(first, Keys(results));
        Assert.Equal("Unit2.pas:9:5", results.CurrentRow?.Key);
        await results.NavigateNextAsync(forward: true);
        Assert.Equal("Hello.pas:7:5", results.CurrentRow?.Key); // after the last match of this set: its first
        results.ShowSet(2);
        Assert.Equal(["Hello.pas", "Unit1.pas", "Unit2.pas"], Keys(results)); // still collapsed
        await commands.ExecuteAsync("searchResults.expandAll");
        Assert.Equal(13, results.Rows.Count);
        Assert.Equal("Unit2.pas:11:7", results.CurrentRow?.Key);

        // The project is closed: the sets are not removed (they live until they are closed or the IDE exits), and a row
        // still navigates.
        Assert.True(await bed.Services.Documents.CloseAllAsync());
        Assert.True(await bed.Services.Projects.CloseAsync());
        Assert.Null(bed.Services.Projects.Project);
        Assert.Equal(["1 \"Step\" pinned", "2 Count current shown"], Strip(results));
        Assert.Equal(13, results.Rows.Count);
        await results.GoToAsync(Row(results, "Unit1.pas:12:3"));
        var opened = Assert.IsType<TextDocumentViewModel>(bed.Services.Documents.ActiveDocument);
        Assert.Equal(BuildTestBed.UnitPath, opened.FilePath);
        Assert.Equal("Count", opened.Buffer.GetText(opened.Selection.Start, opened.Selection.End - opened.Selection.Start));
        results.ShowSet(1);
        Assert.Equal(first, Keys(results));
    });
}
