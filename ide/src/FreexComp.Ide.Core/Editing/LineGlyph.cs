namespace FreexComp.Ide.Core.Editing;

/// <summary>The glyphs of the editor's glyph margin (editor.md §3).</summary>
public enum LineGlyphKind
{
    Execution,
    Frame,
    Breakpoint,
    BreakpointDisabled,
    BreakpointConditional,
    BreakpointConditionalDisabled,
    BreakpointUnverified,
    Bookmark,
    Error,
}

/// <summary>
/// One glyph on a line. <paramref name="Number"/> is the digit of a numbered bookmark (1–9, 0 = none);
/// <paramref name="ToolTip"/> is the provider's tooltip text (UI-ED-021).
/// </summary>
public sealed record LineGlyph(LineGlyphKind Kind, int Number = 0, string? ToolTip = null)
{
    /// <summary>editor.md §3: 1 (highest) = execution arrow … 5 = error marker.</summary>
    public int Priority => Kind switch
    {
        LineGlyphKind.Execution => 1,
        LineGlyphKind.Frame => 2,
        LineGlyphKind.Bookmark => 4,
        LineGlyphKind.Error => 5,
        _ => 3, // breakpoints in any state
    };

    public bool IsBreakpoint => Priority == 3;

    /// <summary>The name <c>IEditorTestProbe.GlyphAt</c> reports: the kind, a numbered bookmark with its digit (<c>Bookmark3</c>).</summary>
    public string ProbeName => Kind == LineGlyphKind.Bookmark && Number > 0 ? "Bookmark" + Number : Kind.ToString();
}

/// <summary>
/// A source of glyph-margin glyphs (architecture.md §11 "Editor margin / renderer"): bookmarks (P3), error markers
/// (P3), breakpoints and the execution point (P6). Raise <see cref="Changed"/> when any line's glyphs change.
/// </summary>
public interface IEditorDecorationProvider
{
    /// <summary>The glyphs of the 1-based <paramref name="line"/>, in any order.</summary>
    IReadOnlyList<LineGlyph> GetLineGlyphs(int line);

    event EventHandler? Changed;
}

/// <summary>
/// The decoration providers of one text document and the rules that combine them: one glyph per line by priority
/// (editor.md §3) and the line background that goes with it (UI-ED-020).
/// </summary>
public sealed class EditorDecorations
{
    private readonly List<IEditorDecorationProvider> _providers = [];

    public event EventHandler? Changed;

    public void Add(IEditorDecorationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
        provider.Changed += OnProviderChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(IEditorDecorationProvider provider)
    {
        if (!_providers.Remove(provider))
            return false;
        provider.Changed -= OnProviderChanged;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>All glyphs of the line, highest priority first (the first is drawn, all go into the tooltip).</summary>
    public IReadOnlyList<LineGlyph> GlyphsAt(int line) =>
        _providers.Count == 0 ? [] : [.. _providers.SelectMany(p => p.GetLineGlyphs(line)).OrderBy(g => g.Priority)];

    /// <summary>The glyph drawn on the line, or null.</summary>
    public LineGlyph? GlyphAt(int line) => GlyphsAt(line).FirstOrDefault();

    /// <summary>
    /// UI-ED-020: the colour token of the line's background — execution &gt; selected call frame &gt; enabled
    /// breakpoint (disabled and unverified ones have none) — or null.
    /// </summary>
    public string? LineBackgroundAt(int line)
    {
        var glyphs = GlyphsAt(line);
        if (glyphs.Any(g => g.Kind == LineGlyphKind.Execution))
            return "editor.execution.line.bg";
        if (glyphs.Any(g => g.Kind == LineGlyphKind.Frame))
            return "editor.callFrame.line.bg";
        if (glyphs.Any(g => g.Kind is LineGlyphKind.Breakpoint or LineGlyphKind.BreakpointConditional))
            return "editor.breakpoint.line.bg";
        return null;
    }

    private void OnProviderChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
