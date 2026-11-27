using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreexComp.Policy.Tests;

/// <summary>
/// The checks behind <see cref="DependencyLicenceTests"/> (LIC-070…082): the licence every package of the resolved
/// graph of every solution project declares, the <c>license</c> member of the toolchain manifest, and the reviewed
/// exceptions file. Everything takes the root directory, so the tests can run it on a fixture or temporary tree. Files
/// are read only: no process, no network, no package restore (LIC-075).
/// </summary>
internal static partial class DependencyLicences
{
    /// <summary>LIC-072: the licences the product may depend on. Changing this list is an owner decision.</summary>
    public static readonly IReadOnlyList<string> AllowedLicences =
        ["MIT", "Apache-2.0", "Apache-2.0 WITH LLVM-exception", "BSD-2-Clause", "BSD-3-Clause", "Zlib", "ISC"];

    /// <summary>LIC-076: the text of a toolchain entry that is a plain public-domain dedication.</summary>
    public const string PublicDomain = "Public domain";

    public const string ExceptionsRelative = "tests/policy/licence-exceptions.txt";
    public const string ExceptionsName = "licence-exceptions.txt";
    public const string NoticesName = "THIRD-PARTY-NOTICES.md";
    public const string ToolchainRelative = "scripts/toolchain.json";

    /// <summary>LIC-071: what to do when a restore output is missing.</summary>
    public const string Remedy = "run 'dotnet restore FreexComp.sln' first";

    private const string EntryShape = "<kind> | <name> | <version> | <licence> | <justification>";

    /// <summary>LIC-080: an identifier beginning with one of these cannot be excepted.</summary>
    private static readonly string[] NeverExcepted = ["GPL", "AGPL", "SSPL", "EUPL", "OSL", "CC-BY-NC", "CC-BY-ND", "BUSL"];
    private static readonly string[] NeverExceptedWords = ["Proprietary", "Commercial"];

    /// <summary>Toolchain key → the text the component cell of its "Shipped with the product" row begins with.</summary>
    private static readonly Dictionary<string, string> ToolchainRows = new(StringComparer.Ordinal)
    {
        ["llvm"] = "LLVM",
        ["python"] = "Python embeddable package",
        ["sqlite"] = "SQLite",
        ["unicode"] = "Unicode Character Database",
    };

    /// <summary>One package of the resolved graph; <see cref="Expression"/> is null when it declares no licence expression.</summary>
    public sealed record Package(string Id, string Version, string? Expression, IReadOnlyList<string> Projects);

    /// <summary>One object entry of the toolchain manifest; <see cref="Licence"/> is null when the member is missing or empty.</summary>
    public sealed record Tool(string Key, string Version, string? Licence);

    /// <summary>One entry line of the exceptions file (LIC-079).</summary>
    public sealed record Excepted(int Line, string Kind, string Name, string Version, string Licence, string Justification);

    /// <summary>What a run looked at and what it found; <see cref="Problems"/> has one line per finding.</summary>
    public sealed record Report(
        IReadOnlyList<string> Problems,
        IReadOnlyList<string> AssetsFiles,
        IReadOnlyList<Package> Packages,
        IReadOnlyList<Tool> Tools,
        IReadOnlyList<Excepted> Exceptions);

    [GeneratedRegex(@"^Project\(""[^""]*""\)\s*=\s*""[^""]*"",\s*""([^""]+)""", RegexOptions.Multiline)]
    private static partial Regex ProjectLine();

    [GeneratedRegex(@"\G\s*(\(|\)|[A-Za-z0-9.+-]+)")]
    private static partial Regex ExpressionToken();

    private static string Absolute(string root, string relative) => Path.Combine([root, .. relative.Split('/')]);

