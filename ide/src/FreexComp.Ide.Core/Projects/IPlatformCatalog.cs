using FreexComp.Compiler.Targets;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Projects;

/// <summary>One installed platform (IDE-PLAT-001): its id, the name shown to the user and the compiler's descriptor.</summary>
public sealed record PlatformDescriptor(string Id, string DisplayName, TargetInfo Target);

/// <summary>
/// The platforms this installation can build for (platform-selector.md IDE-PLAT-001). A project may list a platform
/// that is not in the catalogue: it is shown with the suffix of UI-TB-022 and cannot be built here.
/// </summary>
public interface IPlatformCatalog
{
    /// <summary>The installed platforms in their fixed order.</summary>
    IReadOnlyList<PlatformDescriptor> Installed { get; }

    /// <summary>The installed platform with that id; null when it is not installed.</summary>
    PlatformDescriptor? Find(string id);
}

/// <summary>The product's catalogue: the compiler's target registry — the only IDE code that names it (IDE-PLAT-001).</summary>
public sealed class BuiltInPlatformCatalog : IPlatformCatalog
{
    public static BuiltInPlatformCatalog Instance { get; } = new();

    public IReadOnlyList<PlatformDescriptor> Installed { get; } =
        [.. TargetRegistry.BuiltIn.Select(t => new PlatformDescriptor(t.Id, t.DisplayName, t))];

    public PlatformDescriptor? Find(string id) => Installed.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    /// <summary>The descriptor the code model falls back to for a platform that is not installed (IDE-PLAT-007).</summary>
    public static TargetInfo Baseline => TargetRegistry.Default;
}

/// <summary>The texts a platform is shown with (UI-TB-022, IDE-PLAT-011).</summary>
public static class PlatformLabels
{
    /// <summary>
    /// UI-TB-022: the id with its first letter upper-cased (<c>win64</c> → <c>Win64</c>), followed by
    /// <c> (not installed)</c> when the catalogue does not have the platform.
    /// </summary>
    public static string Label(string id, IPlatformCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Find(id) is null ? Short(id) + " " + Strings.Platform_NotInstalledSuffix : Short(id);
    }

    /// <summary><c>win64</c> → <c>Win64</c> (main-window.md R9).</summary>
    public static string Short(string id) => string.IsNullOrEmpty(id) ? id : char.ToUpperInvariant(id[0]) + id[1..];
}
