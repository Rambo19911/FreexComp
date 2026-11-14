using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Rtl.Tests.Harness;

/// <summary>Module-definition files of <c>rtl/winapi/def/</c> (winapi.md §2).</summary>
[Trait("Phase", "2")]
public sealed partial class DefFileTests
{
    public static string DefDirectory => Path.Combine(RtlSetup.RepositoryRoot, "rtl", "winapi", "def");

    /// <summary>
    /// RTL-810/811: one lower-case file per DLL; <c>LIBRARY &lt;dll&gt;.dll</c>, <c>EXPORTS</c>, then undecorated names without
    /// ordinals, ordinally sorted, unique; <c>;</c> comments; LF only with a final newline. ABI-161: <c>kernel32.def</c> and
    /// <c>ntdll.def</c> exist, <c>ntdll.def</c> exports <c>__C_specific_handler</c>, no file lists <c>__chkstk</c> (RTL-065).
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Req", "RTL-810")]
    [Trait("Req", "RTL-811")]
    [Trait("Req", "RTL-065")]
    [Trait("Req", "RTL-142")]
    public void Def_Format()
    {
        Assert.True(Directory.Exists(DefDirectory), DefDirectory + " does not exist");
        var files = Directory.GetFiles(DefDirectory, "*.def").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var names = files.Select(Path.GetFileName).ToList();
        Assert.Contains("kernel32.def", names);
        Assert.Contains("ntdll.def", names);

        var exports = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var file in files)
        {
            exports[Path.GetFileName(file)] = Check(file, problems);
        }

        Assert.Contains("__C_specific_handler", exports["ntdll.def"]);
        Assert.All(exports, e => Assert.DoesNotContain("__chkstk", e.Value));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Returns the export names of <paramref name="path"/>; every rule violation is added to <paramref name="problems"/>.</summary>
    private static List<string> Check(string path, List<string> problems)
    {
        var fileName = Path.GetFileName(path);
        var baseName = Path.GetFileNameWithoutExtension(path);
        void Problem(string text) => problems.Add($"{fileName}: {text}");

        if (!string.Equals(fileName, fileName.ToLowerInvariant(), StringComparison.Ordinal))
        {
            Problem("file name is not lower case");
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Contains((byte)'\r'))
        {
            Problem("contains CR (LF line ends only)");
        }

        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            Problem("no final newline");
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            Problem("starts with a BOM");
        }

        var lines = Encoding.UTF8.GetString(bytes).TrimEnd('\n').Split('\n').Select((text, index) => (Text: text, Line: index + 1))
            .Where(l => !l.Text.StartsWith(';')).ToList();
        foreach (var blank in lines.Where(l => l.Text.Trim().Length == 0))
        {
            Problem($"line {blank.Line}: blank line");
        }

        lines.RemoveAll(l => l.Text.Trim().Length == 0);
        if (lines.Count < 2 || lines[0].Text != $"LIBRARY {baseName}.dll" || lines[1].Text != "EXPORTS")
        {
            Problem($"must start with 'LIBRARY {baseName}.dll' and 'EXPORTS'");
            return [];
        }

        var names = new List<string>();
        foreach (var (text, line) in lines.Skip(2))
        {
            if (!ExportName().IsMatch(text))
            {
                Problem($"line {line}: '{text}' is not a plain export name (no ordinals, decorations, spaces or keywords)");
                continue;
            }

            if (names.Count > 0 && string.CompareOrdinal(names[^1], text) >= 0)
            {
                Problem($"line {line}: '{text}' is not after '{names[^1]}' in ordinal order (or is a duplicate)");
            }

            names.Add(text);
        }

        return names;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ExportName();
}
