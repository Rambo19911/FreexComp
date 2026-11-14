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
/// <paramref name="ToolTip"/> is the provider's text for kinds without a standard one (an error marker's first error
/// message, a call frame); <paramref name="Condition"/> / <paramref name="PassCount"/> describe a breakpoint (UI-ED-021).
/// </summary>
public sealed record LineGlyph(LineGlyphKind Kind, int Number = 0, string? ToolTip = null, string? Condition = null, int PassCount = 0)
{
    /// <summary>
    /// UI-ED-021: the glyph's tooltip line on 1-based <paramref name="line"/>, from the <c>Msg.Editor.Tip.*</c> texts
    /// (breakpoint with condition / pass count / disabled, unverified breakpoint, bookmark, execution point); an error
    /// marker or call frame shows the provider's <see cref="ToolTip"/>. Null = no line.
    /// </summary>
    public string? FormatToolTip(int line)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        switch (Kind)
        {
            case LineGlyphKind.BreakpointUnverified:
                return Resources.Strings.Msg_Debug_BreakpointUnverified;
            case LineGlyphKind.Bookmark:
                return Number > 0 ? string.Format(culture, Resources.Strings.Msg_Editor_Tip_BookmarkNumbered, Number) : Resources.Strings.Msg_Editor_Tip_Bookmark;
            case LineGlyphKind.Execution:
                return Resources.Strings.Msg_Editor_Tip_ExecutionPoint;
            case LineGlyphKind.Error or LineGlyphKind.Frame:
                return ToolTip;
        }

        var text = new System.Text.StringBuilder(string.Format(culture, Resources.Strings.Msg_Editor_Tip_Breakpoint, line));
        if (Condition is { Length: > 0 })
            text.Append(string.Format(culture, Resources.Strings.Msg_Editor_Tip_BreakpointCondition, Condition));
        if (PassCount > 0)
            text.Append(string.Format(culture, Resources.Strings.Msg_Editor_Tip_BreakpointPassCount, PassCount));
        if (Kind is LineGlyphKind.BreakpointDisabled or LineGlyphKind.BreakpointConditionalDisabled)
            text.Append(Resources.Strings.Msg_Editor_Tip_BreakpointDisabled);
        return text.ToString();
    }

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
/// A source of glyph-margin glyphs and text markers (architecture.md §11 "Editor margin / renderer"): lexer errors
/// (P1), bookmarks and diagnostics (P3), breakpoints and the execution point (P6). Raise <see cref="Changed"/> when
/// any line's glyphs or any marker change.
/// </summary>
public interface IEditorDecorationProvider
{
    /// <summary>The glyphs of the 1-based <paramref name="line"/>, in any order.</summary>
    IReadOnlyList<LineGlyph> GetLineGlyphs(int line);

    /// <summary>The provider's text markers (editor.md §7), in any order; may overlap.</summary>
    IReadOnlyList<TextMarker> GetMarkers() => [];

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

    /// <summary>Every provider's markers.</summary>
    public IReadOnlyList<TextMarker> Markers => _providers.Count == 0 ? [] : [.. _providers.SelectMany(p => p.GetMarkers())];

    /// <summary>The marker pieces drawn: the most severe marker per character (UI-ED-067, <see cref="TextMarkers.Resolve"/>).</summary>
    public IReadOnlyList<TextMarker> DrawnMarkers => TextMarkers.Resolve(Markers);

    /// <summary>The most rows the error tooltip lists (UI-ED-131).</summary>
    public const int MaxErrorTipRows = 5;

    /// <summary>
    /// UI-ED-131: the markers covering the character at <paramref name="offset"/> (a zero-length marker covers one,
    /// UI-ED-066), most severe first (ties keep provider order), at most <see cref="MaxErrorTipRows"/>.
    /// </summary>
    public IReadOnlyList<TextMarker> MarkersAt(int offset) =>
        [.. Markers.Where(m => m.Offset <= offset && offset < m.DrawnEnd).OrderByDescending(m => m.Severity).Take(MaxErrorTipRows)];

    /// <summary>UI-ED-021: the tooltip of the line's glyphs, one line per glyph (highest priority first), or null.</summary>
    public string? ToolTipAt(int line)
    {
        var lines = GlyphsAt(line).Select(g => g.FormatToolTip(line)).OfType<string>().ToList();
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

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
