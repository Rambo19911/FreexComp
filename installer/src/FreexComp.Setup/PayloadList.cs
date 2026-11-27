using System.Text;
using System.Text.RegularExpressions;

namespace FreexComp.Setup;

/// <summary>
/// A payload list (<c>installer/payload-*.txt</c>, INS-002): one glob per line relative to a base folder, <c>/</c>
/// separated; <c>*</c> and <c>?</c> stay inside one folder, <c>**</c> matches any number of folders; <c>#</c> starts a
/// comment. A line that starts with <c>!</c> is an exclusion: the files its glob matches are taken out of what the other
/// lines select, wherever it stands. A glob that matches no file, and an exclusion that takes nothing out, is an error.
/// </summary>
public static class PayloadList
{
    /// <summary>The relative paths (<c>\</c>) of the files under <paramref name="baseDirectory"/> the list selects, sorted.</summary>
    public static IReadOnlyList<string> Resolve(string listFile, string baseDirectory)
    {
        var rules = File.ReadAllLines(listFile)
            .Select(l => (l.IndexOf('#') is var hash and >= 0 ? l[..hash] : l).Trim())
            .Where(l => l.Length > 0)
            .ToList();
        var globs = rules.Where(r => !r.StartsWith('!')).ToList();
        if (globs.Count == 0)
            throw new InvalidDataException($"{listFile}: the list names no file.");
        var root = Path.GetFullPath(baseDirectory);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToList();
        var selected = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var glob in globs)
        {
            var pattern = ToRegex(glob);
            var matches = files.Where(f => pattern.IsMatch(f)).ToList();
            if (matches.Count == 0)
                throw new InvalidDataException($"{listFile}: '{glob}' matches no file under {root}.");
            selected.UnionWith(matches.Select(m => m.Replace('/', '\\')));
        }

        foreach (var rule in rules.Where(r => r.StartsWith('!')))
        {
            var glob = rule[1..].Trim();
            if (glob.Length == 0)
                throw new InvalidDataException($"{listFile}: '{rule}' is not a rule (an exclusion needs a glob).");
            var pattern = ToRegex(glob);
            if (selected.RemoveWhere(f => pattern.IsMatch(f.Replace('\\', '/'))) == 0)
                throw new InvalidDataException($"{listFile}: '{rule}' excludes no selected file under {root}.");
        }

        if (selected.Count == 0)
            throw new InvalidDataException($"{listFile}: the list selects no file under {root} (every selected file is excluded again).");

        return [.. selected];
    }

    /// <summary>Copies the selected files to the same relative paths under <paramref name="target"/>; returns their count.</summary>
    public static int Copy(string listFile, string baseDirectory, string target)
    {
        var files = Resolve(listFile, baseDirectory);
        foreach (var path in files)
        {
            var destination = Path.Combine(target, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(baseDirectory, path), destination, overwrite: true);
        }

        return files.Count;
    }

    internal static Regex ToRegex(string glob)
    {
        var text = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                i++;
                if (i + 1 < glob.Length && glob[i + 1] == '/')
                {
                    i++;
                    text.Append("(?:.*/)?");   // "**/" : zero or more folders
                }
                else
                {
                    text.Append(".*");
                }
            }
            else if (c == '*')
            {
                text.Append("[^/]*");
            }
            else if (c == '?')
            {
                text.Append("[^/]");
            }
            else
            {
                text.Append(Regex.Escape(c == '\\' ? "/" : c.ToString()));
            }
        }

        return new Regex(text.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
