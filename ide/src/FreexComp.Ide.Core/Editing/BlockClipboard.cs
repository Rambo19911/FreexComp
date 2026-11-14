namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// What this IDE last put on the clipboard, when pasting it back is special. The document service gives every document
/// the same instance; at most one of the two is set.
/// </summary>
public sealed class BlockClipboard
{
    /// <summary>The last column block copied or cut (IDE-ED-026): pasting exactly this text inserts it line by line.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// The last whole line copied or cut without a selection (IDE-ED-038): pasting exactly this text inserts it as a
    /// full line above the caret line.
    /// </summary>
    public string? LineText { get; set; }
}
