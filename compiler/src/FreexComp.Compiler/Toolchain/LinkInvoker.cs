using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>Inputs of one link (CMP-625). Paths are absolute.</summary>
/// <param name="UnitObjects">Program unit, <c>System</c>, then every other unit in init order (LANG-520).</param>
internal sealed record LinkRequest(string OutputPath, string ObjectDirectory, ImmutableArray<string> UnitObjects)
{
    /// <summary>CMP-1016: entry symbol, machine and extra linker lines come from the target.</summary>
    public TargetInfo Target { get; init; } = TargetRegistry.Default;

    public AppType AppType { get; init; } = AppType.Console;

    public bool DebugInfo { get; init; }

    public DebugFormat DebugFormat { get; init; } = DebugFormat.CodeView;

    /// <summary>Manifest to embed (CMP-628/629 variant 1); null → none.</summary>
    public string? ManifestPath { get; init; }

    /// <summary><c>{$MAXSTACKSIZE}</c>.</summary>
    public long MaxStackSize { get; init; } = 1048576;

    /// <summary><c>{$MINSTACKSIZE}</c>.</summary>
    public long MinStackSize { get; init; } = 16384;

    /// <summary>Objects from <c>{$L x.obj}</c>; sorted by the invoker.</summary>
    public ImmutableArray<string> ExtraObjects { get; init; } = [];

    /// <summary>Import libraries; sorted by name by the invoker.</summary>
    public ImmutableArray<string> ImportLibraries { get; init; } = [];

    /// <summary>Import name (<c>Foo</c> of <c>external 'x.dll' name 'Foo'</c>) → its <c>.def</c>, for the CMP-623 hint.</summary>
    public ImmutableDictionary<string, string> ImportDefinitions { get; init; } = ImmutableDictionary<string, string>.Empty;
}

internal sealed record LinkResult(bool Success, ImmutableArray<Diagnostic> Diagnostics);

/// <summary>CMP-625…627: response file, <c>lld-link @link.rsp</c>, error mapping, no stale exe.</summary>
internal sealed class LinkInvoker(ToolRunner runner, string lldLinkPath, IFileSystem fs)
{
    private const string ErrorPrefix = "lld-link: error: ";
    private const string Undefined = "undefined symbol: ";
    private const string Duplicate = "duplicate symbol: ";
    private const string DllImport = "__declspec(dllimport) ";

    public static ImmutableArray<string> BuildArguments(LinkRequest request)
    {
        if (!Path.IsPathFullyQualified(request.OutputPath))
        {
            throw new ArgumentException("The output path must be absolute: " + request.OutputPath, nameof(request));
        }

        var args = ImmutableArray.CreateBuilder<string>();
        args.AddRange(
            "/nologo",
            "/out:" + request.OutputPath,
            "/entry:" + request.Target.EntrySymbol,
            request.AppType == AppType.Gui ? "/subsystem:windows" : "/subsystem:console",
            "/machine:" + request.Target.LinkerMachine,
            "/nodefaultlib",
            "/incremental:no",
            "/opt:ref",
            "/opt:icf",
            "/Brepro",
            string.Create(CultureInfo.InvariantCulture, $"/stack:{request.MaxStackSize},{request.MinStackSize}"),
            "/largeaddressaware",
            "/dynamicbase",
            "/nxcompat");
        args.AddRange(request.Target.LinkerArguments);
        if (request.DebugInfo)
        {
            args.Add(request.DebugFormat == DebugFormat.Dwarf ? "/debug:dwarf" : "/debug");
        }

        if (request.ManifestPath is not null)
        {
            args.Add("/manifest:embed");
            args.Add("/manifestinput:" + request.ManifestPath);
        }

        args.AddRange(request.UnitObjects);
        args.AddRange(SortedByName(request.ExtraObjects));
        args.AddRange(SortedByName(request.ImportLibraries));
        return args.ToImmutable();
    }

