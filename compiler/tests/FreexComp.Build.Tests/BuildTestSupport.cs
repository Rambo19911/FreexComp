using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Build.Tests;

/// <summary>
/// An in-memory workspace for build-engine tests: the real <c>rtl/src/System.pas</c> (+ include files, alias table) under
/// <c>C:\rtl\src</c>, the RTL's <c>.def</c> files under <c>C:\rtl\winapi\def</c>, a fake LLVM bin directory <c>C:\llvm\bin</c>
/// and a console project <c>C:\p\Hello.pproj</c> with <c>Hello.pas</c>.
/// </summary>
internal sealed class BuildWorkspace
{
    public const string Rtl = @"C:\rtl\src";
    public const string Llvm = @"C:\llvm\bin";
    public const string ProjectPath = @"C:\p\Hello.pproj";
    public const string MainPath = @"C:\p\Hello.pas";
    public const string Exe = @"C:\p\bin\Debug\Hello.exe";
    public const string HelloProgram = "program Hello; begin Writeln('hi') end.";

    private static readonly Lazy<(string Path, string Text)[]> RtlFiles = new(() =>
    {
        var src = Path.Combine(RepoPaths.Root, "rtl", "src");
        var def = Path.Combine(RepoPaths.Root, "rtl", "winapi", "def");
        return
        [
            .. Directory.GetFiles(src, "*.inc").Append(Path.Combine(src, "System.pas")).Append(Path.Combine(src, "unit-aliases.json"))
                .Select(p => (Path.Combine(Rtl, Path.GetFileName(p)), File.ReadAllText(p))),
            .. Directory.GetFiles(def, "*.def").Select(p => (Path.Combine(@"C:\rtl\winapi\def", Path.GetFileName(p)), File.ReadAllText(p))),
        ];
    });

    public BuildWorkspace(string program = HelloProgram, string? projectJson = null)
    {
        foreach (var (path, text) in RtlFiles.Value)
        {
            Fs.Add(path, text);
        }

        foreach (var tool in new[] { "clang.exe", "lld-link.exe", "llvm-dlltool.exe" })
        {
            Fs.Add(Path.Combine(Llvm, tool), "");
        }

        Fs.Add(MainPath, program);
        Fs.Add(ProjectPath, projectJson ?? ProjectJson("Hello"));
        Tools = new FakeToolchain(Fs);
    }

    public InMemoryFileSystem Fs { get; } = new();

    public FakeToolchain Tools { get; }

    /// <summary>A minimal valid project file text; <paramref name="extra"/> is inserted as further top-level keys.</summary>
    public static string ProjectJson(string name, string mainSource = "Hello.pas", string extra = "") =>
        $$"""{ "formatVersion": 1, "name": "{{name}}", "mainSource": "{{mainSource}}"{{(extra.Length > 0 ? ", " + extra : "")}} }""";

    public BuildRequest Request(BuildTarget target = BuildTarget.Build, string path = ProjectPath) => new()
    {
        ProjectPath = path,
        Target = target,
        ToolchainDirectory = Llvm,
        RtlDirectory = Rtl,
    };

    /// <param name="fs">The engine's file system; null → <see cref="Fs"/> (the fake tools always write to <see cref="Fs"/>).</param>
    public async Task<(BuildResult Result, EventLog Log)> RunAsync(BuildRequest request, CancellationToken? ct = null, IFileSystem? fs = null)
    {
        var log = new EventLog();
        var result = await new BuildEngine(fs ?? Fs, Tools).RunAsync(request, log, ct ?? TestContext.Current.CancellationToken);
        return (result, log);
    }
}

/// <summary>Records events synchronously, in the order they are reported.</summary>
internal sealed class EventLog : IProgress<BuildEvent>
{
    private readonly List<BuildEvent> _events = [];

    public ImmutableArray<BuildEvent> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>One line per event, without timestamps and elapsed times: <c>started Debug win64 Build</c>, <c>info Compiling X</c>, …</summary>
    public ImmutableArray<string> Lines => [.. Events.Select(Describe)];

    public ImmutableArray<string> Info => [.. Events.OfType<BuildEvent.OutputLine>().Where(e => e.Source == OutputSource.Info).Select(e => e.Text)];

    public void Report(BuildEvent value)
    {
        lock (_events)
        {
            _events.Add(value);
        }
    }

