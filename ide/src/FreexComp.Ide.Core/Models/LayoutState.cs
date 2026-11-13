namespace FreexComp.Ide.Core.Models;

/// <summary>The four fixed tool groups of main-window.md §1 (the right dock holds two).</summary>
public enum ToolGroupId
{
    Left,
    RightUpper,
    RightLower,
    Bottom,
}

/// <summary>The four draggable splitters of the main window (UI-WIN-005).</summary>
public enum DockSplitter
{
    Left,
    Right,
    Bottom,

    /// <summary>Between the upper and the lower group of the right dock.</summary>
    RightSplit,
}

/// <summary>One tool group of a profile (IDE-ARCH-061): visible tabs in order, hidden members in order, active tab.</summary>
public sealed class ToolGroupLayout
{
    public List<string> Tools { get; init; } = [];

    public List<string> Hidden { get; init; } = [];

    public string? Active { get; set; }

    public ToolGroupLayout Clone() => new() { Tools = [.. Tools], Hidden = [.. Hidden], Active = Active };
}

/// <summary>A layout profile (main-window.md §2): per tool group its tools, plus the dock sizes in DIP.</summary>
public sealed class ProfileLayout
{
    public double LeftSize { get; set; } = LayoutProfiles.DefaultLeftSize;

    public double RightSize { get; set; } = LayoutProfiles.DefaultRightSize;

    public double BottomSize { get; set; } = LayoutProfiles.DefaultBottomSize;

    /// <summary>Share of the right dock's height given to the upper group.</summary>
    public double RightSplit { get; set; } = LayoutProfiles.DefaultRightSplit;

    public ToolGroupLayout Left { get; init; } = new();

    public ToolGroupLayout RightUpper { get; init; } = new();

    public ToolGroupLayout RightLower { get; init; } = new();

    public ToolGroupLayout Bottom { get; init; } = new();

    public ToolGroupLayout Group(ToolGroupId id) => id switch
    {
        ToolGroupId.Left => Left,
        ToolGroupId.RightUpper => RightUpper,
        ToolGroupId.RightLower => RightLower,
        _ => Bottom,
    };
}

/// <summary>
/// The tool-window arrangement of <c>uistate.json</c> (architecture.md §8.1, UI-WIN-007): the active profile and every
/// profile's modifications (UI-WIN-013). Persistence is task I-025.
/// </summary>
public sealed class LayoutState
{
    public string ActiveProfile { get; set; } = LayoutProfiles.Edit;

    public Dictionary<string, ProfileLayout> Profiles { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The built-in profiles as defined (main-window.md §2), <c>Edit</c> active.</summary>
    public static LayoutState CreateDefault()
    {
        var state = new LayoutState();
        foreach (var name in LayoutProfiles.BuiltIn)
        {
            state.Profiles[name] = LayoutProfiles.Create(name);
        }

        return state;
    }
}

/// <summary>The built-in layout profiles of main-window.md §2. Tool names are the <c>Tool.&lt;Name&gt;</c> names.</summary>
public static class LayoutProfiles
{
    public const string Edit = "Edit";

    public const double DefaultLeftSize = 260;
    public const double DefaultRightSize = 300;
    public const double DefaultBottomSize = 200;
    public const double DefaultRightSplit = 0.5;

    /// <summary>Profiles that exist so far; <c>Design</c> (I-130) and <c>Debug</c> (P6) are added with their tasks.</summary>
    public static readonly IReadOnlyList<string> BuiltIn = [Edit];

    public static ProfileLayout Create(string name) => name switch
    {
        Edit => new ProfileLayout
        {
            Left = new() { Tools = ["ProjectManager", "Structure"], Hidden = ["DataExplorer"], Active = "ProjectManager" },
            Bottom = new() { Tools = ["Messages", "Output", "SearchResults"], Hidden = ["ToDo"], Active = "Messages" },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown layout profile."),
    };
}
