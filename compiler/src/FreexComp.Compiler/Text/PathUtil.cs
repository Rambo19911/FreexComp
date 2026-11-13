namespace FreexComp.Compiler.Text;

/// <summary>
/// Canonical path form (CMP-205): absolute, '\' separators, upper-case drive letter, no '.'/'..' segments,
/// no trailing separator except on a drive root. Compared ordinal case-insensitively. Pure string
/// operation: no file-system access, independent of the current directory.
/// </summary>
public static class PathUtil
{
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    public static bool AreEqual(string a, string b) => Comparer.Equals(Normalize(a), Normalize(b));

    /// <exception cref="ArgumentException">The path is empty or not fully qualified.</exception>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var p = path.Replace('/', '\\');
        string root;
        string rest;
        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = p[2..].Split('\\', 3);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                throw NotAbsolute(path);
            }

            root = @"\\" + parts[0] + @"\" + parts[1];
            rest = parts.Length == 3 ? parts[2] : "";
        }
        else if (p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\')
        {
            root = char.ToUpperInvariant(p[0]) + @":";
            rest = p[3..];
        }
        else
        {
            throw NotAbsolute(path);
        }

        var segments = new List<string>();
        foreach (var segment in rest.Split('\\'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return root.StartsWith(@"\\", StringComparison.Ordinal) ? root : root + @"\";
        }

        return root + @"\" + string.Join('\\', segments);
    }

    private static ArgumentException NotAbsolute(string path) =>
        new($"Path must be absolute: '{path}'", nameof(path));
}
