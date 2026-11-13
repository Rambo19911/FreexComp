namespace FreexComp.Ide.Core.Menus;

/// <summary>A node of a menu definition (UI-MENU-002).</summary>
public abstract record MenuNode;

/// <summary>An item that runs <paramref name="CommandId"/>; <paramref name="Exclusive"/> marks a bullet group (UI-MENU-007).</summary>
public sealed record CommandMenuNode(string CommandId, string? Argument = null, bool Exclusive = false) : MenuNode;

/// <summary>A submenu; <paramref name="Key"/> is the AutomationId without <c>Menu.</c> (<c>File.OpenRecent</c>).</summary>
public sealed record SubmenuNode(string Key, IReadOnlyList<MenuNode> Children) : MenuNode;

public sealed record SeparatorNode : MenuNode
{
    public static readonly SeparatorNode Instance = new();
}

/// <summary>
/// Entries supplied at run time (recent items, documents, configurations), at most <paramref name="Max"/>
/// (UI-MENU-009); each runs <paramref name="ProviderId"/> with an argument. No provider exists before its task, so the
/// list is empty.
/// </summary>
public sealed record DynamicListNode(string ProviderId, int Max) : MenuNode;
