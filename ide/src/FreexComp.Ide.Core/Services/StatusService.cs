using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Core.Services;

/// <inheritdoc />
/// <remarks>Expiry runs on <see cref="IClock"/> so tests advance it (IDE-ARCH-085). Progress and the IDE state arrive in P3.</remarks>
public sealed class StatusService(IClock clock) : IStatusService
{
    private IDisposable? _expiry;
    private object? _current; // identifies the shown message, so a stale sticky or expiry does not clear a newer one

    public event EventHandler? Changed;

    public string? Message { get; private set; }

    public EditorStatus? Editor { get; private set; }

    public string? DesignerSelection { get; private set; }

    public void SetDesignerSelection(string? text)
    {
        if (DesignerSelection != text)
        {
            DesignerSelection = text;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ShowMessage(string text, TimeSpan duration)
    {
        var token = Show(text);
        _expiry = clock.Schedule(duration, () => Clear(token));
    }

    public IDisposable ShowStickyMessage(string text) => new Sticky(this, Show(text));

    public void SetEditor(EditorStatus? editor)
    {
        if (Editor != editor)
        {
            Editor = editor;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private object Show(string text)
    {
        _expiry?.Dispose();
        _expiry = null;
        var token = new object();
        _current = token;
        Message = text;
        Changed?.Invoke(this, EventArgs.Empty);
        return token;
    }

    private void Clear(object token)
    {
        if (_current != token)
        {
            return;
        }

        _expiry?.Dispose();
        _expiry = null;
        _current = null;
        Message = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Sticky(StatusService owner, object token) : IDisposable
    {
        public void Dispose() => owner.Clear(token);
    }
}
