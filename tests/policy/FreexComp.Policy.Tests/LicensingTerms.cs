using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>The scan for registration, activation, licence-key, trial and edition wording in product files (NET-030…032).</summary>
internal static class LicensingTerms
{
    /// <summary>NET-031: phrases that only licensing code uses (regular expressions, case-insensitive).</summary>
    public static IReadOnlyList<string> Patterns { get; } =
    [
        "licen[sc]e[ _-]?key",
        "serial[ _-]?number",
        "product[ _-]?key",
        "unlock[ _-]?code",
        "activation[ _-]?(code|key|file|server|required|wizard)",
        "(activate|register)[ _-]?(your|the|this)[ _-]?(product|copy|licen[sc]e)",
        "registration[ _-]?(code|key|required|wizard)",
        "trial[ _-]?(period|expired|version|days|mode)",
        "days[ _-]?(left|remaining)[ _-]?(in|of)[ _-]?(your|the)[ _-]?trial",
        "(community|starter|professional|enterprise|architect|ultimate)[ _-]?edition",
        "licen[sc]e[ _-]?(server|check|validation|expired|expires)",
    ];

    private static readonly Regex Any = new(
        string.Join("|", Patterns.Select(p => "(?:" + p + ")")),
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] Extensions = [".cs", ".axaml", ".resx", ".json", ".pas", ".inc", ".md", ".ps1", ".txt"];

    /// <summary>
    /// NET-030: text files under <c>&lt;area&gt;/src/</c>, <c>&lt;area&gt;/design/</c>, <c>help/</c> and <c>scripts/</c>
    /// (path relative to the repository root, forward slashes). The embedded third-party notices are excluded:
    /// third-party licence texts use such words.
    /// </summary>
    public static bool InScope(string relativePath)
    {
        var parts = relativePath.Split('/');
        var folder = parts[0] is "help" or "scripts" || (parts.Length > 2 && parts[1] is "src" or "design");
        return folder && parts.Length > 1 && parts[^1] != "ThirdPartyNotices.json"
            && Extensions.Contains(Path.GetExtension(parts[^1]), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One problem per hit that is not exempted, as <c>file(line): matched text</c>, and one per exemption line
    /// (<c>&lt;relative path&gt;|&lt;matched text&gt;|&lt;reason&gt;</c>) that is malformed or matches nothing (NET-032).
    /// </summary>
    public static IReadOnlyList<string> Check(IEnumerable<(string Path, string Text)> files, string exemptions)
    {
        var problems = new List<string>();
        var exempt = new List<(string Line, string Path, string Matched)>();
        foreach (var line in exemptions.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0))
        {
            var cells = line.Split('|');
            if (cells.Length == 3 && cells.All(c => c.Trim().Length > 0))
                exempt.Add((line, cells[0].Trim(), cells[1].Trim()));
            else
                problems.Add("malformed exemption (expected <relative path>|<matched text>|<reason>): " + line);
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, text) in files)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in Any.Matches(lines[i]))
                {
                    var exemption = exempt.FirstOrDefault(e => e.Path == path && e.Matched == match.Value);
                    if (exemption.Line is null)
                        problems.Add($"{path}({i + 1}): {match.Value}");
                    else
                        used.Add(exemption.Line);
                }
            }
        }

        problems.AddRange(exempt.Where(e => !used.Contains(e.Line)).Select(e => "stale exemption: " + e.Line));
        return problems;
    }
}
