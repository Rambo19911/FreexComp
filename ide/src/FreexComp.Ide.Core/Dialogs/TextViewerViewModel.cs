using System.Globalization;
using System.Text;
using FreexComp.Debugger;
using FreexComp.Ide.Core.Debugging;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The shown text changed (<see cref="TextViewerViewModel.DisplayChanged"/>): the first <see cref="Keep"/> characters stay,
/// everything after them is replaced by <see cref="Appended"/>.
/// </summary>
public sealed class TextViewerDisplayChangedEventArgs(int keep, string appended) : EventArgs
{
    public int Keep { get; } = keep;

    public string Appended { get; } = appended;
}

/// <summary>
/// Text Viewer (visualizers.md §5.2, dialogs.md §32; UI-DLG-323…329), 720 × 480, resizable (minimum 400 × 260): the text
/// behind a value's <see cref="VariableInfo.Text"/> handle. On open it asks the debugger for the details once, then reads
/// the text in chunks of <see cref="ChunkSize"/> elements, each continuing at the previous chunk's <c>Next</c>, until the
/// end, a fault or <see cref="Cap"/> characters shown; closing the dialog cancels the reading. <see cref="Hex"/> shows the
/// loaded elements as hex rows (the bytes of 1-byte texts read from the debuggee for them). Nothing is decoded here: the
/// text is the debugger's chunks joined (UI-DLG-329).
/// </summary>
public sealed class TextViewerViewModel : DialogViewModel<bool>
{
    public const string DialogName = "TextViewer";

    /// <summary>UI-DLG-324: the elements asked for per <c>ReadTextAsync</c>.</summary>
    public const int ChunkSize = 65_536;

    /// <summary>UI-DLG-324: at most this many characters are shown.</summary>
    public const int Cap = 16_777_216;

    private const int UnitsPerRow = 8;
    private const int BytesPerRow = 16;
    private const string Separator = " · ";

    private readonly IDebugService _debug;
    private readonly IClipboardService? _clipboard;
    private readonly ILayoutService? _layout;
    private readonly VariableHandle _handle;
    private readonly string _typeName;
    private readonly CancellationTokenSource _cancel = new();
    private readonly StringBuilder _text = new();

    /// <summary>Where each chunk starts: its first element and its offset in <see cref="_text"/> (element ↔ character).</summary>
    private readonly List<(long Element, int Offset)> _chunks = [];

    private readonly StringBuilder _hexText = new();
    private readonly List<byte> _bytes = [];
    private TextDetails? _textDetails;
    private long _next;
    private string _details;
    private string _status = "";
    private bool _statusIsError;
    private bool _isLoading = true;
    private bool _wrap;
    private bool _hex;
    private int _hexRows; // complete rows in _hexText
    private bool _pumping;

    public TextViewerViewModel(IDebugService debug, IClipboardService? clipboard, ILayoutService? layout, string name, VariableInfo value)
    {
        ArgumentNullException.ThrowIfNull(debug);
        ArgumentNullException.ThrowIfNull(value);
        _debug = debug;
        _clipboard = clipboard;
        _layout = layout;
        _handle = value.Text ?? throw new ArgumentException("The value has no text.", nameof(value));
        _typeName = value.TypeName;
        _details = value.TypeName;
        Title = string.Format(CultureInfo.CurrentCulture, Strings.TextViewer_Title, name);
        _wrap = layout?.GetDialogValue(DialogName, "wrap") != "false"; // on by default
        Copy = new DialogButtonViewModel("Copy", Strings.TextViewer_Copy.Replace("_", "", StringComparison.Ordinal), CopyAsync)
        {
            IsAuxiliary = true,
            AccessLabel = Strings.TextViewer_Copy,
            IsEnabled = false, // UI-DLG-327: disabled while loading
        };
        CloseButton = new DialogButtonViewModel("Close", Strings.Button_Close, () => Close(true)) { IsDefault = true, IsCancel = true };
        Buttons = [Copy, CloseButton];
        CloseRequested += (_, _) => _cancel.Cancel(); // UI-DLG-324: closing cancels the reading in progress
        Loading = debug.Track(LoadAsync(_cancel.Token));
    }

