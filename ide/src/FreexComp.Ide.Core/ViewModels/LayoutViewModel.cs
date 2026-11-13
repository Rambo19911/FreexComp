using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Models;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Regions 4–7 of the main window (main-window.md §1–§2): the document area and the four fixed tool groups, arranged
/// by the active profile of <see cref="State"/>. A profile silently skips tool windows that are not registered
/// (UI-WIN-011); a group without visible tool windows is collapsed (UI-WIN-004) and keeps its size for when it returns.
/// Owned by the tool-window service, which resolves names to view-models (null = not registered).
/// </summary>
public sealed class LayoutViewModel : ObservableObject
{
    private readonly Func<string, ToolWindowViewModel?> _resolve;
    private object? _documentArea;

    public LayoutViewModel(LayoutState state, Func<string, ToolWindowViewModel?> resolve)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(resolve);
        State = state;
        _resolve = resolve;
        Left = new ToolGroupViewModel(this, ToolGroupId.Left);
        RightUpper = new ToolGroupViewModel(this, ToolGroupId.RightUpper);
        RightLower = new ToolGroupViewModel(this, ToolGroupId.RightLower);
        Bottom = new ToolGroupViewModel(this, ToolGroupId.Bottom);
        foreach (var group in Groups)
        {
            group.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ToolGroupViewModel.IsCollapsed))
                {
                    OnPropertyChanged(nameof(IsRightCollapsed));
                }
            };
        }

        Refresh();
    }

    public LayoutState State { get; }

    /// <summary>Region 5 (UI-WIN-002): always present; set by the main window's view-model.</summary>
    public object? DocumentArea
    {
        get => _documentArea;
        set => SetProperty(ref _documentArea, value);
    }

    public string ActiveProfile => State.ActiveProfile;

    public ToolGroupViewModel Left { get; }

    public ToolGroupViewModel RightUpper { get; }

    public ToolGroupViewModel RightLower { get; }

    public ToolGroupViewModel Bottom { get; }

    public IReadOnlyList<ToolGroupViewModel> Groups => [Left, RightUpper, RightLower, Bottom];

    /// <summary>The right dock collapses when both of its groups are empty.</summary>
    public bool IsRightCollapsed => RightUpper.IsCollapsed && RightLower.IsCollapsed;

    public double LeftSize
    {
        get => Profile.LeftSize;
        set => SetSize(Profile.LeftSize, value, v => Profile.LeftSize = v);
    }

    public double RightSize
    {
        get => Profile.RightSize;
        set => SetSize(Profile.RightSize, value, v => Profile.RightSize = v);
    }

    public double BottomSize
    {
        get => Profile.BottomSize;
        set => SetSize(Profile.BottomSize, value, v => Profile.BottomSize = v);
    }

    public double RightSplit
    {
        get => Profile.RightSplit;
        set => SetSize(Profile.RightSplit, value, v => Profile.RightSplit = v);
    }

    private ProfileLayout Profile => State.Profiles[State.ActiveProfile];

    public ToolGroupViewModel Group(ToolGroupId id) => Groups[(int)id];

    /// <summary>
    /// Moves <paramref name="tool"/> between the visible and hidden members of its group in the active profile (UI-TW-005).
    /// Showing makes it the active tab. False when the profile does not place the tool window (home docks: I-023).
    /// </summary>
    public bool SetVisible(string tool, bool visible)
    {
        foreach (var id in Enum.GetValues<ToolGroupId>())
        {
            var group = Profile.Group(id);
            var (from, to) = visible ? (group.Hidden, group.Tools) : (group.Tools, group.Hidden);
            if (to.Contains(tool, StringComparer.Ordinal))
            {
                if (visible)
                {
                    group.Active = tool;
                    Refresh();
                }

                return true;
            }

            if (from.Remove(tool))
            {
                if (visible)
                {
                    InsertInProfileOrder(id, tool);
                    group.Active = tool;
                }
                else
                {
                    group.Hidden.Add(tool);
                }

                Refresh();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// UI-WIN-013: adds <paramref name="tool"/>, which the active profile does not place, at the end of
    /// <paramref name="home"/> in that profile, visible and active.
    /// </summary>
    public void Place(string tool, ToolGroupId home)
    {
        var group = Profile.Group(home);
        group.Tools.Add(tool);
        group.Active = tool;
        Refresh();
    }

    /// <summary>Recomputes every group from the profile, e.g. after a tool window was registered.</summary>
    public void Refresh()
    {
        foreach (var group in Groups)
        {
            var layout = Profile.Group(group.Id);
            var windows = layout.Tools.Select(_resolve).OfType<ToolWindowViewModel>().ToList();
            var active = windows.FirstOrDefault(w => w.Name == layout.Active) ?? windows.FirstOrDefault();
            group.Update(windows, active);
        }
    }

    internal bool Activate(ToolGroupId id, string tool)
    {
        if (!Group(id).Tools.Contains(tool, StringComparer.Ordinal))
        {
            return false;
        }

        Profile.Group(id).Active = tool;
        Refresh();
        return true;
    }

    /// <summary>
    /// A shown tool window goes back to its built-in tab position: before the first visible tool that the built-in
    /// profile places after it; at the end when the built-in profile does not order it.
    /// </summary>
    private void InsertInProfileOrder(ToolGroupId id, string tool)
    {
        var builtIn = LayoutProfiles.Create(State.ActiveProfile).Group(id);
        List<string> order = [.. builtIn.Tools, .. builtIn.Hidden];
        var tools = Profile.Group(id).Tools;
        var rank = order.IndexOf(tool);
        var index = rank < 0 ? -1 : tools.FindIndex(t => order.IndexOf(t) > rank);
        tools.Insert(index < 0 ? tools.Count : index, tool);
    }

    private void SetSize(double current, double value, Action<double> set, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (current != value)
        {
            set(value);
            OnPropertyChanged(property);
        }
    }
}
