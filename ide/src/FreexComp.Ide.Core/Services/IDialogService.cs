namespace FreexComp.Ide.Core.Services;

/// <summary>
/// A generic message box request (dialogs.md §2). <paramref name="MessageId"/> is a <c>Msg.*</c> id of
/// states-and-messages.md; <paramref name="Args"/> fill its placeholders; <paramref name="Details"/> fills the
/// Details expander (UI-DLG-025), null for none.
/// </summary>
public sealed record MessageRequest(string MessageId, IReadOnlyList<string> Args, string? Details = null);

/// <summary>Modal dialogs (IDE-ARCH-054). Only the generic message box so far; typed dialogs come with their tasks.</summary>
public interface IDialogService
{
    /// <summary>Shows the message box and returns the chosen button id (UI-DLG-027).</summary>
    Task<string> ShowMessageAsync(MessageRequest request);
}
