using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.EndToEnd;

/// <summary>Card F01-003 in the IDE with the real build engine and toolchain (phase8-build-options.md §4).</summary>
[Trait("Phase", "8")]
[Trait("Kind", "E2E")]
[Trait("Category", "Toolchain")]
public sealed class Phase8LibraryTests
{
    /// <summary>
    /// IDE-TGT-016: a project created from the template <c>library-pascal</c> builds without diagnostics and produces
    /// <c>&lt;name&gt;.dll</c> and <c>&lt;name&gt;.lib</c>; without a host application it is not runnable (IDE-TGT-013).
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-TGT-016")]
    [Trait("Req", "IDE-TGT-015")]
    [Trait("Req", "IDE-TGT-011")]
    public async Task E2E_Phase8_LibraryTemplateBuildsDll()
    {
        await using var h = await HeadlessHarness.StartAsync(o => o.RealBuild = true);
        var toolchain = h.Services.Toolchain.Resolve();
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips

        Assert.Equal(NewProjectOutcome.Created, await h.Services.Projects.CreateProjectAsync(new NewProjectRequest("library-pascal", "Mathlib", h.TempDir)));
        var dir = Path.Combine(h.TempDir, "Mathlib");
        Assert.Equal("library", h.Services.Projects.Project!.AppType);
        Assert.StartsWith("library Mathlib;", File.ReadAllText(Path.Combine(dir, "Mathlib.pas")), StringComparison.Ordinal);

        string Output() => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";
        string Diagnostics() => $"state {h.Services.State.Current.State}; messages: "
            + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}"))
            + "; log: " + string.Join(" | ", h.Log.Lines.Where(l => l.StartsWith("ERROR ", StringComparison.Ordinal)))
            + "; output: " + Output();
        var dll = Path.Combine(dir, @"bin\Debug\Mathlib.dll");
        await h.ExecuteAsync("project.build");
        try
        {
            await h.WaitForAsync(() => h.Services.State.Current.State == IdeState.Idle && (File.Exists(dll) || h.Services.Messages.ErrorCount > 0),
                "the build finished");
        }
        catch (HarnessAssertionException e)
        {
            Assert.Fail(e.Message + " — " + Diagnostics());
        }

        Assert.True(h.Services.Messages.Items.All(i => string.IsNullOrEmpty(i.Code)), Diagnostics()); // zero diagnostics: only the build summary line
        Assert.True(File.Exists(dll), Diagnostics());
        Assert.True(File.Exists(Path.Combine(dir, @"bin\Debug\Mathlib.lib")), Diagnostics());
        h.Clock.Advance(TimeSpan.FromMilliseconds(50)); // batched Output reaches its channel (IDE-P3-070)
        Assert.Contains(h.Services.Output.GetLines(Core.Build.OutputChannels.Build), l => l.Text.Contains("Linking Mathlib.dll", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(dir, @"bin\Debug\Mathlib.exe")));

