namespace FreexComp.Ide.Core.Models;

/// <summary>Identity of an open document (architecture.md §5); its string form is the <c>window.activateDocument</c> argument.</summary>
public readonly record struct DocumentId(Guid Value)
{
    public static DocumentId New() => new(Guid.NewGuid());

    public static DocumentId? TryParse(string? text) => Guid.TryParse(text, out var value) ? new DocumentId(value) : null;

    public override string ToString() => Value.ToString("D");
}
