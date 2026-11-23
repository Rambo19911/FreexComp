using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// The designer clipboard on the OS clipboard (design-surface.md DSG-SUR-110, DSG-SUR-120, DSG-SUR-125; D-078). The surface
/// reads and writes synchronously, the OS clipboard is asynchronous, so this keeps the last known content: a copy updates
/// it at once and writes it through (<see cref="FlushAsync"/> waits for that write); <see cref="RefreshAsync"/> reads what
/// another program may have put there since. <see cref="SequenceNumber"/> changes with every content change seen.
/// </summary>
public sealed class SystemDesignerClipboard(IClipboardService clipboard) : IDesignerClipboard
{
    private Dictionary<string, string> _formats = new(StringComparer.Ordinal);
    private Task<bool> _pending = Task.FromResult(true);

    public long SequenceNumber { get; private set; }

    public string? GetData(string format) => _formats.GetValueOrDefault(format);

    public void SetData(IReadOnlyDictionary<string, string> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = new Dictionary<string, string>(formats, StringComparer.Ordinal);
        SequenceNumber++;
        var text = formats.GetValueOrDefault(ClipboardFormats.Text) ?? "";
        var extra = formats.Where(f => f.Key != ClipboardFormats.Text).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        _pending = WriteAsync(text, extra);
    }

    /// <summary>Waits for the last write; false when the OS clipboard refused it (held open by another program).</summary>
    public Task<bool> FlushAsync() => _pending;

    /// <summary>Reads the OS clipboard; a content other than the last known one replaces it (a new sequence number).</summary>
    public async Task RefreshAsync()
    {
        await _pending;
        string? text;
        string? components;
        try
        {
            text = await clipboard.GetTextAsync();
            components = await clipboard.GetFormatAsync(ClipboardFormats.Components);
        }
        catch (Exception ex) when (IsBusy(ex))
        {
            return; // keep the last known content
        }

        if (text == GetData(ClipboardFormats.Text) && components == GetData(ClipboardFormats.Components))
        {
            return;
        }

        _formats = new Dictionary<string, string>(StringComparer.Ordinal);
        if (text is not null)
        {
            _formats[ClipboardFormats.Text] = text;
        }

        if (components is not null)
        {
            _formats[ClipboardFormats.Components] = components;
        }

        SequenceNumber++;
    }

    private async Task<bool> WriteAsync(string text, IReadOnlyDictionary<string, string> formats)
    {
        try
        {
            await clipboard.SetTextAsync(text, formats);
            return true;
        }
        catch (Exception ex) when (IsBusy(ex))
        {
            return false;
        }
    }

    private static bool IsBusy(Exception ex) =>
        ex is System.Runtime.InteropServices.ExternalException or IOException or UnauthorizedAccessException or TimeoutException
            or InvalidOperationException;
}
