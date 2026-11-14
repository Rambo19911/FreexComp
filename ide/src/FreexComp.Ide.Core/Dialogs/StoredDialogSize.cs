using System.Globalization;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Layout;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>UI-DLG-005: the remembered size of a resizable dialog, UI state <c>dialogs.&lt;Name&gt;.size</c> = <c>"W,H"</c>.</summary>
internal static class StoredDialogSize
{
    public static (double Width, double Height)? Read(ILayoutService? layout, string dialogName)
    {
        var parts = layout?.GetDialogValue(dialogName, "size")?.Split(',');
        return parts is [var w, var h]
               && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
               && double.TryParse(h, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            ? (width, height)
            : null;
    }

    public static void Write(ILayoutService? layout, string dialogName, (double Width, double Height)? size)
    {
        if (size is { } s)
            layout?.SetDialogValue(dialogName, "size", string.Create(CultureInfo.InvariantCulture, $"{s.Width:0},{s.Height:0}"));
    }
}
