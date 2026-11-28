using System.Globalization;
using System.Text;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.CodeModel;

/// <summary>
/// Find References (I-353, F13-016a; IDE-FREF-001…008): <c>search.findReferences</c> on the identifier at the caret
/// lists every reference of the project <c>Compilation</c> in Search Results as a result set of kind References —
/// grouped by file and container, with the reference kinds.
/// </summary>
[Trait("Phase", "8")]
public sealed class FindReferencesCommandTests
{
    private const string Dir = BuildTestBed.Dir;
    private const string Unit2Path = Dir + @"\Unit2.pas";

    private const string Main = "program Hello;\nuses Unit1, Unit2;\nvar\n  C: TCounter;\nbegin\n  C := TCounter.Create;\n  C.Step;\n  Twice(C);\n  Writeln(C.Count);\nend.\n";

    private const string Unit1 =
        "unit Unit1;\ninterface\ntype\n  TCounter = class\n  public\n    Count: Integer;\n    procedure Step;\n  end;\nimplementation\n"
        + "procedure TCounter.Step;\nbegin\n  Count := Count + 1;\nend;\nend.\n";

    private const string Unit2 =
        "unit Unit2;\ninterface\nuses Unit1;\nprocedure Twice(C: TCounter);\nimplementation\nprocedure Twice(C: TCounter);\nbegin\n"
        + "  C.Step;\n  C.Step;\n  if C.Count > 3 then\n    C.Count := 0;\nend;\nend.\n";

    /// <summary>
    /// The project Hello (program, Unit1, Unit2 — in this order) over the real system unit: a field that is declared,
    /// read and written, and a method called from the program and from the other unit.
    /// </summary>
    private static async Task<BuildTestBed> StartAsync(string files = "{ \"path\": \"Hello.pas\" }, { \"path\": \"Unit1.pas\" }, { \"path\": \"Unit2.pas\" }",
        Action<BuildTestBed>? prepare = null)
    {
        var bed = new BuildTestBed();
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(File.Exists))
            bed.File(@"G:\rtl\" + Path.GetFileName(file), File.ReadAllText(file));
        bed.File(BuildTestBed.ProjectPath, "{ \"formatVersion\": 1, \"name\": \"Hello\", \"appType\": \"console\", \"mainSource\": \"Hello.pas\",\n  \"files\": [ " + files + " ] }");
        bed.File(BuildTestBed.MainPath, Main);
        bed.File(BuildTestBed.UnitPath, Unit1);
        bed.File(Unit2Path, Unit2);
        prepare?.Invoke(bed);
        Assert.True(await bed.Services.Projects.OpenAsync(BuildTestBed.ProjectPath));
        Assert.True(await bed.Services.Documents.CloseAllAsync());
        return bed;
    }

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

    /// <summary>Puts the caret into the <paramref name="occurrence"/>-th <paramref name="text"/> and runs Shift+F12's command (the query is not awaited).</summary>
    private static async Task StartFindAsync(BuildTestBed bed, TextDocumentViewModel doc, string text, int occurrence = 1)
    {
        bed.Services.Documents.Activate(doc);
        doc.Selection = TextSelection.At(Offset(doc, text, occurrence) + 1); // inside the identifier
        Assert.True(bed.Services.Commands.CanExecute("search.findReferences"));
        await bed.Services.Commands.ExecuteAsync("search.findReferences");
    }

    private static async Task FindAsync(BuildTestBed bed, TextDocumentViewModel doc, string text, int occurrence = 1)
    {
        await StartFindAsync(bed, doc, text, occurrence);
        await bed.Services.FindReferences.Running;
    }

