using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Composition;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// The main window (main-window.md §1): one property per region view-model. Regions not listed here (menu bar,
/// toolbar row, docks, status-bar fields) arrive with their tasks (I-019, I-021, I-022, I-027).
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(IdeServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        DocumentArea = new DocumentAreaViewModel(services.Commands, services.Keymap);
    }

    /// <summary>UI-WIN-030; the project/state format arrives with I-028.</summary>
    public string Title => Strings.App_ProductName;

    public DocumentAreaViewModel DocumentArea { get; }
}
