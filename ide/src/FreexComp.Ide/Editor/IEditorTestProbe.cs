namespace FreexComp.Ide.Editor;

/// <summary>
/// Read-only view of what the code editor draws, for headless tests (editor.md §13.3, IDE-ARCH-113, IDE-TEST-010):
/// gutter glyphs, line backgrounds, markers and fold markers have no visual children. Implemented by
/// <see cref="CodeEditorView"/>; not used by product code. Lines are 1-based.
/// </summary>
public interface IEditorTestProbe
{
    /// <summary>"Breakpoint", "BreakpointDisabled", "BreakpointConditional", "BreakpointUnverified", "Execution", "Frame", "Bookmark", "Bookmark3", "QuickAction", "Error", or null.</summary>
    string? GlyphAt(int line);

    /// <summary>Colour token of the line's background, e.g. <c>editor.execution.line.bg</c> or <c>editor.currentLine.bg</c>; null for none.</summary>
    string? LineBackgroundAt(int line);

    IReadOnlyList<(int Offset, int Length, string Kind)> Markers { get; }

    /// <summary>"None" | "Modified" | "Saved".</summary>
    string ChangeBarAt(int line);

    IReadOnlyList<(int StartLine, int EndLine, bool IsFolded)> Foldings { get; }

    /// <summary>Colour token of the character at <paramref name="offset"/>, e.g. <c>syntax.keyword</c>.</summary>
    string TokenClassAt(int offset);

    IReadOnlyList<(int Offset, int Length)> BracketHighlights { get; }

    IReadOnlyList<(int Offset, int Length)> OccurrenceHighlights { get; }

    /// <summary>The find matches drawn while the find panel is open (UI-ED-055); empty when it is closed.</summary>
    IReadOnlyList<(int Offset, int Length)> FindHighlights { get; }

    /// <summary>The match drawn as current (<c>editor.findMatch.current.bg</c> + outline), or null.</summary>
    (int Offset, int Length)? CurrentFindHighlight { get; }

    /// <summary>Width of the visible gutter margins in DIP (glyph margin, line numbers, fold margin, change bar).</summary>
    double GutterWidth { get; }

    /// <summary>
    /// The visual rows of a document line, top to bottom (one row unless word wrap breaks the line), each with what
    /// the gutter draws beside it (UI-ED-014). Read from what the margins draw. Empty when the line is not on screen.
    /// </summary>
    IReadOnlyList<EditorVisualRow> VisualRows(int line);

    /// <summary>IDE-ED-149: the identifier the Ctrl+hover underline is drawn under, or null.</summary>
    (int Offset, int Length)? DeclarationLink { get; }

    /// <summary>Colour token of the Ctrl+hover underline (<c>text.link</c>).</summary>
    string DeclarationLinkToken { get; }

    /// <summary>UI-TOK-053: the text view shows the hand cursor (over a Ctrl+hover link).</summary>
    bool HandCursor { get; }
}

/// <summary>
/// One visual row of a document line (<see cref="IEditorTestProbe.VisualRows"/>): top and height in text-view
/// coordinates (DIP); the glyph drawn beside the row (probe name as in <see cref="IEditorTestProbe.GlyphAt"/>) or null;
/// whether a fold marker is drawn beside it; the change-bar state drawn over its full height ("None" | "Modified" | "Saved").
/// </summary>
public sealed record EditorVisualRow(double Top, double Height, string? Glyph, bool FoldMarker, string ChangeBar);
