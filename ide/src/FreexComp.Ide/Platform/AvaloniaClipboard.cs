using Avalonia.Controls;
using Avalonia.Input.Platform;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Platform;

/// <summary>
/// <see cref="IClipboardService"/> over the clipboard of the main window. The window is looked up on each call,
/// because the service is built before the window exists (IDE-ARCH-015); without a window the clipboard reads as
/// empty and writes are ignored.
/// </summary>
public sealed class AvaloniaClipboard(Func<TopLevel?> topLevel) : IClipboardService
{
    public async Task<string?> GetTextAsync()
    {
        var clipboard = topLevel()?.Clipboard;
        return clipboard is null ? null : await clipboard.TryGetTextAsync();
    }

    public async Task SetTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var clipboard = topLevel()?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
