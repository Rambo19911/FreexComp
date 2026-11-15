using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.PBuild;

/// <summary>
/// Where <c>pbuild</c> runs: current directory, output streams, file system. <see cref="ProcessRunner"/>, <see cref="ToolchainDirectory"/>
/// and <see cref="RtlDirectory"/> are seams for in-process tests (null → real processes and the default look-ups).
/// </summary>
public sealed record PbuildEnvironment(string CurrentDirectory, TextWriter Out, TextWriter Error, IFileSystem FileSystem)
{
    public IProcessRunner? ProcessRunner { get; init; }

    public string? ToolchainDirectory { get; init; }

    public string? RtlDirectory { get; init; }
}

/// <summary>
/// The <c>pbuild</c> driver (compiler-cli.md §4, build-engine.md CMP-840…842): arguments → <see cref="BuildRequest"/> →
/// <see cref="BuildEngine"/>, events written to stdout as they arrive (text or one JSON object per line), the summary line last.
/// Exit codes: 0 success, 1 project/compile errors, 2 internal error or toolchain missing. <c>--target run</c>: the program inherits
/// the console and its exit code is printed, not propagated (CMP-841).
/// </summary>
public static class PbuildCommand
{
    public const string HelpText = """
        pbuild <project.pproj | group.pgroup> [--config Debug|Release] [--target build|rebuild|clean|run]
               [--diagnostics text|json] [--rtl <dir>] [--toolchain <dir>] [--verbose]
          --config <name>       configuration (default: the project's active configuration; matched case-insensitively)
          --target <target>     build (default), rebuild, clean or run (build, then run the program)
          --diagnostics text|json   output form (default text)
          --rtl <dir>           RTL root (default: $FREEXCOMP_HOME/rtl/src, else relative to pbuild.exe)
          --toolchain <dir>     LLVM bin dir (default: third_party/llvm/bin, else PATH)
          --verbose             echo external commands
          --version | --help
        """;

    /// <summary>Codes that end <c>pbuild</c> with exit code 2: internal error, toolchain missing.</summary>
    private static readonly HashSet<string> ExitCode2 = new(StringComparer.Ordinal) { "E0001", "E4001" };

    public static int Run(IReadOnlyList<string> args, PbuildEnvironment env, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);
        var clock = Stopwatch.StartNew();
        var cwd = PathUtil.Normalize(env.CurrentDirectory);
        string Full(string path) => PathUtil.Normalize(Path.IsPathFullyQualified(path) ? path : Path.Combine(cwd, path));

        string? project = null, configuration = null, rtl = null, toolchain = null;
        var target = BuildTarget.Build;
        bool json = false, verbose = false, help = false, version = false;
        var invalid = new List<string>();
        var errors = new List<Diagnostic>();

        // C-239: a folder that does not exist is a bad option value (E0005 with option and value); the file system has no
        // directory test, so a folder exists when it holds a file or a folder
        string? Folder(string option, string value)
        {
            var full = Full(value);
            if (env.FileSystem.EnumerateFiles(full, "*").Any() || env.FileSystem.EnumerateDirectories(full).Any())
            {
                return full;
            }

            invalid.Add(option + " " + value);
            return null;
        }

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? Value()
            {
                if (i + 1 < args.Count)
                {
                    return args[++i];
                }

                invalid.Add("\0" + arg);
                return null;
            }

            switch (arg)
            {
                case "--help": help = true; break;
                case "--version": version = true; break;
                case "--verbose": verbose = true; break;
                case "--config": configuration = Value() ?? configuration; break;
                case "--rtl": if (Value() is { } rtlDir) { rtl = Folder(arg, rtlDir); } break;          // C-239: as opc §1
                case "--toolchain": if (Value() is { } bin) { toolchain = Folder(arg, bin); } break;
                case "--target":
                    switch (Value())
                    {
                        case null: break;
                        case "build": target = BuildTarget.Build; break;
                        case "rebuild": target = BuildTarget.Rebuild; break;
                        case "clean": target = BuildTarget.Clean; break;
                        case "run": target = BuildTarget.Run; break;
                        case var other: invalid.Add(arg + " " + other); break;
                    }

                    break;
                case "--diagnostics":
                    switch (Value())
                    {
                        case null: break;
                        case "text": json = false; break;
                        case "json": json = true; break;
                        case var other: invalid.Add(arg + " " + other); break;
                    }

                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        invalid.Add(arg);
                    }
                    else if (project is null)
                    {
                        project = Full(arg);
                    }
                    else
                    {
                        errors.Add(Diagnostic.Create("E0008", Location.ForFile(project), project, Full(arg)));
                    }

