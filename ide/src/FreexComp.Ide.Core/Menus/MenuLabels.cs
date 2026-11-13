using System.Globalization;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Menus;

/// <summary>Menu labels (UI-MENU-003): resources with <c>&amp;</c> before the mnemonic letter.</summary>
public static class MenuLabels
{
    /// <summary><c>Menu_&lt;SubmenuKey&gt;</c> or <c>Menu_&lt;commandId&gt;[_&lt;argument&gt;]</c>, dots and colons as underscores.</summary>
    public static string ResourceName(MenuNode node) => node switch
    {
        SubmenuNode s => "Menu_" + Mangle(s.Key),
        CommandMenuNode c => "Menu_" + Mangle(c.CommandId) + (c.Argument is null ? "" : "_" + Mangle(c.Argument)),
        _ => throw new ArgumentException("Only commands and submenus have labels.", nameof(node)),
    };

    /// <exception cref="InvalidOperationException">The resource is missing.</exception>
    public static string For(MenuNode node)
    {
        var name = ResourceName(node);
        return Strings.ResourceManager.GetString(name, CultureInfo.CurrentCulture)
            ?? throw new InvalidOperationException($"Missing menu label resource {name}.");
    }

    /// <summary>The upper-case mnemonic letter (the character after the single <c>&amp;</c>), or null.</summary>
    public static char? Mnemonic(string label)
    {
        var index = label.IndexOf('&', StringComparison.Ordinal);
        return index >= 0 && index + 1 < label.Length && label.IndexOf('&', index + 1) < 0 && char.IsLetterOrDigit(label[index + 1])
            ? char.ToUpperInvariant(label[index + 1])
            : null;
    }

    private static string Mangle(string text) => text.Replace('.', '_').Replace(':', '_');
}
