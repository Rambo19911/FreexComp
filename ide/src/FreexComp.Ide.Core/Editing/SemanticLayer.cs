using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// The P3 classification layer of one document (phase1-shell.md §5, I-112/I-113): the tree-based syntactic classes
/// (<c>Classifier.GetSyntacticClassifications</c> of the project's binding parse, so conditional code follows the
/// project's defines) with the semantic kinds of <c>Classifier.GetSemanticClassifications</c> in place of plain
/// identifiers. It covers the whole buffer of the version it was computed for; after an edit its spans follow the text
/// and the edited range (widened to the spans the edit touched) falls back to the lexical layer until the next result.
/// </summary>
public sealed class SemanticLayer
{
    private readonly ITextBuffer _buffer;
    private List<TokenClassSpan> _spans = [];
    private List<(int Start, int End)> _dirty = [];

    public SemanticLayer(ITextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        buffer.Changed += OnBufferChanged;
    }

    /// <summary>New spans arrived or the layer was cleared: the view redraws.</summary>
    public event EventHandler? Changed;

    /// <summary>The buffer version the spans were computed for; -1 = none (lexical colours only).</summary>
    public long Version { get; private set; } = -1;

    /// <summary>The layer's spans (sorted, non-overlapping), shifted by the edits since <see cref="Version"/>.</summary>
    public IReadOnlyList<TokenClassSpan> Spans => _spans;

    /// <summary>Takes <paramref name="spans"/> computed for buffer <paramref name="version"/>; a result for an older version is dropped.</summary>
    public bool Set(long version, List<TokenClassSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        if (version != _buffer.Version)
            return false;
        _spans = spans;
        _dirty = [];
        Version = version;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Back to lexical colours only.</summary>
    public void Clear()
    {
        if (Version < 0)
            return;
        _spans = [];
        _dirty = [];
        Version = -1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// <paramref name="lexical"/> (the lexical layer's spans of [<paramref name="start"/>, <paramref name="end"/>)) with
    /// this layer applied: this layer's spans outside the edited ranges, the lexical ones inside them.
    /// </summary>
    public IReadOnlyList<TokenClassSpan> Apply(IReadOnlyList<TokenClassSpan> lexical, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(lexical);
        if (Version < 0)
            return lexical;
        var dirty = _dirty;
        bool InDirty(int s, int e) => dirty.Any(d => s < d.End && e > d.Start || s == e && s >= d.Start && s <= d.End);
        var result = new List<TokenClassSpan>();
        foreach (var span in _spans)
        {
            if (span.Offset + span.Length <= start || span.Offset >= end || InDirty(span.Offset, span.Offset + span.Length))
                continue;
            if (span.TokenClass == ColorTokens.Invalid && lexical.FirstOrDefault(l => l.Offset == span.Offset && l.TokenClass == ColorTokens.Invalid) is { Code: not null } withCode)
                result.Add(span with { Code = withCode.Code, Message = withCode.Message }); // the lexer's message for the error tooltip
            else
                result.Add(span);
        }

        if (dirty.Count > 0)
        {
            foreach (var span in lexical)
            {
                if (InDirty(span.Offset, span.Offset + span.Length))
                    result.Add(span);
            }

            result.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        }

        return result;
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        if (Version < 0)
            return;
        int end = e.Offset + e.RemovedLength, delta = e.InsertedText.Length - e.RemovedLength;
        int dirtyStart = e.Offset, dirtyEnd = e.Offset + e.InsertedText.Length;
        var shifted = new List<TokenClassSpan>(_spans.Count);
        foreach (var span in _spans)
        {
            if (span.Offset + span.Length <= e.Offset)
                shifted.Add(span);
            else if (span.Offset >= end)
                shifted.Add(span with { Offset = span.Offset + delta });
            else
            {
                dirtyStart = Math.Min(dirtyStart, span.Offset);
                dirtyEnd = Math.Max(dirtyEnd, span.Offset + span.Length + delta);
            }
        }

        var dirty = new List<(int Start, int End)>(_dirty.Count + 1);
        foreach (var (s, en) in _dirty)
        {
            if (en < e.Offset)
                dirty.Add((s, en));
            else if (s > end)
                dirty.Add((s + delta, en + delta));
            else
            {
                dirtyStart = Math.Min(dirtyStart, s);
                dirtyEnd = Math.Max(dirtyEnd, Math.Max(en + delta, e.Offset + e.InsertedText.Length));
            }
        }

        dirty.Add((dirtyStart, dirtyEnd));
        _spans = shifted;
        _dirty = dirty;
    }
}

/// <summary>A document's lexical classifier with its <see cref="SemanticLayer"/> applied (what the editor view draws).</summary>
public sealed class LayeredClassifier : ITokenClassifier
{
    private readonly ITokenClassifier _lexical;
    private readonly SemanticLayer _layer;

    public LayeredClassifier(ITokenClassifier lexical, SemanticLayer layer)
    {
        ArgumentNullException.ThrowIfNull(lexical);
        ArgumentNullException.ThrowIfNull(layer);
        _lexical = lexical;
        _layer = layer;
    }

    /// <summary>Either layer changed (handlers are passed through, so a dropped classifier leaves nothing subscribed).</summary>
    public event EventHandler? ClassificationChanged
    {
        add
        {
            _lexical.ClassificationChanged += value;
            _layer.Changed += value;
        }

        remove
        {
            _lexical.ClassificationChanged -= value;
            _layer.Changed -= value;
        }
    }

    public IReadOnlyList<TokenClassSpan> Classify(ITextBuffer buffer, LineRange lines)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var lexical = _lexical.Classify(buffer, lines);
        if (_layer.Version < 0)
            return lexical;
        int first = Math.Clamp(lines.FirstLine, 1, buffer.LineCount);
        int last = Math.Clamp(lines.LastLine, first, buffer.LineCount);
        var (lastOffset, lastLength, lastDelimiter) = buffer.GetLine(last);
        return _layer.Apply(lexical, buffer.GetLine(first).Offset, lastOffset + lastLength + lastDelimiter);
    }
}
