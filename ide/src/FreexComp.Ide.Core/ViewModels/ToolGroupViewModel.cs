using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Models;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// One fixed tool group (a dock, or a half of the right dock): its visible tool windows in profile order and the active
/// tab. Empty = collapsed (UI-WIN-004). Maintained by <see cref="LayoutViewModel"/>.
/// </summary>
public sealed class ToolGroupViewModel : ObservableObject
{
    private readonly LayoutViewModel _owner;
    private IReadOnlyList<ToolWindowViewModel> _windows = [];
    private ToolWindowViewModel? _activeWindow;

    internal ToolGroupViewModel(LayoutViewModel owner, ToolGroupId id)
    {
        _owner = owner;
        Id = id;
    }

    public ToolGroupId Id { get; }

    /// <summary>Visible, registered tool windows, left to right (the tabs).</summary>
    public IReadOnlyList<ToolWindowViewModel> Windows
    {
        get => _windows;
        private set
        {
            if (SetProperty(ref _windows, value))
            {
                OnPropertyChanged(nameof(Tools));
                OnPropertyChanged(nameof(IsCollapsed));
            }
        }
    }

    /// <summary>Names of <see cref="Windows"/>.</summary>
    public IReadOnlyList<string> Tools => [.. _windows.Select(w => w.Name)];

    public ToolWindowViewModel? ActiveWindow
    {
        get => _activeWindow;
        private set
        {
            if (SetProperty(ref _activeWindow, value))
            {
                OnPropertyChanged(nameof(ActiveTool));
            }
        }
    }

    public string? ActiveTool => _activeWindow?.Name;

    public bool IsCollapsed => _windows.Count == 0;

    /// <summary>Selects the tab of <paramref name="tool"/> (a tab click); false when it is not shown in this group.</summary>
    public bool Activate(string tool) => _owner.Activate(Id, tool);

    internal void Update(IReadOnlyList<ToolWindowViewModel> windows, ToolWindowViewModel? active)
    {
        if (!windows.SequenceEqual(_windows))
        {
            Windows = windows;
        }

        ActiveWindow = active;
    }
}
