namespace FreexComp.Ide.Core.Commands;

/// <summary>
/// The generic route of <see cref="CommandScope.Editor"/> commands (commands.md §3, I-158): every implemented Editor
/// command that no service owns is handled by the active document's <see cref="ICommandTarget"/> — availability,
/// execution and check mark — so a new editor operation needs no registration of its own.
/// </summary>
public static class EditorCommandRoute
{
    /// <summary>
    /// Registers the route for every Editor-scope command of phase ≤ <paramref name="implementedPhase"/> that has no
    /// handler yet; call it after the services have registered theirs and before the later-phase stubs.
    /// </summary>
    public static void Register(ICommandService commands, int implementedPhase, Func<ICommandTarget?> activeDocument)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(activeDocument);
        foreach (var definition in commands.Definitions.Where(d => d.Scope == CommandScope.Editor && Composition.Phase8Features.IsAvailable(d.Id, d.Phase, implementedPhase) && !commands.IsHandled(d.Id)))
        {
            commands.RegisterHandler(definition.Id, new Handler(definition.Id, activeDocument));
        }
    }

    private sealed class Handler(string commandId, Func<ICommandTarget?> activeDocument) : ICommandHandler
    {
        public bool CanExecute(string? argument) => activeDocument() is { } target && target.CanExecute(commandId, argument);

        public async Task ExecuteAsync(string? argument)
        {
            if (activeDocument() is { } target)
                await target.ExecuteAsync(commandId, argument);
        }

        public bool IsChecked(string? argument) => activeDocument()?.IsChecked(commandId) == true;
    }
}
