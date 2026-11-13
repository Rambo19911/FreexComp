using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Models;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// One fixed tool group (a dock, or a half of the right dock): its visible tabs in profile order and the active tab.
/// Empty = collapsed (UI-WIN-004). Maintained by <see cref="LayoutViewModel"/>.
/// </summary>
public sealed class ToolGroupViewModel : ObservableObject
{
    private readonly LayoutViewModel _owner;
    private IReadOnlyList<string> _tools = [];
    private string? _activeTool;

    internal ToolGroupViewModel(LayoutViewModel owner, ToolGroupId id)
    {
        _owner = owner;
        Id = id;
    }

    public ToolGroupId Id { get; }

    /// <summary>Visible, registered tool windows, left to right.</summary>
    public IReadOnlyList<string> Tools
    {
        get => _tools;
        private set
        {
            if (SetProperty(ref _tools, value))
            {
                OnPropertyChanged(nameof(IsCollapsed));
            }
        }
    }

    public string? ActiveTool
    {
        get => _activeTool;
        private set => SetProperty(ref _activeTool, value);
    }

    public bool IsCollapsed => Tools.Count == 0;

    /// <summary>Selects the tab of <paramref name="tool"/> (a tab click); false when it is not shown in this group.</summary>
    public bool Activate(string tool) => _owner.Activate(Id, tool);

    internal void Update(IReadOnlyList<string> tools, string? active)
    {
        if (!tools.SequenceEqual(_tools, StringComparer.Ordinal))
        {
            Tools = tools;
        }

        ActiveTool = active;
    }
}
