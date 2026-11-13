using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IDialogService"/> that records every request and answers from a script (IDE-ARCH-054): message boxes
/// return the scripted button id; dialogs are driven by the scripted action, which must close them. A request
/// without a script throws, failing the test.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<DialogViewModel, Task>> _dialogs = new(StringComparer.Ordinal);

    public List<MessageRequest> Requests { get; } = [];

    public List<DialogViewModel> Dialogs { get; } = [];

    /// <summary>Every later request for <paramref name="messageId"/> returns <paramref name="buttonId"/>.</summary>
    public void Answer(string messageId, string buttonId) => _answers[messageId] = buttonId;

    /// <summary>Every later dialog named <paramref name="dialogName"/> is handed to <paramref name="drive"/>, which closes it.</summary>
    public void Script(string dialogName, Func<DialogViewModel, Task> drive) => _dialogs[dialogName] = drive;

    public Task<string> ShowMessageAsync(MessageRequest request)
    {
        Requests.Add(request);
        return _answers.TryGetValue(request.MessageId, out var button)
            ? Task.FromResult(button)
            : throw new InvalidOperationException($"Unscripted message box {request.MessageId}.");
    }

    public async Task<TResult?> ShowAsync<TResult>(DialogViewModel<TResult> viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Dialogs.Add(viewModel);
        if (!_dialogs.TryGetValue(viewModel.Name, out var drive))
            throw new InvalidOperationException($"Unscripted dialog {viewModel.Name}.");
        await drive(viewModel);
        return viewModel.IsClosed ? viewModel.Result : throw new InvalidOperationException($"The script left dialog {viewModel.Name} open.");
    }
}
