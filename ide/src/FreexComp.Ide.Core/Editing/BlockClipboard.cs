namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// The text of the last column block copied or cut in this IDE (IDE-ED-026): pasting exactly this text inserts it line
/// by line. The document service gives every document the same instance.
/// </summary>
public sealed class BlockClipboard
{
    public string? Text { get; set; }
}