    /// <summary>One line per row: a file or container row <c>key (count)</c>, a reference row <c>key letter</c>.</summary>
    private static List<string> Describe(SearchResultsViewModel results) =>
        [.. results.Rows.Select(r => r.ReferenceKind is { } kind ? $"{r.Key} {SearchResultsViewModel.KindLetter(kind)}" : $"{r.Key} ({r.Count})")];

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-FREF-001")]
    [Trait("Req", "IDE-FREF-003")]
    [Trait("Req", "IDE-FREF-004")]
    [Trait("Req", "IDE-FREF-005")]
    [Trait("Req", "UI-TW-611")]
    [Trait("Req", "UI-TW-612")]
    [Trait("Req", "UI-TW-614")]
    public Task GroupedByFileWithKinds() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var results = bed.Services.SearchResults;
        var statuses = new List<string?>();
        bed.Services.Status.Changed += (_, _) => statuses.Add(bed.Services.Status.Message);
        var shown = 0;
        results.ResultsShown += (_, _) => shown++;
        var unit2 = await bed.OpenDocumentAsync(Unit2Path);
        Assert.True(results.IsEmpty);

        // The field, from its read in Unit2: files in project order, containers by their first reference, references by position.
        await FindAsync(bed, unit2, "Count");

        Assert.True(results.IsReferences);
        Assert.Equal(
        [
            "Hello.pas (1)", "Hello.pas#(unit level) (1)", "Hello.pas:9:13 R",
            "Unit1.pas (3)", "Unit1.pas#TCounter (1)", "Unit1.pas:6:5 D", "Unit1.pas#TCounter.Step (2)", "Unit1.pas:12:3 W", "Unit1.pas:12:12 R",
            "Unit2.pas (2)", "Unit2.pas#Twice (2)", "Unit2.pas:10:8 R", "Unit2.pas:11:7 W",
        ], Describe(results));
        Assert.All(results.Rows.Where(r => r.Match is null), r => Assert.True(r.IsExpanded)); // at most 50 files: everything expanded
        Assert.Equal("References to Count — 6 references in 3 files", results.Header);
        Assert.Equal(Format(Strings.Msg_References_DoneMany, 6, 3), statuses[^1]);
        Assert.Equal("Unit2.pas:10:8", results.CurrentRow?.Key); // the reference under the caret
        Assert.Equal(1, shown); // shown, the keyboard focus not asked for (IDE-FREF-003)
        Assert.Equal(SearchResultsViewModel.ToolName, bed.Services.Layout.Layout.Bottom.ActiveTool); // its tab is the selected one
        Assert.False(results.IsSearching);
        Assert.False(results.IsCapped);

        // A row carries the place and its line: the identifier is the highlighted span of the trimmed line.
        var write = results.Rows.Single(r => r.Key == "Unit2.pas:11:7");
        Assert.Equal((Unit2Path, 11, 7, 5), (write.Match!.Path, write.Match.Line, write.Match.Column, write.Match.Length));
        Assert.Equal(("C.Count := 0;", 2, 5), (write.Text, write.HighlightStart, write.HighlightLength));
        Assert.Equal("Twice", results.Rows.Single(r => r.Key == "Unit2.pas#Twice").Container);

        // The method, from its call in the program: the declaration in the class, the implementation, the calls of both files.
        var main = await bed.OpenDocumentAsync(BuildTestBed.MainPath);
        await FindAsync(bed, main, "Step");

        Assert.Equal(
        [
            "Hello.pas (1)", "Hello.pas#(unit level) (1)", "Hello.pas:7:5 C",
            "Unit1.pas (2)", "Unit1.pas#TCounter (1)", "Unit1.pas:7:15 D", "Unit1.pas#TCounter.Step (1)", "Unit1.pas:10:20 I",
            "Unit2.pas (2)", "Unit2.pas#Twice (2)", "Unit2.pas:8:5 C", "Unit2.pas:9:5 C",
        ], Describe(results));
        Assert.Equal("References to Step — 5 references in 3 files", results.Header);
        Assert.Equal("Hello.pas:7:5", results.CurrentRow?.Key);
        Assert.Equal(2, shown);

