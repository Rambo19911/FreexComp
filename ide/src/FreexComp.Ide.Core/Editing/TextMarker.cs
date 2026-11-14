using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Editing;

/// <summary>Severity of a text marker, least severe first (UI-ED-067: error &gt; warning &gt; hint).</summary>
public enum MarkerSeverity
{
    Hint,
    Warning,
    Error,
}

/// <summary>
/// A marked text range (editor.md §7): squiggle (error, warning) or dots (hint). A zero-length marker is drawn one
/// character wide (UI-ED-066). <paramref name="Code"/> / <paramref name="Message"/> feed the error tooltip (UI-ED-131).
/// </summary>
public sealed record TextMarker(int Offset, int Length, MarkerSeverity Severity, string? Code = null, string? Message = null)
{
    /// <summary>The end of the characters the marker covers: a zero-length marker covers one.</summary>
    public int DrawnEnd => Offset + Math.Max(1, Length);
}

public static class TextMarkers
{
    /// <summary>
    /// UI-ED-067: the pieces actually drawn when markers overlap — every character shows only its most severe marker.
    /// Pieces are sorted, non-overlapping, have length ≥ 1 (a zero-length marker covers one character, UI-ED-066), and
    /// adjacent characters with the same winning marker form one piece.
    /// </summary>
    public static IReadOnlyList<TextMarker> Resolve(IReadOnlyList<TextMarker> markers)
    {
        if (markers.Count <= 1)
            return markers.Count == 0 ? [] : [markers[0] with { Length = Math.Max(1, markers[0].Length) }];

        var points = markers.SelectMany(m => new[] { m.Offset, m.DrawnEnd }).Distinct().Order().ToList();
        var pieces = new List<TextMarker>();
        TextMarker? previous = null; // the marker the last piece came from
        for (int i = 0; i + 1 < points.Count; i++)
        {
            int start = points[i], end = points[i + 1];
            TextMarker? winner = null;
            foreach (var m in markers)
            {
                if (m.Offset <= start && m.DrawnEnd >= end && (winner is null || m.Severity > winner.Severity))
                    winner = m;
            }

            if (winner is null)
            {
                previous = null;
                continue;
            }

            if (ReferenceEquals(winner, previous))
                pieces[^1] = pieces[^1] with { Length = end - pieces[^1].Offset };
            else
                pieces.Add(winner with { Offset = start, Length = end - start });
            previous = winner;
        }

        return pieces;
    }
}

/// <summary>
/// UI-ED-068: in P1 every lexer error token (<c>syntax.invalid</c> of the document's language) gets an error marker.
/// The markers are computed when asked for, once per buffer version; <see cref="Changed"/> fires on every edit and when
/// a background classification arrives.
/// </summary>
public sealed class LexerErrorMarkers : IEditorDecorationProvider
{
    private readonly ITextBuffer _buffer;
    private readonly Func<ILanguageDefinition> _language;
    private ILanguageDefinition? _classifierLanguage;
    private ITokenClassifier? _classifier;
    private long _version = -1;
    private IReadOnlyList<TextMarker> _markers = [];

    public LexerErrorMarkers(ITextBuffer buffer, Func<ILanguageDefinition> language)
    {
        _buffer = buffer;
        _language = language;
        buffer.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<LineGlyph> GetLineGlyphs(int line) => [];

    public IReadOnlyList<TextMarker> GetMarkers()
    {
        var language = _language();
        if (!ReferenceEquals(language, _classifierLanguage))
        {
            _classifier?.ClassificationChanged -= OnClassificationChanged;
            _classifierLanguage = language;
            _classifier = language.CreateClassifier();
            _classifier.ClassificationChanged += OnClassificationChanged;
            _version = -1;
        }

        if (_version != _buffer.Version)
        {
            _version = _buffer.Version;
            _markers = [.. _classifier!.Classify(_buffer, new LineRange(1, _buffer.LineCount))
                .Where(s => s.TokenClass == ColorTokens.Invalid)
                .Select(s => new TextMarker(s.Offset, s.Length, MarkerSeverity.Error))];
        }

        return _markers;
    }

    private void OnClassificationChanged(object? sender, EventArgs e)
    {
        _version = -1; // the background spans of this version arrived
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
