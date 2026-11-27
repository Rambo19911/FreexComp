using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// IDE-ED-149 (I-322) Ctrl+hover: while Ctrl alone is held over an offset the session asks the language service whether the
/// identifier there has a declaration (at most one request in flight, cancellable) and exposes the identifier's span as
/// <see cref="Current"/> (the view underlines it in <c>text.link</c> and shows the hand cursor). <see cref="Clear"/> (Ctrl
/// released, pointer left, scrolling) and every edit remove it at once and cancel a running request.
/// </summary>
public sealed class DeclarationLinkSession
{
    /// <summary>The span of the identifier at the offset when it has a declaration; null otherwise (keyword, comment, string, no symbol).</summary>
    public delegate Task<(int Offset, int Length)?> Fetch(int offset, CancellationToken ct);

    private readonly Fetch _fetch;
    private CancellationTokenSource? _request;
    private int? _latest; // the last offset the pointer rested on with Ctrl held

    public DeclarationLinkSession(TextDocumentViewModel document, Fetch fetch)
    {
        ArgumentNullException.ThrowIfNull(document);
        _fetch = fetch;
        document.Buffer.Changed += (_, _) => Clear(); // editing removes it at once
    }

    public event EventHandler? Changed;

    /// <summary>The underlined identifier; null = none.</summary>
    public (int Offset, int Length)? Current { get; private set; }

    /// <summary>The running or last request, up to <see cref="Current"/> being set (tests await it).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Ctrl alone is held with the pointer over text offset <paramref name="offset"/>: inside the underlined token nothing
    /// changes; elsewhere the underline goes and the offset is asked for (after the running request, which is not restarted).
    /// </summary>
    public void PointerAt(int offset)
    {
        if (Current is { } current && current.Offset <= offset && offset < current.Offset + current.Length)
            return;
        Set(null);
        _latest = offset;
        if (_request is null)
            Pending = RequestAsync(offset);
    }

    /// <summary>Removes the underline and cancels a running request (Ctrl released or another modifier, pointer left, scrolled).</summary>
    public void Clear()
    {
        _latest = null;
        _request?.Cancel();
        _request = null;
        Set(null);
    }

    private async Task RequestAsync(int offset)
    {
        while (true)
        {
            var cts = new CancellationTokenSource();
            _request = cts;
            (int Offset, int Length)? span;
            try
            {
                span = await _fetch(offset, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                cts.Dispose();
            }

            if (_request != cts)
                return; // cleared meanwhile (a later request owns the state)
            _request = null;
            if (_latest is not { } latest)
                return;
            if (span is { } s && s.Offset <= latest && latest < s.Offset + s.Length)
            {
                Set(s);
                return;
            }

            if (latest == offset)
                return; // no declaration here
            offset = latest; // the pointer moved on while asking: ask for where it rests now
        }
    }

    private void Set((int Offset, int Length)? span)
    {
        if (Current == span)
            return;
        Current = span;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
