using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The checks behind <see cref="CommercialUseTests"/> (LIC-050…061): the licence line of the runtime source files, the
/// classification of the folders of the three library areas, and the list of everything that reaches a program built
/// with the product. Every check takes the home directory, so the tests can run it on a temporary tree as well.
/// </summary>
internal static partial class CommercialUse
{
    public const string RuntimeLicence = "Apache-2.0 WITH LLVM-exception";

    /// <summary>LIC-050: the first line of every <c>.pas</c> and <c>.inc</c> file of a runtime source root.</summary>
    public const string SourceLine = "// SPDX-License-Identifier: " + RuntimeLicence;

    /// <summary>LIC-051: the first line of every <c>.def</c> file.</summary>
    public const string DefLine = "; SPDX-License-Identifier: " + RuntimeLicence;

    /// <summary>The directories whose files are compiled or processed into a user's program, '/' separated.</summary>
    public static readonly IReadOnlyList<string> SourceRoots = ["rtl/src", "rtl/winapi/src", "rtl/winapi/def", "ui-framework/src", "data/src"];

    /// <summary>LIC-058: the licences allowed for anything inside or beside a user's program. Changing this list is an owner decision.</summary>
    public static readonly IReadOnlyList<string> AllowedLicences =
        [RuntimeLicence, "MIT", "BSD-2-Clause", "BSD-3-Clause", "Zlib", "ISC", "Public domain", "Unicode-3.0"];

    public const string ListName = "user-output-components.txt";

    public const string CompilerOutput = "compiler output";

    /// <summary>LIC-057: the licence cell of a <c>system</c> entry.</summary>
    public const string NoLicence = "—";

    /// <summary>LIC-054: the library areas and the names a directory directly below one may have.</summary>
    public static readonly IReadOnlyList<string> Areas = ["rtl", "ui-framework", "data"];
    private static readonly string[] NotInUserPrograms = ["tests", "samples", "design", "tools"];
    private static readonly string[] BuildFolders = ["bin", "obj"];

    /// <summary>One entry line of the list (LIC-056).</summary>
    public sealed record Entry(int Line, string Kind, string Name, string Licence, string How);

