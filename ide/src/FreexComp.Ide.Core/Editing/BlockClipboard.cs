namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// What this IDE last put on the clipboard, when pasting it back is special. The document service gives every document
/// the same instance. A copied whole line is marked on the OS clipboard instead (<c>IClipboardService.LineFormat</c>, I-170).
/// </summary>
public sealed class BlockClipboard
{
    /// <summary>The last column block copied or cut (IDE-ED-026): pasting exactly this text inserts it line by line.</summary>
    public string? Text { get; set; }
}
