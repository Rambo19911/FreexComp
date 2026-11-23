using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// The command target of a focused design surface (commands.md §1 scope <c>Designer</c>; design-surface.md §3, §9, §12;
/// D-078): <c>edit.*</c> and <c>designer.*</c> go to the current surface controller. With the OS clipboard
/// (<see cref="SystemDesignerClipboard"/>) a paste first reads what another program may have copied, and a copy or cut
/// waits for its write (a refused write shows <c>Status_ClipboardBusy</c>).
/// </summary>
public sealed class DesignSurfaceCommands(FormDesignerViewModel designer) : ICommandTarget
{
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    public bool CanExecute(string commandId, string? argument) => designer.Surface is { } surface && surface.CanExecute(commandId);

    public async Task<bool> ExecuteAsync(string commandId, string? argument)
    {
        var clipboard = designer.Host.Clipboard as SystemDesignerClipboard;
        if (commandId == FreexComp.Designer.Surface.Commands.Paste && clipboard is not null)
        {
            await clipboard.RefreshAsync();
        }

        if (designer.Surface is not { } surface || !surface.CanExecute(commandId))
        {
            return false;
        }

        surface.Execute(commandId, argument);
        if (commandId is FreexComp.Designer.Surface.Commands.Copy or FreexComp.Designer.Surface.Commands.Cut
            && clipboard is not null && !await clipboard.FlushAsync())
        {
            designer.Status?.ShowMessage(Strings.Status_ClipboardBusy, StatusDuration);
        }

        return true;
    }

    public bool IsChecked(string commandId) => false;
}