    private static string Display(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    // ---------------------------------------------------------------- expressions (LIC-072, 073)

    /// <summary>
    /// LIC-073: whether a declared licence is allowed. The grammar is the licence-expression grammar restricted to
    /// identifiers, <c>id WITH exception</c>, <c>AND</c>, <c>OR</c> and parentheses; anything else is not allowed.
    /// </summary>
    public static bool IsAllowed(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var text = expression.Trim();
        var tokens = new List<string>();
        var at = 0;
        while (at < text.Length)
        {
            var match = ExpressionToken().Match(text, at);
            if (!match.Success)
            {
                return false;
            }

            tokens.Add(match.Groups[1].Value);
            at = match.Index + match.Length;
        }

        var position = 0;
        var allowed = ParseOr(tokens, ref position);
        return allowed == true && position == tokens.Count;
    }

    private static bool IsOperator(string token) => token is "AND" or "OR" or "WITH" or "(" or ")";

    /// <summary>Null: the text does not parse.</summary>
    private static bool? ParseOr(List<string> tokens, ref int position)
    {
        var result = ParseAnd(tokens, ref position);
        while (result is not null && position < tokens.Count && tokens[position] == "OR")
        {
            position++;
            var right = ParseAnd(tokens, ref position);
            result = right is null ? null : result.Value || right.Value;
        }

        return result;
    }

    private static bool? ParseAnd(List<string> tokens, ref int position)
    {
        var result = ParsePrimary(tokens, ref position);
        while (result is not null && position < tokens.Count && tokens[position] == "AND")
        {
            position++;
            var right = ParsePrimary(tokens, ref position);
            result = right is null ? null : result.Value && right.Value;
        }

        return result;
    }

    private static bool? ParsePrimary(List<string> tokens, ref int position)
    {
        if (position >= tokens.Count)
        {
            return null;
        }

        var token = tokens[position++];
        if (token == "(")
        {
            var inner = ParseOr(tokens, ref position);
            if (inner is null || position >= tokens.Count || tokens[position] != ")")
            {
                return null;
            }

            position++;
            return inner;
        }

        if (IsOperator(token))
        {
            return null;
        }

        if (position < tokens.Count && tokens[position] == "WITH")
        {
            if (position + 1 >= tokens.Count || IsOperator(tokens[position + 1]))
            {
                return null;
            }

            var pair = token + " WITH " + tokens[position + 1];
            position += 2;
            return AllowedLicences.Contains(pair, StringComparer.Ordinal);
        }

        return AllowedLicences.Contains(token, StringComparer.Ordinal);
    }

    // ---------------------------------------------------------------- the run

    /// <summary>
    /// Checks the tree below <paramref name="root"/>. <paramref name="packages"/>: the resolved graph of every project
    /// of the solution file in the root (LIC-070…074), read from <paramref name="objFolder"/> of each project folder.
    /// <paramref name="toolchain"/>: the toolchain manifest (LIC-076…078). Entries of the exceptions file are always
    /// read (LIC-079, 080); the entries of a kind that is checked are also judged as stale or idle (LIC-081).
    /// </summary>
    public static Report Check(string root, bool packages = true, bool toolchain = true, string objFolder = "obj")
    {
        var problems = new List<string>();
        var assetsFiles = new List<string>();
        var graph = new List<Package>();
        var tools = new List<Tool>();

        var exceptionsFile = Absolute(root, ExceptionsRelative);
        var exceptions = new List<Excepted>();
        var exceptionProblems = new List<string>();
        if (File.Exists(exceptionsFile))
        {
            (exceptions, exceptionProblems) = ReadExceptions(exceptionsFile);
        }

        var noticesFile = Path.Combine(root, NoticesName);
        var notices = File.Exists(noticesFile) ? NoticeRows(File.ReadAllLines(noticesFile)) : [];

        bool HasException(string kind, string name, string version) => exceptions.Any(e => Matches(e, kind, name, version));

        if (packages)
        {
            ReadGraph(root, objFolder, assetsFiles, graph, problems);
            foreach (var package in graph.Where(p => !IsAllowed(p.Expression) && !HasException("package", p.Id, p.Version)))
            {
                problems.Add(package.Expression is null
                    ? $"{package.Id} {package.Version}: declares no licence expression (used by {Users(package)}); review it and add it to {ExceptionsRelative}"
                    : $"{package.Id} {package.Version}: licence '{package.Expression}' is not allowed (used by {Users(package)})");
            }
        }

        if (toolchain)
        {
            if (!File.Exists(noticesFile))
            {
                problems.Add($"{NoticesName} is missing");
            }

            ReadToolchain(root, tools, problems);
            foreach (var tool in tools)
            {
                if (tool.Licence is null)
                {
                    problems.Add($"toolchain.json: entry '{tool.Key}' has no licence");
                    continue;
                }

                if (tool.Licence != PublicDomain && !IsAllowed(tool.Licence) && !HasException("toolchain", tool.Key, tool.Version))
                {
                    problems.Add($"toolchain.json: entry '{tool.Key}': licence '{tool.Licence}' is not allowed");
                }

                // LIC-078: one statement per component
                if (!ToolchainRows.TryGetValue(tool.Key, out var prefix))
                {
                    problems.Add($"toolchain.json: entry '{tool.Key}' has no row mapping for {NoticesName}");
                    continue;
                }

                var rows = notices.Where(r => r.Shipped && r.Component.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                if (rows.Count == 0)
                {
                    problems.Add($"toolchain.json: entry '{tool.Key}' has no row in {NoticesName}");
                }
                else if (rows.All(r => r.Licence != tool.Licence))
                {
                    problems.Add($"toolchain.json: entry '{tool.Key}' says '{tool.Licence}' but {NoticesName} says '{rows[0].Licence}'");
                }
            }
        }

        problems.AddRange(exceptionProblems);

        // LIC-081: no stale and no idle entries
        for (var i = 0; i < exceptions.Count; i++)
        {
            var entry = exceptions[i];
            var where = $"{ExceptionsName}({entry.Line}): {entry.Kind} {entry.Name} {entry.Version}";
            var first = exceptions.Take(i).FirstOrDefault(e => Matches(e, entry.Kind, entry.Name, entry.Version));
            if (first is not null)
            {
                problems.Add($"{where}: duplicate of line {first.Line}");
                continue;
            }

            string? noticeName;
            if (entry.Kind == "package" && packages)
            {
                var package = graph.FirstOrDefault(p => Matches(entry, "package", p.Id, p.Version));
                if (package is null)
                {
                    problems.Add($"{where}: no such dependency");
                    continue;
                }

                if (IsAllowed(package.Expression))
                {
                    problems.Add($"{where}: not needed");
                }

                noticeName = package.Id;
            }
            else if (entry.Kind == "toolchain" && toolchain)
            {
                var tool = tools.FirstOrDefault(t => Matches(entry, "toolchain", t.Key, t.Version));
                if (tool is null)
                {
                    problems.Add($"{where}: no such dependency");
                    continue;
                }

                if (tool.Licence == PublicDomain || IsAllowed(tool.Licence))
                {
                    problems.Add($"{where}: not needed");
                }

                noticeName = ToolchainRows.GetValueOrDefault(tool.Key);
            }
            else
            {
                continue;
            }

            var row = noticeName is null ? null : notices.FirstOrDefault(r => Names(r.Component, noticeName));
            if (row is not null && row.Licence != entry.Licence)
            {
                problems.Add($"{where}: differs from {NoticesName} ('{row.Licence}')");
            }
        }

        return new Report(problems, assetsFiles, graph, tools, exceptions);
    }

    private static bool Matches(Excepted entry, string kind, string name, string version) =>
        entry.Kind == kind && entry.Version == version
        && string.Equals(entry.Name, name, kind == "package" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>LIC-074: the projects that use a package, in solution order, cut after three names.</summary>
    private static string Users(Package package) =>
        string.Join(", ", package.Projects.Take(3)) + (package.Projects.Count > 3 ? ", …" : "");

    // ---------------------------------------------------------------- packages (LIC-070, 071)

    /// <summary>The project files a solution names, '/' separated, in solution order (solution folders name no file).</summary>
    public static IReadOnlyList<string> SolutionProjects(string solutionFile) =>
        [.. ProjectLine().Matches(File.ReadAllText(solutionFile))
            .Select(m => m.Groups[1].Value.Replace('\\', '/'))
            .Where(p => p.EndsWith("proj", StringComparison.OrdinalIgnoreCase))];

    private static void ReadGraph(string root, string objFolder, List<string> assetsFiles, List<Package> graph, List<string> problems)
    {
        var solutions = Directory.GetFiles(root, "*.sln");
        if (solutions.Length != 1)
        {
            problems.Add($"expected one solution file in the root directory, found {solutions.Length}");
            return;
        }

        var found = new Dictionary<(string Id, string Version), (string? Expression, List<string> Projects)>();
        foreach (var project in SolutionProjects(solutions[0]))
        {
            var projectName = project[(project.LastIndexOf('/') + 1)..];
            var assets = Path.Combine(Path.GetDirectoryName(Absolute(root, project))!, objFolder, "project.assets.json");
            var display = Display(root, assets);
            assetsFiles.Add(display);
            if (!File.Exists(assets))
            {
                problems.Add($"{display}: the restore output of {projectName} is missing; {Remedy}");
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(assets));
                var folders = document.RootElement.GetProperty("packageFolders").EnumerateObject()
                    .Select(f => Path.GetFullPath(f.Name, root)).ToList();
                foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
                {
                    if (library.Value.GetProperty("type").GetString() != "package")
                    {
                        continue;
                    }

                    var slash = library.Name.IndexOf('/', StringComparison.Ordinal);
                    var key = (Id: library.Name[..slash], Version: library.Name[(slash + 1)..]);
                    if (found.TryGetValue(key, out var known))
                    {
                        known.Projects.Add(projectName);
                        continue;
                    }

                    var relative = library.Value.GetProperty("path").GetString()!;
                    var name = key.Id.ToLowerInvariant() + ".nuspec";
                    var metadata = folders.Select(f => Path.Combine([f, .. relative.Split('/'), name])).FirstOrDefault(File.Exists);
                    if (metadata is null)
                    {
                        problems.Add($"{key.Id} {key.Version}: metadata file {relative}/{name} is in none of the package folders of {display}; {Remedy}");
                        continue;
                    }

                    found.Add(key, (DeclaredExpression(metadata), [projectName]));
                }
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or System.Xml.XmlException)
            {
                problems.Add($"{display}: cannot be read ({e.Message.ReplaceLineEndings(" ")}); {Remedy}");
            }
        }

        graph.AddRange(found
            .OrderBy(p => p.Key.Id, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Key.Version, StringComparer.Ordinal)
            .Select(p => new Package(p.Key.Id, p.Key.Version, p.Value.Expression, p.Value.Projects)));
    }

    /// <summary>The text of the <c>license</c> element of a package metadata file when its type is <c>expression</c>; else null.</summary>
    private static string? DeclaredExpression(string metadataFile)
    {
        var licence = XDocument.Load(metadataFile).Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "license" && e.Parent?.Name.LocalName == "metadata");
        return licence is not null && (string?)licence.Attribute("type") == "expression" ? licence.Value.Trim() : null;
    }

    // ---------------------------------------------------------------- toolchain manifest (LIC-076)

    private static void ReadToolchain(string root, List<Tool> tools, List<string> problems)
    {
        var file = Absolute(root, ToolchainRelative);
        if (!File.Exists(file))
        {
            problems.Add($"{ToolchainRelative} is missing");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            foreach (var entry in document.RootElement.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.Object))
            {
                var members = entry.Value.EnumerateObject().Select(m => m.Name).ToList();
                var version = entry.Value.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                var licence = entry.Value.TryGetProperty("license", out var l) && l.ValueKind == JsonValueKind.String && l.GetString()!.Trim().Length > 0
                    ? l.GetString()!.Trim()
                    : null;
                tools.Add(new Tool(entry.Name, version, licence));
                if (licence is not null && members.IndexOf("license") != members.IndexOf("version") + 1)
                {
                    problems.Add($"toolchain.json: entry '{entry.Name}': 'license' must follow 'version'");
                }
            }
        }
        catch (JsonException e)
        {
            problems.Add($"{ToolchainRelative}: cannot be read ({e.Message.ReplaceLineEndings(" ")})");
        }
    }

