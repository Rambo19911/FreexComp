using System.Collections.Immutable;

[assembly: Trait("Phase", "7")]                 // DAT-409 (xUnit v3: the trait attribute applies to the assembly)

namespace FreexComp.Data.Tests;

/// <summary>One golden program <c>data/tests/programs/&lt;Name&gt;.pas</c> with its expected output <c>&lt;Name&gt;.out</c> (DAT-400).</summary>
public sealed record DataProgram(string Name, string SourcePath)
{
    public string ExpectedPath => Path.ChangeExtension(SourcePath, ".out");
}

/// <summary>The markers of a program's leading <c>//</c> comment block: <c>// covers: DAT-nnn …</c> and <c>// opc: &lt;options&gt;</c>.</summary>
public sealed record DataHeader(ImmutableArray<string> Covers, ImmutableArray<string> OpcOptions);

/// <summary>Where the runner finds opc, the RTL, the pinned toolchain, the SQLite library and the programs (testing.md §1).</summary>
public static class DataCorpus
{
    public static string RepositoryRoot { get; } = FindRoot();

    public static string OpcPath => Path.Combine(AppContext.BaseDirectory, "opc.exe");

    public static string RtlDirectory => Path.Combine(RepositoryRoot, "rtl", "src");

    public static string ToolchainDirectory => Path.Combine(RepositoryRoot, "third_party", "llvm", "bin");

    public static string SqliteLibrary => Path.Combine(RepositoryRoot, "third_party", "sqlite", "sqlite3.dll");

    public static string SourceDirectory => Path.Combine(RepositoryRoot, "data", "src");

    public static string UiSourceDirectory => Path.Combine(RepositoryRoot, "ui-framework", "src");

    public static string ProgramsDirectory => Path.Combine(RepositoryRoot, "data", "tests", "programs");

    public static string LibDirectory => Path.Combine(ProgramsDirectory, "lib");

    public static string FixturesDirectory => Path.Combine(RepositoryRoot, "data", "tests", "fixtures");

    /// <summary>Every <c>*.pas</c> directly in <paramref name="directory"/>, by ordinal name; an absent folder yields no program.</summary>
    public static ImmutableArray<DataProgram> Discover(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(directory, "*.pas")
            .Select(p => new DataProgram(Path.GetFileNameWithoutExtension(p), p))
            .OrderBy(p => p.Name, StringComparer.Ordinal)];
    }

    public static ImmutableArray<DataProgram> Discover() => Discover(ProgramsDirectory);

    public static DataHeader ReadHeader(string text)
    {
        var covers = ImmutableArray.CreateBuilder<string>();
        var options = ImmutableArray.CreateBuilder<string>();
        foreach (var raw in text.TrimStart('﻿').ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("//", StringComparison.Ordinal))
            {
                break;
            }

            var comment = line[2..].Trim();
            if (comment.StartsWith("covers:", StringComparison.Ordinal))
            {
                covers.AddRange(comment["covers:".Length..].Split([',', ' '], StringSplitOptions.RemoveEmptyEntries));
            }
            else if (comment.StartsWith("opc:", StringComparison.Ordinal))
            {
                options.AddRange(comment["opc:".Length..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return new DataHeader(covers.ToImmutable(), options.ToImmutable());
    }

    /// <summary>The theory rows: display name = program name, <c>Req</c> traits from the <c>// covers:</c> header, <c>Category=Foreground</c> for control programs (testing.md §1.1).</summary>
    public static IEnumerable<TheoryDataRow<string>> Rows()
    {
        foreach (var program in Discover())
        {
            var row = new TheoryDataRow<string>(program.Name) { TestDisplayName = program.Name };
            var covers = ReadHeader(File.ReadAllText(program.SourcePath)).Covers;
            if (covers.Length > 0)
            {
                row.Traits["Req"] = [.. covers];
            }

            // control programs need an interactive desktop (testing.md §1.1, DAT-406)
            if (program.Name.StartsWith("Ctl", StringComparison.Ordinal) || program.Name == "FormStream")
            {
                row.Traits["Category"] = ["Foreground"];
            }

            yield return row;
        }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }
}
