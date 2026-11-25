using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Toolchain;

/// <param name="DiagnosticLocation">Where tool diagnostics are reported (the main source, no position).</param>
internal sealed record ToolRunOptions(Location DiagnosticLocation)
{
    /// <summary>Receives each command line before it runs (only set with <c>--verbose</c>).</summary>
    public Action<string>? CommandLineSink { get; init; }

    /// <summary>Receives raw tool stdout/stderr lines.</summary>
    public Action<string>? ToolOutputSink { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>The clock of <see cref="Timeout"/> (a test seam; the system clock otherwise).</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <param name="Output">stdout and stderr lines in arrival order.</param>
/// <param name="Failure">E4003 (timeout) or E4004 (non-zero exit); null on success.</param>
internal sealed record ToolResult(int ExitCode, ImmutableArray<string> Output, Diagnostic? Failure);

/// <summary>Runs one external tool (CMP-602, 603): echo, capture, timeout, failure diagnostics.</summary>
internal sealed class ToolRunner(IProcessRunner processRunner, ToolRunOptions options)
{
    public ToolRunOptions Options => options;

    /// <param name="removedEnvironment">Environment variables the tool must not see (C-335); null → the environment as it is.</param>
    public async Task<ToolResult> RunAsync(
        string toolPath, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct, IReadOnlyList<string>? removedEnvironment = null)
    {
        options.CommandLineSink?.Invoke(RenderCommandLine(toolPath, arguments));
        var output = new List<string>();
        void OnLine(string line)
        {
            lock (output)
            {
                output.Add(line);
                options.ToolOutputSink?.Invoke(line);
            }
        }

        var toolName = Path.GetFileName(toolPath);
        // CMP-1137: the timeout counts from here, the start of the process (a job waiting for a free slot is not here yet)
        using var limit = new CancellationTokenSource(options.Timeout, options.TimeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        int exitCode;
        try
        {
            exitCode = await (removedEnvironment is null
                ? processRunner.RunAsync(toolPath, arguments, workingDirectory, OnLine, OnLine, timeout.Token)
                : processRunner.RunAsync(toolPath, arguments, workingDirectory, removedEnvironment, OnLine, OnLine, timeout.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var seconds = (int)Math.Ceiling(options.Timeout.TotalSeconds);
            return new ToolResult(-1, Snapshot(output), WithOutput(Diagnostic.Create("E4003", options.DiagnosticLocation, toolName, seconds), output));
        }
        catch (Win32Exception e)
        {
            OnLine(e.Message);
            exitCode = -1;
        }

        var failure = exitCode == 0
            ? null
            : WithOutput(Diagnostic.Create("E4004", options.DiagnosticLocation, toolName, exitCode), output);
        return new ToolResult(exitCode, Snapshot(output), failure);
    }

    /// <summary>The command line as a Windows shell would need it (for display only; never parsed back).</summary>
    public static string RenderCommandLine(string toolPath, IReadOnlyList<string> arguments)
    {
        var sb = new StringBuilder(QuoteWindows(toolPath));
        foreach (var argument in arguments)
        {
            sb.Append(' ').Append(QuoteWindows(argument));
        }

        return sb.ToString();
    }

    /// <summary>Quotes one argument so <c>CommandLineToArgvW</c> yields it back unchanged.</summary>
    public static string QuoteWindows(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var needsQuotes = argument.Length == 0 || argument.IndexOfAny([' ', '\t']) >= 0;
        var sb = new StringBuilder();
        if (needsQuotes)
        {
            sb.Append('"');
        }

        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                sb.Append('\\', backslashes);
            }

            backslashes = 0;
            sb.Append(c);
        }

        sb.Append('\\', needsQuotes ? backslashes * 2 : backslashes);
        if (needsQuotes)
        {
            sb.Append('"');
        }

        return sb.ToString();
    }

    internal static Diagnostic WithOutput(Diagnostic diagnostic, IEnumerable<string> lines) =>
        diagnostic with { Related = [.. Snapshot(lines).Select(l => new RelatedLocation(diagnostic.Location, l))] };

    private static ImmutableArray<string> Snapshot(IEnumerable<string> lines)
    {
        lock (lines)
        {
            return [.. lines];
        }
    }
}

/// <summary>CMP-601: <c>clang --version</c> once per clang path and process; major compared with the pin.</summary>
internal sealed class ToolchainVersionCheck(string expectedVersion)
{
    /// <summary><c>scripts/toolchain.json</c> <c>llvm.version</c> (test <c>Runner_VersionCheck_PinEqualsScriptsToolchainJson</c>).</summary>
    public const string PinnedVersion = "23.1.5";

    private readonly Dictionary<string, Task<ToolResult>> _runs = new(PathUtil.Comparer);

    public static ToolchainVersionCheck Shared { get; } = new(PinnedVersion);

    /// <summary>Null when the major matches; W4002 otherwise; the E4004 of a failing <c>clang --version</c> (not cached).</summary>
    public async Task<Diagnostic?> CheckAsync(IProcessRunner processRunner, string clangPath, Location location, CancellationToken ct)
    {
        Task<ToolResult>? run;
        lock (_runs)
        {
            if (!_runs.TryGetValue(clangPath, out run))
            {
                run = new ToolRunner(processRunner, new ToolRunOptions(location)).RunAsync(clangPath, ["--version"], Path.GetTempPath(), ct);
                _runs[clangPath] = run;
            }
        }

        ToolResult result;
        try
        {
            result = await run.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_runs)
            {
                _runs.Remove(clangPath);
            }

            throw;
        }

        if (result.Failure is { } failure)
        {
            lock (_runs)
            {
                _runs.Remove(clangPath);
            }

            return failure with { Location = location, Related = [.. failure.Related.Select(r => r with { Location = location })] };
        }

        var version = ParseVersion(result.Output);
        return Major(version) == Major(expectedVersion)
            ? null
            : Diagnostic.Create("W4002", location, version ?? "unknown", expectedVersion);
    }

    public static string? ParseVersion(IEnumerable<string> output)
    {
        const string Marker = "clang version ";
        foreach (var line in output)
        {
            var i = line.IndexOf(Marker, StringComparison.Ordinal);
            if (i < 0)
            {
                continue;
            }

            var rest = line[(i + Marker.Length)..];
            var end = 0;
            while (end < rest.Length && (char.IsAsciiDigit(rest[end]) || rest[end] == '.'))
            {
                end++;
            }

            if (end > 0 && char.IsAsciiDigit(rest[0]))
            {
                return rest[..end];
            }
        }

        return null;
    }

    private static int? Major(string? version) =>
        version is not null && int.TryParse(version.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : null;
}
