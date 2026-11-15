using System.Collections.Immutable;
using FreexComp.Rtl.Tests.Harness;

[assembly: Trait("Phase", "4")]                 // UIF-1209 (xUnit v3: the trait attribute applies to the assembly)

namespace FreexComp.UiFramework.Tests;

/// <summary>One layout case of the shared corpus <c>tests/layout-corpus/</c>: <c>Enn-slug.form</c> + optional <c>.steps</c> + <c>.expected.json</c> (layout.md §15).</summary>
public sealed record LayoutCase(string Id, string FormPath, string? StepsPath, string? ExpectedPath)
{
    public string Slug => Path.GetFileNameWithoutExtension(FormPath);
}

/// <summary>
/// Discovery of this area's tests (testing.md §1, UIF-1200): group A programs <c>ui-framework/tests/pascal/*.pas</c>
/// (row id <c>pascal/&lt;Name&gt;</c>) and the lead-owned layout corpus (row id <c>layout/&lt;Enn-slug&gt;</c>).
/// Files directly in <c>pascal/</c> whose name ends in <c>Kit</c> are shared units (e.g. <c>UiTestKit.pas</c>), not programs.
/// </summary>
public static class UiCorpus
{
    /// <summary>Marker of the leading comment block: the program creates window handles (Category=Foreground, UIF-1203).</summary>
    public const string DesktopMarker = "// needs: desktop";

    public static string AreaDirectory => Path.Combine(RtlSetup.RepositoryRoot, "ui-framework");

    public static string SourceDirectory => Path.Combine(AreaDirectory, "src");

    public static string PascalDirectory => Path.Combine(AreaDirectory, "tests", "pascal");

    public static string LayoutCorpusDirectory => Path.Combine(RtlSetup.RepositoryRoot, "tests", "layout-corpus");

    /// <summary>
    /// UIF-1201: opc, the RTL and the toolchain as for the RTL programs; <c>-U rtl/tests</c> (TestKit) comes from the
    /// runner, <c>-U ui-framework/src</c> from each program's <c>// opc: -U ..\..\src</c> marker (checked by <see cref="CheckOptions"/>).
    /// </summary>
    public static RtlSetup Setup => RtlSetup.Default;

    public static ImmutableArray<RtlCase> DiscoverPascal(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(directory, "*.pas")
            .Where(p => !Path.GetFileNameWithoutExtension(p).EndsWith("Kit", StringComparison.Ordinal))
            .Select(p => new RtlCase("pascal/" + Path.GetFileNameWithoutExtension(p), p))
            .OrderBy(c => c.Id, StringComparer.Ordinal)];
    }

    public static ImmutableArray<RtlCase> DiscoverPascal() => DiscoverPascal(PascalDirectory);

    /// <summary>Every <c>*.form</c> of <paramref name="directory"/>; an absent folder yields no case.</summary>
    public static ImmutableArray<LayoutCase> DiscoverLayout(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(directory, "*.form")
            .Select(form =>
            {
                var stem = Path.Combine(Path.GetDirectoryName(form)!, Path.GetFileNameWithoutExtension(form));
                string? Sibling(string suffix) => File.Exists(stem + suffix) ? stem + suffix : null;
                return new LayoutCase("layout/" + Path.GetFileNameWithoutExtension(form), form, Sibling(".steps"), Sibling(".expected.json"));
            })
            .OrderBy(c => c.Id, StringComparer.Ordinal)];
    }

    public static ImmutableArray<LayoutCase> DiscoverLayout() => DiscoverLayout(LayoutCorpusDirectory);

    public static bool NeedsDesktop(string text) =>
        text.TrimStart('﻿').ReplaceLineEndings("\n").Split('\n')
            .Select(l => l.Trim())
            .TakeWhile(l => l.StartsWith("//", StringComparison.Ordinal))
            .Any(l => string.Equals(l, DesktopMarker, StringComparison.Ordinal));

    public static bool NeedsDesktop(RtlCase @case) => NeedsDesktop(File.ReadAllText(@case.SourcePath));

    /// <summary>Null when the program's <c>// opc:</c> options put <c>ui-framework/src</c> on the unit path, else the failure.</summary>
    public static string? CheckOptions(RtlCase @case)
    {
        var options = RtlCorpus.ReadHeader(@case).OpcOptions;
        var dir = Path.GetDirectoryName(@case.SourcePath)!;
        for (var i = 0; i + 1 < options.Length; i++)
        {
            if (options[i] == "-U" && string.Equals(Path.GetFullPath(Path.Combine(dir, options[i + 1])).TrimEnd('\\'), SourceDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return $"{@case.Id}: the leading comment block lacks '// opc: -U ..\\..\\src' (UIF-1201)";
    }

    /// <summary>UIF-1209: the phase of the <c>// phase: N</c> marker; without one, 4 (the RTL runner's default 2 does not apply here).</summary>
    public static int PhaseOf(RtlCase @case)
    {
        var text = File.ReadAllText(@case.SourcePath);
        var marked = text.TrimStart('﻿').ReplaceLineEndings("\n").Split('\n')
            .Select(l => l.Trim())
            .TakeWhile(l => l.StartsWith("//", StringComparison.Ordinal))
            .Any(l => l[2..].TrimStart().StartsWith("phase:", StringComparison.Ordinal));
        return marked ? RtlCorpus.ReadHeader(text).Phase : 4;
    }

    /// <summary>The rows of one theory: programs of <paramref name="phase"/> that do (or do not) need a desktop; <c>Req</c> traits from <c>// covers:</c>.</summary>
    public static IEnumerable<TheoryDataRow<string>> Rows(int phase, bool desktop)
    {
        foreach (var @case in DiscoverPascal().Where(c => PhaseOf(c) == phase && NeedsDesktop(c) == desktop))
        {
            var row = new TheoryDataRow<string>(@case.Id) { TestDisplayName = @case.Id };
            var covers = RtlCorpus.ReadHeader(@case).Covers;
            if (covers.Length > 0)
            {
                row.Traits["Req"] = [.. covers];
            }

            yield return row;
        }
    }

    /// <summary>UIF-1201: compile with opc and run with <c>FXC_UI_NONINTERACTIVE=1</c> under the golden contract.</summary>
    public static async Task<string?> RunAsync(string id, CancellationToken ct)
    {
        var @case = DiscoverPascal().Single(c => c.Id == id);
        if (CheckOptions(@case) is { } bad)
        {
            return bad;
        }

        Environment.SetEnvironmentVariable("FXC_UI_NONINTERACTIVE", "1");          // inherited by every program run
        return await GoldenRunner.RunAsync(@case, Setup, ct).ConfigureAwait(false);
    }
}
