namespace FreexComp.Ide.Core.Models;

/// <summary>
/// Outcome of an operation that can fail for expected reasons (IDE-ARCH-092): success, or the <c>Msg.*</c> id and
/// placeholder arguments of the message the calling command shows.
/// </summary>
public record OperationResult(string? MessageId, IReadOnlyList<string> Args)
{
    public static readonly OperationResult Ok = new(null, []);

    public bool Success => MessageId is null;

    public static OperationResult Fail(string messageId, params string[] args) => new(messageId, args);
}

/// <summary>An <see cref="OperationResult"/> that carries the produced value on success.</summary>
public sealed record OperationResult<T>(T? Value, string? MessageId, IReadOnlyList<string> Args) : OperationResult(MessageId, Args)
    where T : class
{
    public static OperationResult<T> Of(T value) => new(value, null, []);

    public static new OperationResult<T> Fail(string messageId, params string[] args) => new(null, messageId, args);
}
