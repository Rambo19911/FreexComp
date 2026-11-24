using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The network policy check (NET-001…016): every place in the product assemblies that is able to open a connection has a
/// row in the tracked root file <c>NETWORK-POLICY.md</c>, and every row still has its call site.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class NetworkPolicyTests
{
    private const string PolicyFile = "NETWORK-POLICY.md";
    private const string Http = "FreexComp.Policy.Tests.Fixtures.UsesHttp";
    private const string Native = "FreexComp.Policy.Tests.Fixtures.UsesNativeSocket";

    private static readonly string[] RequiredAssemblies =
    [
        "opc", "pbuild", "FreexComp.Compiler", "FreexComp.Build", "FreexComp.LanguageService", "FreexComp.Ide",
        "FreexComp.Ide.Core", "FreexComp.Designer", "FreexComp.Debugger",
    ];

    private static string OwnAssembly => typeof(NetworkPolicyTests).Assembly.Location;

    private static string OwnName => typeof(NetworkPolicyTests).Assembly.GetName().Name!;

    private static string ReadPolicyFile()
    {
        var path = RepoPaths.Combine(PolicyFile);
        Assert.True(File.Exists(path), $"{PolicyFile} is missing in the repository root");
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], $"{PolicyFile} starts with a byte order mark");
        Assert.DoesNotContain((byte)'\r', bytes);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
    }

    /// <summary>An allow-list in the fixed structure with one row (and, for a row that is not a local pipe, one bullet) per entry.</summary>
    private static string AllowList(bool bullets, params (string Assembly, string Type, string Kind)[] rows)
    {
        static bool Pipe(string kind) => kind == "pipe-local";
        var text = new StringBuilder();
        text.Append("# Network behaviour of FreexComp\n\nText.\n\n## What FreexComp never does\n- Nothing.\n\n");
        text.Append("## Features that can open a connection\n");
        foreach (var row in rows.Where(r => bullets && !Pipe(r.Kind)))
            text.Append($"- Fixture {row.Type}: contacts the address you entered; remove it in Options.\n");
        text.Append("\n## Call sites\nText.\n\n");
        text.Append("| Feature | Card | Assembly | Type | Kind | Default | Shown and removed in |\n|---|---|---|---|---|---|---|\n");
        foreach (var row in rows)
        {
            text.Append($"| Fixture {row.Type} | F02-006 | {row.Assembly} | {row.Type} | {row.Kind} | ");
            text.Append(Pipe(row.Kind) ? "always (local only) | — |\n" : "off | Options > Network |\n");
        }

        return text.ToString();
    }

    [Fact]
    [Trait("Req", "NET-001")]
    [Trait("Req", "NET-010")]
    [Trait("Req", "NET-012")]
    [Trait("Req", "NET-016")]
    public void NetworkPolicy_NoUnlistedEndpoints()
    {
        var products = ProductOutputs.All();
        Assert.All(RequiredAssemblies, name => Assert.Contains(products, p => p.Name == name));
        var paths = products.Select(p => Path.Combine(p.OutputDirectory, p.Name + ".dll")).ToList();
        Assert.All(paths, path => Assert.True(File.Exists(path), $"{path} is not built: build the solution first"));

        var references = paths.Sum(path => (long)NetworkPolicy.Scan(path).ReferenceCount);
        Assert.True(references > 1000L * paths.Count, $"the scanner saw only {references} references in {paths.Count} assemblies");

        Assert.Empty(NetworkPolicy.Check(paths, ReadPolicyFile()));
    }

    [Fact]
    [Trait("Req", "NET-041")]
    public void ReadmeLinksToThePolicyPage()
    {
        var readme = File.ReadAllText(RepoPaths.Combine("README.md")).ReplaceLineEndings("\n");
        var start = readme.IndexOf("\n## Network\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "README.md has no section '## Network'");
        var end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        Assert.Contains(PolicyFile, end < 0 ? readme[start..] : readme[start..end], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "NET-003")]
    public void AllowListFormatErrorsAreReported()
    {
        var real = ReadPolicyFile();
        Assert.Empty(Format(real));
        var rows = real.Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| Feature", StringComparison.Ordinal)).ToList();
        Assert.True(rows.Count >= 2, "the real file has fewer than two rows");
        var (first, second) = (rows[0], rows[1]);
        string Cell(string row, int index) => row.Split('|')[index + 1].Trim();
        string WithCell(string row, int index, string value)
        {
            var cells = row.Split('|');
            cells[index + 1] = " " + value + " ";
            return string.Join('|', cells).Replace("|  |", "| |", StringComparison.Ordinal);
        }

        // an empty cell
        var empty = WithCell(first, 0, "");
        Assert.Contains(Format(real.Replace(first, empty, StringComparison.Ordinal)), p => p.Contains("empty cell", StringComparison.Ordinal) && p.Contains(empty, StringComparison.Ordinal));

        // an unknown kind
        var unknown = WithCell(first, 4, "carrier-pigeon");
        Assert.Contains(Format(real.Replace(first, unknown, StringComparison.Ordinal)), p => p.Contains("unknown kind", StringComparison.Ordinal) && p.Contains(unknown, StringComparison.Ordinal));

        // kinds out of alphabetical order
        var unordered = WithCell(first, 4, "socket, http");
        Assert.Contains(Format(real.Replace(first, unordered, StringComparison.Ordinal)), p => p.Contains("alphabetical", StringComparison.Ordinal) && p.Contains(unordered, StringComparison.Ordinal));

        // a duplicate (Assembly, Type) pair
        var duplicate = WithCell(WithCell(second, 2, Cell(first, 2)), 3, Cell(first, 3));
        Assert.Contains(Format(real.Replace(second, duplicate, StringComparison.Ordinal)), p => p.Contains("duplicate", StringComparison.Ordinal) && p.Contains(duplicate, StringComparison.Ordinal));

        // rows not ordered by (Assembly, Type)
        var swapped = real.Replace(first, "\u0001", StringComparison.Ordinal).Replace(second, first, StringComparison.Ordinal).Replace("\u0001", second, StringComparison.Ordinal);
        Assert.Contains(Format(swapped), p => p.Contains("order", StringComparison.Ordinal) && p.Contains(first, StringComparison.Ordinal));

        // "always (local only)" on a row that is not a local pipe
        var notPipe = WithCell(first, 4, "http");
        Assert.Equal("always (local only)", Cell(notPipe, 5));
        Assert.Contains(Format(real.Replace(first, notPipe, StringComparison.Ordinal)), p => p.Contains("always (local only)", StringComparison.Ordinal) && p.Contains(notPipe, StringComparison.Ordinal));

        // an unknown default, an unknown card
        var badDefault = WithCell(first, 5, "on");
        Assert.Contains(Format(real.Replace(first, badDefault, StringComparison.Ordinal)), p => p.Contains("default", StringComparison.OrdinalIgnoreCase) && p.Contains(badDefault, StringComparison.Ordinal));
        var badCard = WithCell(first, 1, "someday");
        Assert.Contains(Format(real.Replace(first, badCard, StringComparison.Ordinal)), p => p.Contains("card", StringComparison.OrdinalIgnoreCase) && p.Contains(badCard, StringComparison.Ordinal));

        // the fixed structure (NET-001): a renamed section, a changed column
        Assert.Contains(Format(real.Replace("## Call sites", "## Places", StringComparison.Ordinal)), p => p.Contains("## Call sites", StringComparison.Ordinal));
        Assert.Contains(Format(real.Replace("| Kind |", "| Sort |", StringComparison.Ordinal)), p => p.Contains("columns", StringComparison.Ordinal));

        static IReadOnlyList<string> Format(string markdown) =>
            [.. NetworkPolicy.Check([], markdown).Where(p => p.StartsWith("allow-list format: ", StringComparison.Ordinal))];
    }

    [Fact]
    [Trait("Req", "NET-011")]
    [Trait("Req", "NET-012")]
    public void FixtureWithHttpClientAndNoRowFails()
    {
        var problems = NetworkPolicy.Check([OwnAssembly], AllowList(bullets: true));
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith($"unlisted endpoint: {OwnName}: {Http} uses System.Net.Http.", StringComparison.Ordinal) && p.EndsWith(" (http)", StringComparison.Ordinal));
        Assert.Contains($"unlisted endpoint: {OwnName}: {Native} uses ws2_32!WSAGetLastError (native)", problems);

        Assert.Empty(NetworkPolicy.Check([OwnAssembly], AllowList(bullets: true, (OwnName, Http, "http"), (OwnName, Native, "native"))));

        // a row of the wrong kind does not cover the call site
        var wrongKind = NetworkPolicy.Check([OwnAssembly], AllowList(bullets: true, (OwnName, Http, "socket"), (OwnName, Native, "native")));
        Assert.Contains(wrongKind, p => p.StartsWith($"unlisted endpoint: {OwnName}: {Http} uses ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "NET-012")]
    public void StaleRowFails()
    {
        const string gone = "FreexComp.Policy.Tests.Fixtures.Gone";
        var problems = NetworkPolicy.Check(
            [OwnAssembly],
            AllowList(bullets: true, (OwnName, gone, "http"), (OwnName, Http, "http, socket"), (OwnName, Native, "native")));
        Assert.Equal(
            [$"stale allow-list row: {OwnName} / {gone} (http)", $"stale allow-list row: {OwnName} / {Http} (socket)"],
            problems);

        // a row for an assembly that is not part of the product is stale too
        Assert.Contains(
            "stale allow-list row: Elsewhere / Some.Type (pipe-local)",
            NetworkPolicy.Check([OwnAssembly], AllowList(bullets: true, ("Elsewhere", "Some.Type", "pipe-local"), (OwnName, Http, "http"), (OwnName, Native, "native"))));
    }

    [Fact]
    [Trait("Req", "NET-002")]
    public void NestedTypesAndLambdasCountAsOutermostType()
    {
        var findings = NetworkPolicy.Scan(OwnAssembly).Findings;
        Assert.Contains(findings, f => f.Type == Http && f.Kind == "http" && f.Target == "System.Net.Http.HttpClient");         // field of the type itself
        Assert.Contains(findings, f => f.Type == Http && f.Kind == "http" && f.Target == "System.Net.Http.HttpRequestMessage"); // field of a nested type
        Assert.Contains(findings, f => f.Type == Http && f.Kind == "http" && f.Target == "System.Net.Http.HttpMethod");         // used in a lambda only
        Assert.All(findings, f => Assert.False(f.Type.Contains('+') || f.Type.Contains('<'), f.Type));
        Assert.Equal([Http, Native], findings.Select(f => f.Type).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "NET-016")]
    public void PipeClientWithNonLiteralServerNameFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "freexcomp-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "PipeFixture.dll");
            EmitPipeFixture(path);
            var problems = NetworkPolicy.Check(
                [path],
                AllowList(
                    bullets: true,
                    ("PipeFixture", "PipeFixture.ExpressionAfterLiteral", "pipe-local"),
                    ("PipeFixture", "PipeFixture.LiteralServer", "pipe-local"),
                    ("PipeFixture", "PipeFixture.NameOnly", "pipe-local"),
                    ("PipeFixture", "PipeFixture.OtherLiteral", "pipe-local"),
                    ("PipeFixture", "PipeFixture.VariableServer", "pipe-local")));
            Assert.Equal(
                [
                    "pipe client with a non-literal server name: PipeFixture.NameOnly",
                    "pipe client with a non-literal server name: PipeFixture.OtherLiteral",
                    "pipe client with a non-literal server name: PipeFixture.VariableServer",
                ],
                problems);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Writes an assembly whose types construct a pipe client in different ways. The pipe type is named by a string so
    /// that this test assembly itself gains no call site; nothing in the written assembly is ever loaded or run.
    /// </summary>
    private static void EmitPipeFixture(string path)
    {
        var pipeType = Type.GetType("System.IO.Pipes.NamedPipeClientStream, System.IO.Pipes", throwOnError: true)!;
        var twoNames = pipeType.GetConstructor([typeof(string), typeof(string)])!;
        var oneName = pipeType.GetConstructor([typeof(string)])!;
        var concat = typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!;
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("PipeFixture"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("PipeFixture");

        void Define(string name, Action<ILGenerator> body)
        {
            var type = module.DefineType("PipeFixture." + name, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var il = type.DefineMethod("Open", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(string)]).GetILGenerator();
            body(il);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            type.CreateType();
        }

        Define("LiteralServer", il => { il.Emit(OpCodes.Ldstr, "."); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Newobj, twoNames); });
        Define("ExpressionAfterLiteral", il =>
        {
            il.Emit(OpCodes.Ldstr, ".");
            il.Emit(OpCodes.Ldstr, "prefix-");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, concat);
            il.Emit(OpCodes.Newobj, twoNames);
        });
        Define("VariableServer", il => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, "name"); il.Emit(OpCodes.Newobj, twoNames); });
        Define("OtherLiteral", il => { il.Emit(OpCodes.Ldstr, "build-server"); il.Emit(OpCodes.Ldstr, "name"); il.Emit(OpCodes.Newobj, twoNames); });
        Define("NameOnly", il => { il.Emit(OpCodes.Ldstr, "."); il.Emit(OpCodes.Newobj, oneName); });
        assembly.Save(path);
    }

    [Fact]
    [Trait("Req", "NET-004")]
    public void MissingFeatureBulletFails()
    {
        (string, string, string)[] rows = [(OwnName, Http, "http"), (OwnName, Native, "native")];
        Assert.Equal(
            [$"missing feature bullet: Fixture {Http}", $"missing feature bullet: Fixture {Native}"],
            NetworkPolicy.Check([OwnAssembly], AllowList(bullets: false, rows)));
        Assert.Empty(NetworkPolicy.Check([OwnAssembly], AllowList(bullets: true, rows)));
    }

    [GeneratedRegex(@"(^|[=:])\\\\(?![.?](\\|$))")]
    private static partial Regex RemoteShare();

    private static string? UrlIn(string argument)
    {
        foreach (var scheme in (string[])["http://", "https://", "ftp://"])
        {
            if (argument.Contains(scheme, StringComparison.OrdinalIgnoreCase))
                return scheme;
        }

        return RemoteShare().IsMatch(argument) ? @"\\host" : null;
    }

    [Fact]
    [Trait("Req", "NET-015")]
    public void ArgumentCheckSeesUrlsAndRemoteShares()
    {
        Assert.Equal("https://", UrlIn("-fdebug-prefix-map=x=HTTPS://example.invalid/src"));
        Assert.Equal("ftp://", UrlIn("ftp://example.invalid/a.lib"));
        Assert.Equal(@"\\host", UrlIn(@"\\buildhost\share\a.lib"));
        Assert.Equal(@"\\host", UrlIn(@"/libpath:\\buildhost\share"));
        Assert.Null(UrlIn(@"\\.\pipe\name"));
        Assert.Null(UrlIn(@"\\?\C:\long\path\a.obj"));
        Assert.Null(UrlIn(@"C:\work\a.obj"));
        Assert.Null(UrlIn("-target"));
    }

    [Fact]
    [Trait("Req", "NET-015")]
    [Trait("Category", "Toolchain")]
    public async Task ToolCommandLinesContainNoUrls()
    {
        var directory = Path.Combine(Path.GetTempPath(), "freexcomp-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // the console project of the IDE's own template
            var template = RepoPaths.Combine("ide", "src", "FreexComp.Ide.Core", "Templates", "console-pascal");
            foreach (var (source, target) in ((string, string)[])[("Program.pas.txt", "Hello.pas"), ("Project.pproj.txt", "Hello.pproj")])
                File.WriteAllText(Path.Combine(directory, target), File.ReadAllText(Path.Combine(template, source)).Replace("$(ProjectName)", "Hello", StringComparison.Ordinal));

            var runner = new RecordingRunner();
            var log = new EventLog();
            var result = await new BuildEngine(PhysicalFileSystem.Instance, runner).RunAsync(
                new BuildRequest
                {
                    ProjectPath = Path.Combine(directory, "Hello.pproj"),
                    Verbose = true,
                    ToolchainDirectory = RepoPaths.Combine("third_party", "llvm", "bin"),
                    RtlDirectory = RepoPaths.Combine("rtl", "src"),
                },
                log,
                TestContext.Current.CancellationToken);
            Assert.True(result.Success, string.Join("\n", log.Events.OfType<BuildEvent.Diagnostic>().Select(d => d.Value.ToString())));

            var started = runner.Started;
            var commandLines = log.Events.OfType<BuildEvent.CommandLine>().Select(c => c.Text).ToList();
            Assert.True(started.Count >= 2, $"only {started.Count} tool processes were started");   // at least the back end and the linker
            Assert.True(commandLines.Count >= 2, $"only {commandLines.Count} command lines were reported");

            var offending = new List<string>();
            foreach (var (tool, arguments) in started)
                offending.AddRange(arguments.Where(a => UrlIn(a) is not null).Select(a => $"{Path.GetFileName(tool)}: {a}"));
            offending.AddRange(commandLines.Where(c => c.Split(' ').Any(word => UrlIn(word.Trim('"')) is not null)));
            Assert.Empty(offending);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Real tool processes; every start is recorded with its argument list.</summary>
    private sealed class RecordingRunner : IProcessRunner
    {
        private readonly ConcurrentQueue<(string Tool, IReadOnlyList<string> Arguments)> _started = new();

        public IReadOnlyList<(string Tool, IReadOnlyList<string> Arguments)> Started => [.. _started];

        public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, Action<string> onStdOut, Action<string> onStdErr, CancellationToken ct)
        {
            _started.Enqueue((fileName, [.. arguments]));
            return ProcessRunner.Instance.RunAsync(fileName, arguments, workingDirectory, onStdOut, onStdErr, ct);
        }
    }

    private sealed class EventLog : IProgress<BuildEvent>
    {
        private readonly ConcurrentQueue<BuildEvent> _events = new();

        public IReadOnlyList<BuildEvent> Events => [.. _events];

        public void Report(BuildEvent value) => _events.Enqueue(value);
    }
}