        // The Project Manager shows the DLL the build wrote; Run has nothing to start without a host application.
        var root = h.Services.ProjectManager.Find(Core.ViewModels.ProjectManagerViewModel.RootKey)!;
        Assert.Equal((true, dll), (root.IsLibrary, root.ToolTipText));
        Assert.False(await h.Services.Run.StartAsync());
        Assert.Equal(Core.Resources.Strings.Msg_Run_NotRunnable, h.Services.Status.Message);
        Assert.Empty(h.Messages.Requests);
    }

    private const string HostSource = """
        program Loader;

        uses Windows;

        type
          TScale = function(Value, Factor: Integer): Integer; stdcall;

        var
          H: HMODULE;
          Scale: TScale;
          R: Integer;
        begin
          H := LoadLibraryW('Mathlib.dll');
          WriteLn('loaded ', H <> 0);
          Scale := TScale(GetProcAddress(H, 'Scale'));
          R := Scale(6, 7);
          WriteLn('scaled ', R);
          WriteLn('freed ', FreeLibrary(H));
        end.

        """;

    private const string LibrarySource = """
        library Mathlib;

        uses MathUnit;

        exports
          Scale;

        end.

        """;

    private const string LibraryUnit = """
        unit MathUnit;

        interface

        function Scale(Value, Factor: Integer): Integer; stdcall;

        implementation

        function Scale(Value, Factor: Integer): Integer; stdcall;
        var
          Product: Integer;
        begin
          Product := Value * Factor;
          Result := Product + 1;
        end;

        end.

        """;

    /// <summary>
    /// IDE-TGT-014 as amended (I-254), with the real build engine, toolchain and debug adapter: a library project whose
    /// <c>run.hostApplication</c> names a program that loads the library at run time is started with Start Debugging; the
    /// host runs under the debugger, a breakpoint in a unit of the library stops there once the host has loaded the
    /// module, the library's variables are readable, and Continue lets the host run to its end.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-TGT-014")]
    public async Task E2E_Phase8_DebugLibraryThroughHost()
    {
        static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
        await using var h = await HeadlessHarness.StartAsync(o =>
        {
            o.RealBuild = true;
            o.InitialFiles[@"Mathlib\host\Loader.pproj"] = """
                { "formatVersion": 1, "name": "Loader", "appType": "console", "mainSource": "Loader.pas",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },
                  "files": [ { "path": "Loader.pas" } ] }
                """;
            o.InitialFiles[@"Mathlib\host\Loader.pas"] = Lf(HostSource);
            // The host finds the library in its working directory: the library's output folder.
            o.InitialFiles[@"Mathlib\Mathlib.pproj"] = """
                { "formatVersion": 1, "name": "Mathlib", "appType": "library", "mainSource": "Mathlib.pas",
                  "configurations": { "Debug": { "optimization": 0, "debugInfo": true, "defines": [], "outputDir": "bin/Debug" } },
                  "run": { "arguments": "", "workingDir": "bin/Debug", "hostApplication": "host/bin/Debug/Loader.exe" },
                  "files": [ { "path": "Mathlib.pas" }, { "path": "MathUnit.pas" } ] }
                """;
            o.InitialFiles[@"Mathlib\Mathlib.pas"] = Lf(LibrarySource);
            o.InitialFiles[@"Mathlib\MathUnit.pas"] = Lf(LibraryUnit);
        });
        var toolchain = h.Services.Toolchain.Resolve();
        if (toolchain.LlvmBinDir is null || toolchain.RtlDir is null)
            Assert.Fail("LLVM toolchain not found (run scripts/bootstrap.ps1)"); // testing.md: fails, never skips

        string Abs(string relative) => Path.Combine(h.TempDir, "Mathlib", relative);
        string Output() => h.TryFind<TextEditor>("Tool.Output.Text")?.Document.Text ?? "";
        string Diagnostics() => $"state {h.Services.State.Current.State}; messages: "
            + string.Join(" | ", h.Services.Messages.Items.Select(i => $"{i.Code} {i.Text}"))
            + "; boxes: " + string.Join(" | ", h.Messages.Requests.Select(r => $"{r.MessageId}({string.Join(", ", r.Args)})"))
            + "; log: " + string.Join(" | ", h.Log.Lines.Where(l => !l.StartsWith("INFO ", StringComparison.Ordinal)))
            + "; debugger: " + string.Join(" | ", h.Services.Output.GetLines(Core.Build.OutputChannels.Debugger).Select(l => l.Text))
            + "; output: " + Output();
        async Task WaitAsync(Func<bool> condition, string step)
        {
            try
            {
                await h.WaitForAsync(condition, step);
            }
            catch (HarnessAssertionException e)
            {
                Assert.Fail(e.Message + " — " + Diagnostics());
            }
        }

        // The host application: a console program of its own, built by the IDE.
        var host = Abs(@"host\bin\Debug\Loader.exe");
        Assert.True(await h.Services.Projects.OpenAsync(Abs(@"host\Loader.pproj")));
        await h.ExecuteAsync("project.build");
        await WaitAsync(() => h.Services.State.Current.State == IdeState.Idle && (File.Exists(host) || h.Services.Messages.ErrorCount > 0), "the host built");
        Assert.True(File.Exists(host), Diagnostics());

        // The library project names it; the debug commands are available.
        Assert.True(await h.Services.Projects.OpenAsync(Abs("Mathlib.pproj")));
        Assert.Equal("library", h.Services.Projects.Project!.AppType);
        var commands = h.Services.Commands;
        Assert.True(commands.CanExecute("run.start"));
        Assert.True(commands.CanExecute("run.stepOver"));
        Assert.True(commands.CanExecute("run.stepInto"));

        // A breakpoint in the library's unit, set before anything runs.
        var unit = Abs("MathUnit.pas");
        var line = Lf(LibraryUnit).Split('\n').ToList().FindIndex(l => l.Contains("Result := Product + 1", StringComparison.Ordinal)) + 1;
        h.Services.Breakpoints!.Toggle(unit, line);

        // Start Debugging builds the library and starts the host under the debugger; the host loads the library and stops in it.
        var debug = h.Services.Debug!;
        await h.ExecuteAsync("run.start");
        await WaitAsync(() => h.Services.State.Current.State == IdeState.Paused || (h.Services.State.Current.State == IdeState.Idle && debug.IsIdle),
            "the stop in the library");
        var state = h.Services.State.Current;
        Assert.True(state.State == IdeState.Paused, Diagnostics());
        Assert.True(File.Exists(Abs(@"bin\Debug\Mathlib.dll")), Diagnostics());
        Assert.Equal(host, state.ProgramPath, ignoreCase: true);
        Assert.Equal((unit.ToUpperInvariant(), line), (state.PausedFile?.ToUpperInvariant(), state.PausedLine));
        await WaitAsync(() => !debug.HasViewWork, "the debug views' requests");

        // The library's type information is in use: a local of the exported routine has its value.
        var evaluation = debug.EvaluateAsync("Product", FreexComp.Debugger.EvaluateContext.Watch);
        await WaitAsync(() => evaluation.IsCompleted, "the value of Product");
        Assert.Equal(("42", "Integer"), ((await evaluation).Value.Value, (await evaluation).Value.TypeName));

        // Continue: the host uses the result, frees the library and ends.
        await h.ExecuteAsync("run.start");
        await WaitAsync(() => h.Services.State.Current.State == IdeState.Idle && debug.IsIdle, "the end of the host");
        h.Clock.Advance(TimeSpan.FromMilliseconds(50)); // batched Output reaches its channel (IDE-P3-070)
        var program = h.Services.Output.GetLines(Core.Build.OutputChannels.Program).Select(l => l.Text).ToList();
        Assert.True(program.Contains("scaled 43") && program.Contains("freed TRUE"), Diagnostics());
        Assert.Empty(h.Messages.Requests);
    }

    /// <summary>
    /// Card F02-004 (IDE-LIB-022, IDE-LIB-023) with the real build engine, toolchain and debug adapter: Step Into at a call
    /// of a SysUtils routine stays in the program with "Step Into Library Source" off and stops in the protected
    /// <c>SysUtils.pas</c> with it on (<see cref="LibrarySteppingScenario"/>).
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-LIB-022")]
    [Trait("Req", "IDE-LIB-023")]
    public Task E2E_Phase8_StepIntoLibrarySource() => LibrarySteppingScenario.StepIntoSysUtilsAsync();

    /// <summary>
    /// Card F05-099 acceptance 4 in the IDE (IDE-LIB-026…035) with the real build engine and toolchain: a GUI project →
    /// Go to Declaration on a framework class opens the protected framework unit → <c>Copy to Project</c> → a visible
    /// constant (the default width of a button) is edited in the copy → the program built and run with the layout dump
    /// has the changed value; the framework file on disk is byte-identical to before.
    /// </summary>
    [AvaloniaFact(Timeout = 120000)]
    [Trait("Req", "IDE-LIB-026")]
    [Trait("Req", "IDE-LIB-034")]
    [Trait("Req", "IDE-LIB-035")]
    public async Task E2E_Phase8_LocalCopyOfFrameworkUnitIsCompiled()
    {
        const string DefaultBounds = "SetBounds(Left, Top, 75, 25);";
        const string ChangedBounds = "SetBounds(Left, Top, 91, 25);";
        await using var h = await Phase5Scenario.StartAsync();
        var services = h.Services;
        var frameworkUnit = Path.Combine(services.Toolchain.Resolve().FrameworkDir!, "src", "StdCtrls.pas");
        var frameworkBytes = File.ReadAllBytes(frameworkUnit);

        // A GUI project whose form has a button without a stored size: its width is the framework's default.
        Assert.Equal(NewProjectOutcome.Created,
            await services.Projects.CreateProjectAsync(new NewProjectRequest("gui-pascal", Phase5Scenario.ProjectName, h.TempDir)));
        Assert.True(await services.Documents.CloseAllAsync());
        var unitPath = Phase5Scenario.UnitPath(h);
        var unitText = File.ReadAllText(unitPath);
        Assert.Contains("Classes, Controls, Forms;", unitText, StringComparison.Ordinal);
        Assert.Contains("= class(TForm)", unitText, StringComparison.Ordinal);
        unitText = unitText.Replace("Classes, Controls, Forms;", "Classes, Controls, Forms, StdCtrls;", StringComparison.Ordinal);
        var classLine = unitText.IndexOf("= class(TForm)", StringComparison.Ordinal) + "= class(TForm)".Length;
        File.WriteAllText(unitPath, unitText.Insert(classLine, "\r\n    Button1: TButton;"));
        var formPath = Phase5Scenario.FormPath(h);
        var formText = File.ReadAllText(formPath).TrimEnd();
        Assert.EndsWith("end", formText, StringComparison.Ordinal);
        File.WriteAllText(formPath, formText[..^3] + "  object Button1: TButton\r\n    Left = 16\r\n    Top = 16\r\n    Caption = 'Go'\r\n  end\r\nend\r\n");
        await services.FileChanges.WhenIdle();

        async Task<int> ButtonWidthAsync()
        {
            var exe = await Phase5Scenario.BuildAsync(h);
            var dump = await LayoutRoundTrip.RunDumpAsync(exe, h.TempDir);
            return Assert.Single(Assert.Single(dump.Forms).Controls, c => c.Name == "Button1").Width;
        }

        Assert.Equal(75, await ButtonWidthAsync()); // built from the library unit

        // Go to Declaration on the framework class opens the framework unit, protected.
        var unit = Assert.IsAssignableFrom<Core.ViewModels.TextDocumentViewModel>((await services.Documents.OpenAsync(unitPath)).Value);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        unit.Selection = Core.Text.TextSelection.At(unit.Buffer.Text.IndexOf("TButton", StringComparison.Ordinal) + 1);
        Assert.True(services.Commands.CanExecute("search.goToDeclaration"));
        await h.ExecuteAsync("search.goToDeclaration");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var library = Assert.IsType<Core.ViewModels.TextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal(frameworkUnit, library.FilePath, ignoreCase: true);
        Assert.True(library.IsLibraryProtected);
        var caret = library.ViewState.Caret;

        // Copy to Project on the library bar: the copy is a project file, open at the same position, writable.
        // The action opens the copy with file reads on a pool thread (IDE-ARCH-081): the wait is for its last step, the
        // confirmation in the status bar.
        h.Click("InfoBar.LibrarySource.Action.CopyToProject");
        await h.WaitForAsync(() => services.Status.Message == "StdCtrls.pas was copied into the project. This project is now built from the copy.",
            "Copy to Project finished");
        var target = Path.Combine(Phase5Scenario.ProjectDir(h), "StdCtrls.pas");
        var copy = Assert.IsType<Core.ViewModels.TextDocumentViewModel>(services.Documents.ActiveDocument);
        Assert.Equal(target, copy.FilePath);
        Assert.Equal(frameworkBytes, File.ReadAllBytes(target));
        Assert.True(services.Projects.IsMember(target));
        Assert.Equal(caret, copy.ViewState.Caret);
        Assert.False(copy.IsReadOnly);
        Assert.Empty(copy.InfoBars);
        Assert.Empty(h.Messages.Requests);

        // The visible constant is changed in the copy and saved.
        var offset = copy.Buffer.Text.IndexOf(DefaultBounds, StringComparison.Ordinal);
        Assert.True(offset >= 0, "the default bounds of a button are not where the test expects them");
        Assert.Equal(-1, copy.Buffer.Text.IndexOf(DefaultBounds, offset + 1, StringComparison.Ordinal));
        copy.Buffer.ApplyEdits([new Core.Text.TextEdit(offset, DefaultBounds.Length, ChangedBounds)]);
        Assert.True(await services.Documents.SaveAsync(copy));
        await services.FileChanges.WhenIdle();

        // The next build compiles the copy: the running program has the changed value.
        Assert.Equal(91, await ButtonWidthAsync());

        // The framework file was never written.
        Assert.Equal(frameworkBytes, File.ReadAllBytes(frameworkUnit));
        Assert.True(library.IsLibraryProtected);
        Assert.Empty(h.Messages.Requests);
    }
}
