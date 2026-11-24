using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreexComp.Policy.Tests;

/// <summary>
/// LIC-015: the tables of <c>THIRD-PARTY-NOTICES.md</c> are complete and have no stale rows — toolchain entries,
/// centrally managed packages (shipped or development-only) and every package id named in a component cell.
/// </summary>
[Trait("Phase", "8")]
public sealed partial class NoticesTableTests
{
    private const string Shipped = "Shipped with the product";
    private const string DevOnly = "Development and test only (not shipped)";

    /// <summary>§3.2: toolchain key → the text a "Shipped with the product" component cell begins with.</summary>
    private static readonly Dictionary<string, string> ToolchainRows = new(StringComparer.Ordinal)
    {
        ["llvm"] = "LLVM",
        ["python"] = "Python embeddable package",
        ["sqlite"] = "SQLite",
    };

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$")]
    private static partial Regex DottedId();

    [GeneratedRegex(@"\.(dll|exe|txt|json|md|pas|zip|msi)$", RegexOptions.IgnoreCase)]
    private static partial Regex FileExtension();

    [Fact]
    [Trait("Req", "LIC-015")]
    public void PackagesAndToolchainMatchTable()
    {
        var problems = Problems(RepoPaths.Root);
        Assert.True(problems.Count == 0, "THIRD-PARTY-NOTICES.md: " + string.Join("; ", problems));
    }

