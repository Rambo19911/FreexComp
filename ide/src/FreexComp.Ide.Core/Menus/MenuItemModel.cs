using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;

namespace FreexComp.Ide.Core.Menus;

public enum MenuToggleKind
{
    None,

    /// <summary>Check mark when on (toggle commands).</summary>
    Check,

    /// <summary>Bullet on the active entry of an exclusive group.</summary>
    Radio,
}

/// <summary>
/// One generated menu entry: a command item, a submenu or a separator. Command items take label-independent state
/// (enabled, checked, gesture) from their <see cref="IdeCommand"/>; a submenu is enabled when a child is (UI-MENU-011),
/// a top-level menu always (UI-MENU-001).
/// </summary>
public sealed class MenuItemModel : ObservableObject
{
    private readonly bool _alwaysEnabled;

    private IReadOnlyList<MenuItemModel> _children = [];

    private MenuItemModel(string? key, string label, IdeCommand? command, string? argument, MenuToggleKind toggleKind,
        IReadOnlyList<MenuItemModel> children, bool isSeparator, bool alwaysEnabled, string? toolTip = null)
    {
        Key = key;
        Label = label;
        Command = command;
        Argument = argument;
        ToggleKind = toggleKind;
        IsSeparator = isSeparator;
        ToolTip = toolTip;
        _alwaysEnabled = alwaysEnabled;
        SetChildren(children);
    }

    /// <summary>
    /// Replaces the children (a submenu with dynamic lists, UI-MENU-009); raises <see cref="Children"/> and
    /// <see cref="IsEnabled"/> changes.
    /// </summary>
    internal void SetChildren(IReadOnlyList<MenuItemModel> children)
    {
        foreach (var child in _children)
        {
            if (child.Command is { } childCommand)
                childCommand.CanExecuteChanged -= OnChildCanExecuteChanged;
            else if (child.IsSubmenu)
                child.PropertyChanged -= OnChildPropertyChanged;
        }

        _children = children;
        foreach (var child in children)
        {
            if (child.Command is { } childCommand)
                childCommand.CanExecuteChanged += OnChildCanExecuteChanged;
            else if (child.IsSubmenu)
                child.PropertyChanged += OnChildPropertyChanged;
        }

        OnPropertyChanged(nameof(Children));
        OnPropertyChanged(nameof(IsEnabled));
    }

    private void OnChildCanExecuteChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(IsEnabled));

    private void OnChildPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsEnabled))
            OnPropertyChanged(nameof(IsEnabled));
    }

    /// <summary>Submenu key (<c>File.OpenRecent</c>); null for commands and separators.</summary>
    public string? Key { get; }

    /// <summary>Label with <c>&amp;</c> before the mnemonic; empty for separators.</summary>
    public string Label { get; }

    public IdeCommand? Command { get; }

    public string? CommandId => Command?.Id;

    /// <summary>UI-MENU-008: the toolbar glyph of the command, or null when it has no toolbar button.</summary>
    public string? IconName => Command?.IconName;

    public string? Argument { get; }

    public MenuToggleKind ToggleKind { get; }

    public IReadOnlyList<MenuItemModel> Children => _children;

    /// <summary>Tooltip of a generated entry (the full path of a recent item, UI-MENU-009); null otherwise.</summary>
    public string? ToolTip { get; }

    public bool IsSeparator { get; }

    public bool IsSubmenu => Key is not null;

    /// <summary>
    /// Id of an action item (a context-menu entry that runs <see cref="Run"/> instead of a command, e.g. the Welcome
    /// row's Pin/Unpin); its element id is <c>Context.&lt;ActionId&gt;</c>. Null for command items.
    /// </summary>
    public string? ActionId { get; private init; }

    /// <summary>The action of an action item (always enabled); null for command items.</summary>
    public Action? Run { get; private init; }

    /// <summary>An action item with a check mark: whether it is checked when the menu is built; null = no check mark.</summary>
    public bool? IsCheckedAction { get; private init; }

    public bool IsEnabled => Run is not null || _alwaysEnabled
        || (Command is not null ? Command.CanExecute(Argument) : Children.Any(c => !c.IsSeparator && c.IsEnabled));

    internal static MenuItemModel Separator() => new(null, "", null, null, MenuToggleKind.None, [], true, false);

    internal static MenuItemModel ForCommand(string label, IdeCommand command, string? argument, MenuToggleKind toggleKind, string? toolTip = null) =>
        new(null, label, command, argument, toggleKind, [], false, false, toolTip);

    /// <summary>An item that runs <paramref name="run"/> (no command; IDE-WEL-008 Pin/Unpin, Remove from List).</summary>
    internal static MenuItemModel ForAction(string actionId, string label, Action run, bool? isChecked = null) =>
        new(null, label, null, null, isChecked is null ? MenuToggleKind.None : MenuToggleKind.Check, [], false, false)
        { ActionId = actionId, Run = run, IsCheckedAction = isChecked };

    internal static MenuItemModel ForSubmenu(string key, string label, IReadOnlyList<MenuItemModel> children, bool alwaysEnabled) =>
        new(key, label, null, null, MenuToggleKind.None, children, false, alwaysEnabled);
}