                    break;
            }
        }

        if (help)
        {
            env.Out.WriteLine(HelpText.ReplaceLineEndings("\n").Replace("\n", env.Out.NewLine, StringComparison.Ordinal));
            return 0;
        }

        if (version)
        {
            env.Out.WriteLine($"pbuild {CompilerInfo.Version} (abi {AbiInfo.Version}, llvm {CompilerInfo.LlvmPin} pin)");
            return 0;
        }

        var location = Location.ForFile(project ?? cwd);
        errors.AddRange(invalid.Select(bad => bad.StartsWith('\0') ? Diagnostic.Create("E0006", location, bad[1..]) : Diagnostic.Create("E0005", location, bad)));
        if (project is null)
        {
            errors.Add(Diagnostic.Create("E0007", location));
        }
        else if (errors.Count == 0 && rtl is not null && !env.FileSystem.FileExists(Path.Combine(rtl, "System.pas")))
        {
            errors.Add(Diagnostic.Create("E0009", location, rtl));                       // as opc: only for an explicit --rtl
        }

        var writer = new EventWriter(env.Out, json);
        if (errors.Count > 0)
        {
            foreach (var d in errors)
            {
                writer.Diagnostic(d);
            }

            writer.Summary(errors.Count, 0, 0, output: null, success: false, clock.Elapsed);
            return 1;
        }

        var request = new BuildRequest
        {
            ProjectPath = project!,
            Configuration = configuration,
            Target = target,
            Verbose = verbose,
            ToolchainDirectory = toolchain ?? env.ToolchainDirectory,
            RtlDirectory = rtl ?? env.RtlDirectory,
            RedirectRunOutput = false,                                                  // CMP-833: the program shares pbuild's console
        };
        BuildResult result;
        try
        {
            result = new BuildEngine(env.FileSystem, env.ProcessRunner).RunAsync(request, writer, ct).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            env.Error.WriteLine(Diagnostic.Create("E0001", location, e.Message).ToText());
            if (verbose)
            {
                env.Error.WriteLine(e.ToString());
            }

            return 2;
        }

        writer.Summary(result.Errors, result.Warnings, result.Hints, result.OutputPath, result.Success, clock.Elapsed);
        var all = result.Projects.SelectMany(p => p.Diagnostics).ToImmutableArray();
        return all.Any(d => d.Severity == DiagnosticSeverity.Error && ExitCode2.Contains(d.Code)) ? 2 : result.Success ? 0 : 1;
    }

    /// <summary>Writes each event synchronously as it is reported (CMP-840).</summary>
    private sealed class EventWriter(TextWriter output, bool json) : IProgress<BuildEvent>
    {
        public void Report(BuildEvent value)
        {
            switch (value)
            {
                case BuildEvent.Started s when json:
                    Line($"{{\"event\":\"started\",\"project\":{Json(s.ProjectPath)},\"configuration\":{Json(s.Configuration)},\"platform\":{Json(s.Platform)},\"target\":{Json(s.Target.ToString().ToLowerInvariant())}}}");
                    break;
                case BuildEvent.Diagnostic d:
                    Diagnostic(d.Value);
                    break;
                case BuildEvent.CommandLine c:
                    Line(json ? "{\"event\":\"commandLine\",\"text\":" + Json(c.Text) + "}" : "> " + c.Text);
                    break;
                case BuildEvent.OutputLine o:
                    Line(json ? $"{{\"event\":\"output\",\"source\":{Json(SourceName(o.Source))},\"text\":{Json(o.Text)}}}" : o.Text);
                    break;
                case BuildEvent.Finished f when json:
                    Line(string.Create(CultureInfo.InvariantCulture,
                        $"{{\"event\":\"finished\",\"project\":{Json(f.ProjectPath)},\"success\":{Bool(f.Success)},\"upToDate\":{Bool(f.UpToDate)},\"errors\":{f.Errors},\"warnings\":{f.Warnings},\"hints\":{f.Hints},\"ms\":{(long)f.Elapsed.TotalMilliseconds}}}"));
                    break;
            }
        }

        public void Diagnostic(Diagnostic d) => Line(json ? d.ToJson() : d.ToText());

        /// <summary>Text: <c>Build succeeded|failed (e errors, w warnings, h hints, s.ss s)</c> (CMP-802); JSON: the summary line of compiler-cli.md §2.</summary>
        public void Summary(int errors, int warnings, int hints, string? output, bool success, TimeSpan elapsed) =>
            Line(json
                ? string.Create(CultureInfo.InvariantCulture,
                    $"{{\"summary\":{{\"errors\":{errors},\"warnings\":{warnings},\"hints\":{hints},\"output\":{(output is null ? "null" : Json(output))},\"ms\":{(long)elapsed.TotalMilliseconds}}}}}")
                : string.Create(CultureInfo.InvariantCulture,
                    $"Build {(success ? "succeeded" : "failed")} ({errors} errors, {warnings} warnings, {hints} hints, {elapsed.TotalSeconds:0.00} s)"));

        private void Line(string text)
        {
            lock (output)
            {
                output.WriteLine(text);
            }
        }

        private static string SourceName(OutputSource source) => source switch
        {
            OutputSource.Tool => "tool",
            OutputSource.ProgramStdOut => "stdout",
            OutputSource.ProgramStdErr => "stderr",
            _ => "info",
        };

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Json(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in value)
            {
                _ = c switch
                {
                    '"' => sb.Append("\\\""),
                    '\\' => sb.Append("\\\\"),
                    '\n' => sb.Append("\\n"),
                    '\r' => sb.Append("\\r"),
                    '\t' => sb.Append("\\t"),
                    < ' ' => sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"),
                    _ => sb.Append(c),
                };
            }

            return sb.Append('"').ToString();
        }
    }
}
