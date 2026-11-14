namespace FreexComp.Ide.Core.Models;

/// <summary>
/// Outcome of an operation that can fail for expected reasons (IDE-ARCH-092): success, or the <c>Msg.*</c> id and
/// placeholder arguments of the message the calling command shows.
/// </summary>
public record OperationResult(string? MessageId, IReadOnlyList<string> Args)
{
    public static readonly OperationResult Ok = new(null, []);

    /// <summary>The message id of an operation the user canceled in a question it asked (nothing to show).</summary>
    public const string CanceledId = "Canceled";

    public bool Success => MessageId is null;

    /// <summary>The user canceled (e.g. <c>Msg.File.TooLarge</c> answered Cancel): not a success, but nothing to report.</summary>
    public bool IsCanceled => MessageId == CanceledId;

    /// <summary>A failure whose message the caller shows.</summary>
    public bool NeedsMessage => !Success && !IsCanceled;

    public static OperationResult Fail(string messageId, params string[] args) => new(messageId, args);
}

/// <summary>An <see cref="OperationResult"/> that carries the produced value on success.</summary>
public sealed record OperationResult<T>(T? Value, string? MessageId, IReadOnlyList<string> Args) : OperationResult(MessageId, Args)
    where T : class
{
    public static OperationResult<T> Of(T value) => new(value, null, []);

    public static new OperationResult<T> Fail(string messageId, params string[] args) => new(null, messageId, args);

    public static OperationResult<T> Canceled() => new(null, CanceledId, []);
}