    [GeneratedRegex(@"'([^']*\.dll)'", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryLiteral();

    [GeneratedRegex(@"^[ \t]*LIBRARY[ \t]+""?([^\s""]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LibraryStatement();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex BackQuoted();

    private static string Absolute(string home, string relative) => Path.Combine([home, .. relative.Split('/')]);

    private static string Display(string home, string path) => Path.GetRelativePath(home, path).Replace('\\', '/');

    private static IEnumerable<string> FilesOf(string home, string root)
    {
        var directory = Absolute(home, root);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [];
    }

    private static bool HasExtension(string file, params string[] extensions) =>
        extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// LIC-050, 051, 053: one problem per file of the runtime source roots whose first line is not the licence line of
    /// its kind, and per file of a kind that cannot carry the line (the alias table, <c>.json</c>, is the one exemption:
    /// that format has no comments).
    /// </summary>
    public static List<string> LicenceLineProblems(string home)
    {
        var problems = new List<string>();
        foreach (var root in SourceRoots)
        {
            if (!Directory.Exists(Absolute(home, root)))
            {
                problems.Add($"{root}: runtime source root is missing");
            }

            foreach (var file in FilesOf(home, root))
            {
                var name = Display(home, file);
                if (HasExtension(file, ".json"))
                {
                    continue;
                }

                if (!HasExtension(file, ".pas", ".inc", ".def"))
                {
                    problems.Add($"{name}: file type is not allowed in a runtime source root");
                    continue;
                }

                // The first line without its line end: a file may have LF or CR LF line ends.
                var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
                var end = text.IndexOf('\n', StringComparison.Ordinal);
                var first = end < 0 ? text : text[..end];
                if (first.EndsWith('\r'))
                {
                    first = first[..^1];
                }

                if (first != (HasExtension(file, ".def") ? DefLine : SourceLine))
                {
                    problems.Add($"{name}: first line is not the licence line");
                }
            }
        }

        return problems;
    }

    /// <summary>True when the directory holds a file outside build folders, at any depth.</summary>
    private static bool HoldsFiles(string directory) =>
        Directory.EnumerateFiles(directory).Any()
        || Directory.EnumerateDirectories(directory)
            .Where(d => !BuildFolders.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            .Any(HoldsFiles);

    /// <summary>The names of the directories directly inside <paramref name="directory"/> that hold files, in ordinal order.</summary>
    public static List<string> FoldersOf(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateDirectories(directory)
                .Where(d => !BuildFolders.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase) && HoldsFiles(d))
                .Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal)]
            : [];

    /// <summary>The directory names LIC-054 allows directly below a library area.</summary>
    public static IReadOnlyList<string> ClassifiedFolders(string area)
    {
        List<string> names = ["src", .. NotInUserPrograms];
        if (area == "rtl")
        {
            names.Add("winapi");
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>LIC-054: every directory directly below a library area is runtime source or named as not part of a user's program.</summary>
    public static List<string> FolderProblems(string home)
    {
        var problems = new List<string>();
        foreach (var area in Areas)
        {
            var classified = ClassifiedFolders(area);
            problems.AddRange(FoldersOf(Absolute(home, area)).Where(f => !classified.Contains(f, StringComparer.Ordinal))
                .Select(f => $"{area}/{f}: directory is not classified (runtime source, or tests, samples, design, tools)"));
        }

        // the binding folder holds its two roots and nothing else
        var winapi = Absolute(home, "rtl/winapi");
        if (Directory.Exists(winapi))
        {
            problems.AddRange(FoldersOf(winapi).Where(f => f is not ("src" or "def"))
                .Select(f => $"rtl/winapi/{f}: only the runtime source roots src and def are allowed here"));
            problems.AddRange(Directory.EnumerateFiles(winapi).Order(StringComparer.Ordinal)
                .Select(f => $"{Display(home, f)}: only the runtime source roots src and def are allowed here"));
        }

        return problems;
    }

    /// <summary>
    /// LIC-056, 057: reads the list. Lines with four cells become entries (also when a cell is empty, so that the
    /// licence check can name the entry); everything that breaks the format is returned as a problem.
    /// </summary>
    public static (List<Entry> Entries, List<string> Problems) ReadList(string file, string home)
    {
        var entries = new List<Entry>();
        var problems = new List<string>();
        var bytes = File.ReadAllBytes(file);
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            problems.Add($"{ListName}: starts with a byte order mark");
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            problems.Add($"{ListName}: not valid UTF-8");
            return (entries, problems);
        }

        if (text.Contains('\r'))
        {
            problems.Add($"{ListName}: contains a carriage return (LF line ends only)");
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var at = $"{ListName}({i + 1})";
            var cells = line.Split('|');
            if (cells.Length != 4)
            {
                problems.Add($"{at}: expected '<kind> | <name> | <licence> | <how it reaches the program>'");
                continue;
            }

            var entry = new Entry(i + 1, cells[0].Trim(), cells[1].Trim(), cells[2].Trim(), cells[3].Trim());
            entries.Add(entry);
            if (line != string.Join(" | ", entry.Kind, entry.Name, entry.Licence, entry.How) || cells.Any(string.IsNullOrWhiteSpace))
            {
                problems.Add($"{at}: four cells, none empty, separated by ' | ' without other blanks around them");
            }

            switch (entry.Kind)
            {
                case "own":
                    if (entry.Licence != RuntimeLicence)
                    {
                        problems.Add($"{at}: the licence of the own entry '{entry.Name}' must be '{RuntimeLicence}'");
                    }

                    if (entry.Name != CompilerOutput && (entry.Name.Contains('\\') || !Directory.Exists(Absolute(home, entry.Name))))
                    {
                        problems.Add($"{at}: '{entry.Name}' is neither a repository path nor '{CompilerOutput}'");
                    }

                    break;
                case "third-party":
                    break;
                case "system":
                    if (entry.Licence != NoLicence)
                    {
                        problems.Add($"{at}: the licence cell of the system entry '{entry.Name}' must be '{NoLicence}'");
                    }

                    if (entry.Name != entry.Name.ToLowerInvariant() || !entry.Name.EndsWith(".dll", StringComparison.Ordinal))
                    {
                        problems.Add($"{at}: '{entry.Name}' is not a library file name in lower case");
                    }

                    break;
                default:
                    problems.Add($"{at}: kind '{entry.Kind}' is not one of own, third-party, system");
                    break;
            }
        }

        problems.AddRange(entries.GroupBy(e => (e.Kind, e.Name)).Where(g => g.Count() > 1)
            .Select(g => $"{ListName}({g.Last().Line}): '{g.Key.Name}' is listed twice"));
        return (entries, problems);
    }

    /// <summary>LIC-058: the licence of everything that is inside or beside a user's program is on the allow-list.</summary>
    public static List<string> LicenceProblems(IEnumerable<Entry> entries) =>
        [.. entries.Where(e => e.Kind is "own" or "third-party" && !AllowedLicences.Contains(e.Licence, StringComparer.Ordinal))
            .Select(e => $"{ListName}({e.Line}): licence '{e.Licence}' of '{e.Name}' is not allowed in user programs")];

    /// <summary>The rows of the table that follows the heading <paramref name="heading"/> in a Markdown file: column name and cell, in column order.</summary>
    public static List<List<(string Column, string Cell)>> TableAfter(string file, string heading)
    {
        var table = File.ReadAllText(file).ReplaceLineEndings("\n").Split('\n')
            .SkipWhile(l => l.Trim() != heading).Skip(1)
            .TakeWhile(l => !l.StartsWith('#'))
            .Where(l => l.StartsWith('|'))
            .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray())
            .ToList();
        return table.Count < 2
            ? []
            : [.. table.Skip(2).Select(row => table[0].Select((column, i) => (column, i < row.Length ? row[i] : "")).ToList())];
    }

    /// <summary>The cell of the column <paramref name="column"/> in a row of <see cref="TableAfter"/>; null when the table has no such column.</summary>
    public static string? Cell(List<(string Column, string Cell)> row, string column) =>
        row.Where(c => c.Column == column).Select(c => c.Cell).FirstOrDefault();

    /// <summary>
    /// LIC-059: every <c>third-party</c> entry has its row in the "Shipped with the product" table of the notices page —
    /// the row whose component cell is the name, or starts with the name followed by " (" or "," (the longest name wins) —
    /// and that row states the same licence.
    /// </summary>
    public static List<string> NoticesProblems(IEnumerable<Entry> entries, string noticesFile)
    {
        var problems = new List<string>();
        var thirdParty = entries.Where(e => e.Kind == "third-party").ToList();
        var rows = TableAfter(noticesFile, "## Shipped with the product");
        if (rows.Count == 0 || Cell(rows[0], "Component") is null || Cell(rows[0], "License") is null)
        {
            problems.Add("THIRD-PARTY-NOTICES.md: the table 'Shipped with the product' with the columns Component and License is missing");
            return problems;
        }

        foreach (var entry in thirdParty)
        {
            var row = rows.FirstOrDefault(r => Owner(Cell(r, "Component")!) == entry.Name);
            if (row is null)
            {
                problems.Add($"{ListName}({entry.Line}): '{entry.Name}' has no row in THIRD-PARTY-NOTICES.md, section 'Shipped with the product'");
            }
            else if (Cell(row, "License") != entry.Licence)
            {
                problems.Add($"{ListName}({entry.Line}): licence '{entry.Licence}' of '{entry.Name}' differs from '{Cell(row, "License")}' in THIRD-PARTY-NOTICES.md");
            }
        }

        return problems;

        string? Owner(string cell) => thirdParty.Select(e => e.Name)
            .Where(n => cell == n || cell.StartsWith(n + " (", StringComparison.Ordinal) || cell.StartsWith(n + ",", StringComparison.Ordinal))
            .MaxBy(n => n.Length);
    }

    /// <summary>
    /// LIC-060: (a) the <c>own</c> path entries are exactly the runtime source roots; (b) every library file name in a
    /// string literal of a runtime source file is a <c>system</c> entry or a file named between back-quotes by a
    /// <c>third-party</c> entry; (c) every <c>LIBRARY</c> of a <c>.def</c> file is a <c>system</c> entry; (d) every
    /// <c>system</c> entry is used.
    /// </summary>
    public static List<string> CompletenessProblems(IReadOnlyCollection<Entry> entries, string home)
    {
        var problems = new List<string>();

        var ownPaths = entries.Where(e => e.Kind == "own" && e.Name != CompilerOutput).ToList();
        problems.AddRange(SourceRoots.Where(root => ownPaths.All(e => e.Name != root))
            .Select(root => $"runtime source root {root} is not listed as an own entry"));
        problems.AddRange(ownPaths.Where(e => !SourceRoots.Contains(e.Name, StringComparer.Ordinal))
            .Select(e => $"{ListName}({e.Line}): own entry '{e.Name}' is not a runtime source root"));

        var system = entries.Where(e => e.Kind == "system").Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var deployed = entries.Where(e => e.Kind == "third-party")
            .SelectMany(e => BackQuoted().Matches(e.How).Select(m => m.Groups[1].Value.ToLowerInvariant()))
            .ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceRoots.SelectMany(root => FilesOf(home, root)))
        {
            var name = Display(home, file);
            if (HasExtension(file, ".pas", ".inc"))
            {
                foreach (var literal in LibraryLiteral().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal))
                {
                    var library = literal[(literal.LastIndexOf('\\') + 1)..].ToLowerInvariant();
                    used.Add(library);
                    if (!system.Contains(library) && !deployed.Contains(library))
                    {
                        problems.Add($"{library} is named by {name} but not listed");
                    }
                }
            }
            else if (HasExtension(file, ".def"))
            {
                var statements = LibraryStatement().Matches(File.ReadAllText(file));
                if (statements.Count == 0)
                {
                    problems.Add($"{name}: no LIBRARY statement");
                }

                foreach (var library in statements.Select(m => m.Groups[1].Value.ToLowerInvariant()))
                {
                    used.Add(library);
                    if (!system.Contains(library))
                    {
                        problems.Add($"{library} is the LIBRARY of {name} but not listed as a system entry");
                    }
                }
            }
        }

        problems.AddRange(entries.Where(e => e.Kind == "system" && !used.Contains(e.Name))
            .Select(e => $"{ListName}({e.Line}): system entry '{e.Name}' is not named by any runtime source file"));
        return problems;
    }
}
