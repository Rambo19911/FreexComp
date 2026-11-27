using System.Diagnostics;
using System.Xml.Linq;
using FreexComp.Debugger;

namespace FreexComp.Setup.Tests;

/// <summary>
/// A product folder <c>H</c> on disk (installed by the setup program or unzipped from the portable zip) and what the
/// end-to-end tests do with it. Every program of it runs under the reduced environment of installer.md §1: <c>PATH</c> is
/// <c>%SystemRoot%\System32</c> only, <c>FREEXCOMP_HOME</c> is unset and the profile folders are the sandbox's, so
/// nothing outside <c>H</c> (but Windows itself) can be used and nothing lands in the real user profile.
/// </summary>
internal sealed class ProductFolder(string home, SetupSandbox box)
{
    private static readonly TimeSpan ToolLimit = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DebugLimit = TimeSpan.FromSeconds(120);
    private static readonly string[] Programs = ["FreexComp.Ide.exe", "opc.exe", "pbuild.exe", "prc.exe"];
    private static readonly string[] LicenceFiles = ["LICENSE", "LICENSE-RUNTIME", "NOTICE", "THIRD-PARTY-NOTICES.txt"];

    /// <summary>The line of <see cref="HelloSource"/> the debug session stops at (1-based).</summary>
    private const int BreakpointLine = 8;

    private const string HelloSource =
        "program Hello;\n" +                               // 1
        "\n" +                                             // 2
        "procedure Greet(Count: Integer);\n" +             // 3
        "var\n" +                                          // 4
        "  Total: Integer;\n" +                            // 5
        "begin\n" +                                        // 6
        "  Total := Count * 2;\n" +                        // 7
        "  Writeln('Hello, world! ', Total);\n" +          // 8
        "end;\n" +                                         // 9
        "\n" +                                             // 10
        "begin\n" +                                        // 11
        "  Greet(21);\n" +                                 // 12
        "end.\n";                                          // 13

    public string Home { get; } = home;

    public string Combine(params string[] parts) => Path.Combine([Home, .. parts]);

