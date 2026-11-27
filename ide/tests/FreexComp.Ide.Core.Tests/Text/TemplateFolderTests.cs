using System.Globalization;
using System.Text;
using System.Text.Json;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Text;

/// <summary>
/// The templates folder <c>R\templates\*.json</c> (I-325, F11-052; code-templates.md §2 IDE-SET-081…083): read at start-up,
/// merged after the built-ins and before <c>templates.json</c>, watched (debounced, retried while locked), a file that cannot
/// be used skipped as a whole and reported in Messages and the log.
/// </summary>
[Trait("Phase", "8")]
public sealed class TemplateFolderTests
{
    private const string Head = "unit U;\ninterface\nimplementation\nprocedure P;\nbegin\n";
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private sealed class Ide
    {
        public required IdeServices Services { get; init; }
        public required InMemoryFileSystem Fs { get; init; }
        public required ManualClock Clock { get; init; }
        public required ManualFileWatcherFactory Watchers { get; init; }
        public required RecordingLog Log { get; init; }

        public CodeTemplateService Templates => Services.Templates;

        public string Roaming => Services.UserPaths.RoamingDirectory;

        public string Folder => Path.Combine(Roaming, "templates");

        public string PathOf(string name) => Path.Combine(Folder, name);

        /// <summary>Writes (or replaces) a folder file and raises the watcher event.</summary>
        public void Drop(string name, string json, FileChangeKind kind = FileChangeKind.Created)
        {
            Fs.CreateDirectory(Folder);
            Fs.WriteAllBytes(PathOf(name), Encoding.UTF8.GetBytes(json));
            Watchers.Raise(new FileChange(kind, PathOf(name)));
        }

        public IReadOnlyList<MessageItem> Rows => [.. Services.Messages.Items];
    }

    /// <param name="files">Folder files written before start-up (name → JSON); null = no folder.</param>
    private static Ide Start(IReadOnlyDictionary<string, string>? files, string? userFile = null, bool createRoaming = true)
    {
        var fs = new InMemoryFileSystem();
        var clock = new ManualClock();
        var watchers = new ManualFileWatcherFactory();
        var log = new RecordingLog();
        var userDir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"), "user");
        var roaming = Path.Combine(userDir, "roaming");
        if (createRoaming)
            fs.CreateDirectory(roaming);
        if (files is not null)
        {
            fs.CreateDirectory(Path.Combine(roaming, "templates"));
            foreach (var (name, json) in files)
            {
                var path = Path.Combine(roaming, "templates", name);
                fs.CreateDirectory(Path.GetDirectoryName(path)!);
                fs.WriteAllBytes(path, Encoding.UTF8.GetBytes(json));
            }
        }

        if (userFile is not null)
            fs.WriteAllBytes(Path.Combine(roaming, "templates.json"), Encoding.UTF8.GetBytes(userFile));
        var services = TestComposition.Build(o =>
        {
            o.FileSystem = fs;
            o.Clock = clock;
            o.FileWatchers = watchers;
            o.Log = log;
            o.UserDir = userDir;
        });
        Assert.Equal(roaming, services.UserPaths.RoamingDirectory);
        return new Ide { Services = services, Fs = fs, Clock = clock, Watchers = watchers, Log = log };
    }

    private static string File(params string[] templates) => "{ \"formatVersion\": 1, \"templates\": [" + string.Join(", ", templates) + "] }";

    private static string T(string abbreviation, string body, string description = "d", string language = "pascal") =>
        JsonSerializer.Serialize(new { abbreviation, description, language, body });