    // ---------------------------------------------------------------- notices table

    private sealed record NoticeRow(bool Shipped, string Component, string Licence);

    /// <summary>The rows of the tables of the notices file (columns Component, Version, License, …), with their section.</summary>
    private static List<NoticeRow> NoticeRows(string[] lines)
    {
        var rows = new List<NoticeRow>();
        var section = "";
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                section = line[3..].Trim();
                continue;
            }

            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length >= 3 && cells[0] != "Component" && !cells[0].StartsWith("---", StringComparison.Ordinal))
            {
                rows.Add(new NoticeRow(section == "Shipped with the product", cells[0], cells[2]));
            }
        }

        return rows;
    }

    /// <summary>The matching rule of the notices generator: the cell equals the name, or starts with it followed by <c> (</c> or <c>,</c>.</summary>
    private static bool Names(string component, string name) =>
        component.StartsWith(name, StringComparison.OrdinalIgnoreCase)
        && (component.Length == name.Length || component[name.Length] == ','
            || component.AsSpan(name.Length).StartsWith(" (", StringComparison.Ordinal));

    // ---------------------------------------------------------------- exceptions file (LIC-079, 080)

    /// <summary>Reads the exceptions file: its well-formed entries, and one problem per line that is not one (with the line number).</summary>
    public static (List<Excepted> Entries, List<string> Problems) ReadExceptions(string file)
    {
        var entries = new List<Excepted>();
        var problems = new List<string>();
        var bytes = File.ReadAllBytes(file);
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            problems.Add($"{ExceptionsName}: must be UTF-8 without a byte order mark");
        }

        if (bytes.Contains((byte)'\r'))
        {
            problems.Add($"{ExceptionsName}: must use LF line endings");
        }

        var lines = new UTF8Encoding(false).GetString(bytes).TrimStart('﻿').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var where = $"{ExceptionsName}({i + 1})";
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var cells = line.Split(" | ").Select(c => c.Trim()).ToArray();
            if (cells.Length != 5)
            {
                problems.Add($"{where}: expected five cells '{EntryShape}', found {cells.Length}");
                continue;
            }

            if (cells.Any(c => c.Length == 0))
            {
                problems.Add($"{where}: empty cell");
                continue;
            }

            if (cells[0] is not ("package" or "toolchain"))
            {
                problems.Add($"{where}: unknown kind '{cells[0]}'");
                continue;
            }

            if (cells[4].Length < 20)
            {
                problems.Add($"{where}: the justification has fewer than 20 characters");
                continue;
            }

            if (!CanBeExcepted(cells[3]))
            {
                problems.Add($"{where}: '{cells[3]}' cannot be excepted");
                continue;
            }

            entries.Add(new Excepted(i + 1, cells[0], cells[1], cells[2], cells[3], cells[4]));
        }

        return (entries, problems);
    }

    /// <summary>LIC-080: an exception cannot admit copyleft or use restrictions; LGPL only together with a <c>WITH</c> part.</summary>
    public static bool CanBeExcepted(string licence)
    {
        if (NeverExceptedWords.Any(w => licence.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var identifiers = licence.Split([' ', '\t', '(', ')', ',', ';', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < identifiers.Length; i++)
        {
            if (NeverExcepted.Any(p => identifiers[i].StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (identifiers[i].StartsWith("LGPL", StringComparison.OrdinalIgnoreCase)
                && (i + 2 >= identifiers.Length || identifiers[i + 1] != "WITH"))
            {
                return false;
            }
        }

        return true;
    }
}
