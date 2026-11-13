using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IDialogService"/> that records every request and answers from a script (IDE-ARCH-054): message boxes
/// return the scripted button id; dialogs are driven by the scripted action, which must close them. A request
/// without a script throws, failing the test. Queued answers (<see cref="Enqueue"/>) are used first, in order; then
/// the standing answer (<see cref="Answer(string, string)"/>).
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    private readonly Dictionary<string, Func<MessageRequest, string>> _answers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<Func<MessageRequest, string>>> _queued = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<DialogViewModel, Task>> _dialogs = new(StringComparer.Ordinal);

    public List<MessageRequest> Requests { get; } = [];

    public List<DialogViewModel> Dialogs { get; } = [];

    /// <summary>Every later request for <paramref name="messageId"/> returns <paramref name="buttonId"/>.</summary>
    public void Answer(string messageId, string buttonId) => _answers[messageId] = _ => buttonId;

    /// <summary>Every later request for <paramref name="messageId"/> is answered by <paramref name="answer"/>.</summary>
    public void Answer(string messageId, Func<MessageRequest, string> answer) => _answers[messageId] = answer;

    /// <summary>The next requests for <paramref name="messageId"/> return <paramref name="buttonIds"/>, one each, in order.</summary>
    public void Enqueue(string messageId, params string[] buttonIds)
    {
        foreach (var button in buttonIds)
        {
            Enqueue(messageId, _ => button);
        }
    }

    /// <summary>The next request for <paramref name="messageId"/> is answered by <paramref name="answer"/> (after earlier queued ones).</summary>
    public void Enqueue(string messageId, Func<MessageRequest, string> answer)
    {
        if (!_queued.TryGetValue(messageId, out var queue))
        {
            _queued[messageId] = queue = new Queue<Func<MessageRequest, string>>();
        }

        queue.Enqueue(answer);
    }

    /// <summary>Every later dialog named <paramref name="dialogName"/> is handed to <paramref name="drive"/>, which closes it.</summary>
    public void Script(string dialogName, Func<DialogViewModel, Task> drive) => _dialogs[dialogName] = drive;

    public Task<string> ShowMessageAsync(MessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);
        if (_queued.TryGetValue(request.MessageId, out var queue) && queue.Count > 0)
        {
            return Task.FromResult(queue.Dequeue()(request));
        }

        return _answers.TryGetValue(request.MessageId, out var answer)
            ? Task.FromResult(answer(request))
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