    public override string Name => DialogName;

    public override string Title { get; }

    public override double Width => 720;

    public override double? Height => 480;

    public override bool CanResize => true;

    public override (double Width, double Height)? MinimumSize => (400, 260);

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Copy { get; }

    public DialogButtonViewModel CloseButton { get; }

    /// <summary>The reading of the details and the text (completed when it ended, also by closing); never faults.</summary>
    public Task Loading { get; }

    /// <summary>The latest work of the hex rows (reading the bytes of a 1-byte text); completed when none.</summary>
    public Task HexPending { get; private set; } = Task.CompletedTask;

    /// <summary>The debugger's details; null until they arrived or when they cannot be read.</summary>
    public TextDetails? TextDetails => _textDetails;

    /// <summary>The <c>Details</c> line: the parts of UI-DLG-323 joined by <c> · </c>; the type name alone without details.</summary>
    public string Details
    {
        get => _details;
        private set => SetProperty(ref _details, value);
    }

    /// <summary>The text loaded so far (UTF-16, as the debugger gave it).</summary>
    public string Text => _text.ToString();

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary><c>Msg.Debug.TextLoading</c> while loading, <c>TextCut</c> / <c>TextUnreadable</c> at the end, else empty.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary><see cref="Status"/> is shown in <c>status.error</c>.</summary>
    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    /// <summary>Lines wrap (UI state <c>dialogs.TextViewer.wrap</c>, default on).</summary>
    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (SetProperty(ref _wrap, value))
                _layout?.SetDialogValue(DialogName, "wrap", value ? "true" : "false");
        }
    }

    /// <summary>UI-DLG-326: the text box shows hex rows of the loaded elements.</summary>
    public bool Hex
    {
        get => _hex;
        set
        {
            if (!SetProperty(ref _hex, value))
                return;
            if (value)
            {
                _hexText.Clear();
                _hexRows = 0;
                EmitHex(); // what is known already; the bytes still missing are read
            }
            else
            {
                _hexText.Clear();
                _hexRows = 0;
                DisplayChanged?.Invoke(this, new TextViewerDisplayChangedEventArgs(0, _text.ToString()));
            }
        }
    }

    /// <summary>What the text box shows: the text, or its hex rows.</summary>
    public string DisplayText => Hex ? _hexText.ToString() : _text.ToString();

    /// <summary>The text box follows <see cref="DisplayText"/> with these changes (appended chunks, a switch of the view).</summary>
    public event EventHandler<TextViewerDisplayChangedEventArgs>? DisplayChanged;

    private bool IsByteText => _textDetails is { ElementSize: 1, PayloadAddress: not null };

    private int PerRow => IsByteText ? BytesPerRow : UnitsPerRow;

    /// <summary>The length of a complete hex row (without its line break).</summary>
    private int RowLength => PerRow == BytesPerRow ? 8 + 1 + (BytesPerRow * 3) - 1 + 2 + BytesPerRow : 8 + 1 + (UnitsPerRow * 5) - 1 + 2 + UnitsPerRow;

    /// <summary>The element at <paramref name="offset"/> of <see cref="DisplayText"/> (switching keeps the element in view).</summary>
    public long ElementAt(int offset)
    {
        if (Hex)
            return (long)(Math.Max(0, offset) / (RowLength + 1)) * PerRow;
        var index = _chunks.FindLastIndex(c => c.Offset <= offset);
        if (index < 0)
            return 0;
        var (element, start) = _chunks[index];
        var end = index + 1 < _chunks.Count ? _chunks[index + 1].Element : _next;
        return Math.Min(element + (offset - start), end);
    }

    /// <summary>The offset in <see cref="DisplayText"/> where <paramref name="element"/> is shown (its row in hex).</summary>
    public int DisplayOffsetOf(long element)
    {
        if (Hex)
            return (int)Math.Min(int.MaxValue, Math.Max(0, element) / PerRow * (RowLength + 1));
        var index = _chunks.FindLastIndex(c => c.Element <= element);
        if (index < 0)
            return 0;
        var (start, offset) = _chunks[index];
        var end = index + 1 < _chunks.Count ? _chunks[index + 1].Offset : _text.Length;
        return (int)Math.Min(offset + (element - start), end);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            TextDetails details;
            try
            {
                details = await _debug.GetTextDetailsAsync(_handle, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested && e is DebuggerException or ObjectDisposedException or OperationCanceledException)
            {
                SetStatus(e is DebuggerException ? e.Message : Strings.Msg_Debug_NotPaused, isError: true); // UI-DLG-323
                return;
            }

            _textDetails = details;
            Details = string.Join(Separator, DetailParts(details));
            OnPropertyChanged(nameof(TextDetails));
            await ReadAllAsync(details, ct);
        }
        catch (Exception e) when (ct.IsCancellationRequested && e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            // Closed: nothing is shown any more.
        }
        finally
        {
            IsLoading = false;
            Copy.IsEnabled = true;
        }
    }

    private async Task ReadAllAsync(TextDetails details, CancellationToken ct)
    {
        while (true)
        {
            if (_text.Length >= Cap)
            {
                SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_TextCut, Grouped(Cap)), isError: false);
                return;
            }

            SetStatus(Progress(details), isError: false);
            TextChunk chunk;
            try
            {
                chunk = await _debug.ReadTextAsync(_handle, _next, (int)Math.Min(ChunkSize, Cap - _text.Length), ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested && e is DebuggerException or ObjectDisposedException or OperationCanceledException)
            {
                SetUnreadable(e);
                return;
            }

            Append(chunk);
            if (chunk.Truncated)
            {
                // UI-DLG-325: the readable prefix stays; the debugger names the first element it cannot read.
                await ReportUnreadableAsync(ct);
                return;
            }

            if (chunk.AtEnd || chunk.Next <= chunk.Start)
            {
                SetStatus("", isError: false);
                return;
            }
        }
    }

    private async Task ReportUnreadableAsync(CancellationToken ct)
    {
        try
        {
            await _debug.ReadTextAsync(_handle, _next, 1, ct);
            SetUnreadable(null);
        }
        catch (Exception e) when (!ct.IsCancellationRequested && e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            SetUnreadable(e);
        }
    }

    private void SetUnreadable(Exception? fault)
    {
        var message = fault switch
        {
            DebuggerException e => e.Message,
            null => Strings.Msg_Debug_Unavailable,
            _ => Strings.Msg_Debug_NotPaused, // the session went away under the dialog
        };
        SetStatus(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Debug_TextUnreadable, message), isError: true);
    }

    private void Append(TextChunk chunk)
    {
        _chunks.Add((chunk.Start, _text.Length));
        _text.Append(chunk.Text);
        _next = Math.Max(_next, chunk.Next);
        if (Hex)
            EmitHex();
        else
            DisplayChanged?.Invoke(this, new TextViewerDisplayChangedEventArgs(_text.Length - chunk.Text.Length, chunk.Text));
    }

    private string Progress(TextDetails details)
    {
        var bytes = details.ElementSize == 1; // Start / Next count bytes for 1-byte kinds
        return details.Length < 0
            ? string.Format(CultureInfo.CurrentCulture, bytes ? Strings.Msg_Debug_TextLoadingBytesNoLength : Strings.Msg_Debug_TextLoadingNoLength, Grouped(_next))
            : string.Format(CultureInfo.CurrentCulture, bytes ? Strings.Msg_Debug_TextLoadingBytes : Strings.Msg_Debug_TextLoading, Grouped(_next), Grouped(details.Length));
    }

    private void SetStatus(string text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }

    private IEnumerable<string> DetailParts(TextDetails details)
    {
        yield return _typeName;
        yield return details.Length < 0
            ? Strings.TextViewer_LengthUnknown
            : string.Format(CultureInfo.CurrentCulture, Strings.TextViewer_Length, Grouped(details.Length));
        if (details.MaxLength is { } max)
            yield return string.Format(CultureInfo.CurrentCulture, Strings.TextViewer_MaxLength, Grouped(max));
        if (details.RefCount is { } count)
            yield return count == -1 ? Strings.TextViewer_RefCountConstant : string.Format(CultureInfo.CurrentCulture, Strings.TextViewer_RefCount, Grouped(count));
        yield return details.Encoding;
        if (details.PayloadAddress is { } address)
            yield return "$" + address.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static string Grouped(long n) => n.ToString("N0", CultureInfo.CurrentCulture);

    private async Task CopyAsync()
    {
        if (_clipboard is not null)
            await _clipboard.SetTextAsync(_text.ToString()); // the whole text loaded, not the hex rows
    }

    /// <summary>Brings the hex rows up to the elements known: the UTF-16 units of the text, or the bytes read so far.</summary>
    private void EmitHex()
    {
        if (!Hex)
            return;
        if (IsByteText)
        {
            Rows(_bytes.Count, i => _bytes[i], (b, row) => row.Append(b.ToString("X2", CultureInfo.InvariantCulture)),
                b => b is >= 0x20 and < 0x7F ? (char)b : '.');
            if (_bytes.Count < _next && !_pumping)
                HexPending = _debug.Track(PumpBytesAsync(_cancel.Token));
        }
        else
        {
            Rows(_text.Length, i => _text[i], (c, row) => row.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)),
                c => char.IsControl(c) || char.IsSurrogate(c) ? '.' : c);
        }
    }

    /// <summary>Replaces the last (partial) row and appends the rows of elements up to <paramref name="count"/>.</summary>
    private void Rows<T>(int count, Func<int, T> element, Action<T, StringBuilder> hex, Func<T, char> shown)
    {
        var perRow = PerRow;
        var keep = _hexRows == 0 ? 0 : (_hexRows * (RowLength + 1)) - 1;
        var appended = new StringBuilder();
        var cellWidth = perRow == BytesPerRow ? 2 : 4;
        for (var first = _hexRows * perRow; first < count; first += perRow)
        {
            if (first > 0)
                appended.Append('\n');
            var n = Math.Min(perRow, count - first);
            appended.Append(first.ToString("X8", CultureInfo.InvariantCulture)).Append(' ');
            for (var i = 0; i < perRow; i++)
            {
                if (i > 0)
                    appended.Append(' ');
                if (i < n)
                    hex(element(first + i), appended);
                else
                    appended.Append(' ', cellWidth);
            }

            appended.Append("  ");
            for (var i = 0; i < n; i++)
                appended.Append(shown(element(first + i)));
        }

        _hexText.Length = keep;
        _hexText.Append(appended);
        _hexRows = count / perRow;
        DisplayChanged?.Invoke(this, new TextViewerDisplayChangedEventArgs(keep, appended.ToString()));
    }

    /// <summary>UI-DLG-326: the bytes of a 1-byte text for its hex rows, from the payload address, as far as the text is loaded.</summary>
    private async Task PumpBytesAsync(CancellationToken ct)
    {
        _pumping = true;
        try
        {
            while (Hex && _bytes.Count < _next && _textDetails?.PayloadAddress is { } address)
            {
                var count = (int)Math.Min(ChunkSize, _next - _bytes.Count);
                byte[] bytes;
                try
                {
                    bytes = await _debug.ReadMemoryAsync(address + (ulong)_bytes.Count, count, ct);
                }
                catch (Exception e) when (!ct.IsCancellationRequested && e is DebuggerException or ObjectDisposedException or OperationCanceledException)
                {
                    SetUnreadable(e);
                    return;
                }

                if (bytes.Length == 0)
                    return;
                _bytes.AddRange(bytes);
                EmitHex();
            }
        }
        catch (Exception e) when (ct.IsCancellationRequested && e is DebuggerException or ObjectDisposedException or OperationCanceledException)
        {
            // Closed.
        }
        finally
        {
            _pumping = false;
        }
    }
}
