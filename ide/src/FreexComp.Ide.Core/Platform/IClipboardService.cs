namespace FreexComp.Ide.Core.Platform;

/// <summary>Text clipboard of the OS (IDE-ARCH-012).</summary>
public interface IClipboardService
{
    /// <summary>
    /// IDE-ED-038: the clipboard format marking text copied as a whole line (its value is that text). It lives on the OS
    /// clipboard, so another IDE instance pastes the line as a line too; any other program's copy replaces it.
    /// </summary>
    const string LineFormat = "application/x-freexcomp-line";

    /// <summary>The clipboard text, or null when it holds no text.</summary>
    Task<string?> GetTextAsync();

    Task SetTextAsync(string text);

    /// <summary>Puts <paramref name="text"/> and extra string <paramref name="formats"/> (name to value) on the clipboard in one operation.</summary>
    Task SetTextAsync(string text, IReadOnlyDictionary<string, string> formats);

    /// <summary>The value of the string <paramref name="format"/> on the clipboard, or null when it holds none.</summary>
    Task<string?> GetFormatAsync(string format);
}