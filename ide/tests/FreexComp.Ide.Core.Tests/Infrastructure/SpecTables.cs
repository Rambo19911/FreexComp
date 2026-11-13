namespace FreexComp.Ide.Core.Tests.Infrastructure;

/// <summary>
/// Reads Markdown tables of the spec (IDE-TEST-022). <c>docs/</c> is git-ignored, so every test using this is
/// <c>[Trait("Category","Spec")]</c> and is skipped only when the repository has no <c>docs</c> directory.
/// </summary>
internal static class SpecTables
{
    /// <summary>Full path of <c>docs/&lt;relativePath&gt;</c>; skips the test when <c>docs/</c> does not exist.</summary>
    public static string DocPath(string relativePath)
    {
        var docs = Path.Combine(RepoRoot(), "docs");
        if (!Directory.Exists(docs))
        {
            Assert.Skip("docs/ not present (product-only checkout)");
        }

        var path = Path.Combine(docs, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Spec file missing: {path}");
        return path;
    }

    /// <summary>
    /// Rows of the first table after the heading whose text starts with <paramref name="sectionHeading"/>,
    /// keyed by column header. Cells are trimmed, backticks removed and <c>\|</c> unescaped.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(string path, string sectionHeading)
    {
        var lines = File.ReadAllLines(path);
        var start = Array.FindIndex(lines, l => l.StartsWith('#') && l.TrimStart('#').Trim().StartsWith(sectionHeading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"Heading '{sectionHeading}' not found in {path}");

        var i = start + 1;
        while (i < lines.Length && !lines[i].StartsWith('|') && !lines[i].StartsWith('#'))
        {
            i++;
        }

        Assert.True(i < lines.Length && lines[i].StartsWith('|'), $"No table under '{sectionHeading}' in {path}");
        var header = SplitRow(lines[i]);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (i += 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            var cells = SplitRow(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < header.Count; c++)
            {
                row[header[c]] = c < cells.Count ? cells[c] : "";
            }

            rows.Add(row);
        }

        return rows;
    }

    private static List<string> SplitRow(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var body = line.Trim();
        for (var i = 1; i < body.Length; i++)
        {
            var ch = body[i];
            if (ch == '\\' && i + 1 < body.Length && body[i + 1] == '|')
            {
                current.Append('|');
                i++;
            }
            else if (ch == '|')
            {
                cells.Add(current.ToString().Replace("`", "", StringComparison.Ordinal).Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        return cells;
    }

    private static string RepoRoot()
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
