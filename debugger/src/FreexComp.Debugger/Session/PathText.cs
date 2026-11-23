namespace FreexComp.Debugger.Session;

/// <summary>Path conventions of the public contract: absolute, normalised Windows paths compared ordinal-ignore-case (DBG-415).</summary>
internal static class PathText
{
    /// <summary>Full path with an upper-case drive letter (DBG-074). Text that is not a usable path is returned unchanged.</summary>
    public static string Normalize(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }

        return path.Length >= 2 && path[1] == ':' && char.IsAsciiLetterLower(path[0])
            ? char.ToUpperInvariant(path[0]) + path[1..]
            : path;
    }

    /// <summary>True when <paramref name="path"/> lies in the directory <paramref name="root"/> or below it.</summary>
    public static bool IsUnder(string path, string root)
    {
        var directory = Normalize(root).TrimEnd('\\', '/') + "\\";
        return path.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
    }
}
