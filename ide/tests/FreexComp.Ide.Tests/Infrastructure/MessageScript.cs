using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>
/// The harness's <see cref="IDialogService"/> (IDE-TEST-016): a message box with a scripted answer returns it without a
/// window; every other message box and every dialog is shown as the real window by the production service, to be
/// driven through <see cref="HeadlessHarness.WaitForDialogAsync"/>. A message box still open at dispose fails the test.
/// </summary>
public sealed class MessageScript(IDialogService windows) : IDialogService
{
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);

    public List<MessageRequest> Requests { get; } = [];

    /// <summary>Message boxes shown as windows and not closed yet.</summary>
    public List<MessageRequest> Unanswered { get; } = [];

    /// <summary>Every later request for <paramref name="messageId"/> returns <paramref name="buttonId"/>.</summary>
    public void Answer(string messageId, string buttonId) => _answers[messageId] = buttonId;

    public async Task<string> ShowMessageAsync(MessageRequest request)
    {
        Requests.Add(request);
        if (_answers.TryGetValue(request.MessageId, out var button))
        {
            // A scripted answer must be a button the box would show (definition, limited by the request).
            var shown = request.Buttons ?? MessageDefinitions.Get(request.MessageId).Buttons;
            if (!shown.Contains(button))
                throw new InvalidOperationException($"{request.MessageId} does not show '{button}' (buttons: {string.Join(", ", shown)}).");
            return button;
        }

        Unanswered.Add(request);
        var answer = await windows.ShowMessageAsync(request);
        Unanswered.Remove(request);
        return answer;
    }

    public Task<TResult?> ShowAsync<TResult>(DialogViewModel<TResult> viewModel) => windows.ShowAsync(viewModel);
}
