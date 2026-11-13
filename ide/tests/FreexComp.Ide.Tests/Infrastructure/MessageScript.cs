using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Tests.Infrastructure;

/// <summary>
/// Pre-answered message boxes of a harness (IDE-TEST-016). Until the message-box window exists (I-036) this is the
/// harness's <see cref="IDialogService"/>: an unanswered request throws and fails the test at dispose.
/// </summary>
public sealed class MessageScript : IDialogService
{
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);

    public List<MessageRequest> Requests { get; } = [];

    public List<MessageRequest> Unanswered { get; } = [];

    /// <summary>Every later request for <paramref name="messageId"/> returns <paramref name="buttonId"/>.</summary>
    public void Answer(string messageId, string buttonId) => _answers[messageId] = buttonId;

    public Task<string> ShowMessageAsync(MessageRequest request)
    {
        Requests.Add(request);
        if (_answers.TryGetValue(request.MessageId, out var button))
        {
            return Task.FromResult(button);
        }

        Unanswered.Add(request);
        throw new InvalidOperationException($"Unanswered message box {request.MessageId}.");
    }
}
