using System.Globalization;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Build;

/// <summary>
/// The question before a build of a project with commands (phase8 trust-prompt.md §2, card F09-008): fake engine, scripted
/// dialogs and pickers, in-memory files; the trust list is <c>&lt;user dir&gt;\roaming\trust.json</c> of the composition.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class TrustPromptTests
{
    private const string Events = """, "events": { "preBuild": [ "tools\\gen.exe --out  src\\Gen.pas " ], "postBuild": [ "copy /y a b", "echo done" ] }""";
    private const string Configurations =
        """, "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } }""";
    private const string Exe = BuildTestBed.Dir + @"\bin\Debug\Hello.exe";

    private static string TrustFile(BuildTestBed bed) => Path.Combine(bed.Services.UserPaths.RoamingDirectory, "trust.json");

    private static async Task<BuildTestBed> StartAsync(string extra = Events)
    {
        var bed = new BuildTestBed();
        bed.Engine.DefaultOutputPath = Exe;
        bed.Fs.CreateDirectory(BuildTestBed.Dir + @"\bin\Debug");
        await bed.OpenHelloAsync(extra: Configurations + extra);
        return bed;
    }

    /// <summary>Scripts the prompt: every one shown is recorded in the returned list and answered by <paramref name="drive"/>.</summary>
    private static List<TrustProjectViewModel> Script(BuildTestBed bed, Func<TrustProjectViewModel, Task> drive)
    {
        var shown = new List<TrustProjectViewModel>();
        bed.Dialogs.Script(TrustProjectViewModel.DialogName, async vm =>
        {
            var prompt = Assert.IsType<TrustProjectViewModel>(vm);
            shown.Add(prompt);
            await drive(prompt);
        });
        return shown;
    }

    private static Task Press(TrustProjectViewModel prompt, string buttonId) => prompt.Buttons.Single(b => b.Id == buttonId).PressAsync();

    private static void WriteProject(BuildTestBed bed, string path, string name, string extra) =>
        bed.File(path, $$"""
            { "formatVersion": 1, "name": "{{name}}", "appType": "console", "mainSource": "{{name}}.pas",
              "files": [ { "path": "{{name}}.pas" } ]{{extra}} }
            """);

    /// <summary>IDE-TRUST-003, 004 (card acceptance 4): no commands → no dialog for build, rebuild and run; no trust file.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-003")]
    [Trait("Req", "IDE-TRUST-004")]
    public Task ProjectWithoutEventsNeverPrompts() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync(extra: "");
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Rebuild));
        Assert.True(await bed.Services.Run.StartAsync());
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Services.Run.ProgramEnded += (_, _) => ended.TrySetResult();
        bed.Processes.Last.Exit(0);
        await ended.Task;

        Assert.Empty(bed.Dialogs.Dialogs); // an unscripted dialog would also have thrown
        Assert.Equal(3, bed.Engine.Requests.Count);
        Assert.False(bed.Fs.FileExists(TrustFile(bed)));
        Assert.False(bed.Services.Trust.IsPromptOpen);
    });

    /// <summary>IDE-TRUST-003, 005, 013: the prompt lists the project and its commands verbatim, in file order; the IDE is still Idle.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-003")]
    [Trait("Req", "IDE-TRUST-005")]
    [Trait("Req", "IDE-TRUST-013")]
    public Task UntrustedProjectPromptsWithItsCommands() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var states = new List<IdeState>();
        bed.Services.State.Changed += (_, _) => states.Add(bed.Services.State.Current.State);
        var finished = 0;
        bed.Services.Build.BuildFinished += (_, _) => finished++;
        var shown = Script(bed, async prompt =>
        {
            // While the prompt is open: Idle, nothing reached the engine, no build event.
            Assert.True(bed.Services.Trust.IsPromptOpen);
            Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);
            Assert.Empty(states);
            Assert.Empty(bed.Engine.Requests);
            Assert.Equal(0, finished);
            Assert.Empty(bed.Services.Messages.Items);
            await Press(prompt, "Cancel");
        });

        Assert.Null(await bed.Services.Build.BuildAsync(BuildKind.Build));

        var prompt = Assert.Single(shown);
        Assert.Equal("Project Commands — Hello", prompt.Title);
        Assert.Equal(Strings.Msg_Trust_Intro, prompt.Intro);
        Assert.Equal(
        [
            new TrustRow(TrustRowKind.Project, 0, -1, "", BuildTestBed.ProjectPath),
            new TrustRow(TrustRowKind.Command, 0, 0, "Before build", @"tools\gen.exe --out  src\Gen.pas "), // not trimmed
            new TrustRow(TrustRowKind.Command, 0, 1, "After build", "copy /y a b"),
            new TrustRow(TrustRowKind.Command, 0, 2, "After build", "echo done"),
        ], prompt.Rows);
        Assert.Equal(@"Before build: tools\gen.exe --out  src\Gen.pas ", prompt.Rows[1].AutomationName);
        Assert.Null(prompt.Error);
        Assert.Equal(["BuildWithoutEvents", "TrustProject", "TrustFolder", "Cancel"], prompt.Buttons.Select(b => b.Id));
        Assert.Equal("BuildWithoutEvents", prompt.DefaultButton!.Id); // UI-DLG-291: the answer that stores nothing
        Assert.Equal("Cancel", prompt.CancelButton!.Id);
        Assert.Equal("Trust This Project", prompt.Buttons[1].Label);
        Assert.False(bed.Services.Trust.IsPromptOpen);

        // Rebuild and the run commands ask too.
        Assert.Null(await bed.Services.Build.BuildAsync(BuildKind.Rebuild));
        Assert.False(await bed.Services.Run.StartAsync());
        Assert.Equal(3, shown.Count);
        Assert.Empty(bed.Engine.Requests);
    });

    /// <summary>IDE-TRUST-001, 002: one store on &lt;roaming&gt;\trust.json; every request carries it with RequireTrust.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-001")]
    [Trait("Req", "IDE-TRUST-002")]
    public Task RequestsCarryStoreAndRequireTrust() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync(extra: "");
        var store = Assert.IsType<FileTrustStore>(bed.Services.Trust.Store);
        Assert.Equal(TrustFile(bed), store.FilePath);
        Assert.Equal(ProjectTrust.DefaultStorePath(name => name == "FREEXCOMP_USER_DIR" ? Path.GetDirectoryName(bed.Services.UserPaths.RoamingDirectory) : null),
            store.FilePath); // the file the command-line build reads for the same user directory (CMP-1230)

        await bed.Services.Build.BuildAsync(BuildKind.Build);
        await bed.Services.Build.BuildAsync(BuildKind.Rebuild);
        await bed.Services.Build.BuildAsync(BuildKind.Clean);
        Assert.True(await bed.Services.Run.StartAsync());
        Assert.Equal(4, bed.Engine.Requests.Count);
        Assert.All(bed.Engine.Requests, r =>
        {
            Assert.Same(store, r.TrustStore);
            Assert.Equal(EventsPolicy.RequireTrust, r.Events);
        });

        // Nothing is cached: an entry written by another process is honoured by the next check.
        var other = await StartAsync();
        new FileTrustStore(new Core.Projects.CompilerFileSystemAdapter(other.Fs), TrustFile(other)).TrustFolder(@"C:\p");
        Assert.NotNull(await other.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Empty(other.Dialogs.Dialogs);
        Assert.Equal(EventsPolicy.RequireTrust, Assert.Single(other.Engine.Requests).Events);
    });

    /// <summary>IDE-TRUST-006: the shown commands' hash is stored for the path; the build runs with RequireTrust; no second question.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-006")]
    public Task TrustThisProjectStoresTheShownHashAndBuilds() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var shown = Script(bed, prompt => Press(prompt, "TrustProject"));

        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);

        Assert.True(result!.Success);
        Assert.Single(shown);
        var entry = Assert.Single(bed.Services.Trust.Store.Read().Entries);
        Assert.Equal(new TrustEntry(TrustEntryKind.Project, BuildTestBed.ProjectPath, ProjectTrust.CommandsHash(bed.Services.Projects.Definition!)), entry);
        Assert.Contains(BuildTestBed.ProjectPath.Replace(@"\", @"\\", StringComparison.Ordinal), bed.Text(TrustFile(bed)), StringComparison.Ordinal);
        var request = Assert.Single(bed.Engine.Requests);
        Assert.Equal(EventsPolicy.RequireTrust, request.Events);
        Assert.Same(bed.Services.Trust.Store, request.TrustStore);
        Assert.Empty(ProjectTrust.FindUntrusted(new Core.Projects.CompilerFileSystemAdapter(bed.Fs), request.ProjectPath, request.ProjectInGroup, request.TrustStore));

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.True(await bed.Services.Run.StartAsync());
        Assert.Single(shown); // not asked again
        Assert.Equal(3, bed.Engine.Requests.Count);
    });

    /// <summary>IDE-TRUST-005, 006 (card acceptance 3): changed commands of a trusted project ask again, with the note.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-005")]
    [Trait("Req", "IDE-TRUST-006")]
    public Task EditingPostBuildAfterTrustPromptsAgain() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var shown = Script(bed, prompt => Press(prompt, "TrustProject"));
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.DoesNotContain(Assert.Single(shown).Rows, r => r.Kind == TrustRowKind.Changed); // never trusted before: no note

        var definition = bed.Services.Projects.Definition!;
        Assert.True(await bed.Services.Projects.ApplyOptionsAsync(
            definition with { Events = definition.Events with { PostBuild = ["copy /y a b", "del /q c"] } }));

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Equal(2, shown.Count);
        Assert.Equal(TrustState.Changed, Assert.Single(shown[1].Projects).State);
        Assert.Equal(
        [
            new TrustRow(TrustRowKind.Project, 0, -1, "", BuildTestBed.ProjectPath),
            new TrustRow(TrustRowKind.Changed, 0, -1, "", Strings.Msg_Trust_Changed), // UI-DLG-293: the first child row
            new TrustRow(TrustRowKind.Command, 0, 0, "Before build", @"tools\gen.exe --out  src\Gen.pas "),
            new TrustRow(TrustRowKind.Command, 0, 1, "After build", "copy /y a b"),
            new TrustRow(TrustRowKind.Command, 0, 2, "After build", "del /q c"),
        ], shown[1].Rows);

        // Trusted again with the new commands: the entry is replaced, the next build is silent.
        var entry = Assert.Single(bed.Services.Trust.Store.Read().Entries);
        Assert.Equal(ProjectTrust.CommandsHash(bed.Services.Projects.Definition!), entry.CommandsHash);
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Equal(2, shown.Count);
    });

    /// <summary>IDE-TRUST-007: one build with Events = Skip and its Output line; nothing stored; the next build asks again.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-007")]
    [Trait("Req", "IDE-TRUST-002")]
    public Task BuildWithoutEventsSkipsOnceAndStoresNothing() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var shown = Script(bed, prompt => prompt.PressDefaultAsync()); // Enter

        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);

        Assert.True(result!.Success);
        var request = Assert.Single(bed.Engine.Requests);
        Assert.Equal(EventsPolicy.Skip, request.Events);
        Assert.Same(bed.Services.Trust.Store, request.TrustStore);
        var output = bed.OutputText(OutputChannels.Build);
        var header = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Build_StartedHeader, "Hello", "Debug", "win64");
        Assert.Equal(header, output[0]);
        Assert.Equal(Strings.Msg_Trust_EventsSkipped, output[1]); // directly after the started header
        Assert.Single(output, Strings.Msg_Trust_EventsSkipped);
        Assert.False(bed.Fs.FileExists(TrustFile(bed)));

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Rebuild));
        Assert.Equal(2, shown.Count); // asked again
        Assert.Equal([EventsPolicy.Skip, EventsPolicy.Skip], bed.Engine.Requests.Select(r => r.Events));
        Assert.DoesNotContain(bed.Engine.Requests, r => r.Events == EventsPolicy.Run);
        Assert.False(bed.Fs.FileExists(TrustFile(bed)));
    });

    /// <summary>IDE-TRUST-008: a trusted folder covers the project and every other project inside it.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-008")]
    public Task TrustFolderCoversProjectsInside() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var pickers = (FakeFileDialogService)bed.Services.FileDialogs;
        pickers.NextFolder(@"C:\p");
        var shown = Script(bed, prompt => Press(prompt, "TrustFolder"));

        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);

        Assert.True(result!.Success);
        Assert.Single(shown);
        Assert.Equal(BuildTestBed.Dir, Assert.Single(pickers.Requests).InitialFolder); // the directory of the first listed project
        Assert.Equal([new TrustEntry(TrustEntryKind.Folder, @"C:\p", null)], bed.Services.Trust.Store.Read().Entries);
        Assert.Equal(EventsPolicy.RequireTrust, Assert.Single(bed.Engine.Requests).Events);

        // A second project in that folder builds without a question.
        WriteProject(bed, @"C:\p\Other\Other.pproj", "Other", Events);
        bed.File(@"C:\p\Other\Other.pas", "program Other;\nbegin\nend.\n");
        Assert.True(await bed.Services.Projects.OpenAsync(@"C:\p\Other\Other.pproj"));
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Single(shown);
        Assert.Equal(@"C:\p\Other\Other.pproj", bed.Engine.Requests[^1].ProjectPath);
    });

    /// <summary>IDE-TRUST-008: a folder that does not contain the projects keeps the prompt, with the message; a cancelled picker changes nothing.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-008")]
    public Task FolderThatDoesNotCoverKeepsThePrompt() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var pickers = (FakeFileDialogService)bed.Services.FileDialogs;
        var shown = Script(bed, async prompt =>
        {
            var rows = prompt.Rows;

            pickers.NextCancel();
            await Press(prompt, "TrustFolder");
            Assert.False(prompt.IsClosed);
            Assert.Null(prompt.Error);
            Assert.Same(rows, prompt.Rows);
            Assert.False(bed.Fs.FileExists(TrustFile(bed)));

            pickers.NextFolder(@"C:\elsewhere");
            await Press(prompt, "TrustFolder");
            Assert.False(prompt.IsClosed);
            Assert.Equal(@"The folder C:\elsewhere is now trusted, but it does not contain the projects still listed.", prompt.Error);
            Assert.Equal(rows, prompt.Rows); // still the same project and commands
            Assert.True(bed.Services.Trust.IsPromptOpen);
            Assert.Empty(bed.Engine.Requests);

            await Press(prompt, "Cancel");
        });

        Assert.Null(await bed.Services.Build.BuildAsync(BuildKind.Build));

        Assert.Single(shown);
        Assert.Empty(bed.Engine.Requests);
        // The folder stays trusted: it was the user's explicit choice.
        Assert.Equal([new TrustEntry(TrustEntryKind.Folder, @"C:\elsewhere", null)], bed.Services.Trust.Store.Read().Entries);
    });

    /// <summary>IDE-TRUST-009: Cancel builds nothing and starts nothing; Idle; Messages and Output untouched; the status text.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-009")]
    public Task CancelStartsNoBuildAndNoRun() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        bed.Services.Messages.AddError("kept");
        bed.Services.Output.Append(OutputChannels.Build, "kept line", OutputKind.Normal);
        var before = bed.OutputText(OutputChannels.Build);
        var states = new List<IdeState>();
        bed.Services.State.Changed += (_, _) => states.Add(bed.Services.State.Current.State);
        var shown = Script(bed, prompt => prompt.PressCancelAsync()); // Escape / the title-bar close

        foreach (var command in (string[])["project.build", "run.start"])
        {
            await bed.Services.Commands.ExecuteAsync(command);

            Assert.Empty(bed.Engine.Requests);
            Assert.Empty(bed.Processes.Started);
            Assert.Empty(states);
            Assert.Equal(IdeStateInfo.Idle, bed.Services.State.Current);
            Assert.Equal(Strings.Msg_Trust_BuildCancelled, bed.Services.Status.Message);
            Assert.Equal("kept", Assert.Single(bed.Services.Messages.Items).Text);
            Assert.Equal([.. before, Strings.Msg_Trust_BuildCancelled], bed.OutputText(OutputChannels.Build).Distinct());
            Assert.Null(bed.Services.Build.ResultText);
        }

        Assert.Equal(2, shown.Count);
        Assert.False(bed.Fs.FileExists(TrustFile(bed)));
    });

    /// <summary>IDE-TRUST-010: a trust list that cannot be written shows Msg.Trust.SaveFailed; the prompt stays open and unchanged.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-010")]
    public Task SaveFailureKeepsThePromptOpen() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var content = bed.File(TrustFile(bed), "{ \"formatVersion\": 1, \"folders\": [], \"projects\": [] }\n");
        bed.Fs.SetReadOnly(content, true);
        var original = bed.Text(content);
        bed.Dialogs.Answer("Msg.Trust.SaveFailed", "OK");
        var pickers = (FakeFileDialogService)bed.Services.FileDialogs;
        var shown = Script(bed, async prompt =>
        {
            var rows = prompt.Rows;
            await Press(prompt, "TrustProject");
            Assert.False(prompt.IsClosed);
            pickers.NextFolder(@"C:\p");
            await Press(prompt, "TrustFolder");
            Assert.False(prompt.IsClosed);
            Assert.Same(rows, prompt.Rows);
            Assert.Null(prompt.Error);
            Assert.True(bed.Services.Trust.IsPromptOpen);
            await Press(prompt, "Cancel");
        });

        Assert.Null(await bed.Services.Build.BuildAsync(BuildKind.Build));

        Assert.Single(shown);
        Assert.Equal(2, bed.Dialogs.Requests.Count);
        Assert.All(bed.Dialogs.Requests, r =>
        {
            Assert.Equal("Msg.Trust.SaveFailed", r.MessageId);
            Assert.False(string.IsNullOrEmpty(Assert.Single(r.Args))); // the system's error text
        });
        Assert.Equal(original, bed.Text(content));
        Assert.Empty(bed.Engine.Requests);
        var definition = MessageDefinitions.Get("Msg.Trust.SaveFailed");
        Assert.Equal(MessageKind.Error, definition.Kind);
        Assert.Equal(["OK"], definition.Buttons);
    });

    /// <summary>IDE-TRUST-004, 005: an untrusted dependency is listed in the same dialog, before the project that needs it.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-004")]
    [Trait("Req", "IDE-TRUST-005")]
    public Task DependenciesAreListedInOneDialog() => TestUiThread.RunAsync(async () =>
    {
        var bed = new BuildTestBed();
        const string Lib = @"C:\p\Lib\Lib.pproj";
        WriteProject(bed, Lib, "Lib", """, "events": { "postBuild": [ "echo lib" ] }""");
        bed.File(@"C:\p\Lib\Lib.pas", "program Lib;\nbegin\nend.\n");
        await bed.OpenHelloAsync(extra: """, "events": { "preBuild": [ "echo app" ] }, "dependencies": [ "../Lib/Lib.pproj" ]""");
        var shown = Script(bed, prompt => Press(prompt, "TrustProject"));

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));

        var prompt = Assert.Single(shown);
        Assert.Equal("Project Commands — Hello", prompt.Title); // the project the command was started for
        Assert.Equal("Trust These Projects", prompt.Buttons[1].Label); // UI-DLG-292
        Assert.Equal(
        [
            new TrustRow(TrustRowKind.Project, 0, -1, "", Lib),
            new TrustRow(TrustRowKind.Command, 0, 0, "After build", "echo lib"), // no empty "Before build" row (UI-DLG-296)
            new TrustRow(TrustRowKind.Project, 1, -1, "", BuildTestBed.ProjectPath),
            new TrustRow(TrustRowKind.Command, 1, 0, "Before build", "echo app"),
        ], prompt.Rows);
        Assert.Equal([Lib, BuildTestBed.ProjectPath], bed.Services.Trust.Store.Read().Entries.Select(e => e.Path));
        Assert.All(bed.Services.Trust.Store.Read().Entries, e => Assert.Equal(TrustEntryKind.Project, e.Kind));

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Single(shown);
    });

    /// <summary>IDE-TRUST-011: trusting writes the trust list only; the project file and its .freexcomp folder keep their bytes.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-011")]
    public Task NothingAboutTrustIsWrittenIntoTheProject() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        bed.File(BuildTestBed.Dir + @"\.freexcomp\session.json", "{ \"formatVersion\": 1 }\n");
        Dictionary<string, string> Snapshot() => bed.Fs.AllFiles
            .Where(f => !string.Equals(f, TrustFile(bed), StringComparison.OrdinalIgnoreCase)
                && !f.StartsWith(BuildTestBed.Dir + @"\bin\", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f, f => Convert.ToHexString(bed.Fs.ReadAllBytes(f)), StringComparer.OrdinalIgnoreCase);
        var before = Snapshot();
        Assert.Contains(BuildTestBed.ProjectPath, before.Keys);
        Assert.Contains(BuildTestBed.Dir + @"\.freexcomp\session.json", before.Keys);
        var pickers = (FakeFileDialogService)bed.Services.FileDialogs;
        pickers.NextFolder(@"C:\elsewhere");
        Script(bed, async prompt =>
        {
            await Press(prompt, "TrustFolder"); // stores a folder and keeps the prompt
            await Press(prompt, "TrustProject");
        });

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));

        Assert.Equal(2, bed.Services.Trust.Store.Read().Entries.Length);
        Assert.Equal(before, Snapshot()); // no file added, removed or changed: project, session, settings, UI state
    });

    /// <summary>IDE-TRUST-003, IDE-EVT-022: Clean and Check Syntax of an untrusted project never ask (and run no command).</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-003")]
    [Trait("Req", "IDE-EVT-022")]
    public Task CleanAndCheckSyntaxNeverPrompt() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();

        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Clean));
        await bed.Services.Build.CheckSyntaxAsync();
        await bed.Services.Commands.ExecuteAsync("project.clean");

        Assert.Empty(bed.Dialogs.Dialogs);
        Assert.Equal([BuildTarget.Clean, BuildTarget.Clean], bed.Engine.Requests.Select(r => r.Target));
        Assert.False(bed.Fs.FileExists(TrustFile(bed)));

        // Opening and saving the project do not ask either.
        Assert.True(await bed.Services.Projects.ApplyOptionsAsync(bed.Services.Projects.Definition!));
        Assert.Empty(bed.Dialogs.Dialogs);
    });

    /// <summary>IDE-TRUST-012: E9021 / W9003 from the engine are ordinary project-level rows; no second prompt.</summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-TRUST-012")]
    public Task EngineDiagnosticE9021IsShownWithoutASecondPrompt() => TestUiThread.RunAsync(async () =>
    {
        var bed = await StartAsync();
        var shown = Script(bed, prompt => Press(prompt, "TrustProject"));
        var e9021 = FakeBuildEngine.Diag("E9021", DiagnosticSeverity.Error, "The project has build commands and is not trusted", BuildTestBed.ProjectPath);
        var w9003 = FakeBuildEngine.Diag("W9003", DiagnosticSeverity.Warning, "The list of trusted locations cannot be read", BuildTestBed.ProjectPath);
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, w9003, e9021)); // the file changed between the listing and the engine's read

        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);

        Assert.False(result!.Success);
        Assert.Single(shown);
        Assert.Single(bed.Engine.Requests);
        var rows = bed.Services.Messages.Items.Where(m => !m.IsSummary).ToList();
        Assert.Equal([("W9003", MessageSeverity.Warning), ("E9021", MessageSeverity.Error)], rows.Select(m => (m.Code, m.Severity)));
        Assert.All(rows, m =>
        {
            Assert.Null(m.Location); // a project-level row like any other: no position inside a file
            Assert.False(m.IsNavigable);
        });
        var output = bed.OutputText(OutputChannels.Build);
        Assert.Contains(e9021.ToText(), output);
        Assert.Contains(w9003.ToText(), output);
        Assert.Equal(Strings.Msg_Build_FailedOne, bed.Services.Build.ResultText);
        Assert.Equal(IdeStateInfo.Idle, bed.Services.State.Current);
    });
}
