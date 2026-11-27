using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FreexComp.Compiler.Text;

namespace FreexComp.Build;

/// <summary>
/// The rules of the <c>.pproj</c> keys <c>icon</c>, <c>versionInfo</c> and <c>resources</c> (resource-manager.md CMP-1428…1433): their
/// forms, and the resource script the build generates from them (<c>&lt;objdir&gt;\&lt;project name&gt;.project.rc</c>), which goes
/// through the build's resource path as its first resource input.
/// </summary>
public static class ProjectResources
{
    /// <summary>The values of <see cref="ProjectResource.Type"/>, exactly as written.</summary>
    public static ImmutableArray<string> Types { get; } = ["RCDATA", "ICON", "BITMAP", "CURSOR"];

    /// <summary>The suffix of the generated script after the project name.</summary>
    internal const string ScriptSuffix = ".project.rc";

    /// <summary>
    /// CMP-1428: an identifier <c>[A-Za-z_][A-Za-z0-9_]*</c> of at most 64 characters, or an integer 1…65535 (ASCII digits, leading
    /// zeros allowed).
    /// </summary>
    public static bool IsValidName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length > 0 && char.IsAsciiDigit(name[0]))
        {
            return Number(name) is >= 1 and <= ushort.MaxValue;
        }

        return name.Length is >= 1 and <= 64 && (char.IsAsciiLetter(name[0]) || name[0] == '_')
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    /// <summary>CMP-1428: one to four numbers 0…65535 (ASCII digits, at most five each) separated by '.'.</summary>
    public static bool IsValidVersion(string version) => ParseVersion(version) is not null;

    /// <summary>A valid name as the script writes it and as duplicates are compared: upper case, or a number in decimal.</summary>
    internal static string CanonicalName(string name) =>
        char.IsAsciiDigit(name[0]) ? Number(name).ToString(CultureInfo.InvariantCulture) : name.ToUpperInvariant();

    /// <summary>The generated script of <paramref name="project"/> built into <paramref name="objectDirectory"/>.</summary>
    internal static string ScriptPath(string objectDirectory, Project project) => Path.Combine(objectDirectory, project.Name + ScriptSuffix);

    /// <summary>
    /// CMP-1429, 1430: the generated script, or null when there is nothing to generate. <paramref name="missing"/> holds the full paths
    /// of the files that do not exist (reported as W9002 by the caller); their entries are left out. The model is expected to have
    /// passed the loader's checks (CMP-1428). Paths are resolved against the project directory.
    /// </summary>
    internal static string? RenderScript(Project project, bool debugInfo, string outputFileName, IReadOnlyCollection<string> missing)
    {
        ArgumentNullException.ThrowIfNull(project);
        string Full(string path) => PathUtil.Normalize(Path.Combine(project.Directory, path.Replace('/', '\\')));
        bool Present(string full) => !missing.Contains(full, PathUtil.Comparer);

        var lines = new List<string>();
        if (project.Icon is { } icon && Full(icon) is var iconPath && Present(iconPath))
        {
            lines.Add("1 ICON " + Quote(iconPath));
        }

        foreach (var resource in project.Resources.IsDefault ? [] : project.Resources)
        {
            var path = Full(resource.Path);
            if (!Present(path))
            {
                continue;
            }

            var name = CanonicalName(resource.Name);
            if (!char.IsAsciiDigit(name[0]))
            {
                lines.Add("#undef " + name);                // a define of the same name would replace the name (the script is preprocessed)
            }

            lines.Add($"{name} {resource.Type} {Quote(path)}");
        }

        if (project.VersionInfo is { } info)
        {
            var file = ParseVersion(info.FileVersion ?? "1.0.0.0") ?? throw new ArgumentException($"Invalid file version '{info.FileVersion}'", nameof(project));
            var product = info.ProductVersion is { } p
                ? ParseVersion(p) ?? throw new ArgumentException($"Invalid product version '{p}'", nameof(project))
                : file;
            string Numbers(ushort[] v) => string.Join(",", v.Select(n => n.ToString(CultureInfo.InvariantCulture)));
            string Dotted(ushort[] v) => string.Join(".", v.Select(n => n.ToString(CultureInfo.InvariantCulture)));

            lines.Add("1 VERSIONINFO");
            lines.Add("FILEVERSION " + Numbers(file));
            lines.Add("PRODUCTVERSION " + Numbers(product));
            lines.Add("FILEFLAGSMASK 0x3F");                                 // VS_FFI_FILEFLAGSMASK
            lines.Add("FILEFLAGS " + (debugInfo ? "0x1" : "0"));             // VS_FF_DEBUG
            lines.Add("FILEOS 0x40004");                                     // VOS_NT_WINDOWS32
            lines.Add("FILETYPE " + (project.AppType == "library" ? "2" : "1"));   // VFT_DLL / VFT_APP
            lines.Add("BEGIN");
            lines.Add("  BLOCK \"StringFileInfo\"");
            lines.Add("  BEGIN");
            lines.Add("    BLOCK \"040904B0\"");                             // U.S. English, Unicode
            lines.Add("    BEGIN");
            foreach (var (key, value) in new[]
                     {
                         ("CompanyName", info.CompanyName), ("FileDescription", info.FileDescription), ("FileVersion", Dotted(file)),
                         ("InternalName", project.Name), ("LegalCopyright", info.LegalCopyright), ("OriginalFilename", outputFileName),
                         ("ProductName", info.ProductName), ("ProductVersion", Dotted(product)), ("Comments", info.Comments),
                     })
            {
                lines.Add($"      VALUE \"{key}\", {Quote(value ?? "")}");
            }

            lines.Add("    END");
            lines.Add("  END");
            lines.Add("  BLOCK \"VarFileInfo\"");
            lines.Add("  BEGIN");
            lines.Add("    VALUE \"Translation\", 0x409, 1200");
            lines.Add("  END");
            lines.Add("END");
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder();
        text.Append("// Generated from ").Append(Path.GetFileName(project.FilePath)).Append(" by the build; changes are overwritten.\n");
        foreach (var line in lines)
        {
            text.Append(line).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// A string of the script: <c>\</c> and <c>"</c> doubled (CMP-1430); line feed, carriage return and tab written as <c>\n</c>,
    /// <c>\r</c>, <c>\t</c>; other control characters left out (a string of the script cannot hold them).
    /// </summary>
    private static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    text.Append(@"\\");
                    break;
                case '"':
                    text.Append("\"\"");
                    break;
                case '\n':
                    text.Append(@"\n");
                    break;
                case '\r':
                    text.Append(@"\r");
                    break;
                case '\t':
                    text.Append(@"\t");
                    break;
                default:
                    if (!char.IsControl(c))
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        return text.Append('"').ToString();
    }

    /// <summary>The four numbers of a version (missing parts 0), or null when it is not of the form of <see cref="IsValidVersion"/>.</summary>
    private static ushort[]? ParseVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var parts = version.Split('.');
        if (parts.Length > 4)
        {
            return null;
        }

        var numbers = new ushort[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (Number(parts[i]) is not (>= 0 and <= ushort.MaxValue) || parts[i].Length > 5)
            {
                return null;
            }

            numbers[i] = (ushort)Number(parts[i]);
        }

        return numbers;
    }

    /// <summary>The value of a run of ASCII digits (at most nine, so that it cannot overflow); -1 for anything else.</summary>
    private static int Number(string digits) =>
        digits.Length is >= 1 and <= 9 && digits.All(char.IsAsciiDigit) ? int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) : -1;
}
