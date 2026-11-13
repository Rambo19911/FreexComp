using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.TestSupport;

/// <summary>
/// <see cref="IDialogService"/> that records every message request and returns the scripted button id
/// (IDE-ARCH-054). A request without a scripted answer throws, failing the test.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);

    public List<MessageRequest> Requests { get; } = [];

    /// <summary>Every later request for <paramref name="messageId"/> returns <paramref name="buttonId"/>.</summary>
    public void Answer(string messageId, string buttonId) => _answers[messageId] = buttonId;

    public Task<string> ShowMessageAsync(MessageRequest request)
    {
        Requests.Add(request);
        return _answers.TryGetValue(request.MessageId, out var button)
            ? Task.FromResult(button)
            : throw new InvalidOperationException($"Unscripted message box {request.MessageId}.");
    }
}
