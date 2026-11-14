using System.Globalization;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Editing;

/// <summary>The Go to Line field (dialogs.md §12): <c>L</c>, <c>L:C</c> or <c>L,C</c> (phase1-shell.md §10), decimal digits only.</summary>
public static class GoToLineInput
{
    /// <summary>
    /// True when <paramref name="text"/> (surrounding spaces ignored) is a line in 1…<paramref name="lineCount"/>,
    /// optionally followed by <c>:</c> or <c>,</c> and a column (any decimal number; a column below 1 means 1).
    /// </summary>
    public static bool TryParse(string text, int lineCount, out int line, out int? column)
    {
        ArgumentNullException.ThrowIfNull(text);
        line = 0;
        column = null;
        var parts = text.Trim().Split(':', ',');
        if (parts.Length > 2 || !TryNumber(parts[0], out line) || line < 1 || line > lineCount)
            return false;
        if (parts.Length == 1)
            return true;
        if (!TryNumber(parts[1], out var c))
            return false;
        column = c;
        return true;
    }

    /// <summary><c>Msg.GoToLine.OutOfRange</c> for a document of <paramref name="lineCount"/> lines.</summary>
    public static string OutOfRangeMessage(int lineCount) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Msg_GoToLine_OutOfRange, lineCount);

    private static bool TryNumber(string text, out int value)
    {
        value = 0;
        return text.Length > 0 && text.All(char.IsAsciiDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
