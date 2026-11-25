using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Compiler.Targets;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Tests.Build;
using FreexComp.Ide.Core.Tests.Debugging;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.TestSupport;
using BuildKind = FreexComp.Ide.Core.Build.BuildKind;
using IdeStateInfo = FreexComp.Ide.Core.Build.IdeStateInfo;

namespace FreexComp.Ide.Core.Tests.Projects;

/// <summary>
/// The active platform in the IDE (platform-selector.md §2, IDE-PLAT-001…008; UI-TB-022; task I-278): the command
/// <c>project.setPlatform</c>, the tool-bar combo, the Project menu, the status bar and the root badge, the build, run
/// and debug requests and output paths, and the code model. A second installed platform comes from
/// <see cref="TestPlatformCatalog"/>.
/// </summary>
[Trait("Phase", "8")]
public sealed class PlatformSelectorTests
{
    private const string Second = TestPlatformCatalog.SecondId;

    private const string TwoPlatforms = """
        , "platforms": ["win64", "test32"]
        """;

    private const string Unit =
        "unit Unit1;\ninterface\nprocedure Run;\nimplementation\nvar\n  Count: Integer;\nprocedure Run;\nbegin\n" +
        "{$IFDEF WIN64}\n  Count := 64;\n{$ELSE}\n  Count := 32;\n{$ENDIF}\n{$IFDEF LEGACY}\n  Count := 1;\n{$ENDIF}\nend;\nend.\n";

    private static BuildTestBed Bed(Action<TestCompositionOptions>? more = null) => new(o =>
    {
        o.PlatformCatalog = new TestPlatformCatalog();
        more?.Invoke(o);
    });

    /// <summary>The run-time library sources at the bed's library folder, so that programs are analysed completely.</summary>
    private static void WithRtl(BuildTestBed bed)
    {
        var src = Path.Combine(SpecTables.RepoRootPath(), "rtl", "src");
        foreach (var file in Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json")).Where(File.Exists))
            bed.File(@"G:\rtl\" + Path.GetFileName(file), File.ReadAllText(file));
    }

    private static ToolBarComboModel Combo(MainWindowViewModel window) =>
        window.ToolBars.SelectMany(t => t.Items.OfType<ToolBarComboModel>()).Single(c => c.Name == "Platform");

    private static MenuItemModel Submenu(MainWindowViewModel window, string key) =>
        window.MainMenu.Single(m => m.Key == "Project").Children.Single(c => c.Key == key);

    private static (string Label, string? Argument, bool? Bullet)[] Entries(MainWindowViewModel window, string key) =>
        [.. Submenu(window, key).Children.Select(c => (c.Label, c.Argument, c.IsCheckedEntry))];

    private static string Badge(BuildTestBed bed) => bed.Services.ProjectManager.Roots[0].Badge!;

    private static string ProjectFile(BuildTestBed bed) => System.Text.Encoding.UTF8.GetString(bed.Fs.ReadAllBytes(BuildTestBed.ProjectPath));

    private static string? ClassAt(IReadOnlyList<TokenClassSpan> spans, int offset) =>
        spans.FirstOrDefault(s => s.Offset <= offset && offset < s.Offset + s.Length).TokenClass;

    private static IReadOnlyList<TokenClassSpan> Drawn(TextDocumentViewModel doc) =>
        doc.CreateClassifier().Classify(doc.Buffer, new LineRange(1, doc.Buffer.LineCount));

    private static IdeStateInfo Building => new(IdeState.Building, BuildKind.Build, false, null, null);

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-001")]
    [Trait("Req", "IDE-PLAT-002")]
    [Trait("Req", "IDE-PLAT-003")]
    [Trait("Req", "IDE-PLAT-004")]
    [Trait("Req", "UI-TB-022")]
    public Task SetPlatform_WritesActivePlatform_UpdatesComboStatusBadgeAndMenuBullet() => TestUiThread.RunAsync(SetPlatformOnUiThread);

    private static async Task SetPlatformOnUiThread()
    {
        var bed = Bed();
        await bed.OpenHelloAsync(extra: TwoPlatforms);
        var window = new MainWindowViewModel(bed.Services);
        var combo = Combo(window);
        var status = new List<string>();
        window.StatusBar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatusBarViewModel.ConfigurationText))
                status.Add(window.StatusBar.ConfigurationText);
        };

