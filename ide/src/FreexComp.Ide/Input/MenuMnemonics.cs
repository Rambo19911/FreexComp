using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace FreexComp.Ide.Input;

/// <summary>
/// UI-A11Y-002 inside an open menu level: a plain letter or digit selects the item of that level whose mnemonic it is —
/// a submenu opens, a command runs and the menu closes. (The toolkit matches access keys across all levels at once, so
/// a submenu's mnemonic that is also a top-level one would open the top-level menu instead.)
/// </summary>
public static class MenuMnemonics
{
    /// <summary>True when <paramref name="focused"/> is a menu item and <paramref name="symbol"/> chose one of its level.</summary>
    public static bool Handle(MenuItem focused, string? symbol, Menu menuBar)
    {
        if (symbol is not [var c] || !char.IsLetterOrDigit(c))
        {
            return false;
        }

        var level = focused.Parent switch
        {
            MenuItem parent => parent.Items.OfType<MenuItem>(),
            Menu menu => menu.Items.OfType<MenuItem>(),
            _ => [],
        };
        var match = level.FirstOrDefault(i => i.IsEffectivelyEnabled && i.IsVisible && MnemonicOf(i) is { } m && char.ToUpperInvariant(m) == char.ToUpperInvariant(c));
        if (match is null)
        {
            return false;
        }

        if (match.ItemCount > 0)
        {
            match.IsSubMenuOpen = true;
            Dispatcher.UIThread.Post(
                () => match.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsEffectivelyEnabled && i.IsVisible)?.Focus(NavigationMethod.Directional),
                DispatcherPriority.Loaded);
        }
        else if (match.Command is { } command && command.CanExecute(match.CommandParameter))
        {
            menuBar.Close();
            command.Execute(match.CommandParameter);
        }

        return true;
    }

    /// <summary>The access key of a generated header (<c>_</c> before it, <c>__</c> = literal underscore).</summary>
    public static char? MnemonicOf(MenuItem item)
    {
        if (item.Header is not string header)
        {
            return null;
        }

        for (var i = 0; i < header.Length - 1; i++)
        {
            if (header[i] == '_')
            {
                if (header[i + 1] != '_')
                {
                    return header[i + 1];
                }

                i++;
            }
        }

        return null;
    }
}
