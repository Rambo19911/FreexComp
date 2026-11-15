using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.ToolWindows;

/// <summary>
/// A tool window as registered with <see cref="IToolWindowService"/> (architecture.md §11). <paramref name="Name"/> is
/// the <c>Tool.&lt;Name&gt;</c> name, <paramref name="TitleKey"/> the <c>Strings.resx</c> key of caption and tab text,
/// <paramref name="HomeDock"/> the group it joins when shown in a profile that does not place it (UI-WIN-013),
/// <paramref name="Phase"/> its first phase (UI-TW-006), <paramref name="Scope"/> the command scope of its content
/// (commands.md §1: <c>ProjectManager</c> for the Project Manager, <c>ToolList</c> otherwise).
/// </summary>
public sealed record ToolWindowDefinition(
    string Name,
    string TitleKey,
    ToolGroupId HomeDock,
    int Phase,
    Func<ToolWindowViewModel> Create,
    CommandScope Scope = CommandScope.ToolList);

/// <summary>A button of a tool window's tool bar (UI-TW-008): a command and its 16 px icon.</summary>
public sealed record ToolWindowToolBarItem(string CommandId, string IconName);

/// <summary>One entry of a <see cref="ToolWindowToolBarChoice"/>; shown as <see cref="Label"/>.</summary>
public sealed record ToolBarChoiceItem(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// A labelled combo box at the left end of a tool window's tool bar (the Output channel, UI-TW-501), drawn before the
/// buttons. AutomationId <c>Tool.&lt;Name&gt;.&lt;<see cref="Name"/>&gt;</c>.
/// </summary>
public sealed class ToolWindowToolBarChoice(string name, string label, double width, IReadOnlyList<ToolBarChoiceItem> items)
    : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private int _selectedIndex;

    public string Name { get; } = name;

    public string Label { get; } = label;

    public double Width { get; } = width;

    public IReadOnlyList<ToolBarChoiceItem> Items { get; } = items;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, Math.Clamp(value, 0, Items.Count - 1));
    }

    public string SelectedId => Items[SelectedIndex].Id;
}