    /// <summary>The complete environment of a program run from <c>H</c>.</summary>
    public Dictionary<string, string> ReducedEnvironment()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = windows,
            ["windir"] = windows,
            ["SystemDrive"] = Path.GetPathRoot(windows)!.TrimEnd('\\'),
            ["PATH"] = Environment.SystemDirectory,
            ["TEMP"] = box.Temp,
            ["TMP"] = box.Temp,
            ["APPDATA"] = box.AppData,
            ["LOCALAPPDATA"] = box.LocalAppData,
            ["USERPROFILE"] = Directory.CreateDirectory(box.In("profile")).FullName,
            ["ProgramData"] = box.ProgramData,
        };
    }

    public ChildResult Run(string program, string workingDirectory, params string[] arguments) =>
        ChildProgram.Run(Combine(program), arguments, workingDirectory, ReducedEnvironment(), ToolLimit);

    /// <summary>Every file of <c>H</c>, relative, <c>\</c> separated, sorted ordinal-ignore-case.</summary>
    public List<string> Files() =>
        [.. Directory.EnumerateFiles(Home, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Home, f)).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The layout of installer.md §2: the four programs, the licence files (LIC-042), the network policy page (NET-040),
    /// the help pages, the library sources, exactly the third-party files the payload lists name, no debug symbols and
    /// nothing of the build inputs that are not shipped; every file as the manifest <c>payload.sha256</c> records it.
    /// </summary>
    /// <param name="portable">True: a portable folder (marker, no setup files); false: an installed one.</param>
    public void AssertLayout(bool portable)
    {
        var repository = SetupProgramBuild.RepositoryRoot();
        var files = Files();
        foreach (var program in Programs)
            Assert.Contains(program, files);

        // LIC-042: licenses\ holds exactly the four files, as they are in the repository
        Assert.Equal(LicenceFiles.Select(f => @"licenses\" + f).Order(StringComparer.OrdinalIgnoreCase),
            files.Where(f => f.StartsWith(@"licenses\", StringComparison.OrdinalIgnoreCase)));
        foreach (var licence in LicenceFiles)
            AssertSameFile(Path.Combine(repository, licence), Combine("licenses", licence));

        // NET-040: the network policy page next to the programs
        AssertSameFile(Path.Combine(repository, "NETWORK-POLICY.md"), Combine("NETWORK-POLICY.md"));

        // the user help pages: every tracked page of help/, unchanged, and nothing else
        var pages = TrackedFiles(repository, "help");
        Assert.NotEmpty(pages);
        Assert.Equal(pages, files.Where(f => f.StartsWith(@"help\", StringComparison.OrdinalIgnoreCase)));
        foreach (var page in pages)
            AssertSameFile(Path.Combine(repository, page), Combine(page));

        // the library sources the tools compile from (LibrarySourceLayoutTests checks them in depth)
        foreach (var source in (string[])[@"rtl\src\System.pas", @"rtl\src\unit-aliases.json", @"rtl\winapi\src\Windows.pas", @"rtl\winapi\def\kernel32.def"])
            Assert.Contains(source, files);
        Assert.Contains(files, f => f.StartsWith(@"ui-framework\src\", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.StartsWith(@"data\src\", StringComparison.OrdinalIgnoreCase));

        // third_party: exactly what the payload lists select (INS-002)
        var thirdParty = Path.Combine(repository, "third_party");
        var listed = PayloadList.Resolve(Path.Combine(repository, "installer", "payload-llvm.txt"), Path.Combine(thirdParty, "llvm"))
            .Select(f => @"third_party\llvm\" + f)
            .Concat(PayloadList.Resolve(Path.Combine(repository, "installer", "payload-third-party.txt"), thirdParty).Select(f => @"third_party\" + f))
            .Order(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(listed, files.Where(f => f.StartsWith(@"third_party\", StringComparison.OrdinalIgnoreCase)));

        // no debug symbol files (lead decision, L-045), and the Unicode data file is a build input only
        Assert.DoesNotContain(files, f => f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
        Assert.False(Directory.Exists(Combine("third_party", "unicode")));
        Assert.DoesNotContain(files, f => Path.GetFileName(f).StartsWith("UnicodeData", StringComparison.OrdinalIgnoreCase));

        // the manifest lists every payload file, and every file is as recorded (INS-003)
        var entries = PackageFormat.ParseManifest(File.ReadAllBytes(Combine(PackageFormat.ManifestName)));
        Assert.NotNull(entries);
        string[] own = portable ? [PackageFormat.ManifestName, PackageWriter.PortableMarkerName] : [PackageFormat.ManifestName, InstallRecord.FileName, InstallRecord.UninstallProgram];
        Assert.Equal(entries.Select(e => e.Path).Concat(own).Order(StringComparer.OrdinalIgnoreCase), files);
        Parallel.ForEach(entries, entry => Assert.True(entry.Hash == PackageFormat.HashFile(Combine(entry.Path)), entry.Path + " differs from payload.sha256"));

        // INS-020: the marker and no setup files in a portable folder, the reverse in an installed one (checked by the list above)
        Assert.Equal(portable, File.Exists(Combine(PackageWriter.PortableMarkerName)));
        Assert.Equal(!portable, File.Exists(Combine(InstallRecord.FileName)));
        Assert.Equal(!portable, File.Exists(Combine(InstallRecord.UninstallProgram)));
    }

    /// <summary>
    /// Compiles and runs a hello program with the folder's <c>opc</c> (with debug information), compiles a resource script
    /// with its <c>prc</c> and builds and runs a project with its <c>pbuild</c>. Returns the program built by <c>opc</c>.
    /// </summary>
    public (string Source, string Program) BuildAndRunHello(string work)
    {
        Directory.CreateDirectory(work);
        var source = Path.Combine(work, "hello.pas");
        File.WriteAllText(source, HelloSource);
        var program = Path.Combine(work, "hello.exe");
        var compile = Run("opc.exe", work, source, "-g", "-O0", "-o", program);
        Assert.True(compile.ExitCode == 0 && File.Exists(program), compile.Describe("opc"));
        var hello = ChildProgram.Run(program, [], work, ReducedEnvironment(), ToolLimit);
        Assert.Equal((0, "Hello, world! 42\n", ""), (hello.ExitCode, hello.Output.ReplaceLineEndings("\n"), hello.Error));

        // the resource compiler (llvm-rc, preprocessing through clang)
        var script = Path.Combine(work, "greeting.rc");
        File.WriteAllText(script, "#define IDS_GREETING 1\nSTRINGTABLE\nBEGIN\n  IDS_GREETING, \"Hello, world!\"\nEND\n");
        var resources = Run("prc.exe", work, script);
        var compiled = Path.Combine(work, "greeting.res");
        Assert.True(resources.ExitCode == 0 && File.Exists(compiled) && new FileInfo(compiled).Length > 32, resources.Describe("prc"));

        // the project build tool: build, then run
        var projectFolder = Directory.CreateDirectory(Path.Combine(work, "project")).FullName;
        File.WriteAllText(Path.Combine(projectFolder, "Hi.pas"), "program Hi;\nbegin\n  Writeln('Hi from a project');\nend.\n");
        var project = Path.Combine(projectFolder, "Hi.pproj");
        File.WriteAllText(project,
            """
            {
              "formatVersion": 1,
              "name": "Hi",
              "language": "pascal",
              "appType": "console",
              "mainSource": "Hi.pas",
              "files": [],
              "platforms": ["win64"],
              "configurations": {
                "Debug": { "optimization": 0, "debugInfo": true, "defines": ["DEBUG"], "outputDir": "bin/Debug" }
              },
              "activeConfiguration": "Debug"
            }
            """);
        var build = Run("pbuild.exe", projectFolder, project, "--target", "run");
        Assert.True(build.ExitCode == 0 && build.Output.Contains("Hi from a project", StringComparison.Ordinal), build.Describe("pbuild"));
        Assert.True(File.Exists(Path.Combine(projectFolder, "bin", "Debug", "Hi.exe")), build.Describe("pbuild"));
        return (source, program);
    }

    /// <summary>
    /// A debug session on the hello program with the folder's debug adapter, through the product's own client: stop at a
    /// breakpoint, read the arguments and locals, run to the end. While the program is stopped, every module the adapter
    /// process has loaded must come from <c>H</c> or from Windows. Returns the adapter's loaded modules (file names) and
    /// what it wrote to its error stream.
    /// </summary>
    public async Task<(IReadOnlyList<string> AdapterModules, IReadOnlyList<string> AdapterErrors)> DebugHelloAsync(string source, string program, CancellationToken ct)
    {
        var adapter = Combine("third_party", "llvm", "bin", "lldb-dap.exe");
        Assert.True(File.Exists(adapter), adapter);
        var errors = new List<string>();
        var stopped = new TaskCompletionSource<StopInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminated = new TaskCompletionSource<TerminatedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new System.Text.StringBuilder();
        IReadOnlyList<string> modules;

        var session = new DebugSessionFactory().Create(new DebugSessionOptions
        {
            AdapterPath = adapter,
            UserSourceRoots = [Path.GetDirectoryName(source)!],
            // safety nets for a loaded machine (the full test run builds and tests in parallel); every wait is on an event
            RequestTimeout = TimeSpan.FromSeconds(60),
            LaunchTimeout = DebugLimit,
            Trace = entry =>
            {
                if (entry.Direction == DapDirection.AdapterStderr)
                    lock (errors)
                        errors.Add(entry.Json);
            },
        });
        await using (session)
        {
            session.Stopped += (_, stop) => stopped.TrySetResult(stop);
            session.Terminated += (_, end) =>
            {
                terminated.TrySetResult(end);
                stopped.TrySetException(new InvalidOperationException($"the session ended before the breakpoint: {end.Cause}, exit code {end.ExitCode}, {end.Message}"));
            };
            session.Output += (_, e) =>
            {
                if (e.Category == OutputCategory.Stdout)
                    lock (output)
                        output.Append(e.Text);
            };

            // The adapter starts here, with this process's environment. A Python installed on the user's machine must not
            // reach it: the variables an unisolated runtime would follow point at a folder that breaks its start.
            using (ForeignPython())
            {
                await session.LaunchAsync(
                    new LaunchRequest
                    {
                        Program = program,
                        Breakpoints = [new SourceBreakpoints(source, [new BreakpointSpec { Id = 1, Line = BreakpointLine }])],
                    },
                    ct);
            }

            var stop = await stopped.Task.WaitAsync(DebugLimit, ct);
            Assert.Equal(StopReason.Breakpoint, stop.Reason);
            Assert.Equal([1], stop.HitBreakpointIds);
            Assert.Equal(source, stop.Location?.Path, ignoreCase: true);
            Assert.Equal(BreakpointLine, stop.Location?.Line);
            Assert.True(session.HasTypeInfo, "the debug-info sidecar of hello.exe was not read");

            var frames = (await session.GetStackTraceAsync(stop.ThreadId, ct: ct)).Frames;
            Assert.Equal("Greet", frames[0].Name);
            Assert.Equal(12, frames[1].Location?.Line);
            var scopes = await session.GetScopesAsync(frames[0].Id, ct);
            var arguments = await session.GetChildrenAsync(scopes.Single(s => s.Kind == ScopeKind.Arguments).Handle, ct: ct);
            var locals = await session.GetChildrenAsync(scopes.Single(s => s.Kind == ScopeKind.Locals).Handle, ct: ct);
            Assert.Equal(["Count = 21 : Integer"], arguments.Select(v => $"{v.Name} = {v.Value} : {v.TypeName}"));
            Assert.Equal(["Total = 42 : Integer"], locals.Select(v => $"{v.Name} = {v.Value} : {v.TypeName}"));

            modules = AdapterModules(adapter);

            await session.ContinueAsync(ct);
            var end = await terminated.Task.WaitAsync(DebugLimit, ct);
            Assert.Equal((TerminationCause.Exited, 0), (end.Cause, end.ExitCode));
        }

        lock (output)
            Assert.Equal("Hello, world! 42\n", output.ToString().ReplaceLineEndings("\n"));

        // nothing but H and Windows: the adapter, its library and the bundled Python runtime come from the folder
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\";
        Assert.All(modules, module => Assert.True(
            module.StartsWith(Home + @"\", StringComparison.OrdinalIgnoreCase) || module.StartsWith(windows, StringComparison.OrdinalIgnoreCase),
            "the debug adapter loaded a module from outside the product folder: " + module));
        foreach (var needed in (string[])[@"third_party\llvm\bin\liblldb.dll", @"third_party\python-embed\python313.dll"])
            Assert.Contains(Combine(needed), modules, StringComparer.OrdinalIgnoreCase);
        lock (errors)
            return (modules, [.. errors]);
    }

    /// <summary>
    /// Sets <c>PYTHONHOME</c> and <c>PYTHONPATH</c> of this process to a folder whose <c>encodings</c> package fails to
    /// load, until the result is disposed. The bundled runtime ignores both (its <c>python313._pth</c>); a runtime that
    /// followed them would not start, and the debug adapter with it. The end-to-end tests run one after the other, and
    /// no other test of this assembly starts a program that reads the two variables.
    /// </summary>
    private ForeignPythonVariables ForeignPython()
    {
        var folder = box.In("foreign-python");
        Directory.CreateDirectory(Path.Combine(folder, "encodings"));
        File.WriteAllText(Path.Combine(folder, "encodings", "__init__.py"), "raise ImportError('a foreign Python folder was used')" + Environment.NewLine);
        return new ForeignPythonVariables(folder);
    }

    private sealed class ForeignPythonVariables : IDisposable
    {
        private static readonly string[] Names = ["PYTHONHOME", "PYTHONPATH"];
        private readonly string?[] _before = [.. Names.Select(Environment.GetEnvironmentVariable)];

        public ForeignPythonVariables(string folder)
        {
            foreach (var name in Names)
                Environment.SetEnvironmentVariable(name, folder);
        }

        public void Dispose()
        {
            for (var i = 0; i < Names.Length; i++)
                Environment.SetEnvironmentVariable(Names[i], _before[i]);
        }
    }

    /// <summary>The modules loaded by the adapter process started from this folder (there is exactly one while a session is stopped).</summary>
    private static List<string> AdapterModules(string adapter)
    {
        var found = new List<List<string>>();
        foreach (var process in Process.GetProcessesByName("lldb-dap"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, adapter, StringComparison.OrdinalIgnoreCase))
                        found.Add([.. process.Modules.Cast<ProcessModule>().Select(m => m.FileName).Order(StringComparer.OrdinalIgnoreCase)]);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // another run's adapter that ended meanwhile, or one this user may not read
                }
            }
        }

        Assert.True(found.Count == 1, $"expected one running debug adapter {adapter}, found {found.Count}");
        return found[0];
    }

    /// <summary>
    /// LSRC-006: the library source layout checks of the policy tests, run against this folder (a child process of the
    /// built test program with <c>FREEXCOMP_LAYOUT_HOME</c> = <c>H</c>). All of them must run and pass; none may be skipped.
    /// </summary>
    public void AssertLibrarySourceLayout()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = output.Parent!.Name;   // bin/<configuration>/<framework>/
        var tests = Path.Combine(SetupProgramBuild.RepositoryRoot(), "tests", "policy", "FreexComp.Policy.Tests", "bin", configuration, "net10.0", "FreexComp.Policy.Tests.exe");
        Assert.True(File.Exists(tests), tests + " is missing (the policy tests are built with this project)");
        var report = box.In("layout-" + Guid.NewGuid().ToString("N") + ".xml");

        var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value ?? "", StringComparer.OrdinalIgnoreCase);
        environment["FREEXCOMP_LAYOUT_HOME"] = Home;
        var run = ChildProgram.Run(tests,
            ["-class", "FreexComp.Policy.Tests.LibrarySourceLayoutTests", "-failSkips", "-noColor", "-noLogo", "-xml", report],
            Path.GetDirectoryName(tests)!, environment, ToolLimit);
        Assert.True(run.ExitCode == 0 && File.Exists(report), run.Describe("LibrarySourceLayoutTests"));

        var assembly = XDocument.Load(report).Root!.Element("assembly");
        Assert.NotNull(assembly);
        var counts = (Total: (int)assembly.Attribute("total")!, Passed: (int)assembly.Attribute("passed")!,
            Failed: (int)assembly.Attribute("failed")!, Skipped: (int)assembly.Attribute("skipped")!);
        Assert.True(counts is { Total: >= 5, Failed: 0, Skipped: 0 } && counts.Passed == counts.Total, $"{counts}\n{run.Output}");
    }

    private static void AssertSameFile(string expected, string actual)
    {
        Assert.True(File.Exists(actual), actual + " is missing");
        Assert.True(File.ReadAllBytes(expected).AsSpan().SequenceEqual(File.ReadAllBytes(actual)), actual + " differs from " + expected);
    }

    /// <summary>The tracked files under a repository folder, relative to the repository, <c>\</c> separated, sorted.</summary>
    private static List<string> TrackedFiles(string repository, string folder)
    {
        var git = ChildProgram.Run("git", ["-C", repository, "-c", "core.quotepath=off", "ls-files", "--", folder], repository, null, ToolLimit);
        Assert.True(git.ExitCode == 0, git.Describe("git ls-files"));
        return [.. git.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.Replace('/', '\\')).Order(StringComparer.OrdinalIgnoreCase)];
    }
}
