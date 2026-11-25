using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Menus;

/// <summary>One generated entry of a dynamic list: the provider's command with <paramref name="Argument"/>.</summary>
public sealed record DynamicMenuEntry(string Label, string Argument, string? ToolTip)
{
    /// <summary>An entry of an exclusive group (UI-MENU-007): whether it carries the bullet; null = no bullet group.</summary>
    public bool? IsChecked { get; init; }
}

/// <summary>The entries of the <see cref="DynamicListNode"/>s (UI-MENU-002, UI-MENU-009).</summary>
public interface IDynamicMenuSource
{
    /// <summary>At most <paramref name="max"/> entries of <paramref name="providerId"/>; empty for providers it does not know.</summary>
    IReadOnlyList<DynamicMenuEntry> GetEntries(string providerId, int max);

    /// <summary>Some list changed; the menus that hold dynamic lists are regenerated.</summary>
    event EventHandler? Changed;
}

/// <summary>File › Open Recent (IDE-P1-131): recent projects and files, labels shortened in the middle (UI-MENU-009).</summary>
public sealed class RecentMenuSource : IDynamicMenuSource
{
    public const int MaxLabelLength = 60;
    private readonly IRecentItemsService _recent;

    public RecentMenuSource(IRecentItemsService recent)
    {
        ArgumentNullException.ThrowIfNull(recent);
        _recent = recent;
        recent.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<DynamicMenuEntry> GetEntries(string providerId, int max)
    {
        var items = providerId switch
        {
            "file.openRecentProject" => _recent.Projects,
            "file.openRecentFile" => _recent.Files,
            _ => [],
        };
        return [.. items.Take(max).Select(i => new DynamicMenuEntry(ShortenPath(i.Path), i.Path, i.Path))];
    }

    /// <summary>
    /// UI-MENU-009: a path longer than <paramref name="max"/> keeps its root and first folder, then <c>…</c>, then as many
    /// trailing segments as fit (<c>C:\Users\…\Hello\Hello.pproj</c>); a file name too long for that keeps its end.
    /// </summary>
    public static string ShortenPath(string path, int max = MaxLabelLength)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length <= max)
            return path;
        var parts = path.Split('\\');
        if (parts.Length >= 3)
        {
            var head = parts[0] + "\\" + parts[1] + "\\…";
            var tail = parts[^1];
            if (head.Length + 1 + tail.Length <= max)
            {
                for (var i = parts.Length - 2; i >= 2 && head.Length + 1 + parts[i].Length + 1 + tail.Length <= max; i--)
                    tail = parts[i] + "\\" + tail;
                return head + "\\" + tail;
            }
        }

        return "…" + path[^(max - 1)..];
    }
}

/// <summary>
/// Project › Active Configuration and Project › Active Platform (IDE-PM-090, IDE-PLAT-004): one entry per configuration
/// and per listed platform of the open project in file order, the active one with the bullet. Platform entries are
/// labelled per UI-TB-022 and carry the platform id as their argument.
/// </summary>
public sealed class ProjectMenuSource : IDynamicMenuSource
{
    private readonly Projects.IProjectService _projects;

    public ProjectMenuSource(Projects.IProjectService projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        _projects = projects;
        projects.ProjectChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<DynamicMenuEntry> GetEntries(string providerId, int max)
    {
        if (_projects.Project is not { } project)
            return [];
        return providerId switch
        {
            "project.setConfiguration" =>
                [.. project.Configurations.Take(max).Select(c => new DynamicMenuEntry(c, c, null) { IsChecked = c == project.ActiveConfiguration })],
            "project.setPlatform" =>
            [
                .. project.Platforms.Take(max).Select(p =>
                    new DynamicMenuEntry(Projects.PlatformLabels.Label(p, _projects.PlatformCatalog), p, null) { IsChecked = p == project.ActivePlatform }),
            ],
            _ => [],
        };
    }
}

/// <summary>Several sources as one: the first that knows a provider supplies its entries.</summary>
public sealed class CompositeMenuSource : IDynamicMenuSource
{
    private readonly IReadOnlyList<IDynamicMenuSource> _sources;

    public CompositeMenuSource(params IDynamicMenuSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;
        foreach (var source in sources)
            source.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<DynamicMenuEntry> GetEntries(string providerId, int max) =>
        _sources.Select(s => s.GetEntries(providerId, max)).FirstOrDefault(e => e.Count > 0) ?? [];
}
