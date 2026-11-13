using CommunityToolkit.Mvvm.ComponentModel;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>Severity of an info bar; the view maps it to its colour tokens (editor.md UI-ED-091).</summary>
public enum InfoBarSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One button of an info bar: <see cref="Name"/> is the AutomationId part (<c>InfoBar.&lt;Kind&gt;.Action.&lt;Name&gt;</c>,
/// UI-ED-095), <see cref="Label"/> the resource text.
/// </summary>
public sealed class InfoBarAction(string name, string label, Func<Task> execute)
{
    public string Name { get; } = name;

    public string Label { get; } = label;

    public Task ExecuteAsync() => execute();
}

/// <summary>
/// A non-modal strip in a document (editor.md §9). <see cref="Kind"/> is the AutomationId part
/// (<c>InfoBar.&lt;Kind&gt;</c>); a document holds at most one bar per kind (UI-ED-092).
/// </summary>
public sealed class InfoBarViewModel : ObservableObject
{
    private readonly Action<InfoBarViewModel>? _dismiss;

    public InfoBarViewModel(string kind, InfoBarSeverity severity, string text, IReadOnlyList<InfoBarAction> actions, Action<InfoBarViewModel>? dismiss)
    {
        Kind = kind;
        Severity = severity;
        Text = text;
        Actions = actions;
        _dismiss = dismiss;
    }

    public string Kind { get; }

    public InfoBarSeverity Severity { get; }

    public string Text { get; }

    public IReadOnlyList<InfoBarAction> Actions { get; }

    /// <summary>True when the bar has a close (×) button (UI-ED-093).</summary>
    public bool CanDismiss => _dismiss is not null;

    /// <summary>The close (×) button: the same as the non-destructive action.</summary>
    public void Dismiss() => _dismiss?.Invoke(this);
}
