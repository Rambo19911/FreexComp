using FreexComp.Ide.Core.Dialogs;

namespace FreexComp.Ide.Core.Services;

/// <summary>
/// A generic message box request (dialogs.md §2). <paramref name="MessageId"/> is a <c>Msg.*</c> id of
/// states-and-messages.md; <paramref name="Args"/> fill its placeholders; <paramref name="Details"/> fills the
/// Details expander (UI-DLG-025), null for none. <paramref name="Buttons"/> limits the definition's buttons to these
/// ids (shown in the definition's order; e.g. UI-DLG-040 "Retry, Cancel only"); null = all of them.
/// </summary>
public sealed record MessageRequest(string MessageId, IReadOnlyList<string> Args, string? Details = null, IReadOnlyList<string>? Buttons = null);

/// <summary>Modal dialogs (IDE-ARCH-054, UI-DLG-008). UI thread only.</summary>
public interface IDialogService
{
    /// <summary>Shows the message box and returns the chosen button id (UI-DLG-024, UI-DLG-027).</summary>
    Task<string> ShowMessageAsync(MessageRequest request);

    /// <summary>Shows the window of <paramref name="viewModel"/> modally over the main window; returns its result when it closes.</summary>
    Task<TResult?> ShowAsync<TResult>(DialogViewModel<TResult> viewModel);
}
