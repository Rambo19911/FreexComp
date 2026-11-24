using System.Collections.Immutable;
using System.Globalization;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;

namespace FreexComp.Opc;

/// <summary>Where <c>opc</c> runs: the current directory (relative paths, defaults), its output streams and file system.</summary>
public sealed record OpcEnvironment(string CurrentDirectory, TextWriter Out, TextWriter Error, IFileSystem FileSystem);

/// <summary>The parsed command line (CMP-850). <see cref="Compilation"/>/<see cref="Emit"/> are null for <c>--help</c>/<c>--version</c> or after errors.</summary>
public sealed record OpcArguments(
    CompilationOptions? Compilation,
    EmitOptions? Emit,
    bool Json,
    bool Verbose,
    bool Help,
    bool Version,
    ImmutableArray<Diagnostic> Errors);

/// <summary>
/// The <c>opc</c> driver (compiler-cli.md §1–2, build-engine.md CMP-850…852): arguments → <see cref="CompilationOptions"/> /
/// <see cref="EmitOptions"/> → <see cref="Compilation.Emit"/>; diagnostics on stdout in text or JSON form (JSON ends with the
/// summary line); internal errors (<c>E0001</c>) on stderr. Exit codes: 0 ok (warnings allowed), 1 errors, 2 internal error
/// or toolchain missing.
/// </summary>
public static class OpcCommand
{
    /// <summary>The option table of compiler-cli.md §1 (CMP-852), without the references to internal documents.</summary>
    public const string HelpText = """
        opc <main.pas> [options]
          -o <file>             output exe (default: <main>.exe in the current directory)
          -U <dir>              unit search path (repeatable; searched in order, after the main file's directory)
          --obj-dir <dir>       intermediates (.ll, .obj, .pcu); default ./obj/Debug for -O0, ./obj/Release otherwise
          -D <SYMBOL>           define conditional symbol (repeatable)
          -O0 | -O1 | -O2       optimisation (default -O0)
          -g                    emit debug info: LLVM debug metadata (<output>.pdb or DWARF in the exe) and
                                the Pascal debug-info sidecar <output>.pdi
          --app-type console|gui    PE subsystem (default console)
          --manifest default|none|<file>   application manifest embedded as RT_MANIFEST 1:
                                `default` = Common Controls 6 + per-monitor-v2 DPI awareness; default value is `default`
                                for --app-type gui and `none` for console
          --rtti broad|published    default RTTI scope (default broad)
          --unit-scope <name>   unit scope name tried as prefix for unresolved unit names (repeatable, searched in
                                order). Scoped unit names such as `System.SysUtils` resolve through
                                `<rtl>/unit-aliases.json` without this option
          --emit ll|obj|exe     stop after stage (default exe)
          --check               parse + semantic analysis only, no output files
          --diagnostics text|json   (default text)
          --rtl <dir>           RTL root (default: $FREEXCOMP_HOME/rtl/src, else relative to opc.exe)
          --toolchain <dir>     LLVM bin dir (default: third_party/llvm/bin, else PATH)
          --jobs <N> | -j <N>   maximum number of parallel compile jobs (0 = automatic: one per processor; default 0)
          --verbose             echo external commands
          --version | --help
        """;

    /// <summary>Codes that end <c>opc</c> with exit code 2 (compiler-cli.md §1): internal error, toolchain missing.</summary>
    private static readonly HashSet<string> ExitCode2 = new(StringComparer.Ordinal) { "E0001", "E4001" };