        // A type name: the references in a heading without a body belong to the unit level.
        await FindAsync(bed, unit2, "TCounter");
        Assert.Equal(
        [
            "Hello.pas (2)", "Hello.pas#(unit level) (2)", "Hello.pas:4:6 T", "Hello.pas:6:8 T",
            "Unit1.pas (2)", "Unit1.pas#TCounter (1)", "Unit1.pas:4:3 D", "Unit1.pas#TCounter.Step (1)", "Unit1.pas:10:11 T",
            "Unit2.pas (2)", "Unit2.pas#(unit level) (1)", "Unit2.pas:4:20 T", "Unit2.pas#Twice (1)", "Unit2.pas:6:20 T",
        ], Describe(results));

        // One reference in one file, several in one file: the other two count texts.
        await FindAsync(bed, main, "C", occurrence: 1);
        Assert.Equal("References to C — 5 references in 1 file", results.Header);
        Assert.Equal(Format(Strings.Msg_References_DoneOneFile, 5), statuses[^1]);
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-FREF-004")]
    [Trait("Req", "UI-TW-614")]
    public Task ContainersAreRoutinesTypesOrUnitLevel() => TestUiThread.RunAsync(async () =>
    {
        const string unit =
            "unit Unit1;\ninterface\ntype\n  TAmount = record\n    Value: Integer;\n  end;\n  TBox = class\n    Size: TAmount;\n    procedure Grow(By: TAmount);\n  end;\n"
            + "var\n  Total: TAmount;\nprocedure Show(A: TAmount); overload;\nprocedure Show(A, B: TAmount); overload;\nimplementation\n"
            + "procedure TBox.Grow(By: TAmount);\nvar\n  Old: TAmount;\nbegin\n  Old := Size;\n  Size := By;\nend;\n"
            + "procedure Outer;\n  procedure Inner;\n  var\n    N: TAmount;\n  begin\n    N := Total;\n  end;\nbegin\n  Inner;\nend;\n"
            + "procedure Show(A: TAmount);\nbegin\nend;\nprocedure Show(A, B: TAmount);\nbegin\nend;\nend.\n";
        var bed = await StartAsync("{ \"path\": \"Hello.pas\" }, { \"path\": \"Unit1.pas\" }", b =>
        {
            b.File(BuildTestBed.MainPath, "program Hello;\nuses Unit1;\nbegin\nend.\n");
            b.File(BuildTestBed.UnitPath, unit);
        });
        var results = bed.Services.SearchResults;
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);

        await FindAsync(bed, doc, "TAmount", occurrence: 3);

        Assert.Equal(
        [
            "Unit1.pas (11)",
            "Unit1.pas#TAmount (1)", "Unit1.pas:4:3 D",                                   // the type itself
            "Unit1.pas#TBox (2)", "Unit1.pas:8:11 T", "Unit1.pas:9:24 T",                  // a field and a method heading in the class: the type
            "Unit1.pas#(unit level) (3)", "Unit1.pas:12:10 T", "Unit1.pas:13:19 T", "Unit1.pas:14:22 T", // a var section, headings of the interface section
            "Unit1.pas#TBox.Grow (2)", "Unit1.pas:16:25 T", "Unit1.pas:18:8 T",            // a method: heading and body
            "Unit1.pas#Outer.Inner (1)", "Unit1.pas:26:8 T",                              // a nested routine
            "Unit1.pas#Show (1)", "Unit1.pas:33:19 T",                                    // two routines of one name: the second key gets @2
            "Unit1.pas#Show@2 (1)", "Unit1.pas:36:22 T",
        ], Describe(results));
        Assert.Equal("Unit1.pas:9:24", results.CurrentRow?.Key);
        Assert.Equal(Strings.Msg_References_UnitLevel, results.Rows.Single(r => r.Key == "Unit1.pas#(unit level)").Container);

        // A container row collapses on its own; the two Show rows keep their own state.
        results.SetExpanded(results.Rows.Single(r => r.Key == "Unit1.pas#Show"), false);
        Assert.DoesNotContain(results.Rows, r => r.Key == "Unit1.pas:33:19");
        Assert.Contains(results.Rows, r => r.Key == "Unit1.pas:36:22");
        await bed.Services.Commands.ExecuteAsync("searchResults.collapseAll");
        Assert.Equal(["Unit1.pas (11)"], Describe(results));
        await bed.Services.Commands.ExecuteAsync("searchResults.expandAll");
        Assert.Equal(19, results.Rows.Count);
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-FREF-001")]
    public Task UnsavedBufferIsSearched() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var results = bed.Services.SearchResults;
        var unit2 = await bed.OpenDocumentAsync(Unit2Path);

        // Typed and not saved: one more call; the write of the field is deleted.
        var buffer = unit2.Buffer;
        var write = Offset(unit2, "    C.Count := 0;");
        buffer.ApplyEdits([new TextEdit(write, "    C.Count := 0;".Length, "    C.Step;")]);
        Assert.True(unit2.IsModified);
        Assert.Equal(Unit2, bed.Text(Unit2Path)); // the file is as it was

        await FindAsync(bed, unit2, "Step");
        Assert.Equal(["Unit2.pas (3)", "Unit2.pas#Twice (3)", "Unit2.pas:8:5 C", "Unit2.pas:9:5 C", "Unit2.pas:11:7 C"],
            Describe(results).SkipWhile(r => !r.StartsWith("Unit2.pas", StringComparison.Ordinal)));

        await FindAsync(bed, unit2, "Count");
        Assert.Equal(["Unit2.pas (1)", "Unit2.pas#Twice (1)", "Unit2.pas:10:8 R"],
            Describe(results).SkipWhile(r => !r.StartsWith("Unit2.pas", StringComparison.Ordinal)));
        Assert.Equal("References to Count — 5 references in 3 files", results.Header);
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-FREF-001")]
    [Trait("Req", "IDE-FREF-002")]
    public Task NotOnIdentifierNoneAndCancel() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync(prepare: b => b.File(Unit2Path, Unit2.Replace("  C.Step;\n  C.Step;\n", "  C.Step;\n  Zork;\n", StringComparison.Ordinal)));
        var results = bed.Services.SearchResults;
        var commands = bed.Services.Commands;
        var worker = bed.Services.CodeModel.Worker;
        var unit2 = await bed.OpenDocumentAsync(Unit2Path);

        // Not on an identifier: the message of Go to Declaration, nothing is asked, nothing is shown.
        var before = worker.RequestCount;
        await FindAsync(bed, unit2, "begin");
        Assert.Equal(Strings.Msg_Editor_NotAnIdentifier, bed.Services.Status.Message);
        Assert.Equal(before, worker.RequestCount);
        Assert.True(results.IsEmpty);

        // An identifier without a symbol: no references, and the window is not shown or changed — also not an existing set.
        await FindAsync(bed, unit2, "Zork");
        Assert.Equal(Format(Strings.Msg_References_None, "Zork"), bed.Services.Status.Message);
        Assert.True(results.IsEmpty);
        Assert.NotEqual(SearchResultsViewModel.ToolName, bed.Services.Layout.Layout.Bottom.ActiveTool);
        await FindAsync(bed, unit2, "Count");
        var rows = results.Rows;
        var header = results.Header;
        Assert.Equal("References to Count — 6 references in 3 files", header);
        await FindAsync(bed, unit2, "Zork");
        Assert.Equal(Format(Strings.Msg_References_None, "Zork"), bed.Services.Status.Message);
        Assert.Same(rows, results.Rows);
        Assert.Equal(header, results.Header);

        // A query that takes long: the worker is busy with something else, so the query waits behind it.
        using var gate = new ManualResetEventSlim();
        var blocker = worker.RunAsync("test-block", _ =>
        {
            gate.Wait();
            return 0;
        });
        Assert.False(commands.CanExecute("searchResults.cancel"));
        await StartFindAsync(bed, unit2, "Step");
        var running = bed.Services.FindReferences.Running;
        Assert.False(running.IsCompleted);
        Assert.True(bed.Services.FindReferences.IsSearching);
        Assert.True(commands.CanExecute("searchResults.cancel"));
        Assert.False(results.IsSearching); // the shown set is not searched again: its header stays

        // The status bar names it after 400 ms, and keeps the text.
        bed.Clock.Advance(TimeSpan.FromMilliseconds(399));
        Assert.NotEqual(Format(Strings.Msg_References_Searching, "Step"), bed.Services.Status.Message);
        bed.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(Format(Strings.Msg_References_Searching, "Step"), bed.Services.Status.Message);
        bed.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(Format(Strings.Msg_References_Searching, "Step"), bed.Services.Status.Message);

        // Cancelled: it answers at once (the worker is still busy), changes nothing and says so.
        await commands.ExecuteAsync("searchResults.cancel");
        await running;
        Assert.Equal(Strings.Msg_References_Canceled, bed.Services.Status.Message);
        Assert.False(bed.Services.FindReferences.IsSearching);
        Assert.False(commands.CanExecute("searchResults.cancel"));
        Assert.Same(rows, results.Rows);
        Assert.Equal(header, results.Header);

        // A newer query cancels the older one: only the newer result is shown.
        await StartFindAsync(bed, unit2, "Step");
        var older = bed.Services.FindReferences.Running;
        await StartFindAsync(bed, unit2, "Twice");
        var newer = bed.Services.FindReferences.Running;
        await older;
        Assert.Same(rows, results.Rows);
        Assert.True(bed.Services.FindReferences.IsSearching);
        gate.Set();
        await blocker;
        await newer;
        Assert.Equal("References to Twice — 3 references in 2 files", results.Header);
        Assert.Equal(Format(Strings.Msg_References_DoneMany, 3, 2), bed.Services.Status.Message);
        Assert.False(bed.Services.FindReferences.IsSearching);
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-FREF-005")]
    [Trait("Req", "UI-TW-609")]
    [Trait("Req", "UI-TW-611")]
    [Trait("Req", "UI-TW-612")]
    public Task LibraryFilesLastAndCollapsed() => TestUiThread.RunAsync(async () =>
    {
        // A library unit (under the run-time library root), a unit beside the project that is not a member, two members.
        const string library = "unit LibMath;\ninterface\nfunction LibTwice(A: Integer): Integer;\nimplementation\nfunction LibTwice(A: Integer): Integer;\nbegin\n  Result := A * 2;\nend;\n"
            + "function LibFour(A: Integer): Integer;\nbegin\n  Result := LibTwice(LibTwice(A));\nend;\nend.\n";
        var bed = await StartAsync("{ \"path\": \"Hello.pas\" }, { \"path\": \"Unit1.pas\" }", b =>
        {
            b.File(@"G:\rtl\LibMath.pas", library);
            b.File(BuildTestBed.MainPath, "program Hello;\nuses Unit1, Aside, LibMath;\nbegin\n  Writeln(LibTwice(1));\nend.\n");
            b.File(BuildTestBed.UnitPath, "unit Unit1;\ninterface\nimplementation\nuses LibMath;\nvar\n  N: Integer;\nbegin\n  N := LibTwice(2);\nend.\n");
            b.File(Dir + @"\Aside.pas", "unit Aside;\ninterface\nimplementation\nuses LibMath;\nprocedure Run;\nbegin\n  LibTwice(3);\nend;\nend.\n");
        });
        var results = bed.Services.SearchResults;
        Assert.True(bed.Services.LibrarySource.IsLibrarySource(@"G:\rtl\LibMath.pas"));
        Assert.False(bed.Services.Projects.IsMember(Dir + @"\Aside.pas"));
        var main = await bed.OpenDocumentAsync(BuildTestBed.MainPath);

        await FindAsync(bed, main, "LibTwice");

        // Project members in project order, then the other file, then the library file: collapsed, its rows hidden.
        Assert.Equal(
        [
            "Hello.pas (1)", "Hello.pas#(unit level) (1)", "Hello.pas:4:11 C",
            "Unit1.pas (1)", "Unit1.pas#(unit level) (1)", "Unit1.pas:8:8 C",
            "Aside.pas (1)", "Aside.pas#Run (1)", "Aside.pas:7:3 C",
            @"G:\rtl\LibMath.pas (4)",
        ], Describe(results));
        var row = results.Rows[^1];
        Assert.True(row.IsLibrary);
        Assert.False(row.IsExpanded);
        Assert.All(results.Rows.Take(9), r => Assert.False(r.IsLibrary));
        Assert.Equal("References to LibTwice — 7 references in 4 files (4 in library sources)", results.Header);
        Assert.Equal(Format(Strings.Msg_References_DoneMany, 7, 4), bed.Services.Status.Message);

        // Expanded by the user: the heading of the interface section at the unit level, then the routines.
        await results.GoToAsync(row);
        Assert.Equal(
        [
            @"G:\rtl\LibMath.pas (4)", @"G:\rtl\LibMath.pas#(unit level) (1)", @"G:\rtl\LibMath.pas:3:10 D",
            @"G:\rtl\LibMath.pas#LibTwice (1)", @"G:\rtl\LibMath.pas:5:10 I",
            @"G:\rtl\LibMath.pas#LibFour (2)", @"G:\rtl\LibMath.pas:11:13 C", @"G:\rtl\LibMath.pas:11:22 C",
        ], Describe(results).Skip(9));
    });

    [Fact(Timeout = 120000)]
    [Trait("Req", "IDE-FREF-006")]
    public Task CapAtFiveThousand() => TestUiThread.RunAsync(async () =>
    {
        // The declaration and 5 000 writes: 5 001 references.
        var text = new StringBuilder("unit Unit1;\ninterface\nimplementation\nvar\n  Total: Integer;\nprocedure Fill;\nbegin\n");
        for (var i = 0; i < FindInFilesService.MatchCap; i++)
            text.Append("  Total := ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(";\n");
        text.Append("end;\nend.\n");
        var bed = await StartAsync("{ \"path\": \"Hello.pas\" }, { \"path\": \"Unit1.pas\" }", b =>
        {
            b.File(BuildTestBed.MainPath, "program Hello;\nuses Unit1;\nbegin\nend.\n");
            b.File(BuildTestBed.UnitPath, text.ToString());
        });
        var results = bed.Services.SearchResults;
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);

        await FindAsync(bed, doc, "Total");

        Assert.Equal(FindInFilesService.MatchCap, results.Rows.Count(r => r.Match is not null));
        Assert.Equal(["Unit1.pas (5000)", "Unit1.pas#(unit level) (1)", "Unit1.pas:5:3 D", "Unit1.pas#Fill (4999)", "Unit1.pas:8:3 W"], Describe(results).Take(5));
        Assert.Equal("Unit1.pas:5006:3", results.Rows[^1].Key); // the first 5 000 in display order
        Assert.True(results.IsCapped);
        Assert.Equal(Format(Strings.Msg_References_Capped, 5000.ToString("N0", CultureInfo.CurrentCulture)), results.CappedText);
        Assert.Equal("References to Total — 5000 references in 1 file", results.Header); // the header counts what is shown
        Assert.Equal(Format(Strings.Msg_References_DoneOneFile, 5000), bed.Services.Status.Message);

        // A Find in Files set names its own cap.
        await results.RunAsync(new FindInFilesRequest(new FindQuery("Total", MatchCase: true, WholeWord: false, Regex: false), FindInFilesScope.Project));
        Assert.False(results.IsReferences);
        Assert.Equal(Format(Strings.Msg_FindInFiles_Capped, 5000.ToString("N0", CultureInfo.CurrentCulture)), results.CappedText);
    });

    [Fact(Timeout = 60000)]
    [Trait("Req", "IDE-FREF-008")]
    public Task RepeatFollowsTheSymbol() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var results = bed.Services.SearchResults;
        var commands = bed.Services.Commands;
        var references = bed.Services.FindReferences;
        var unit2 = await bed.OpenDocumentAsync(Unit2Path);
        await FindAsync(bed, unit2, "Step", occurrence: 2); // line 9
        Assert.Equal("Unit2.pas:9:5", results.CurrentRow?.Key);
        Assert.Equal("References to Step — 5 references in 3 files", results.Header);

        // An edit above the start: one more call. Repeat asks again where the identifier is now.
        BuildTestBed.Type(unit2, "  C.Step;\n", Offset(unit2, "  C.Step;"));
        Assert.True(commands.CanExecute("searchResults.repeat"));
        await commands.ExecuteAsync("searchResults.repeat");
        await references.Running;
        Assert.Equal("References to Step — 6 references in 3 files", results.Header);
        Assert.Equal(["Unit2.pas (3)", "Unit2.pas#Twice (3)", "Unit2.pas:8:5 C", "Unit2.pas:9:5 C", "Unit2.pas:10:5 C"],
            Describe(results).SkipWhile(r => !r.StartsWith("Unit2.pas", StringComparison.Ordinal)));
        Assert.Equal("Unit2.pas:10:5", results.CurrentRow?.Key); // the start identifier, one line further down

        // While the repeat runs the header says so; the rows stay until the new ones arrive.
        using (var gate = new ManualResetEventSlim())
        {
            var blocker = bed.Services.CodeModel.Worker.RunAsync("test-block", _ =>
            {
                gate.Wait();
                return 0;
            });
            var rows = results.Rows;
            await commands.ExecuteAsync("searchResults.repeat");
            Assert.True(results.IsSearching);
            Assert.Equal("References to Step — " + Strings.SearchResults_Searching, results.Header);
            Assert.Same(rows, results.Rows);
            gate.Set();
            await blocker;
            await references.Running;
            Assert.False(results.IsSearching);
            Assert.Equal("References to Step — 6 references in 3 files", results.Header);
        }

        // The identifier is deleted: the status bar says so and the rows stay.
        var before = results.Rows;
        var start = Offset(unit2, "  C.Step;", occurrence: 3);
        unit2.Buffer.ApplyEdits([new TextEdit(start, "  C.Step;\n".Length, "")]);
        await commands.ExecuteAsync("searchResults.repeat");
        await references.Running;
        Assert.Equal(Strings.Msg_References_SymbolGone, bed.Services.Status.Message);
        Assert.Same(before, results.Rows);
        Assert.Equal("References to Step — 6 references in 3 files", results.Header);

        // With the document closed the stored line and column are used: the field's read in the saved file.
        await FindAsync(bed, unit2, "Count");
        Assert.Equal("Unit2.pas:10:8", results.CurrentRow?.Key);
        Assert.True(await bed.Services.Documents.SaveAsync(unit2));
        Assert.True(await bed.Services.Documents.CloseAsync(unit2));
        await commands.ExecuteAsync("searchResults.repeat");
        await references.Running;
        Assert.Equal("References to Count — 6 references in 3 files", results.Header);
        Assert.Equal("Unit2.pas:10:8", results.CurrentRow?.Key);

        // Clear gives the empty state back, as for a Find in Files set.
        await commands.ExecuteAsync("searchResults.clear");
        Assert.True(results.IsEmpty);
        Assert.False(commands.CanExecute("searchResults.repeat"));
    });
}
