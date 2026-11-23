using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.TestSupport;

/// <summary><see cref="IStatusService"/> that records messages with their duration (they never expire).</summary>
public sealed class RecordingStatusService : IStatusService
{
    public event EventHandler? Changed;

    /// <summary>Every message in order; sticky ones have <see cref="TimeSpan.MaxValue"/>.</summary>
    public List<(string Text, TimeSpan Duration)> Messages { get; } = [];

    public string? Message => Messages.Count == 0 ? null : Messages[^1].Text;

    public EditorStatus? Editor { get; private set; }

    public string? DesignerSelection { get; private set; }

    public void SetDesignerSelection(string? text)
    {
        DesignerSelection = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ShowMessage(string text, TimeSpan duration)
    {
        Messages.Add((text, duration));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IDisposable ShowStickyMessage(string text)
    {
        ShowMessage(text, TimeSpan.MaxValue);
        return new Nothing();
    }

    public void SetEditor(EditorStatus? editor)
    {
        Editor = editor;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