    private static string Describe(BuildEvent e) => e switch
    {
        BuildEvent.Started s => $"started {Path.GetFileName(s.ProjectPath)} {s.Configuration} {s.Platform} {s.Target}",
        BuildEvent.Diagnostic d => $"diagnostic {d.Value.Code}",
        BuildEvent.CommandLine c => "command " + Path.GetFileName(c.Text.Split(' ')[0]),
        BuildEvent.OutputLine o => $"{o.Source.ToString().ToLowerInvariant()} {o.Text}",
        BuildEvent.Finished f => $"finished {Path.GetFileName(f.ProjectPath)} success={f.Success} upToDate={f.UpToDate} {f.Errors}/{f.Warnings}/{f.Hints}",
        _ => e.ToString(),
    };
}

/// <summary>
/// A fake LLVM toolchain behind <see cref="IProcessRunner"/>: <c>clang --version</c> prints the pinned version, <c>clang -c</c>
/// writes the <c>-o</c> object, <c>llvm-dlltool</c> the <c>-l</c> library, <c>lld-link @rsp</c> the <c>/out:</c> exe; other
/// programs (the built exe) run through <see cref="Program"/>.
/// </summary>
internal sealed class FakeToolchain(IFileSystem fs) : IProcessRunner
{
    private readonly ConcurrentQueue<string> _calls = new();
    private readonly ConcurrentQueue<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> _programs = new();

    /// <summary>Every started program that is not a tool: full path, arguments, working directory.</summary>
    public ImmutableArray<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> Programs => [.. _programs];

    /// <summary>lld-link succeeds without writing the exe.</summary>
    public bool LinkWritesNothing { get; set; }

    /// <summary>lld-link copies this real file (from disk) as the exe.</summary>
    public string? LinkCopiesFrom { get; set; }

    /// <summary>File names (without directory) of every started process, in start order.</summary>
    public ImmutableArray<string> Calls => [.. _calls];

    /// <summary>Runs before a tool does its work (e.g. to block until cancelled).</summary>
    public Func<string, IReadOnlyList<string>, CancellationToken, Task>? BeforeTool { get; set; }

    /// <summary>Lines lld-link prints and its exit code; null → success.</summary>
    public (string[] Output, int ExitCode)? LinkFailure { get; set; }

    /// <summary>Behaviour of a started program (not a tool): stdout, stderr, exit code.</summary>
    public Func<string, IReadOnlyList<string>, string, (string[] StdOut, string[] StdErr, int ExitCode)> Program { get; set; } =
        (_, _, _) => ([], [], 0);

    public async Task<int> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string workingDirectory, Action<string> onStdOut, Action<string> onStdErr, CancellationToken ct)
    {
        var name = Path.GetFileName(fileName);
        if (!(name == "clang.exe" && arguments is ["--version"]))
        {
            _calls.Enqueue(name);
        }

        if (BeforeTool is { } before)
        {
            await before(name, arguments, ct);
        }

        ct.ThrowIfCancellationRequested();
        switch (name)
        {
            case "clang.exe" when arguments is ["--version"]:
                onStdOut("clang version " + CompilerInfo.LlvmPin);
                return 0;
            case "clang.exe":
                fs.WriteAllBytes(arguments[IndexOf(arguments, "-o") + 1], Encoding.UTF8.GetBytes("obj"));
                return 0;
            case "llvm-dlltool.exe":
                fs.WriteAllBytes(arguments[IndexOf(arguments, "-l") + 1], Encoding.UTF8.GetBytes("lib"));
                return 0;
            case "lld-link.exe":
                if (LinkFailure is { } failure)
                {
                    foreach (var line in failure.Output)
                    {
                        onStdErr(line);
                    }

                    return failure.ExitCode;
                }

                var rsp = fs.ReadAllText(arguments[0][1..]);
                var outLine = rsp.Split('\n').Single(l => l.TrimStart('"').StartsWith("/out:", StringComparison.Ordinal));
                if (!LinkWritesNothing)
                {
                    fs.WriteAllBytes(outLine.Trim('"')["/out:".Length..], LinkCopiesFrom is { } real ? File.ReadAllBytes(real) : Encoding.UTF8.GetBytes("exe"));
                    // a library link also writes its import library (CMP-1063)
                    if (rsp.Split('\n').SingleOrDefault(l => l.TrimStart('"').StartsWith("/implib:", StringComparison.Ordinal)) is { } implib)
                    {
                        fs.WriteAllBytes(implib.Trim('"')["/implib:".Length..], Encoding.UTF8.GetBytes("import library"));
                    }
                }

                onStdOut("fake lld-link done");
                return 0;
            default:
                _programs.Enqueue((fileName, [.. arguments], workingDirectory));
                var (stdout, stderr, exit) = Program(fileName, arguments, workingDirectory);
                foreach (var line in stdout)
                {
                    onStdOut(line);
                }

                foreach (var line in stderr)
                {
                    onStdErr(line);
                }

                return exit;
        }
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        throw new InvalidOperationException("missing " + value);
    }
}
