namespace FreexComp.Ide.Editor;

/// <summary>
/// Read-only view of what the code editor draws, for headless tests (editor.md §13.3, IDE-ARCH-113, IDE-TEST-010):
/// gutter glyphs, line backgrounds, markers and fold markers have no visual children. Implemented by
/// <see cref="CodeEditorView"/>; not used by product code. Lines are 1-based.
/// </summary>
public interface IEditorTestProbe
{
    /// <summary>"Breakpoint", "BreakpointDisabled", "BreakpointConditional", "BreakpointUnverified", "Execution", "Frame", "Bookmark", "Bookmark3", "Error", or null.</summary>
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
}