        // The catalogue: the product's platforms and the second descriptor of the tests.
        var catalog = bed.Services.PlatformCatalog;
        Assert.Same(catalog, bed.Services.Projects.PlatformCatalog);
        Assert.Equal([("win64", "Windows 64-bit"), (Second, "Test 32-bit")], catalog.Installed.Select(p => (p.Id, p.DisplayName)));
        Assert.Same(TargetInfo.Win64, catalog.Find("win64")!.Target);
        Assert.Contains("WIN32", catalog.Find(Second)!.Target.PredefinedSymbols);
        Assert.Null(catalog.Find("win32"));
        Assert.Equal(["win64"], BuiltInPlatformCatalog.Instance.Installed.Select(p => p.Id)); // the product: the registry

        // Without the key the active platform is the first listed one; nothing is written.
        var project = bed.Services.Projects.Project!;
        Assert.Equal(["win64", Second], project.Platforms);
        Assert.Equal("win64", project.ActivePlatform);
        Assert.Null(bed.Services.Projects.Definition!.ActivePlatform);
        Assert.Equal(["Win64", "Test32"], combo.Items);
        Assert.Equal("Win64", combo.Selected);
        Assert.Equal("Debug | Win64", window.StatusBar.ConfigurationText);
        Assert.Equal("Debug · Win64", Badge(bed));

        // Project › Active Platform follows the Active Configuration block (the submenu and Configuration Manager…,
        // IDE-CFG-010): one entry per listed platform, the bullet on the active one.
        var menu = window.MainMenu.Single(m => m.Key == "Project").Children.ToList();
        var configuration = menu.FindIndex(m => m.Key == "Project.Configuration");
        Assert.Equal(["project.configurationManager", null, "project.openOutputFolder"], menu.Skip(configuration + 1).Take(3).Select(m => m.CommandId));
        Assert.Equal("Project.Platform", menu[configuration + 2].Key);
        Assert.Equal("Active Pla&tform", Submenu(window, "Project.Platform").Label);
        Assert.Equal([("&1 Win64", "win64", true), ("&2 Test32", Second, false)], Entries(window, "Project.Platform"));
        Assert.All(Submenu(window, "Project.Platform").Children, e => Assert.Equal(("project.setPlatform", MenuToggleKind.Radio), (e.CommandId, e.ToggleKind)));
        Assert.Equal([("&1 Debug", "Debug", true), ("&2 Release", "Release", false)], Entries(window, "Project.Configuration")); // IDE-PM-090

        // project.setPlatform:<id>: the definition, the file, and every place that shows the platform.
        Assert.True(bed.Services.Commands.CanExecute("project.setPlatform", Second));
        await bed.Services.Commands.ExecuteAsync("project.setPlatform", Second);
        project = bed.Services.Projects.Project!;
        Assert.Equal(Second, project.ActivePlatform);
        Assert.Equal(Second, bed.Services.Projects.Definition!.ActivePlatform);
        Assert.False(project.IsModified);
        Assert.Contains("  \"platforms\": [\n    \"win64\",\n    \"test32\"\n  ],\n  \"activePlatform\": \"test32\",\n", // directly after platforms (CMP-1362)
            ProjectFile(bed).Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(["Win64", "Test32"], combo.Items);
        Assert.Equal("Test32", combo.Selected);
        Assert.Equal("Debug | Test32", window.StatusBar.ConfigurationText);
        Assert.Contains("Debug | Test32", status);
        Assert.Equal("Debug · Test32", Badge(bed));
        Assert.Equal([("&1 Win64", "win64", false), ("&2 Test32", Second, true)], Entries(window, "Project.Platform"));

        // The configuration and the platform are independent.
        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "Release");
        Assert.Equal(("Release | Test32", "Release · Test32"), (window.StatusBar.ConfigurationText, Badge(bed)));
        Assert.Equal([("&1 Debug", "Debug", false), ("&2 Release", "Release", true)], Entries(window, "Project.Configuration"));
        await bed.Services.Commands.ExecuteAsync("project.setPlatform", "win64");
        Assert.Equal("win64", bed.Services.Projects.Project!.ActivePlatform);
        Assert.Contains("\"activePlatform\": \"win64\"", ProjectFile(bed), StringComparison.Ordinal);
        Assert.Equal(("Win64", "Release | Win64", "Release · Win64"), (combo.Selected, window.StatusBar.ConfigurationText, Badge(bed)));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-003")]
    public Task SetPlatform_NotListedIsNoOp_DisabledWhileBuilding() => TestUiThread.RunAsync(NotListedOnUiThread);

