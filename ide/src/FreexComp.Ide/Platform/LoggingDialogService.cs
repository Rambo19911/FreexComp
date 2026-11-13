using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Platform;

/// <summary>
/// Stand-in for the message box until I-036 provides the dialog window: logs the request and answers
/// <c>Cancel</c>, the non-destructive choice (UI-DLG-024).
/// </summary>
public sealed class LoggingDialogService(ILog log) : IDialogService
{
    public const string Answer = "Cancel";

    public Task<string> ShowMessageAsync(MessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        log.Warning("dialogs", $"Message box not available yet; {request.MessageId} answered {Answer}. {request.Details}");
        return Task.FromResult(Answer);
    }
}
