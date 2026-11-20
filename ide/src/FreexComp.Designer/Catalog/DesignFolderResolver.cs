namespace FreexComp.Designer.Catalog;

/// <summary>
/// DSG-TP-001: finds a framework design folder (the folder holding <c>index.json</c>). The order is: the setting when
/// non-empty; <c>$FREEXCOMP_HOME/&lt;relative&gt;</c>; <c>&lt;exe dir&gt;/&lt;relative&gt;</c>; the first ancestor of the exe
/// directory that contains <c>&lt;relative&gt;/index.json</c> (developer runs from the repository).
/// </summary>
public static class DesignFolderResolver
{
    /// <summary>The ui-framework design folder relative to a home / repository root.</summary>
    public const string FrameworkFolder = "ui-framework/design";

    /// <summary>The data-access design folder (optional, Phase 7).</summary>
    public const string DataFolder = "data/design";

    /// <summary>
    /// Returns the design folder, or null when none of the candidates has an <c>index.json</c>. A non-empty setting
    /// is returned as is, whether or not it exists: the user chose it, and a missing index there is reported as
    /// <c>Msg.Designer.MetadataMissing</c> with that path.
    /// </summary>
    /// <param name="setting">Value of <c>toolchain.frameworkDesignDir</c> / <c>toolchain.dataDesignDir</c>.</param>
    /// <param name="freexcompHome">Value of the <c>FREEXCOMP_HOME</c> environment variable (null or empty = unset).</param>
    /// <param name="exeDirectory">Directory of the IDE executable.</param>
    /// <param name="relativeFolder"><see cref="FrameworkFolder"/> or <see cref="DataFolder"/>.</param>
    /// <param name="fileExists">File probe (the disk in production, a fake in tests).</param>
    public static string? Resolve(string? setting, string? freexcompHome, string exeDirectory, string relativeFolder,
        Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(setting))
        {
            return setting;
        }

        var relative = relativeFolder.Replace('/', Path.DirectorySeparatorChar);
        bool HasIndex(string folder) => fileExists(Path.Combine(folder, "index.json"));

        if (!string.IsNullOrWhiteSpace(freexcompHome) && Path.Combine(freexcompHome, relative) is var home && HasIndex(home))
        {
            return home;
        }

        if (Path.Combine(exeDirectory, relative) is var besideExe && HasIndex(besideExe))
        {
            return besideExe;
        }

        for (var dir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(exeDirectory)); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, relative);
            if (HasIndex(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
