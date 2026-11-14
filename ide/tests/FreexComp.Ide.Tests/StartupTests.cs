using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests;

/// <summary>Start-up (phase1-shell.md §2, IDE-ARCH-015 step 9): command line, last project, safe mode.</summary>
[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed class StartupTests
{
    private const string WelcomeSetting = "environment.showWelcomeOnStartup";

    private static string Pproj(string name, params string[] files) =>
        $$"""{ "formatVersion": 1, "name": "{{name}}", "mainSource": "{{name}}.pas", "files": [ {{string.Join(", ", files.Select(f => $$"""{ "path": "{{f}}" }"""))}} ] }""";

    /// <summary>Folder <paramref name="name"/> with <c>name.pproj</c> (main source and <c>Unit1.pas</c>).</summary>
    private static void AddProject(HarnessOptions o, string name)
    {
        o.InitialFiles[$"{name}/{name}.pproj"] = Pproj(name, $"{name}.pas", "Unit1.pas");
        o.InitialFiles[$"{name}/{name}.pas"] = $"program {name};\nbegin\nend.\n";
        o.InitialFiles[$"{name}/Unit1.pas"] = "unit Unit1;\ninterface\nimplementation\nend.\n";
    }

    private static byte[] Recent(params (string Path, string LastOpened, bool OpenAtExit)[] projects) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1,
            projects = projects.Select(p => new { path = p.Path, lastOpened = p.LastOpened, pinned = false, openAtExit = p.OpenAtExit }),
            files = Array.Empty<object>(),
        });

    /// <summary><see cref="HeadlessHarness.TempDir"/> of a harness started with <see cref="HarnessOptions.Root"/> = <paramref name="root"/>.</summary>
    private static string Work(string root) => Path.Combine(root, "work");

    private static bool HasWelcome(HeadlessHarness h) => h.Services.Documents.Documents.OfType<WelcomeDocumentViewModel>().Any();

    private static string[] OpenFiles(HeadlessHarness h) =>
        [.. h.Services.Documents.Documents.Select(d => d.FilePath).OfType<string>().Select(p => Path.GetRelativePath(h.TempDir, p).Replace('\\', '/'))];

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-010")]
    [Trait("Req", "IDE-P1-015")]
    public async Task CommandLineOpensProjectAndFiles()
    {
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            AddProject(o, "Hello");
            AddProject(o, "Other");
            o.InitialFiles["notes.txt"] = "notes";
            o.InitialFiles["All.pgroup"] = "{}";
            o.Settings.Remove(WelcomeSetting); // the Welcome page would show without command-line paths
            o.CommandLineArgs.AddRange(["Hello/Hello.pproj", "Other/Other.pproj", "notes.txt", "missing.pas", "--new-window", "All.pgroup"]);
            o.MessageAnswers["Msg.File.NotFound"] = "OK";
            o.MessageAnswers["Msg.Project.OpenFailed"] = "OK";
        });
        await h.Startup;

        // The first project opens as the project; the second is ignored with a log warning; other files open as documents.
        Assert.Equal(Path.Combine(h.TempDir, "Hello", "Hello.pproj"), h.Services.Projects.Project?.FilePath);
        Assert.Equal(["Hello/Hello.pas", "notes.txt"], OpenFiles(h));
        Assert.Contains(h.Log.Lines, l => l.StartsWith("WARN", StringComparison.Ordinal) && l.Contains("Other.pproj", StringComparison.Ordinal));
        Assert.False(HasWelcome(h)); // IDE-P1-015: command-line paths come first

        // A missing path and a project group are reported after the window is shown, in command-line order.
        Assert.Equal(
            [
                ("Msg.File.NotFound", Path.Combine(h.TempDir, "missing.pas")),
                ("Msg.Project.OpenFailed", Path.Combine(h.TempDir, "All.pgroup")),
            ],
            h.Messages.Requests.Select(r => (r.MessageId, r.Args[0])));
        Assert.Equal(Strings.Reason_GroupsNotSupported, h.Messages.Requests[1].Args[1]);
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-016")]
    [Trait("Req", "IDE-P1-015")]
    [Trait("Req", "UI-WIN-071")]
    public async Task ReopensLastProjectWhenEnabled()
    {
        // The flagged project with the newest lastOpened is reopened with its session (not the newer unflagged one).
        var root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        string Abs(string relative) => Path.Combine(Work(root), relative);
        void Setup(HarnessOptions o)
        {
            AddProject(o, "A");
            AddProject(o, "B");
            o.InitialFiles["A/.freexcomp/session.json"] =
                """{ "formatVersion": 1, "project": "A.pproj", "documents": [ { "path": "A.pas" }, { "path": "Unit1.pas" } ], "activeDocument": "Unit1.pas" }""";
            o.Settings.Remove(WelcomeSetting);
        }

        await using (var h = await HeadlessHarness.StartAsync(o =>
        {
            Setup(o);
            o.UserFiles["roaming/recent.json"] = Recent(
                (Abs(@"B\B.pproj"), "2026-11-13T11:00:00Z", false),
                (Abs(@"A\A.pproj"), "2026-11-13T10:00:00Z", true),
                (Abs(@"C\C.pproj"), "2026-11-13T09:00:00Z", true));
            o.Root = root;
        }))
        {
            await h.Startup;
            Assert.Equal(Abs(@"A\A.pproj"), h.Services.Projects.Project?.FilePath);
            Assert.Equal(["A/A.pas", "A/Unit1.pas"], OpenFiles(h));
            Assert.Equal(Abs(@"A\Unit1.pas"), h.Services.Documents.ActiveDocument?.FilePath);
            Assert.False(HasWelcome(h));

            // Closing the project (the user) clears the flag; opening one sets it.
            await h.ExecuteAsync("file.closeProject");
            Assert.False(h.Services.RecentItems.Projects.Single(p => p.Path == Abs(@"A\A.pproj")).OpenAtExit);
            Assert.Equal(Abs(@"C\C.pproj"), h.Services.RecentItems.LastProject?.Path); // the other flagged entry stays
            using (var recent = JsonDocument.Parse(File.ReadAllText(Path.Combine(h.UserDir, "roaming", "recent.json"))))
            {
                var a = recent.RootElement.GetProperty("projects").EnumerateArray().Single(p => p.GetProperty("path").GetString() == Abs(@"A\A.pproj"));
                Assert.False(a.TryGetProperty("openAtExit", out _)); // written only while set
            }

            Assert.True(await h.Services.Projects.OpenAsync(Abs(@"B\B.pproj")));
            Assert.Equal(Abs(@"B\B.pproj"), h.Services.RecentItems.LastProject?.Path);
        }

        // The newest flagged project no longer exists: no message, on to the Welcome page and the loose-file session.
        root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        await using (var h = await HeadlessHarness.StartAsync(o =>
        {
            Setup(o);
            o.InitialFiles["notes.txt"] = "notes";
            o.UserFiles["roaming/recent.json"] = Recent(
                (Abs(@"Gone\Gone.pproj"), "2026-11-13T12:00:00Z", true),
                (Abs(@"A\A.pproj"), "2026-11-13T10:00:00Z", true));
            o.UserFiles["local/session-noproject.json"] = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { formatVersion = 1, documents = new[] { new { path = Abs("notes.txt") } } }));
            o.Root = root;
        }))
        {
            await h.Startup;
            Assert.Null(h.Services.Projects.Project);
            Assert.True(HasWelcome(h));
            Assert.Equal(["notes.txt"], OpenFiles(h));
            Assert.Empty(h.Messages.Requests);
        }

        // environment.reopenLastProject = false: straight to the Welcome page.
        await using (var h = await HeadlessHarness.StartAsync(o =>
        {
            Setup(o);
            o.Settings[SettingsCatalog.EnvironmentReopenLastProject.Name] = false;
            o.UserFiles["roaming/recent.json"] = Recent((Abs(@"A\A.pproj"), "2026-11-13T10:00:00Z", true));
        }))
        {
            await h.Startup;
            Assert.Null(h.Services.Projects.Project);
            Assert.True(HasWelcome(h));
        }
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "IDE-P1-012")]
    public async Task SafeModeWritesNoUserFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        void Setup(HarnessOptions o)
        {
            AddProject(o, "A");
            o.Settings[SettingsCatalog.EditorTabWidth.Name] = 7;
            o.UserFiles["roaming/uistate.json"] = "not json"u8.ToArray(); // normally renamed, with a status message
            o.UserFiles["roaming/recent.json"] = Recent((Path.Combine(Work(root), "A", "A.pproj"), "2026-11-13T10:00:00Z", true));
            var orphan = Path.Combine("local", "recovery", "dead-instance");
            o.UserFiles[Path.Combine(orphan, "owner.json")] =
                JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, processId = int.MaxValue, startTimeTicks = 1L });
            o.UserFiles[Path.Combine(orphan, "0f8fad5b-d9cb-469f-a165-70867728950e.txt")] = [0xEF, 0xBB, 0xBF, .. "recovered"u8];
            o.UserFiles[Path.Combine(orphan, "0f8fad5b-d9cb-469f-a165-70867728950e.json")] = JsonSerializer.SerializeToUtf8Bytes(new
            {
                formatVersion = 1, path = (string?)null, title = "Unit9.pas", encoding = "utf-8", lineEnding = "crlf",
                savedUtc = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc), baseTimestampUtc = (DateTime?)null,
            });
            o.Root = root;
        }

        var h = await HeadlessHarness.StartAsync(o =>
        {
            Setup(o);
            o.CommandLineArgs.Add("--SAFE");
        });
        try
        {
            await h.Startup;

            // Default settings and layout, no reopened project, no recovery prompt.
            Assert.True(h.Services.Options.Safe);
            Assert.Equal(SettingsCatalog.EditorTabWidth.Default, h.Services.Settings.Get(SettingsCatalog.EditorTabWidth));
            Assert.True(HasWelcome(h)); // the harness preset (false) is in settings.json, which is not read
            Assert.Null(h.Services.Projects.Project);
            Assert.DoesNotContain(h.Services.Documents.Documents, d => d.Title == "Unit9.pas");
            Assert.Equal("", h.Status.Message);

            // Changes that normally write user files: a setting, a project opened (recent, session) and closed.
            h.Services.Settings.Set(SettingsCatalog.EditorTabWidth, 3);
            Assert.True(await h.Services.Projects.OpenAsync(Path.Combine(h.TempDir, "A", "A.pproj")));
            Assert.Single(h.Services.Documents.Documents.OfType<TextDocumentViewModel>()); // no session restored
            await h.ExecuteAsync("file.closeProject");
            h.Clock.Advance(TimeSpan.FromMinutes(1));
            await h.Services.Settings.FlushAsync();
            await h.Services.Keymap.FlushAsync();
        }
        finally
        {
            await h.DisposeAsync(); // the window closes: the exit-time writes
        }

        string[] writes = ["WriteAllBytes", "WriteAllBytesDurable", "MoveFile", "ReplaceFile", "OverwriteFile", "DeleteFile", "DeleteDirectory", "CreateDirectory"];
        var written = h.FileSystem.Calls.Where(c => writes.Any(w => c.StartsWith(w + " ", StringComparison.Ordinal))
            && (c.Contains(h.UserDir, StringComparison.OrdinalIgnoreCase) || c.Contains(".freexcomp", StringComparison.OrdinalIgnoreCase)));
        Assert.Empty(written);

        // The same user files without --safe: read, applied and written (the fixture is effective).
        root = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", Guid.NewGuid().ToString("N"));
        await using var normal = await HeadlessHarness.StartAsync(o =>
        {
            Setup(o);
            o.Settings[WelcomeSetting] = false;
        });
        await normal.Startup;
        await normal.WaitForAsync(() => normal.Services.Documents.Documents.Any(d => d.Title == "Unit9.pas"), "recovered copy opened");
        Assert.Equal(7, normal.Services.Settings.Get(SettingsCatalog.EditorTabWidth));
        Assert.Equal(Path.Combine(normal.TempDir, "A", "A.pproj"), normal.Services.Projects.Project?.FilePath);
        Assert.Single(Directory.GetFiles(Path.Combine(normal.UserDir, "roaming"), "uistate.corrupt-*.json"));
        await normal.Services.Recovery.WhenIdle();
        await normal.Services.Recovery.DeleteInstanceDirectoryAsync();
    }
}
