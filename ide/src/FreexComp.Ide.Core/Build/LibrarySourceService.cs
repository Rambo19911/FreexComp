using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Build;

/// <summary>
/// IDE-LIB-001: the one place that says what library source is — the sources of the toolchain's libraries (run-time
/// library, UI framework, data access) installed with the product.
/// </summary>
public interface ILibrarySourceService
{
    /// <summary>
    /// The library root directories: the existing values of <see cref="ToolchainPaths.RtlDir"/>,
    /// <see cref="ToolchainPaths.FrameworkDir"/> and <see cref="ToolchainPaths.DataDir"/>, then the Win32 binding
    /// directory <c>&lt;RtlDir&gt;/../winapi/src</c> when it exists on disk (I-259) — normalised, in this order.
    /// Read from the toolchain locator on every call, so a changed toolchain setting is seen at once.
    /// </summary>
    IReadOnlyList<string> Roots { get; }

    /// <summary>True iff the normalised <paramref name="path"/> lies under a root (compared ignoring case).</summary>
    bool IsLibrarySource(string path);
}

/// <inheritdoc cref="ILibrarySourceService"/>
public sealed class LibrarySourceService(IToolchainLocator toolchain, IFileSystem fileSystem) : ILibrarySourceService
{
    public IReadOnlyList<string> Roots
    {
        get
        {
            var paths = toolchain.Resolve();
            var winApi = paths.RtlDir is { Length: > 0 } rtl ? PathUtil.Normalize(Path.Combine(rtl, "..", "winapi", "src")) : null;
            if (winApi is not null && !fileSystem.DirectoryExists(winApi))
                winApi = null;
            return [.. new[] { paths.RtlDir, paths.FrameworkDir, paths.DataDir, winApi }.OfType<string>()
                .Select(dir => PathUtil.Normalize(dir).TrimEnd('\\'))
                .Where(dir => dir.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    public bool IsLibrarySource(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = PathUtil.Normalize(path);
        return Roots.Any(root => normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));
    }
}
