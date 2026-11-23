using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FreexComp.Ide.Core.Commands;

/// <summary>
/// Bindable wrapper of one command (IDE-ARCH-041). Menus, toolbars and the palette bind to these only.
/// Created and refreshed by <see cref="CommandService"/>.
/// </summary>
public sealed class IdeCommand : ObservableObject, ICommand
{
    private readonly CommandService _owner;
    private string _title;
    private string _gestureText;
    private bool _isChecked;

    internal IdeCommand(CommandService owner, CommandDefinition definition, string title, string gestureText)
    {
        _owner = owner;
        Definition = definition;
        _title = title;
        _gestureText = gestureText;
    }

    public event EventHandler? CanExecuteChanged;

    public CommandDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Title
    {
        get => _title;
        internal set => SetProperty(ref _title, value);
    }

    /// <summary>The displayed (first) gesture, empty when unbound (UI-KEY-005).</summary>
    /// <summary>The main-menu label that replaces the menu's own while it applies (menus.md §7 "label Continue while paused"); null otherwise.</summary>
    public string? MenuLabel
    {
        get => _menuLabel;
        internal set => SetProperty(ref _menuLabel, value);
    }

    private string? _menuLabel;

    public string GestureText
    {
        get => _gestureText;
        internal set => SetProperty(ref _gestureText, value);
    }

    /// <summary>The toolbar icon of the command, null when it has no toolbar button (single source: <see cref="Menus.ToolBarDefinitions.IconOf"/>).</summary>
    public string? IconName => Menus.ToolBarDefinitions.IconOf(Id);

    public bool IsChecked
    {
        get => _isChecked;
        internal set => SetProperty(ref _isChecked, value);
    }

    public bool IsVisible => true;

    public bool CanExecute(object? parameter) => _owner.CanExecute(Id, parameter as string);

    public void Execute(object? parameter) => _ = _owner.ExecuteAsync(Id, parameter as string);

    internal void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
