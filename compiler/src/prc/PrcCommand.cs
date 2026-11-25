using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Toolchain;

namespace FreexComp.Prc;

/// <summary>Where <c>prc</c> runs: the current directory (relative paths, defaults), its output streams and file system.</summary>
public sealed record PrcEnvironment(string CurrentDirectory, TextWriter Out, TextWriter Error, IFileSystem FileSystem)
{
    /// <summary>Starts the external tools (a test seam; real processes otherwise).</summary>
    public IProcessRunner ProcessRunner { get; init; } = Compiler.Toolchain.ProcessRunner.Instance;
}

/// <summary>The parsed command line (CMP-1310). Paths are absolute.</summary>
public sealed record PrcArguments(
    string? Script,
    string? Output,
    string? ObjectDirectory,
    string ToolchainDirectory,
    ImmutableArray<string> IncludeDirectories,
    ImmutableArray<string> Defines,
    bool Json,
    bool Verbose,
    bool Help,
    bool Version,
    ImmutableArray<Diagnostic> Errors);

/// <summary>
/// The <c>prc</c> driver (resource-compiler.md §4, CMP-1310…1312): compiles one resource script to a compiled resource file.
/// Diagnostics on stdout in text or JSON form (JSON ends with the summary line). Exit codes: 0 output written, 1 the script
/// has errors or the command line is wrong, 2 toolchain missing, a tool timed out, or an internal error.
/// </summary>
public static class PrcCommand
{
    public const string HelpText = """
        prc <script.rc> [options]
          -o <file>             output file (default: <script>.res beside the script)
          -I <dir>              include directory (repeatable; searched in order after the script's directory)
          -D <NAME[=value]>     define a preprocessor symbol (repeatable)
          --obj-dir <dir>       intermediates (default: ./obj/Resources)
          --toolchain <dir>     LLVM bin dir (default as opc)
          --diagnostics text|json   (default text)
          --verbose             echo external commands
          --version | --help
        """;

    /// <summary>Codes that end <c>prc</c> with exit code 2 (CMP-1311).</summary>
    private static readonly HashSet<string> ExitCode2 = new(StringComparer.Ordinal) { "E0001", "E4001", "E4003" };

    public static int Run(IReadOnlyList<string> args, PrcEnvironment env, CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var parsed = Parse(args, env.CurrentDirectory);
        if (parsed.Help)
        {
            env.Out.WriteLine(HelpText.ReplaceLineEndings("\n").Replace("\n", env.Out.NewLine, StringComparison.Ordinal));
            return 0;
        }

        if (parsed.Version)
        {
            env.Out.WriteLine($"prc {CompilerInfo.Version} (abi {AbiInfo.Version}, llvm {CompilerInfo.LlvmPin} pin)");
            return 0;
        }

        if (!parsed.Errors.IsEmpty)
        {
            return Finish(env, parsed, parsed.Errors, output: null, clock);
        }

        var script = parsed.Script!;
        var location = Location.ForFile(script);
        try
        {
            var (paths, missing) = ToolchainLocator.Locate(
                env.FileSystem, parsed.ToolchainDirectory, ToolchainEnvironment.Current, location, ToolchainLocator.PreprocessorOnly);
            if (paths is null)
            {
                return Finish(env, parsed, [missing!], output: null, clock);
            }

            var options = new ToolRunOptions(location)
            {
                CommandLineSink = parsed.Verbose
                    ? line => WriteLine(env.Out, parsed.Json ? "{\"event\":\"commandLine\",\"text\":" + JsonString(line) + "}" : "> " + line)
                    : null,
            };
            var request = new ResourceCompileRequest(script, parsed.Output!, parsed.ObjectDirectory!)
            {
                IncludeDirectories = parsed.IncludeDirectories,
                Defines = parsed.Defines,
            };
            var result = new ResourceCompiler(new ToolRunner(env.ProcessRunner, options), paths, env.FileSystem)
                .CompileAsync(request, ct).GetAwaiter().GetResult();
            return Finish(env, parsed, result.Diagnostics, result.Success ? request.OutputPath : null, clock);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // internal errors go to stderr, with the stack trace under --verbose (as opc, CMP-851)
            env.Error.WriteLine(Diagnostic.Create("E0001", location, e.Message).ToText());
            if (parsed.Verbose)
            {
                env.Error.WriteLine(e.ToString());
            }

            return 2;
        }
    }

