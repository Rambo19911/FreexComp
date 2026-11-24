using FreexComp.Ide.Core.Models;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Projects;

/// <summary>Names of folders created in the Project Manager (phase8-project-files.md §2, IDE-PM-111, IDE-PM-112).</summary>
public static class FolderNames
{
    private static readonly char[] InvalidChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> DeviceNames = new(
        new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).Select(i => "COM" + i))
            .Concat(Enumerable.Range(1, 9).Select(i => "LPT" + i)),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>IDE-PM-112: leading and trailing spaces do not count.</summary>
    public static string Normalize(string name) => name.Trim(' ');

    /// <summary>
    /// IDE-PM-112, first failure: empty or only spaces and dots, a character of <c>\ / : * ? " &lt; &gt; |</c> or a control
    /// character, a trailing dot, a reserved device name (with or without an extension) → <c>Msg.Project.FolderNameInvalid</c>;
    /// a file or directory of that name in <paramref name="parentDirectory"/> other than <paramref name="currentName"/>
    /// itself → <c>Msg.Project.FolderExists</c> [name].
    /// </summary>
    public static OperationResult Validate(IdeFileSystem fileSystem, string parentDirectory, string name, string? currentName = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(name);
        name = Normalize(name);
        var stem = name.Split('.')[0];
        if (name.Trim('.').Length == 0 || name.IndexOfAny(InvalidChars) >= 0 || name.Any(char.IsControl) || name.EndsWith('.')
            || DeviceNames.Contains(stem.TrimEnd(' ')))
            return OperationResult.Fail("Msg.Project.FolderNameInvalid");
        if (string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Ok;
        var path = Path.Combine(parentDirectory, name);
        return fileSystem.FileExists(path) || fileSystem.DirectoryExists(path)
            ? OperationResult.Fail("Msg.Project.FolderExists", name)
            : OperationResult.Ok;
    }

    /// <summary>IDE-PM-111: <c>&lt;prefix&gt;&lt;N&gt;&lt;suffix&gt;</c> with the smallest N ≥ 1 for which no file or directory of that name exists.</summary>
    public static string FirstFree(IdeFileSystem fileSystem, string directory, string prefix, string suffix = "")
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        for (var n = 1; ; n++)
        {
            var name = $"{prefix}{n}{suffix}";
            var path = Path.Combine(directory, name);
            if (!fileSystem.FileExists(path) && !fileSystem.DirectoryExists(path))
                return name;
        }
    }
}