    public static int Run(IReadOnlyList<string> args, OpcEnvironment env, CancellationToken ct)
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
            env.Out.WriteLine($"opc {CompilerInfo.Version} (abi {AbiInfo.Version}, llvm {CompilerInfo.LlvmPin} pin)");
            return 0;
        }

        if (!parsed.Errors.IsEmpty)
        {
            return Finish(env, parsed, parsed.Errors, output: null, clock);
        }

        var options = parsed.Compilation!;
        if (options.RtlDirectory.Length > 0 && !env.FileSystem.FileExists(Path.Combine(options.RtlDirectory, "System.pas")))
        {
            return Finish(env, parsed, [Diagnostic.Create("E0009", Location.ForFile(options.MainFile), options.RtlDirectory)], output: null, clock);
        }

        var emit = parsed.Emit!;
        if (parsed.Verbose)
        {
            emit = emit with
            {
                CommandLineSink = line => WriteLine(env.Out, parsed.Json ? "{\"event\":\"commandLine\",\"text\":" + JsonString(line) + "}" : "> " + line),
                // CMP-1139: the command line of a back-end job names its unit or DLL in JSON form
                JobCommandLineSink = (line, unit, dll) => WriteLine(env.Out, parsed.Json
                    ? "{\"event\":\"commandLine\",\"text\":" + JsonString(line)
                        + (unit is not null ? ",\"unit\":" + JsonString(unit) : dll is not null ? ",\"dll\":" + JsonString(dll) : "") + "}"
                    : "> " + line),
            };
        }

        EmitResult result;
        try
        {
            result = Compilation.Create(options, [], env.FileSystem).Emit(emit, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // CMP-851: internal errors go to stderr, with the stack trace under --verbose
            var internalError = Diagnostic.Create("E0001", Location.ForFile(options.MainFile), e.Message);
            env.Error.WriteLine(internalError.ToText());
            if (parsed.Verbose)
            {
                env.Error.WriteLine(e.ToString());
            }

            return 2;
        }

        return Finish(env, parsed, result.Diagnostics, result.OutputPath, clock);
    }

    /// <summary>Writes the diagnostics (and, in JSON form, the summary line); returns the exit code.</summary>
    private static int Finish(OpcEnvironment env, OpcArguments parsed, ImmutableArray<Diagnostic> diagnostics, string? output, System.Diagnostics.Stopwatch clock)
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

    /// <summary>CMP-850: compiler-cli.md §1; relative paths and the defaults of <c>-o</c>/<c>--obj-dir</c> are based on <paramref name="currentDirectory"/>.</summary>
    public static OpcArguments Parse(IReadOnlyList<string> args, string currentDirectory)
    {
        var errors = ImmutableArray.CreateBuilder<Diagnostic>();
        var cwd = PathUtil.Normalize(currentDirectory);
        string Full(string path) => PathUtil.Normalize(Path.IsPathFullyQualified(path) ? path : Path.Combine(cwd, path));

        string? main = null, output = null, objDir = null, rtl = null, toolchain = null, manifest = null;
        var searchPaths = ImmutableArray.CreateBuilder<string>();
        var defines = ImmutableArray.CreateBuilder<string>();
        var scopes = ImmutableArray.CreateBuilder<string>();
        var optimization = OptimizationLevel.O0;
        var appType = AppType.Console;
        var stage = EmitStage.Exe;
        var rtti = "broad";
        var jobs = 0;
        bool debug = false, json = false, verbose = false, help = false, version = false;
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

            string? Choice(params string[] allowed)
            {
                var value = Value();
                if (value is null)
                {
                    return null;
                }

                if (allowed.Contains(value, StringComparer.Ordinal))
                {
                    return value;
                }

                invalid.Add(arg + " " + value);
                return null;
            }

            switch (arg)
            {
                case "--help": help = true; break;
                case "--version": version = true; break;
                case "-o": if (Value() is { } o) { output = Full(o); } break;
                case "-U": if (Value() is { } u) { searchPaths.Add(Full(u)); } break;
                case "--obj-dir": if (Value() is { } d) { objDir = Full(d); } break;
                case "-D": if (Value() is { } symbol) { defines.Add(symbol); } break;
                case "-O0": optimization = OptimizationLevel.O0; break;
                case "-O1": optimization = OptimizationLevel.O1; break;
                case "-O2": optimization = OptimizationLevel.O2; break;
                case "-g": debug = true; break;
                case "--app-type": if (Choice("console", "gui") is { } a) { appType = a == "gui" ? AppType.Gui : AppType.Console; } break;
                case "--manifest": if (Value() is { } m) { manifest = m is "default" or "none" ? m : Full(m); } break;
                case "--rtti": if (Choice("broad", "published") is { } r) { rtti = r; } break;
                case "--unit-scope": if (Value() is { } scope) { scopes.Add(scope); } break;
                case "--emit":
                    if (Choice("ll", "obj", "exe") is { } e)
                    {
                        stage = e switch { "ll" => EmitStage.Ll, "obj" => EmitStage.Obj, _ => EmitStage.Exe };
                    }

                    break;
                case "--check": stage = EmitStage.Check; break;
                case "--diagnostics": if (Choice("text", "json") is { } f) { json = f == "json"; } break;
                case "--rtl": if (Value() is { } rtlDir) { rtl = Full(rtlDir); } break;
                case "--toolchain": if (Value() is { } bin) { toolchain = Full(bin); } break;
                case "--jobs" or "-j":                                       // CMP-1130: 0…256, 0 = automatic
                    if (Value() is { } n)
                    {
                        if (int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit <= 256)
                        {
                            jobs = limit;
                        }
                        else
                        {
                            invalid.Add(arg + " " + n);
                        }
                    }

                    break;
                case "--verbose": verbose = true; break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        invalid.Add(arg);
                    }
                    else if (main is null)
                    {
                        main = Full(arg);
                    }
                    else
                    {
                        errors.Add(Diagnostic.Create("E0008", Location.ForFile(main), main, Full(arg)));
                    }

                    break;
            }
        }

        var location = Location.ForFile(main ?? cwd);
        foreach (var bad in invalid)
        {
            errors.Add(bad.StartsWith('\0') ? Diagnostic.Create("E0006", location, bad[1..]) : Diagnostic.Create("E0005", location, bad));
        }

        if (help || version)
        {
            return new OpcArguments(null, null, json, verbose, help, version, []);
        }

        if (main is null)
        {
            errors.Add(Diagnostic.Create("E0007", location));
        }

        if (errors.Count > 0)
        {
            return new OpcArguments(null, null, json, verbose, false, false, errors.ToImmutable());
        }

        var compilation = new CompilationOptions
        {
            MainFile = main!,
            SearchPaths = searchPaths.ToImmutable(),
            Defines = defines.ToImmutable(),
            ObjectDirectory = objDir ?? Path.Combine(cwd, "obj", optimization == OptimizationLevel.O0 ? "Debug" : "Release"),
            RtlDirectory = rtl ?? "",
            ToolchainDirectory = toolchain ?? "",
            Optimization = optimization,
            DebugInfo = debug,
            AppType = appType,
            Rtti = rtti,
            Manifest = manifest ?? (appType == AppType.Gui ? "default" : "none"),
            UnitScopeNames = scopes.ToImmutable(),
            MaxParallelism = jobs,
        };
        var emit = new EmitOptions
        {
            Stage = stage,
            OutputPath = output ?? Path.Combine(cwd, Path.GetFileNameWithoutExtension(main!) + ".exe"),
            Verbose = verbose,
        };
        return new OpcArguments(compilation, emit, json, verbose, false, false, []);
    }

    private static string JsonString(string value)
    {
        var sb = new System.Text.StringBuilder("\"");
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