    private static async Task NotListedOnUiThread()
    {
        var bed = Bed();
        var commands = bed.Services.Commands;
        Assert.False(commands.CanExecute("project.setPlatform", Second)); // no project

        await bed.OpenHelloAsync(extra: TwoPlatforms);
        var file = ProjectFile(bed);
        var changes = 0;
        bed.Services.Projects.ProjectChanged += (_, _) => changes++;

        // A platform the project does not list — installed or not: nothing changes, a warning is logged.
        var warnings = bed.Log.Lines.Count(l => l.Contains("WARN", StringComparison.Ordinal));
        await commands.ExecuteAsync("project.setPlatform", "win32");
        Assert.Equal(warnings + 1, bed.Log.Lines.Count(l => l.Contains("WARN", StringComparison.Ordinal)));
        Assert.Contains(bed.Log.Lines, l => l.Contains("Platform 'win32' is not listed", StringComparison.Ordinal));
        Assert.False(await ((ProjectService)bed.Services.Projects).SetActivePlatformAsync("arm"));

        // The active platform again, or no argument: nothing to do, nothing written.
        await commands.ExecuteAsync("project.setPlatform", "win64");
        await commands.ExecuteAsync("project.setPlatform");
        Assert.Equal((0, "win64", file), (changes, bed.Services.Projects.Project!.ActivePlatform, ProjectFile(bed)));
        Assert.Null(bed.Services.Projects.Definition!.ActivePlatform);

        // Not Idle: the command is disabled and does nothing.
        Assert.True(bed.Services.State.TryTransition(Building));
        Assert.False(commands.CanExecute("project.setPlatform", Second));
        await commands.ExecuteAsync("project.setPlatform", Second);
        Assert.Equal((0, "win64", file), (changes, bed.Services.Projects.Project!.ActivePlatform, ProjectFile(bed)));
        Assert.True(bed.Services.State.TryTransition(IdeStateInfo.Idle));
        Assert.True(commands.CanExecute("project.setPlatform", Second));
        await commands.ExecuteAsync("project.setPlatform", Second);
        Assert.Equal((1, Second), (changes, bed.Services.Projects.Project!.ActivePlatform));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-004")]
    [Trait("Req", "UI-TB-023")]
    public Task Combo_EnabledOnlyWithTwoPlatformsAndIdle_SelectionRunsTheCommand() => TestUiThread.RunAsync(ComboOnUiThread);

    private static async Task ComboOnUiThread()
    {
        var bed = Bed();
        var window = new MainWindowViewModel(bed.Services);
        var combo = Combo(window);
        Assert.Equal(88, combo.Width);
        Assert.True(combo.ElidesItems); // UI-TB-023
        Assert.Equal((0, null, false), (combo.Items.Count, combo.Selected, combo.IsEnabled)); // no project

        // Two listed platforms and Idle: enabled; choosing an item runs project.setPlatform:<id>.
        await bed.OpenHelloAsync(extra: TwoPlatforms);
        Assert.Equal(["Win64", "Test32"], combo.Items);
        Assert.Equal(("Win64", true), (combo.Selected, combo.IsEnabled));
        combo.Choose!("Test32");
        Assert.Equal(Second, bed.Services.Projects.Project!.ActivePlatform);
        Assert.Equal("Test32", combo.Selected);
        combo.Choose("Nothing"); // no such item
        Assert.Equal(Second, bed.Services.Projects.Project!.ActivePlatform);

        // Not Idle: disabled, the selection stays visible.
        Assert.True(bed.Services.State.TryTransition(Building));
        Assert.Equal(("Test32", false), (combo.Selected, combo.IsEnabled));
        Assert.True(bed.Services.State.TryTransition(IdeStateInfo.Idle));
        Assert.True(combo.IsEnabled);

        // A project with one platform: its label stays visible, the combo is disabled.
        Assert.True(await bed.Services.Projects.CloseAsync());
        Assert.Equal((0, null, false), (combo.Items.Count, combo.Selected, combo.IsEnabled));
        await bed.OpenHelloAsync();
        Assert.Equal(["Win64"], combo.Items);
        Assert.Equal(("Win64", false), (combo.Selected, combo.IsEnabled));
    }

    [Fact(Timeout = TestDeadline.DebugTestTimeout)]
    [Trait("Req", "IDE-PLAT-005")]
    public Task BuildRunAndDebugRequestsCarryTheActivePlatform_OutputPathIsThePlatforms() => TestUiThread.RunAsync(RequestsOnUiThread);

    private static async Task RequestsOnUiThread()
    {
        var shell = new FakeShellService();
        var bed = Bed(o => o.Shell = shell);
        await bed.OpenHelloAsync(extra: TwoPlatforms + """
            , "platformOptions": { "test32": { "defines": ["LEGACY"] } }
            """);
        var projects = bed.Services.Projects;
        const string baseline = BuildTestBed.Dir + @"\bin\Debug", platform = BuildTestBed.Dir + @"\bin\test32\Debug";

        // The baseline platform keeps the configuration's folder (CMP-1367).
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Equal(("win64", "Debug"), (bed.Engine.Requests[^1].Platform, bed.Engine.Requests[^1].Configuration));
        Assert.Equal(baseline + @"\Hello.exe", bed.Services.Run.PreviousExecutable(projects.Project!));
        Assert.Equal(baseline + @"\Hello.exe", RunParametersViewModel.ExecutablePath(projects.Definition!, "Debug"));
        Assert.Equal(baseline, ((ProjectService)projects).OutputFolder());

        // The other platform: every request carries it; run, Run Parameters and Open Output Folder use its folder.
        await bed.Services.Commands.ExecuteAsync("project.setPlatform", Second);
        foreach (var kind in (BuildKind[])[BuildKind.Build, BuildKind.Rebuild, BuildKind.Clean])
        {
            Assert.NotNull(await bed.Services.Build.BuildAsync(kind));
            Assert.Equal((Second, "Debug"), (bed.Engine.Requests[^1].Platform, bed.Engine.Requests[^1].Configuration));
        }

        Assert.Equal(platform + @"\Hello.exe", bed.Services.Run.PreviousExecutable(projects.Project!));
        Assert.Equal(platform + @"\Hello.exe", RunParametersViewModel.ExecutablePath(projects.Definition!, "Debug"));
        Assert.Equal(BuildTestBed.Dir + @"\bin\test32\Release\Hello.exe", RunParametersViewModel.ExecutablePath(projects.Definition!, "Release"));
        bed.Fs.CreateDirectory(platform);
        await bed.Services.Commands.ExecuteAsync("project.openOutputFolder");
        Assert.Equal(["OpenFolder " + platform], shell.Calls);

        // Run after a failed build offers the earlier program of this platform — bin\test32\Debug — not the baseline's.
        bed.File(baseline + @"\Hello.exe", "MZ");
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", BuildTestBed.UnitPath, 3, 1)));
        Assert.False(await bed.Services.Run.StartAsync());
        Assert.Empty(bed.Processes.Started);
        bed.File(platform + @"\Hello.exe", "MZ");
        bed.Dialogs.Enqueue("Msg.Run.BuildFailedRunPrevious", "RunLastBuild");
        bed.Engine.Enqueue(FakeBuildEngine.Failure(BuildTestBed.ProjectPath, FakeBuildEngine.Diag("E2003", DiagnosticSeverity.Error, "Undeclared identifier 'Foo'", BuildTestBed.UnitPath, 3, 1)));
        Assert.True(await bed.Services.Run.StartAsync());
        Assert.Equal(platform + @"\Hello.exe", bed.Processes.Last.Request.FileName);
        Assert.Equal(Second, bed.Engine.Requests[^1].Platform);
        bed.Processes.Last.Exit(0);

        // A folder of the platform's own (platformOptions.<id>.outputDir, CMP-1366) is used as written.
        var definition = projects.Definition!;
        Assert.True(await projects.ApplyOptionsAsync(definition with
        {
            PlatformOptions = definition.PlatformOptions.SetItem(Second, definition.PlatformOptions[Second] with { OutputDir = "out/$(Platform)-$(Config)" }),
        }));
        Assert.Equal(BuildTestBed.Dir + @"\out\test32-Debug\Hello.exe", bed.Services.Run.PreviousExecutable(projects.Project!));
        Assert.Equal(BuildTestBed.Dir + @"\out\test32-Debug", ((ProjectService)projects).OutputFolder());

        // A start under the debugger builds with the same request.
        using var debug = new DebugTestBed(o => o.PlatformCatalog = new TestPlatformCatalog());
        debug.File(debug.ProjectPath, """
            { "formatVersion": 1, "name": "Hello", "appType": "console", "mainSource": "Hello.pas",
              "platforms": ["win64", "test32"], "activePlatform": "test32",
              "files": [ { "path": "Hello.pas" } ] }
            """);
        debug.File(debug.MainPath, "program Hello;\nbegin\nend.\n");
        Assert.True(await debug.Services.Projects.OpenAsync(debug.ProjectPath));
        debug.Sessions.UseScenario("{}");
        Assert.True(await debug.Debug.StartAsync());
        Assert.Equal((Second, "Debug"), (debug.Engine.Requests[^1].Platform, debug.Engine.Requests[^1].Configuration));
        await debug.Debug.StopAsync();
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-006")]
    public Task ChangingThePlatform_ReclassifiesCodeUnderIfdefWin64() => TestUiThread.RunAsync(ReclassifiesOnUiThread);

    private static async Task ReclassifiesOnUiThread()
    {
        var bed = Bed();
        WithRtl(bed);
        await bed.OpenHelloAsync(extra: TwoPlatforms);
        Assert.True(await bed.Services.Documents.CloseAllAsync());
        bed.File(BuildTestBed.UnitPath, Unit);
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        var delay = TimeSpan.FromMilliseconds(bed.Services.Settings.Get(SettingsCatalog.EditorLiveDiagnosticsDelayMs));
        int wide = Unit.IndexOf("Count := 64", StringComparison.Ordinal), narrow = Unit.IndexOf("Count := 32", StringComparison.Ordinal);

        // win64: WIN64 is predefined, so the {$ELSE} branch is inactive code.
        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.Equal(doc.Buffer.Version, doc.Semantic.Version);
        Assert.NotEqual("syntax.inactive", ClassAt(Drawn(doc), wide));
        Assert.Equal("syntax.inactive", ClassAt(Drawn(doc), narrow));

        // project.setPlatform:test32 — the document stays open; within the analysis delay the first branch is the inactive one.
        await bed.Services.Commands.ExecuteAsync("project.setPlatform", Second);
        Assert.Same(doc, bed.Services.Documents.ActiveDocument);
        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.Equal("syntax.inactive", ClassAt(Drawn(doc), wide));
        Assert.NotEqual("syntax.inactive", ClassAt(Drawn(doc), narrow));
        Assert.Same(TestPlatformCatalog.Second, bed.Services.CodeModel.Synchronize()!.Options.Target);

        // … and back.
        await bed.Services.Commands.ExecuteAsync("project.setPlatform", "win64");
        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.NotEqual("syntax.inactive", ClassAt(Drawn(doc), wide));
        Assert.Equal("syntax.inactive", ClassAt(Drawn(doc), narrow));
        Assert.Same(TargetInfo.Win64, bed.Services.CodeModel.Synchronize()!.Options.Target);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-006")]
    public Task PlatformDefinesAndSearchPathsReachTheCodeModel() => TestUiThread.RunAsync(ListsOnUiThread);

    private static async Task ListsOnUiThread()
    {
        var bed = Bed();
        WithRtl(bed);
        await bed.OpenHelloAsync(extra: TwoPlatforms + """
            , "defines": ["COMMON"],
              "platformOptions": { "test32": { "defines": ["LEGACY"], "searchPaths": ["lib32"] } }
            """);
        Assert.True(await bed.Services.Documents.CloseAllAsync());
        bed.File(BuildTestBed.Dir + @"\lib32\Extra.pas", "unit Extra;\ninterface\nconst\n  Answer = 42;\nimplementation\nend.\n");
        bed.File(BuildTestBed.MainPath, "program Hello;\nuses Unit1, Extra;\nbegin\nend.\n");
        bed.File(BuildTestBed.UnitPath, Unit);
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        var delay = TimeSpan.FromMilliseconds(bed.Services.Settings.Get(SettingsCatalog.EditorLiveDiagnosticsDelayMs));
        var legacy = Unit.IndexOf("Count := 1;", StringComparison.Ordinal);
        var token = TestContext.Current.CancellationToken;
        static bool Missing(Diagnostic d) => d.Severity == DiagnosticSeverity.Error && d.ToText().Contains("Extra", StringComparison.Ordinal);

        // win64: the platform lists of test32 do not apply — LEGACY is not defined, the unit Extra is not found.
        var options = bed.Services.CodeModel.Synchronize()!.Options;
        Assert.Equal(["COMMON"], options.Defines.Where(d => d is "COMMON" or "LEGACY"));
        Assert.DoesNotContain(BuildTestBed.Dir + @"\lib32", options.SearchPaths);
        Assert.Contains(await bed.Services.CodeModel.GetProjectDiagnosticsAsync(token), Missing);
        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.Equal("syntax.inactive", ClassAt(Drawn(doc), legacy));

        // test32: its defines after the project's, its search path after the project's (CMP-1365).
        await bed.Services.Commands.ExecuteAsync("project.setPlatform", Second);
        options = bed.Services.CodeModel.Synchronize()!.Options;
        Assert.Equal(["COMMON", "LEGACY"], options.Defines.Where(d => d is "COMMON" or "LEGACY"));
        Assert.Contains(BuildTestBed.Dir + @"\lib32", options.SearchPaths);
        Assert.DoesNotContain(await bed.Services.CodeModel.GetProjectDiagnosticsAsync(token), Missing);
        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.NotEqual("syntax.inactive", ClassAt(Drawn(doc), legacy));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-007")]
    [Trait("Req", "IDE-PLAT-005")]
    [Trait("Req", "UI-TB-022")]
    public Task ActivePlatformNotInstalled_BaselineAnalysis_SuffixShown_BuildReportsE9016() => TestUiThread.RunAsync(NotInstalledOnUiThread);

    private static async Task NotInstalledOnUiThread()
    {
        // The product's catalogue and the real build engine over the test's file system: nothing is faked about the platform.
        var bed = new BuildTestBed(o => o.BuildEngine = new BuildEngine(new CompilerFileSystemAdapter(o.FileSystem)));
        WithRtl(bed);
        await bed.OpenHelloAsync(extra: """
            , "platforms": ["win64", "win32"], "activePlatform": "win32",
              "platformOptions": { "win32": { "defines": ["LEGACY"] } }
            """);
        Assert.True(await bed.Services.Documents.CloseAllAsync());
        bed.File(BuildTestBed.UnitPath, Unit);
        var window = new MainWindowViewModel(bed.Services);
        var combo = Combo(window);

        // The project loads; the platform that is not installed is active and shown with its suffix everywhere.
        Assert.Equal("win32", bed.Services.Projects.Project!.ActivePlatform);
        Assert.Equal(["Win64", "Win32 (not installed)"], combo.Items);
        Assert.Equal(("Win32 (not installed)", true), (combo.Selected, combo.IsEnabled));
        Assert.Equal("Debug | Win32 (not installed)", window.StatusBar.ConfigurationText);
        Assert.Equal("Debug · Win32 (not installed)", Badge(bed));
        Assert.Equal([("&1 Win64", "win64", false), ("&2 Win32 (not installed)", "win32", true)], Entries(window, "Project.Platform"));

        // The code model: the baseline descriptor (WIN64 predefined) with the lists of that platform (LEGACY defined).
        var options = bed.Services.CodeModel.Synchronize()!.Options;
        Assert.Equal(TargetInfo.Win64.PredefinedSymbols, options.Target.PredefinedSymbols);
        Assert.Contains("LEGACY", options.Defines);
        var doc = await bed.OpenDocumentAsync(BuildTestBed.UnitPath);
        var delay = TimeSpan.FromMilliseconds(bed.Services.Settings.Get(SettingsCatalog.EditorLiveDiagnosticsDelayMs));
        bed.Clock.Advance(delay);
        await bed.Services.SemanticHighlights.PendingFor(doc);
        Assert.NotEqual("syntax.inactive", ClassAt(Drawn(doc), Unit.IndexOf("Count := 64", StringComparison.Ordinal)));
        Assert.Equal("syntax.inactive", ClassAt(Drawn(doc), Unit.IndexOf("Count := 32", StringComparison.Ordinal)));
        Assert.NotEqual("syntax.inactive", ClassAt(Drawn(doc), Unit.IndexOf("Count := 1;", StringComparison.Ordinal)));

        // Build: the engine's E9016 in Messages, like any project error; nothing is built.
        var result = await bed.Services.Build.BuildAsync(BuildKind.Build);
        Assert.False(result!.Success);
        var row = Assert.Single(bed.Services.Messages.Items, i => !i.IsSummary);
        Assert.Equal(("E9016", Core.Build.MessageSeverity.Error), (row.Code, row.Severity));
        Assert.Equal("Platform 'win32' is not installed (installed: win64)", row.Text);
        Assert.Equal(IdeState.Idle, bed.Services.State.Current.State);

        // The installed platform again: labels without the suffix.
        await bed.Services.Commands.ExecuteAsync("project.setPlatform", "win64");
        Assert.Equal(("Win64", "Debug | Win64", "Debug · Win64"), (combo.Selected, window.StatusBar.ConfigurationText, Badge(bed)));
        Assert.DoesNotContain("LEGACY", bed.Services.CodeModel.Synchronize()!.Options.Defines);
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "IDE-PLAT-002")]
    [Trait("Req", "IDE-PLAT-004")]
    public Task ProjectWithOnePlatform_BehavesAsBefore() => TestUiThread.RunAsync(OnePlatformOnUiThread);

    private static async Task OnePlatformOnUiThread()
    {
        var bed = new BuildTestBed(); // the product's catalogue
        await bed.OpenHelloAsync();
        var window = new MainWindowViewModel(bed.Services);
        var combo = Combo(window);
        var file = ProjectFile(bed);

        Assert.Equal(["win64"], bed.Services.Projects.Project!.Platforms);
        Assert.Equal("win64", bed.Services.Projects.Project.ActivePlatform);
        Assert.Equal(["Win64"], combo.Items);
        Assert.Equal(("Win64", false), (combo.Selected, combo.IsEnabled));
        Assert.Equal(("Debug | Win64", "Debug · Win64"), (window.StatusBar.ConfigurationText, Badge(bed)));
        Assert.Equal([("&1 Win64", "win64", true)], Entries(window, "Project.Platform"));

        // Selecting the only platform is nothing to do; no key appears in the file — also not with the next save.
        await bed.Services.Commands.ExecuteAsync("project.setPlatform", "win64");
        Assert.Equal(file, ProjectFile(bed));
        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "Release");
        Assert.DoesNotContain("activePlatform", ProjectFile(bed), StringComparison.Ordinal);
        Assert.DoesNotContain("platformOptions", ProjectFile(bed), StringComparison.Ordinal);
        await bed.Services.Commands.ExecuteAsync("project.setConfiguration", "Debug");

        // Build request, output path and code model as before the platform could be chosen.
        Assert.NotNull(await bed.Services.Build.BuildAsync(BuildKind.Build));
        Assert.Equal("win64", bed.Engine.Requests[^1].Platform);
        Assert.Equal(BuildTestBed.Dir + @"\bin\Debug\Hello.exe", bed.Services.Run.PreviousExecutable(bed.Services.Projects.Project!));
        Assert.Equal(BuildTestBed.Dir + @"\bin\Debug", ((ProjectService)bed.Services.Projects).OutputFolder());
        Assert.Same(TargetInfo.Win64, bed.Services.CodeModel.Synchronize()!.Options.Target);
    }
}