    /// <summary>One argument per line (LF, final LF), quoted when it contains spaces.</summary>
    public static string RenderResponseFile(IEnumerable<string> arguments)
    {
        var sb = new StringBuilder();
        foreach (var argument in arguments)
        {
            sb.Append(ToolRunner.QuoteWindows(argument)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// CMP-626: each <c>undefined symbol: X</c> → E4005 (<c>__declspec(dllimport) X</c> is <c>__imp_X</c>, with
    /// the CMP-623 hint when the import's <c>.def</c> is known), each <c>duplicate symbol: X</c> → E4007; any other
    /// error line, or a failure without any error line, → one E4004 carrying the whole output, reported first.
    /// </summary>
    public static ImmutableArray<Diagnostic> MapErrors(
        IReadOnlyList<string> output, int exitCode, Location location, ImmutableDictionary<string, string> importDefinitions, string? outputPath = null)
    {
        // CMP-806 (C-154): an error line naming outputPath with a sharing-violation / access-denied reason → E4008
        var mapped = ImmutableArray.CreateBuilder<Diagnostic>();
        var unmapped = false;
        foreach (var line in output)
        {
            if (!line.StartsWith(ErrorPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var message = line[ErrorPrefix.Length..];
            if (outputPath is not null && message.Contains(outputPath, StringComparison.OrdinalIgnoreCase) && OutputInUse(message))
            {
                if (!mapped.Any(d => d.Code == "E4008"))
                {
                    mapped.Add(Diagnostic.Create("E4008", location, outputPath));
                }
            }
            else if (message.StartsWith(Undefined, StringComparison.Ordinal))
            {
                var symbol = message[Undefined.Length..].Trim();
                var import = symbol.StartsWith(DllImport, StringComparison.Ordinal) ? symbol[DllImport.Length..] : null;
                var d = Diagnostic.Create("E4005", location, import is null ? symbol : "__imp_" + import);
                if (import is not null && importDefinitions.TryGetValue(import, out var def))
                {
                    d = d with { Related = [new RelatedLocation(location, "add it to " + def)] };
                }

                mapped.Add(d);
            }
            else if (message.StartsWith(Duplicate, StringComparison.Ordinal))
            {
                mapped.Add(Diagnostic.Create("E4007", location, message[Duplicate.Length..].Trim()));
            }
            else
            {
                unmapped = true;
            }
        }

        if (unmapped || (exitCode != 0 && mapped.Count == 0))
        {
            mapped.Insert(0, ToolRunner.WithOutput(Diagnostic.Create("E4004", location, "lld-link.exe", exitCode), output));
        }

        return mapped.ToImmutable();
    }

    public async Task<LinkResult> LinkAsync(LinkRequest request, CancellationToken ct)
    {
        var rsp = Path.Combine(request.ObjectDirectory, "link.rsp");
        fs.WriteAllBytes(rsp, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(RenderResponseFile(BuildArguments(request))));
        fs.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        if (!TryDelete(request.OutputPath))
        {
            // CMP-806: the old exe is held by another process (a running program): the link would fail the same way
            return new LinkResult(false, [Diagnostic.Create("E4008", runner.Options.DiagnosticLocation, request.OutputPath)]);
        }

        var result = await runner.RunAsync(lldLinkPath, ["@" + rsp], request.ObjectDirectory, ct).ConfigureAwait(false);
        var diagnostics = result.Failure?.Code == "E4003"
            ? [result.Failure]
            : MapErrors(result.Output, result.ExitCode, runner.Options.DiagnosticLocation, request.ImportDefinitions, request.OutputPath);
        var success = result.ExitCode == 0 && diagnostics.IsEmpty;
        if (!success)
        {
            TryDelete(request.OutputPath);
        }

        return new LinkResult(success, diagnostics);
    }

    /// <summary>Deletes the file if present; false when another process holds it (sharing violation, access denied).</summary>
    private bool TryDelete(string path)
    {
        try
        {
            fs.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The reasons lld-link and Windows give for an output file another process holds.</summary>
    private static bool OutputInUse(string message) =>
        message.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
        || message.Contains("device or resource busy", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SortedByName(ImmutableArray<string> paths) =>
        paths.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.OrdinalIgnoreCase);
}
