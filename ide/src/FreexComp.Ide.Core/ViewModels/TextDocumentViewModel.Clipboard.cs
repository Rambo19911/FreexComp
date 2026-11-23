using System.Runtime.InteropServices;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Clipboard commands of the document (IDE-ED-038): <c>edit.cut</c>, <c>edit.copy</c>, <c>edit.paste</c>, plus
/// <c>edit.delete</c> and <c>edit.selectAll</c>. Without a selection Copy and Cut take the whole caret line; Paste of
/// such a line inserts it above the caret line. A busy clipboard is retried, then reported in the status bar.
/// </summary>
public partial class TextDocumentViewModel
{
    private const string CutCommandId = "edit.cut";
    private const string CopyCommandId = "edit.copy";
    private const string PasteCommandId = "edit.paste";
    private const string DeleteCommandId = "edit.delete";
    private const string SelectAllCommandId = "edit.selectAll";

    /// <summary>IDE-ED-038: a failed clipboard access is retried this many times at <see cref="ClipboardRetryInterval"/>.</summary>
    internal const int ClipboardRetries = 3;

    internal static readonly TimeSpan ClipboardRetryInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>The OS clipboard (set by the document service); null: the commands do nothing.</summary>
    public IClipboardService? Clipboard { get; internal set; }

    private static bool IsClipboardCommand(string commandId) =>
        commandId is CutCommandId or CopyCommandId or PasteCommandId or DeleteCommandId or SelectAllCommandId;

    /// <summary>
    /// menus.md with IDE-ED-038: Copy always (a line without selection), Cut and Paste when writable (the clipboard
    /// cannot be read synchronously, UI-MENU-006); Delete is refused with <c>Msg.Editor.ReadOnlyEdit</c> when read-only.
    /// </summary>
    private bool CanExecuteClipboard(string commandId) => commandId switch
    {
        CutCommandId or PasteCommandId => !Buffer.IsReadOnly,
        _ => true,
    };

    private async Task<bool> ExecuteClipboardAsync(string commandId)
    {
        if (!CanExecuteClipboard(commandId))
            return false;
        switch (commandId)
        {
            case CopyCommandId:
                await WriteClipboardAsync(CopyText());
                return true;
            case CutCommandId:
                var lineCut = Selection.IsEmpty && Block is null;
                if (await WriteClipboardAsync(CopyText()))
                    _ = lineCut ? Command(() => Apply(EditorOperations.DeleteLine(Buffer, Selection, Options.TabWidth))) : DeleteSelection();
                return true;
            case PasteCommandId:
                if (await ReadClipboardAsync() is { Length: > 0 } text)
                    PasteFromClipboard(text, await WithClipboardRetryAsync(clipboard => clipboard.GetFormatAsync(IClipboardService.LineFormat)) == text);
                return true;
            case DeleteCommandId:
                DeleteForward();
                return true;
            default: // SelectAllCommandId
                Selection = new TextSelection(0, Buffer.Length);
                return true;
        }
    }

    /// <summary>
    /// The selection (<see cref="CopySelection"/>), else the caret line with its line break, marked as a line with
    /// <see cref="IClipboardService.LineFormat"/> (I-170: on the OS clipboard, so other IDE instances see it).
    /// </summary>
    private (string Text, bool IsLine) CopyText()
    {
        if (!Selection.IsEmpty || Block is not null)
            return (CopySelection(), false);

        var (offset, length, breakLength) = Buffer.GetLine(ViewState.Caret.Line);
        var line = Buffer.GetText(offset, length + breakLength);
        if (breakLength == 0)
            line += LineEnding.NewLine;
        BlockClipboard.Text = null;
        return (line, true);
    }

    /// <summary>A copied line (<paramref name="isLine"/>) goes in as a full line above the caret line (caret column kept).</summary>
    private void PasteFromClipboard(string text, bool isLine)
    {
        if (!isLine || !Selection.IsEmpty || Block is not null)
        {
            Paste(text);
            return;
        }

        var caret = ViewState.Caret;
        Command(() =>
        {
            var lineStart = Buffer.GetLine(caret.Line).Offset;
            var column = Selection.Caret - lineStart;
            var before = Buffer.Length;
            if (!TryInsertText(lineStart, 0, text))
                return false;
            Selection = TextSelection.At(lineStart + Buffer.Length - before + column);
            return true;
        });
    }

    private async Task<bool> WriteClipboardAsync((string Text, bool IsLine) copy) =>
        await WithClipboardRetryAsync(async clipboard =>
        {
            if (copy.IsLine)
                await clipboard.SetTextAsync(copy.Text, new Dictionary<string, string> { [IClipboardService.LineFormat] = copy.Text });
            else
                await clipboard.SetTextAsync(copy.Text);
            return true;
        });

    private Task<string?> ReadClipboardAsync() => WithClipboardRetryAsync(clipboard => clipboard.GetTextAsync());

    /// <summary>IDE-ED-038: tries once and retries <see cref="ClipboardRetries"/> times; still failing → <c>Status_ClipboardBusy</c>.</summary>
    private async Task<T?> WithClipboardRetryAsync<T>(Func<IClipboardService, Task<T>> access)
    {
        if (Clipboard is not { } clipboard)
            return default;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await access(clipboard);
            }
            catch (Exception ex) when (IsClipboardFailure(ex) && attempt < ClipboardRetries)
            {
                await DelayAsync(ClipboardRetryInterval); // I-209: resumes on the UI thread, where the clipboard may be used
            }
            catch (Exception ex) when (IsClipboardFailure(ex))
            {
                StatusMessage?.Invoke(this, Strings.Status_ClipboardBusy);
                return default;
            }
        }
    }

    /// <summary>What a clipboard held open by another program throws.</summary>
    private static bool IsClipboardFailure(Exception ex) =>
        ex is ExternalException or IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException;

    private Task DelayAsync(TimeSpan delay)
    {
        if (Clock is not { } clock)
            return Task.CompletedTask;
        var done = new TaskCompletionSource();
        clock.Schedule(delay, done.SetResult);
        return done.Task;
    }
}
