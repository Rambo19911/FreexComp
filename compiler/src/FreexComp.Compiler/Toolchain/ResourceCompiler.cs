using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>Inputs of one script compilation (CMP-1301). Paths are absolute.</summary>
/// <param name="OutputPath">The compiled resource file.</param>
internal sealed record ResourceCompileRequest(string ScriptPath, string OutputPath, string ObjectDirectory)
{
    /// <summary>Searched in order after the script's directory.</summary>
    public ImmutableArray<string> IncludeDirectories { get; init; } = [];

    /// <summary><c>NAME</c> or <c>NAME=value</c>, in order.</summary>
    public ImmutableArray<string> Defines { get; init; } = [];
}

/// <summary>Line of the tool input → where it came from (CMP-1304); <see cref="Line"/> is 1-based.</summary>
internal readonly record struct ResourceLineOrigin(string File, int Line);

/// <param name="Dependencies">CMP-1307: the script, its included files, its data file candidates.</param>
/// <param name="LineMap">One element per line of <c>&lt;base&gt;.rc.i</c>.</param>
internal sealed record ResourceCompileResult(
    bool Success,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<string> Dependencies,
    ImmutableArray<ResourceLineOrigin> LineMap);

/// <summary>
/// Compiles one resource script (resource-compiler.md §3, CMP-1300…1309): the preprocessor of the toolchain with the product's
/// own header files, then the resource tool on the preprocessed text without its line markers.
/// </summary>
internal sealed partial class ResourceCompiler(ToolRunner runner, ToolchainPaths toolchain, IFileSystem fs)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>CMP-1303: the argument list of the preprocessor run.</summary>
    public static ImmutableArray<string> PreprocessArguments(ResourceCompileRequest request)
    {
        var args = ImmutableArray.CreateBuilder<string>();
        args.AddRange("-E", "-x", "c", "-w", "-nostdinc", "-DRC_INVOKED");
        args.AddRange(request.Defines.Select(d => "-D" + d));
        args.AddRange("-I", Path.GetDirectoryName(request.ScriptPath)!);
        foreach (var directory in request.IncludeDirectories)
        {
            args.AddRange("-I", directory);
        }

        args.AddRange("-isystem", Path.Combine(request.ObjectDirectory, ResourceHeaders.DirectoryName));
        args.AddRange("-o", PreprocessedPath(request), request.ScriptPath);
        return args.ToImmutable();
    }

    /// <summary>CMP-1305: the argument list of the resource tool run.</summary>
    public static ImmutableArray<string> CompileArguments(ResourceCompileRequest request)
    {
        var args = ImmutableArray.CreateBuilder<string>();
        args.AddRange("/no-preprocess", "/C", "65001", "/I", Path.GetDirectoryName(request.ScriptPath)!);
        foreach (var directory in request.IncludeDirectories)
        {
            args.AddRange("/I", directory);
        }

        args.AddRange("/fo", request.OutputPath + ".tmp", ToolInputPath(request));
        return args.ToImmutable();
    }

    /// <summary><c>&lt;objdir&gt;\&lt;base&gt;.rc.pp</c>: what the preprocessor writes.</summary>
    public static string PreprocessedPath(ResourceCompileRequest request) =>
        Path.Combine(request.ObjectDirectory, Path.GetFileNameWithoutExtension(request.ScriptPath) + ".rc.pp");

    /// <summary><c>&lt;objdir&gt;\&lt;base&gt;.rc.i</c>: the preprocessed text without line markers, the input of the resource tool.</summary>
    public static string ToolInputPath(ResourceCompileRequest request) =>
        Path.Combine(request.ObjectDirectory, Path.GetFileNameWithoutExtension(request.ScriptPath) + ".rc.i");

    public async Task<ResourceCompileResult> CompileAsync(ResourceCompileRequest request, CancellationToken ct)
    {
        var script = Location.ForFile(request.ScriptPath);
        var temporary = request.OutputPath + ".tmp";
        try
        {
            var result = await RunAsync(request, script, temporary, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                fs.Delete(temporary);
                fs.Delete(request.OutputPath);                              // CMP-1305: no stale output after a failure
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            fs.Delete(temporary);                                           // CMP-1309
            throw;
        }
    }

    private async Task<ResourceCompileResult> RunAsync(ResourceCompileRequest request, Location script, string temporary, CancellationToken ct)
    {
        // CMP-1301: the script is UTF-8 text
        byte[] bytes;
        try
        {
            if (!fs.FileExists(request.ScriptPath))
            {
                return Failed(Diagnostic.Create("E3110", script, request.ScriptPath));
            }

            bytes = fs.ReadAllBytes(request.ScriptPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(Diagnostic.Create("E3110", script, request.ScriptPath));
        }

        if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..] or [0x00, 0x00, 0xFE, 0xFF, ..])
        {
            return Failed(Diagnostic.Create("E3113", script, request.ScriptPath));
        }

        // CMP-1300: the resource tool is looked for when a script is compiled
        if (!fs.FileExists(toolchain.ResourceTool))
        {
            return Failed(Diagnostic.Create("E4001", script, ToolchainLocator.ResourceToolName, toolchain.Directory));
        }

        ResourceHeaders.Write(fs, request.ObjectDirectory);                 // CMP-1302
        fs.CreateDirectory(request.ObjectDirectory);

        // CMP-1303: step 1
        var preprocessed = PreprocessedPath(request);
        fs.Delete(preprocessed);
        var step1 = await runner.RunAsync(toolchain.Clang, PreprocessArguments(request), request.ObjectDirectory, ct).ConfigureAwait(false);
        if (step1.Failure is { Code: "E4003" } timeout)
        {
            return Failed(timeout);
        }

        if (step1.ExitCode != 0 || !fs.FileExists(preprocessed))
        {
            return Failed(PreprocessorErrors(step1.Output, step1.ExitCode, script));
        }

        // CMP-1304: the tool input has no marker line
        var (text, map, included) = StripMarkers(fs.ReadAllText(preprocessed), request.ScriptPath);
        var input = ToolInputPath(request);
        fs.WriteAllBytes(input, Utf8.GetBytes(text));

        // CMP-1305: step 2. The tool looks for data files in its working directory first, so it runs in the script's directory
        fs.Delete(temporary);
        fs.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        var step2 = await runner.RunAsync(toolchain.ResourceTool, CompileArguments(request), Path.GetDirectoryName(request.ScriptPath)!, ct).ConfigureAwait(false);
        if (step2.Failure is { Code: "E4003" } toolTimeout)
        {
            return Failed(toolTimeout);
        }

        if (step2.ExitCode != 0 || !fs.FileExists(temporary))
        {
            var errors = ToolErrors(step2.Output, script);
            return Failed(errors.IsEmpty
                ? [step2.Failure ?? ToolRunner.WithOutput(Diagnostic.Create("E4004", script, ToolchainLocator.ResourceToolName, step2.ExitCode), step2.Output)]
                : errors);
        }

        fs.WriteAllBytes(request.OutputPath, fs.ReadAllBytes(temporary));  // CMP-141: replaced atomically
        fs.Delete(temporary);
        return new ResourceCompileResult(true, [], Dependencies(request, text, included), map);
    }

    private static ResourceCompileResult Failed(params ImmutableArray<Diagnostic> diagnostics) => new(false, diagnostics, [], []);

    /// <summary>
    /// CMP-1303: each <c>&lt;path&gt;:&lt;line&gt;:&lt;col&gt;: error: &lt;text&gt;</c> or <c>… fatal error: &lt;text&gt;</c> line is one E3111 at that position;
    /// without such a line one E3111 at the script with the first output line.
    /// </summary>
    internal static ImmutableArray<Diagnostic> PreprocessorErrors(IReadOnlyList<string> output, int exitCode, Location script)
    {
        var errors = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var line in output)
        {
            var match = PreprocessorError().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var position = new LinePosition(int.Parse(match.Groups["line"].ValueSpan, CultureInfo.InvariantCulture) - 1, int.Parse(match.Groups["col"].ValueSpan, CultureInfo.InvariantCulture) - 1);
            var file = match.Groups["file"].Value;
            var path = Path.IsPathFullyQualified(file) ? PathUtil.Normalize(file) : file;
            errors.Add(Diagnostic.Create("E3111", new Location(path, default, new LinePositionSpan(position, position)), match.Groups["text"].Value));
        }

        if (errors.Count == 0)
        {
            var first = output.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? FormattableString.Invariant($"the preprocessor ended with exit code {exitCode}");
            errors.Add(Diagnostic.Create("E3111", script, first));
        }

        return errors.ToImmutable();
    }

    /// <summary>
    /// CMP-1306: each line that starts with the tool's name is one E3112 with the text after the name; a following
    /// <c>error : …</c> line belongs to it. The pinned tool names no line of its input, so the position is the script, (0,0).
    /// </summary>
    internal static ImmutableArray<Diagnostic> ToolErrors(IReadOnlyList<string> output, Location script)
    {
        const string Detail = "error : ";
        var prefix = Path.GetFileNameWithoutExtension(ToolchainLocator.ResourceToolName) + ": ";
        var errors = ImmutableArray.CreateBuilder<Diagnostic>();
        for (var i = 0; i < output.Count; i++)
        {
            if (!output[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var message = output[i][prefix.Length..].Trim();
            if (i + 1 < output.Count && output[i + 1].StartsWith(Detail, StringComparison.Ordinal))
            {
                message += " " + output[++i][Detail.Length..].Trim();
            }

            errors.Add(Diagnostic.Create("E3112", script, message));
        }

        return errors.ToImmutable();
    }

    /// <summary>
    /// CMP-1304: removes the line markers (<c># &lt;n&gt; "&lt;file&gt;" …</c>) and records where every remaining line came from.
    /// </summary>
    /// <returns>The text for the resource tool (LF line ends), its line map and the included files in order of first use.</returns>
    internal static (string Text, ImmutableArray<ResourceLineOrigin> Map, ImmutableArray<string> Included) StripMarkers(string preprocessed, string scriptPath)
    {
        var text = new StringBuilder();
        var map = ImmutableArray.CreateBuilder<ResourceLineOrigin>();
        var included = ImmutableArray.CreateBuilder<string>();
        var file = scriptPath;
        var line = 1;
        var lines = preprocessed.Split('\n');
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
        {
            var current = lines[i].TrimEnd('\r');
            var marker = LineMarker().Match(current);
            if (marker.Success)
            {
                line = int.Parse(marker.Groups["line"].ValueSpan, CultureInfo.InvariantCulture);
                var name = marker.Groups["file"].Value.Replace(@"\\", @"\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal);
                if (name.StartsWith('<'))
                {
                    file = name;                                            // a built-in pseudo file of the preprocessor
                    continue;
                }

                file = PathUtil.Normalize(Path.IsPathFullyQualified(name) ? name : Path.Combine(Path.GetDirectoryName(scriptPath)!, name));
                if (!PathUtil.AreEqual(file, scriptPath) && !included.Contains(file, PathUtil.Comparer))
                {
                    included.Add(file);
                }

                continue;
            }

            text.Append(current).Append('\n');
            map.Add(new ResourceLineOrigin(file, line++));
        }

        return (text.ToString(), map.ToImmutable(), included.ToImmutable());
    }

    /// <summary>
    /// CMP-1307: the script, the included files and every quoted string or blank-delimited word of the tool input that names an
    /// existing file — absolute, or relative to the script's directory or an include directory (the order the tool searches).
    /// </summary>
    private ImmutableArray<string> Dependencies(ResourceCompileRequest request, string toolInput, ImmutableArray<string> included)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(PathUtil.Comparer);
        void Add(string path)
        {
            if (seen.Add(path))
            {
                result.Add(path);
            }
        }

        Add(PathUtil.Normalize(request.ScriptPath));
        foreach (var file in included)
        {
            Add(file);
        }

        ImmutableArray<string> directories = [Path.GetDirectoryName(request.ScriptPath)!, .. request.IncludeDirectories];
        var tried = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Candidate().Matches(toolInput))
        {
            var candidate = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Value.TrimEnd(',');
            if (candidate.Length == 0 || !tried.Add(candidate) || candidate.AsSpan().IndexOfAny("<>|*?\"") >= 0 || candidate.Any(char.IsControl))
            {
                continue;
            }

            foreach (var name in candidate.Contains(@"\\", StringComparison.Ordinal) ? [candidate, candidate.Replace(@"\\", @"\", StringComparison.Ordinal)] : new[] { candidate })
            {
                if (Resolve(name, directories) is { } path)
                {
                    Add(path);
                    break;
                }
            }
        }

        return result.ToImmutable();
    }

    private string? Resolve(string name, ImmutableArray<string> directories)
    {
        try
        {
            if (Path.IsPathFullyQualified(name))
            {
                var absolute = PathUtil.Normalize(name);
                return fs.FileExists(absolute) ? absolute : null;
            }

            if (name.Contains(':', StringComparison.Ordinal))
            {
                return null;
            }

            foreach (var directory in directories)
            {
                var path = PathUtil.Normalize(Path.Combine(directory, name));
                if (fs.FileExists(path))
                {
                    return path;
                }
            }
        }
        catch (ArgumentException)
        {
            // not a path
        }

        return null;
    }

    [GeneratedRegex(@"^(?<file>.+?):(?<line>\d+):(?<col>\d+): (?:fatal )?error: (?<text>.*)$")]
    private static partial Regex PreprocessorError();

    [GeneratedRegex(@"^# (?<line>\d+) ""(?<file>(?:[^""\\]|\\.)*)""(?: \d+)*\s*$")]
    private static partial Regex LineMarker();

    [GeneratedRegex(@"""(?<quoted>[^""]*)""|[^\s""]+")]
    private static partial Regex Candidate();
}