    [Fact]
    [Trait("Req", "LIC-015")]
    public void ReportsMissingStaleAndUnmappedEntries()
    {
        // The checker itself: a temp copy with one gap of each kind is reported, each with its own message.
        var temp = Directory.CreateTempSubdirectory("policy-notices-");
        try
        {
            foreach (var relative in new[] { "THIRD-PARTY-NOTICES.md", "Directory.Packages.props", Path.Combine("scripts", "toolchain.json"),
                         JsonRelative, AssetsRelative })
            {
                var target = Path.Combine(temp.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(RepoPaths.Combine(relative), target);
            }

            Assert.Empty(Problems(temp.FullName));

            var table = Path.Combine(temp.FullName, "THIRD-PARTY-NOTICES.md");
            File.WriteAllText(table, File.ReadAllText(table)
                .Replace("| SQLite | 3.54.1 |", "| SQLite | 3.0.0 |", StringComparison.Ordinal)
                .Replace("| Microsoft.NET.Test.Sdk | 18.12.0 |", "| Microsoft.NET.Test.Sdk, Contoso.Gone.Package | 18.12.0 |", StringComparison.Ordinal));
            var props = Path.Combine(temp.FullName, "Directory.Packages.props");
            var propsText = File.ReadAllText(props);
            var end = propsText.IndexOf("</ItemGroup>", StringComparison.Ordinal);
            File.WriteAllText(props, propsText.Insert(end, "  <PackageVersion Include=\"Contoso.New.Tool\" Version=\"1.0.0\" />\n  "));
            var toolchain = Path.Combine(temp.FullName, "scripts", "toolchain.json");
            File.WriteAllText(toolchain, File.ReadAllText(toolchain).Replace("\"dotnetSdk\"", "\"zlib\": { \"version\": \"1.3\" },\n  \"dotnetSdk\"", StringComparison.Ordinal));

            var problems = Problems(temp.FullName);
            Assert.Contains("toolchain entry 'sqlite' 3.54.1 has no row with that version", problems);
            Assert.Contains("toolchain entry 'zlib' has no notice mapping", problems);
            Assert.Contains("package 'Contoso.New.Tool' 1.0.0 is neither shipped (notice JSON) nor in the development table with that version", problems);
            Assert.Contains("stale package id 'Contoso.Gone.Package' in row 'Microsoft.NET.Test.Sdk, Contoso.Gone.Package'", problems);
            Assert.Equal(4, problems.Count);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    private static readonly string JsonRelative = Path.Combine("ide", "src", "FreexComp.Ide.Core", "Resources", "ThirdPartyNotices.json");
    private static readonly string AssetsRelative = Path.Combine("ide", "src", "FreexComp.Ide", "obj", "project.assets.json");

    private static List<string> Problems(string root)
    {
        var problems = new List<string>();
        var table = File.ReadAllLines(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
        var shipped = Rows(table, Shipped);
        var devOnly = Rows(table, DevOnly);
        Assert.NotEmpty(shipped);
        Assert.NotEmpty(devOnly);

        // (a) toolchain entries
        using (var toolchain = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "scripts", "toolchain.json"))))
        {
            foreach (var entry in toolchain.RootElement.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.Object))
            {
                if (!ToolchainRows.TryGetValue(entry.Name, out var prefix))
                {
                    problems.Add($"toolchain entry '{entry.Name}' has no notice mapping");
                    continue;
                }

                var version = entry.Value.GetProperty("version").GetString();
                if (!shipped.Any(r => r.Component.StartsWith(prefix, StringComparison.Ordinal) && r.Version == version))
                    problems.Add($"toolchain entry '{entry.Name}' {version} has no row with that version");
            }
        }

        // (b) centrally managed packages
        var packages = XDocument.Load(Path.Combine(root, "Directory.Packages.props")).Descendants("PackageVersion")
            .Select(e => ((string)e.Attribute("Include")!, (string)e.Attribute("Version")!)).ToList();
        var notices = new List<(string Version, string[] Packages)>();
        using (var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, JsonRelative))))
        {
            foreach (var notice in json.RootElement.EnumerateArray())
                notices.Add((notice.GetProperty("version").GetString()!,
                    [.. notice.GetProperty("packages").EnumerateArray().Select(p => p.GetString()!)]));
        }

        foreach (var (id, version) in packages)
        {
            var isShipped = notices.Any(n => n.Version == version && n.Packages.Contains(id, StringComparer.OrdinalIgnoreCase));
            var isDev = devOnly.Any(r => r.Version == version && Tokens(r.Component).Contains(id, StringComparer.OrdinalIgnoreCase));
            if (!isShipped && !isDev)
                problems.Add($"package '{id}' {version} is neither shipped (notice JSON) nor in the development table with that version");
        }

        // (c) no stale package ids in component cells
        var known = new HashSet<string>(packages.Select(p => p.Item1), StringComparer.OrdinalIgnoreCase);
        using (var assets = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, AssetsRelative))))
        {
            foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
                known.Add(library.Name[..library.Name.IndexOf('/', StringComparison.Ordinal)]);
        }

        foreach (var row in shipped.Concat(devOnly))
        {
            foreach (var token in DottedTokens(row.Component).Where(t => !known.Contains(t)))
                problems.Add($"stale package id '{token}' in row '{row.Component}'");
        }

        return problems;
    }

    /// <summary>Tokens of a component cell: split at commas, spaces, slashes and parentheses.</summary>
    private static string[] Tokens(string cell) =>
        cell.Split([',', ' ', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Dotted package-id tokens; of an abbreviated list (<c>A.B.Win32/Linux/macOS</c>) only the first token counts,
    /// tokens with a file extension are ignored.
    /// </summary>
    private static IEnumerable<string> DottedTokens(string cell) =>
        cell.Split([',', ' ', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Split('/')[0].Trim('`'))
            .Where(t => DottedId().IsMatch(t) && !FileExtension().IsMatch(t));

    private static List<(string Component, string Version)> Rows(string[] lines, string section)
    {
        var rows = new List<(string, string)>();
        var start = Array.FindIndex(lines, l => l.TrimEnd() == "## " + section);
        Assert.True(start >= 0, $"section '{section}' is missing");
        foreach (var line in lines.Skip(start + 1).SkipWhile(l => !l.StartsWith('|')).TakeWhile(l => l.StartsWith('|')).Skip(2))
        {
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            rows.Add((cells[0], cells[1]));
        }

        return rows;
    }
}