    /// <summary>CMP-1310: relative paths and the defaults of <c>-o</c>/<c>--obj-dir</c> are based on <paramref name="currentDirectory"/>.</summary>
    public static PrcArguments Parse(IReadOnlyList<string> args, string currentDirectory)
    {
        var errors = ImmutableArray.CreateBuilder<Diagnostic>();
        var cwd = PathUtil.Normalize(currentDirectory);
        string Full(string path) => PathUtil.Normalize(Path.IsPathFullyQualified(path) ? path : Path.Combine(cwd, path));

        string? script = null, output = null, objDir = null, toolchain = null;
        var includes = ImmutableArray.CreateBuilder<string>();
        var defines = ImmutableArray.CreateBuilder<string>();
        bool json = false, verbose = false, help = false, version = false;
        var invalid = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? Value()
            {
                if (i + 1 < args.Count)
                {
                    return args[++i];
                }

                invalid.Add("\0" + arg);                                    // missing argument (E0006)
                return null;
            }

            switch (arg)
            {
                case "--help": help = true; break;
                case "--version": version = true; break;
                case "-o": if (Value() is { } o) { output = Full(o); } break;
                case "-I": if (Value() is { } include) { includes.Add(Full(include)); } break;
                case "-D": if (Value() is { } symbol) { defines.Add(symbol); } break;
                case "--obj-dir": if (Value() is { } d) { objDir = Full(d); } break;
                case "--toolchain": if (Value() is { } bin) { toolchain = Full(bin); } break;
                case "--diagnostics":
                    if (Value() is { } form)
                    {
                        if (form is "text" or "json")
                        {
                            json = form == "json";
                        }
                        else
                        {
                            invalid.Add(arg + " " + form);
                        }
                    }

                    break;
                case "--verbose": verbose = true; break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        invalid.Add(arg);
                    }
                    else if (script is null)
                    {
                        script = Full(arg);
                    }
                    else
                    {
                        errors.Add(Diagnostic.Create("E0008", Location.ForFile(script), script, Full(arg)));
                    }

                    break;
            }
        }

        var location = Location.ForFile(script ?? cwd);
        foreach (var bad in invalid)
        {
            errors.Add(bad.StartsWith('\0') ? Diagnostic.Create("E0006", location, bad[1..]) : Diagnostic.Create("E0005", location, bad));
        }

        if (help || version)
        {
            return new PrcArguments(null, null, null, "", [], [], json, verbose, help, version, []);
        }

        if (script is null)
        {
            errors.Add(Diagnostic.Create("E0007", location));
        }

        if (errors.Count > 0)
        {
            return new PrcArguments(null, null, null, "", [], [], json, verbose, false, false, errors.ToImmutable());
        }

        return new PrcArguments(
            script,
            output ?? Path.ChangeExtension(script!, ".res"),
            objDir ?? Path.Combine(cwd, "obj", "Resources"),
            toolchain ?? "",
            includes.ToImmutable(),
            defines.ToImmutable(),
            json,
            verbose,
            false,
            false,
            []);
    }

    /// <summary>Writes the diagnostics (and, in JSON form, the summary line); returns the exit code.</summary>
    private static int Finish(PrcEnvironment env, PrcArguments parsed, ImmutableArray<Diagnostic> diagnostics, string? output, System.Diagnostics.Stopwatch clock)
    {
        foreach (var d in diagnostics)
        {
            WriteLine(env.Out, parsed.Json ? d.ToJson() : d.ToText());
        }

        var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        if (parsed.Json)
        {
            var warnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            var hints = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Hint);
            WriteLine(env.Out, string.Create(CultureInfo.InvariantCulture,
                $"{{\"summary\":{{\"errors\":{errors},\"warnings\":{warnings},\"hints\":{hints},\"output\":{(output is null ? "null" : JsonString(output))},\"ms\":{clock.ElapsedMilliseconds}}}}}"));
        }

        return diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && ExitCode2.Contains(d.Code)) ? 2 : errors > 0 ? 1 : 0;
    }

    private static void WriteLine(TextWriter writer, string text)
    {
        lock (writer)
        {
            writer.WriteLine(text);
        }
    }

    private static string JsonString(string value)
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
