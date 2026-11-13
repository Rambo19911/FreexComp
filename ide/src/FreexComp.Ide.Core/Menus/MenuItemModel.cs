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

    private MenuItemModel(string? key, string label, IdeCommand? command, string? argument, MenuToggleKind toggleKind,
        IReadOnlyList<MenuItemModel> children, bool isSeparator, bool alwaysEnabled)
    {
        Key = key;
        Label = label;
        Command = command;
        Argument = argument;
        ToggleKind = toggleKind;
        Children = children;
        IsSeparator = isSeparator;
        _alwaysEnabled = alwaysEnabled;

        foreach (var child in children)
        {
            if (child.Command is { } childCommand)
            {
                childCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsEnabled));
            }
            else if (child.IsSubmenu)
            {
                child.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(IsEnabled))
                    {
                        OnPropertyChanged(nameof(IsEnabled));
                    }
                };
            }
        }
    }

    /// <summary>Submenu key (<c>File.OpenRecent</c>); null for commands and separators.</summary>
    public string? Key { get; }

    /// <summary>Label with <c>&amp;</c> before the mnemonic; empty for separators.</summary>
    public string Label { get; }

    public IdeCommand? Command { get; }

    public string? CommandId => Command?.Id;

    /// <summary>UI-MENU-008: the toolbar glyph of the command, or null when it has no toolbar button.</summary>
    public string? IconName => CommandId is { } id ? ToolBarDefinitions.IconOf(id) : null;

    public string? Argument { get; }

    public MenuToggleKind ToggleKind { get; }

    public IReadOnlyList<MenuItemModel> Children { get; }

    public bool IsSeparator { get; }

    public bool IsSubmenu => Key is not null;

    public bool IsEnabled => _alwaysEnabled
        || (Command is not null ? Command.CanExecute(Argument) : Children.Any(c => !c.IsSeparator && c.IsEnabled));

    internal static MenuItemModel Separator() => new(null, "", null, null, MenuToggleKind.None, [], true, false);

    internal static MenuItemModel ForCommand(string label, IdeCommand command, string? argument, MenuToggleKind toggleKind) =>
        new(null, label, command, argument, toggleKind, [], false, false);

    internal static MenuItemModel ForSubmenu(string key, string label, IReadOnlyList<MenuItemModel> children, bool alwaysEnabled) =>
        new(key, label, null, null, MenuToggleKind.None, children, false, alwaysEnabled);
}
