using Avalonia.Controls;
using Avalonia.Input;
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

    /// <summary>The text and each extra format in one data item; a format name goes to the OS unchanged (a registered clipboard format).</summary>
    public async Task SetTextAsync(string text, IReadOnlyDictionary<string, string> formats)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(formats);
        if (topLevel()?.Clipboard is not { } clipboard)
            return;
        var item = new DataTransferItem();
        item.SetText(text);
        foreach (var (name, value) in formats)
            item.Set(Format(name), value);
        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
    }

    public async Task<string?> GetFormatAsync(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return topLevel()?.Clipboard is { } clipboard ? await clipboard.TryGetValueAsync(Format(format)) : null;
    }

    private static DataFormat<string> Format(string name) => DataFormat.CreateStringPlatformFormat(name);
}