    private static string Skipped(string file, string reason) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Msg_Templates_FileSkipped, file, reason);

    private static string Format(string text, params object[] args) => string.Format(CultureInfo.CurrentCulture, text, args);

    private static async Task<TextDocumentViewModel> OpenAsync(Ide ide, string text)
    {
        ide.Services.Settings.Set(SettingsCatalog.EditorTabWidth, 2);
        ide.Services.Settings.Set(SettingsCatalog.EditorInsertSpaces, true);
        ide.Fs.CreateDirectory(@"C:\t");
        var caret = text.IndexOf('|', StringComparison.Ordinal);
        ide.Fs.WriteAllBytes(@"C:\t\U.pas", Encoding.UTF8.GetBytes(text.Remove(caret, 1)));
        var doc = Assert.IsType<TextDocumentViewModel>((await ide.Services.Documents.OpenAsync(@"C:\t\U.pas")).Value);
        ide.Services.Documents.Activate(doc);
        doc.Selection = TextSelection.At(caret);
        return doc;
    }

    private static string Marked(TextDocumentViewModel doc) => doc.Buffer.Text.ReplaceLineEndings("\n").Insert(doc.Selection.Caret, "|");

    private static void Type(TextDocumentViewModel doc, string text)
    {
        foreach (var c in text)
            Assert.True(doc.TypeText(c.ToString()));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-081")]
    public Task FolderFileAddsTemplate() => TestUiThread.RunAsync(async () =>
    {
        var ide = Start(new Dictionary<string, string>
        {
            ["team.json"] = File(T("hello", "WriteLn('Hello');$end$", "greeting")),
            [@"sub\inner.json"] = File(T("inner", "inner;")), // sub-folders are ignored
            ["notes.txt"] = File(T("notes", "notes;")), // other extensions are ignored
            ["team.json.bak"] = File(T("backup", "backup;")),
        });
        var hello = Assert.IsType<CodeTemplate>(ide.Templates.Find("HELLO"));
        Assert.Equal(new CodeTemplate("hello", "greeting", "pascal", "WriteLn('Hello');$end$"), hello);
        Assert.Equal(new CodeTemplateOrigin(CodeTemplateOriginKind.Folder, "team.json"), ide.Templates.OriginOf(hello));
        Assert.Equal(new CodeTemplateOrigin(CodeTemplateOriginKind.BuiltIn), ide.Templates.OriginOf(ide.Templates.Find("begin")!));
        Assert.All((string[])["inner", "notes", "backup"], a => Assert.Null(ide.Templates.Find(a)));
        Assert.Empty(ide.Rows);

        // The folder template expands like any other (Tab expansion, IDE-ED-064).
        var doc = await OpenAsync(ide, Head + "  hello|\nend;\nend.\n");
        Assert.True(doc.HandleKey("Tab"));
        Assert.Equal(Head + "  WriteLn('Hello');|\nend;\nend.\n", Marked(doc));

        // Without a folder nothing is created at start-up (IDE-SET-081, IDE-SET-001).
        var bare = Start(null);
        Assert.Equal(CodeTemplateService.BuiltIns.Count, bare.Templates.Templates.Count);
        Assert.False(bare.Fs.DirectoryExists(bare.Folder));
        var none = Start(null, createRoaming: false);
        Assert.Equal(CodeTemplateService.BuiltIns.Count, none.Templates.Templates.Count);
        Assert.False(none.Fs.DirectoryExists(none.Folder));
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-082")]
    public void PrecedenceBuiltinFolderUserFile()
    {
        var ide = Start(new Dictionary<string, string>
        {
            ["b.json"] = File(T("WL", "WriteLn(B);", "from b"), T("x", "x := B;", "x from b"), T("y", "y := B;")),
            ["a.json"] = File(T("wl", "WriteLn(A);", "from a"), T("X", "x := A;"), T("onlyA", "a;")),
        }, userFile: File(T("y", "y := User;", "y from user")));
        var t = ide.Templates;

        // built-ins → folder files in ordinal file-name order → templates.json; the same abbreviation case-insensitive.
        Assert.Equal("WriteLn(B);", t.Find("wl")!.Body);
        Assert.Equal("x := B;", t.Find("x")!.Body);
        Assert.Equal("a;", t.Find("onlyA")!.Body);
        Assert.Equal("y := User;", t.Find("y")!.Body);
        Assert.Single(t.Templates, x => x.Abbreviation.Equals("wl", StringComparison.OrdinalIgnoreCase));
        Assert.Single(t.Templates, x => x.Abbreviation.Equals("x", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new CodeTemplateOrigin(CodeTemplateOriginKind.Folder, "b.json"), t.OriginOf(t.Find("wl")!));
        Assert.Equal(new CodeTemplateOrigin(CodeTemplateOriginKind.Folder, "a.json"), t.OriginOf(t.Find("onlyA")!));
        Assert.Equal(new CodeTemplateOrigin(CodeTemplateOriginKind.User), t.OriginOf(t.Find("y")!));
        Assert.Equal(new CodeTemplateOrigin(CodeTemplateOriginKind.BuiltIn), t.OriginOf(t.Find("begin")!));

        // The Options page shows the folder templates with "Origin: <file>" on the description cell.
        var page = new CodeTemplatesPageViewModel(t);
        Assert.Equal(Format(Strings.Options_Templates_Origin, "b.json"), page.Rows.Single(r => r.Abbreviation == "x").OriginToolTip);
        Assert.Null(page.Rows.Single(r => r.Abbreviation == "begin").OriginToolTip);
        Assert.Null(page.Rows.Single(r => r.Abbreviation == "y").OriginToolTip);
        Assert.False(page.HasChanges);

        // Editing a folder template stores the edited copy in templates.json; the folder file stays as it is.
        var before = ide.Fs.ReadAllBytes(ide.PathOf("b.json"));
        page.Rows.Single(r => r.Abbreviation == "x").Body = "x := Edited;";
        Assert.True(page.HasChanges);
        page.Apply();
        Assert.Equal(before, ide.Fs.ReadAllBytes(ide.PathOf("b.json")));
        Assert.Equal("x := Edited;", t.Find("x")!.Body);
        Assert.Equal(new CodeTemplateOrigin(CodeTemplateOriginKind.User), t.OriginOf(t.Find("x")!));
        using var user = JsonDocument.Parse(ide.Fs.ReadAllBytes(Path.Combine(ide.Roaming, "templates.json")));
        var written = user.RootElement.GetProperty("templates").EnumerateArray()
            .Select(e => (e.GetProperty("abbreviation").GetString(), e.GetProperty("deleted").GetBoolean())).ToList();
        Assert.Equal([("x", false), ("y", false)], written.Order()); // unchanged folder templates and built-ins are not written
        Assert.False(new CodeTemplatesPageViewModel(t).HasChanges);

        // A restart reads the same set.
        var again = Start(new Dictionary<string, string>
        {
            ["b.json"] = File(T("WL", "WriteLn(B);", "from b"), T("x", "x := B;", "x from b"), T("y", "y := B;")),
            ["a.json"] = File(T("wl", "WriteLn(A);", "from a"), T("X", "x := A;"), T("onlyA", "a;")),
        }, userFile: Encoding.UTF8.GetString(ide.Fs.ReadAllBytes(Path.Combine(ide.Roaming, "templates.json"))));
        Assert.Equal(t.Templates, again.Templates.Templates);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-083")]
    public Task DroppedFileAvailableWithoutRestart() => TestUiThread.RunAsync(async () =>
    {
        // Acceptance 2: a file dropped into the folder while the IDE runs is used without a restart.
        var ide = Start(new Dictionary<string, string>());
        var changes = 0;
        ide.Templates.Changed += (_, _) => changes++;
        var doc = await OpenAsync(ide, Head + "  |\nend;\nend.\n");
        ide.Drop("drop.json", File(T("drop", "Dropped($end$);", "dropped")));
        ide.Clock.Advance(Debounce - TimeSpan.FromMilliseconds(1));
        Assert.Null(ide.Templates.Find("drop"));
        ide.Watchers.Raise(new FileChange(FileChangeKind.Changed, ide.PathOf("drop.json"))); // debounced after the last event
        ide.Clock.Advance(Debounce - TimeSpan.FromMilliseconds(1));
        Assert.Null(ide.Templates.Find("drop"));
        Assert.Equal(0, changes);
        ide.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.NotNull(ide.Templates.Find("drop"));
        Assert.Equal(1, changes);

        // Ctrl+J lists it, Tab expands it.
        await ide.Services.Commands.ExecuteAsync("edit.insertTemplate");
        Assert.Contains("drop", doc.Completion!.Items.Select(i => i.Label));
        Assert.True(doc.HandleKey("Escape"));
        Type(doc, "drop");
        Assert.True(doc.HandleKey("Tab"));
        Assert.Equal(Head + "  Dropped(|);\nend;\nend.\n", Marked(doc));

        // The folder itself created while the IDE runs: watched from then on.
        var late = Start(null);
        Assert.False(late.Fs.DirectoryExists(late.Folder));
        late.Fs.CreateDirectory(late.Folder);
        late.Fs.WriteAllBytes(late.PathOf("late.json"), Encoding.UTF8.GetBytes(File(T("late", "late;"))));
        late.Watchers.Raise(new FileChange(FileChangeKind.Created, late.Folder));
        late.Clock.Advance(Debounce);
        Assert.NotNull(late.Templates.Find("late"));
        late.Drop("later.json", File(T("later", "later;")));
        late.Clock.Advance(Debounce);
        Assert.NotNull(late.Templates.Find("later"));
        Assert.Empty(late.Rows);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-083")]
    public void InvalidFileSkippedAndReportedInMessages()
    {
        // Acceptance 3: a file that cannot be used is skipped as a whole and reported once in Messages and the log.
        var ide = Start(new Dictionary<string, string>
        {
            ["1-json.json"] = "{ \"formatVersion\": 1,\n  \"templates\": [ { \"abbreviation\": \"j1\" },, ] }",
            ["2-version.json"] = "{ \"formatVersion\": 2, \"templates\": [" + T("v1", "v;") + "] }",
            ["3-deleted.json"] = "{ \"formatVersion\": 1, \"templates\": [" + T("d1", "d;") + ", { \"abbreviation\": \"d2\", \"language\": \"pascal\", \"body\": \"x\", \"deleted\": false } ] }",
            ["4-abbrev.json"] = File(T("ok4", "ok;"), T("1x", "x;")),
            ["5-empty.json"] = File(T("ok5", "ok;"), T("e5", "")),
            ["6-language.json"] = File(T("ok6", "ok;"), T("l6", "l;", language: "cobol")),
            ["7-dollar.json"] = File(T("ok7", "ok;"), T("u7", "x := 1;\n  y := $value;")),
            ["8-good.json"] = File(T("good", "good;")),
        });
        Assert.NotNull(ide.Templates.Find("good"));
        Assert.All((string[])["j1", "v1", "d1", "d2", "ok4", "ok5", "e5", "ok6", "l6", "ok7", "u7"], a => Assert.Null(ide.Templates.Find(a)));

        string[] expected =
        [
            Skipped("1-json.json", Format(Strings.Msg_Templates_InvalidJson, 2, 43)),
            Skipped("2-version.json", Strings.Msg_Templates_WrongFormat),
            Skipped("3-deleted.json", Format(Strings.Msg_Templates_DeletedNotAllowed, "d2")),
            Skipped("4-abbrev.json", Format(Strings.Msg_Templates_InvalidAbbreviation, "1x")),
            Skipped("5-empty.json", Format(Strings.Msg_Templates_EmptyBody, "e5")),
            Skipped("6-language.json", Format(Strings.Msg_Templates_UnknownLanguage, "l6", "cobol")),
            Skipped("7-dollar.json", Format(Strings.Msg_Templates_Unterminated, "u7", 2, 8)),
        ];
        Assert.Equal(expected, ide.Rows.Select(r => r.Text));
        Assert.All(ide.Rows, r => Assert.Equal((MessageSeverity.Warning, "", false), (r.Severity, r.Code, r.IsSummary)));
        Assert.Equal(7, ide.Services.Messages.WarningCount);
        Assert.Equal((ide.PathOf("1-json.json"), 2, 43), (ide.Rows[0].Location!.FilePath, ide.Rows[0].Location!.Line, ide.Rows[0].Location!.Column));
        Assert.Equal((ide.PathOf("2-version.json"), 1, 1), (ide.Rows[1].Location!.FilePath, ide.Rows[1].Location!.Line, ide.Rows[1].Location!.Column));
        Assert.True(ide.Rows[0].IsNavigable);
        Assert.All(expected, text => Assert.Single(ide.Log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.EndsWith(": " + text, StringComparison.Ordinal)));

        // A change of one file reports that file again (once per change), the others not; a fixed file is used.
        ide.Services.Messages.AddSummary("Build succeeded", MessageSeverity.Info);
        ide.Drop("6-language.json", File(T("ok6", "ok;"), T("l6", "l;")), FileChangeKind.Changed);
        ide.Drop("7-dollar.json", File(T("ok7", "ok;"), T("u7", "$$x$")), FileChangeKind.Changed);
        ide.Watchers.Raise(new FileChange(FileChangeKind.Changed, ide.PathOf("7-dollar.json")));
        ide.Clock.Advance(Debounce);
        Assert.NotNull(ide.Templates.Find("l6"));
        Assert.Null(ide.Templates.Find("u7"));
        Assert.Equal(9, ide.Rows.Count);
        Assert.Equal(Skipped("7-dollar.json", Format(Strings.Msg_Templates_Unterminated, "u7", 1, 4)), ide.Rows[7].Text); // before the summary row
        Assert.True(ide.Rows[8].IsSummary);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], ide.Rows.Select(r => r.Index));
        Assert.Equal(8, ide.Services.Messages.WarningCount);

        // A locked file is read again 100 ms apart, 3 times; still locked after that, it is reported as unreadable.
        var locks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [ide.PathOf("locked.json")] = 2, [ide.PathOf("stuck.json")] = 4 };
        ide.Fs.BeforeRead = path =>
        {
            if (locks.TryGetValue(path, out var n) && n > 0)
            {
                locks[path] = n - 1;
                throw new IOException("The file is in use.", unchecked((int)0x80070020));
            }
        };
        ide.Drop("locked.json", File(T("locked", "locked;")));
        ide.Drop("stuck.json", File(T("stuck", "stuck;")));
        ide.Clock.Advance(Debounce);
        Assert.Null(ide.Templates.Find("locked"));
        ide.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Null(ide.Templates.Find("locked"));
        ide.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.NotNull(ide.Templates.Find("locked"));
        Assert.Equal(9, ide.Rows.Count);
        ide.Clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Null(ide.Templates.Find("stuck"));
        Assert.Equal(10, ide.Rows.Count);
        Assert.Equal(Skipped("stuck.json", Format(Strings.Msg_Templates_Unreadable, "The file is in use.")), ide.Rows[8].Text);
        Assert.Equal(0, locks[ide.PathOf("stuck.json")]); // the first read and 3 retries
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-083")]
    public void FolderTimestampChangeReadsNothingAgain()
    {
        // Writing a file in the folder also changes the folder's last-write time, which the R watcher reports as a change
        // of the folder itself: that is no reason to read the folder again (an invalid file would be reported twice).
        var ide = Start(new Dictionary<string, string>());
        ide.Drop("bad.json", File(T("bad", "cost $5")));
        ide.Watchers.Raise(new FileChange(FileChangeKind.Changed, ide.Folder));
        ide.Clock.Advance(Debounce);
        ide.Watchers.Raise(new FileChange(FileChangeKind.Changed, ide.Folder)); // also when it arrives after the debounce
        ide.Clock.Advance(Debounce);
        Assert.Equal([Skipped("bad.json", Format(Strings.Msg_Templates_Unterminated, "bad", 1, 6))], ide.Rows.Select(r => r.Text));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-083")]
    public Task ChangedFileReplacesItsTemplates() => TestUiThread.RunAsync(async () =>
    {
        var ide = Start(new Dictionary<string, string>
        {
            ["team.json"] = File(T("a", "a := 1;"), T("b", "b := 1;"), T("wl", "WriteLn(Team);")),
            ["mine.json"] = File(T("m", "m;")),
        });
        var doc = await OpenAsync(ide, Head + "  |\nend;\nend.\n");
        var changes = 0;
        ide.Templates.Changed += (_, _) => changes++;
        ide.Drop("team.json", File(T("b", "b := 2;$end$"), T("c", "c := 2;")), FileChangeKind.Changed);
        ide.Clock.Advance(Debounce);
        Assert.Equal(1, changes);
        Assert.Null(ide.Templates.Find("a"));
        Assert.Equal("b := 2;$end$", ide.Templates.Find("b")!.Body);
        Assert.NotNull(ide.Templates.Find("c"));
        Assert.NotNull(ide.Templates.Find("m")); // the other file's templates stay
        Assert.Equal(CodeTemplateService.BuiltIn("wl"), ide.Templates.Find("wl")); // no longer overridden
        Assert.Equal(new CodeTemplateOrigin(CodeTemplateOriginKind.BuiltIn), ide.Templates.OriginOf(ide.Templates.Find("wl")!));

        Type(doc, "b");
        Assert.True(doc.HandleKey("Tab"));
        Assert.Equal(Head + "  b := 2;|\nend;\nend.\n", Marked(doc));

        // A file made invalid by a change: its templates are gone, it is reported.
        ide.Drop("team.json", "{ broken", FileChangeKind.Changed);
        ide.Clock.Advance(Debounce);
        Assert.Null(ide.Templates.Find("b"));
        Assert.Single(ide.Rows);
        Assert.Equal(2, changes);
    });

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-SET-083")]
    public void DeletedFileRemovesItsTemplates()
    {
        var ide = Start(new Dictionary<string, string>
        {
            ["team.json"] = File(T("a", "a;"), T("begin", "begin $end$ end;")),
            ["mine.json"] = File(T("m", "m;")),
        });
        Assert.Equal("begin $end$ end;", ide.Templates.Find("begin")!.Body);

        // Deleted: its templates are gone, an overridden built-in is back.
        ide.Fs.DeleteFile(ide.PathOf("team.json"));
        ide.Watchers.Raise(new FileChange(FileChangeKind.Deleted, ide.PathOf("team.json")));
        ide.Clock.Advance(Debounce);
        Assert.Null(ide.Templates.Find("a"));
        Assert.Equal(CodeTemplateService.BuiltIn("begin"), ide.Templates.Find("begin"));
        Assert.NotNull(ide.Templates.Find("m"));

        // Renamed to another extension: gone; renamed back: there again.
        ide.Fs.MoveFile(ide.PathOf("mine.json"), ide.PathOf("mine.off"), overwrite: false);
        ide.Watchers.Raise(new FileChange(FileChangeKind.Renamed, ide.PathOf("mine.off"), ide.PathOf("mine.json")));
        ide.Clock.Advance(Debounce);
        Assert.Null(ide.Templates.Find("m"));
        ide.Fs.MoveFile(ide.PathOf("mine.off"), ide.PathOf("other.json"), overwrite: false);
        ide.Watchers.Raise(new FileChange(FileChangeKind.Renamed, ide.PathOf("other.json"), ide.PathOf("mine.off")));
        ide.Clock.Advance(Debounce);
        Assert.Equal(new CodeTemplateOrigin(CodeTemplateOriginKind.Folder, "other.json"), ide.Templates.OriginOf(ide.Templates.Find("m")!));

        // The folder deleted: every folder template is gone.
        ide.Fs.DeleteFile(ide.PathOf("other.json"));
        ide.Fs.DeleteDirectory(ide.Folder);
        ide.Watchers.Raise(new FileChange(FileChangeKind.Deleted, ide.Folder));
        ide.Clock.Advance(Debounce);
        Assert.Null(ide.Templates.Find("m"));
        Assert.Equal(CodeTemplateService.BuiltIns, ide.Templates.Templates);
        Assert.Empty(ide.Rows);
    }
}
