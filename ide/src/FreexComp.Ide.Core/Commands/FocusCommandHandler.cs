namespace FreexComp.Ide.Core.Commands;

/// <summary>
/// Handler of a <see cref="CommandScope.Focus"/> command (commands.md §3): resolved against the focused element.
/// (1) a text input keeps its native behaviour: its keys are not routed (<see cref="CommandService.HandleKey(KeyGesture)"/>),
/// a menu/toolbar invocation goes to the text input's own target (I-153) and nowhere else; (2)–(4) the focused scope's
/// <see cref="ICommandTarget"/>; undo/redo in a tool window that does not handle them go to the active document
/// (UI-CMD-011); (5) otherwise disabled.
/// </summary>
public sealed class FocusCommandHandler(string commandId, IFocusTracker focus) : ICommandHandler
{
    private static readonly HashSet<string> ForwardedToDocument = new(["edit.undo", "edit.redo"], StringComparer.Ordinal);

    public bool CanExecute(string? argument) => Resolve(argument) is not null;

    public async Task ExecuteAsync(string? argument)
    {
        if (Resolve(argument) is { } target)
        {
            await target.ExecuteAsync(commandId, argument);
        }
    }

    private ICommandTarget? Resolve(string? argument)
    {
        var state = focus.Current;
        if (state.IsTextInput)
        {
            return state.Target is { } input && input.CanExecute(commandId, argument) ? input : null;
        }

        if (state.Target is { } own && own.CanExecute(commandId, argument))
        {
            return own;
        }

        return state.ToolWindowName is not null && ForwardedToDocument.Contains(commandId)
            && focus.ActiveDocumentTarget is { } document && document.CanExecute(commandId, argument)
            ? document
            : null;
    }
}
