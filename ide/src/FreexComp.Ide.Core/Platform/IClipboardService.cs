namespace FreexComp.Ide.Core.Platform;

/// <summary>Text clipboard of the OS (IDE-ARCH-012).</summary>
public interface IClipboardService
{
    /// <summary>The clipboard text, or null when it holds no text.</summary>
    Task<string?> GetTextAsync();

    Task SetTextAsync(string text);
}
