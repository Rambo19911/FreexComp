using System.ComponentModel;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// The fold regions of one document and which of them are folded (editor-features.md §5). Regions are recomputed
/// from the language's <see cref="IFoldingProvider"/> after every change; a folded region stays folded when the new
/// region at its (shifted) start line has the same kind and the same start-line text (IDE-ED-056). The folded start
/// lines are mirrored into <see cref="EditorViewState.FoldedLines"/>; setting that list from outside (session restore)
/// folds the regions at those lines (IDE-ED-057). A caret or selection end placed inside a folded region, and an edit
/// inside one or right after its <c>…</c> box, unfolds it (IDE-ED-055). The editor view draws <see cref="Regions"/>.
/// </summary>
public sealed class FoldingModel
{
    private static readonly FoldKind[] FoldAllKinds = [FoldKind.Routine, FoldKind.Type, FoldKind.Region, FoldKind.Comment];
    private static readonly FoldKind[] Containers = [FoldKind.Routine, FoldKind.Type, FoldKind.Block, FoldKind.Uses];

    private readonly ITextBuffer _buffer;
    private readonly EditorViewState _viewState;
    private IFoldingProvider? _provider;
    private List<FoldRegion> _regions = [];
    private bool _writingViewState;

    public FoldingModel(ITextBuffer buffer, EditorViewState viewState, IFoldingProvider? provider)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _viewState = viewState ?? throw new ArgumentNullException(nameof(viewState));
        _provider = provider;
        buffer.Changed += OnBufferChanged;
        viewState.PropertyChanged += OnViewStateChanged;
        Recompute();
    }

    /// <summary>Sorted by start offset, outer before inner.</summary>
    public IReadOnlyList<FoldRegion> Regions => _regions;

    /// <summary>The regions or their folded state changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The document's language changed (Save As with another extension).</summary>
    public void SetProvider(IFoldingProvider? provider)
    {
        _provider = provider;
        Recompute();
    }

    public void SetFolded(FoldRegion region, bool folded)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (region.IsFolded != folded && _regions.Contains(region))
        {
            SetFoldedCore(region, folded);
            Publish();
        }
    }

    /// <summary>Folds to restore once the regions of this buffer version are computed (a move in a large buffer).</summary>
    private (long Version, List<(int Line, FoldKind Kind, string Text)> Folds)? _pendingFolds;

    /// <summary>
    /// IDE-ED-033: runs the line-move <paramref name="edit"/>; every folded region lying inside one of
    /// <paramref name="moves"/> (1-based line ranges moved by <c>Delta</c> lines) is folded again at its new place, matched by
    /// kind and start-line text (the edit itself would unfold or lose it). Returns the edit's result.
    /// </summary>
    public bool KeepFoldedThroughMove(IReadOnlyList<(int First, int Last, int Delta)> moves, Func<bool> edit)
    {
        ArgumentNullException.ThrowIfNull(moves);
        ArgumentNullException.ThrowIfNull(edit);
        List<(int Line, FoldKind Kind, string Text)> keep =
            [.. from r in _regions where r.IsFolded from m in moves where r.StartLine >= m.First && r.EndLine <= m.Last select (r.StartLine + m.Delta, r.Kind, r.StartLineText)];
        if (!edit())
            return false;
        if (keep.Count == 0)
            return true;
        if (!_computing)
        {
            if (Refold(keep))
                Publish();
        }
        else
        {
            _pendingFolds = (_buffer.Version, keep); // applied when the background regions of this version arrive
        }

        return true;
    }

    private bool Refold(List<(int Line, FoldKind Kind, string Text)> folds)
    {
        bool any = false;
        foreach (var (line, kind, text) in folds)
        {
            if (_regions.FirstOrDefault(r => !r.IsFolded && r.StartLine == line && r.Kind == kind && r.StartLineText == text) is { } region)
            {
                SetFoldedCore(region, true);
                any = true;
            }
        }

        return any;
    }

    // edit.fold / edit.unfold / edit.foldAll / edit.unfoldAll (IDE-ED-054)

    public bool CanFold => RegionToFold() is not null;
    public bool CanUnfold => RegionToUnfold() is not null;
    public bool CanFoldAll => _regions.Any(r => !r.IsFolded && IsFoldAllKind(r));
    public bool CanUnfoldAll => _regions.Any(r => r.IsFolded);

    /// <summary>Folds the innermost unfolded region containing the caret line; a caret it would hide moves to its start line.</summary>
    public bool Fold() => RegionToFold() is { } region && Apply(() => SetFoldedCore(region, true));

    /// <summary>Unfolds the innermost folded region starting on the caret line (or whose <c>…</c> box the caret follows).</summary>
    public bool Unfold() => RegionToUnfold() is { } region && Apply(() => SetFoldedCore(region, false));

    /// <summary>Folds every routine, type, region and top-level multi-line comment.</summary>
    public bool FoldAll() => CanFoldAll && Apply(() =>
    {
        foreach (var region in _regions.Where(r => !r.IsFolded && IsFoldAllKind(r)).ToList())
            SetFoldedCore(region, true);
    });

    public bool UnfoldAll() => CanUnfoldAll && Apply(() =>
    {
        foreach (var region in _regions.Where(r => r.IsFolded))
            SetFoldedCore(region, false);
    });

    /// <summary>Unfolds every folded region hiding <paramref name="offset"/> (IDE-ED-055).</summary>
    public void Reveal(int offset)
    {
        bool changed = false;
        foreach (var region in _regions)
        {
            if (region.IsFolded && Hides(region, offset))
            {
                SetFoldedCore(region, false);
                changed = true;
            }
        }

        if (changed)
            Publish();
    }

    private static bool Hides(FoldRegion region, int offset) => region.StartOffset < offset && offset < region.EndOffset;

    private bool Apply(Action change)
    {
        change();
        Publish();
        return true;
    }

    private int CaretOffset => OffsetOf(_viewState.Caret);

    private int OffsetOf(TextPosition position) =>
        _buffer.GetOffset(Math.Clamp(position.Line, 1, _buffer.LineCount), position.Column);

    private FoldRegion? RegionToFold()
    {
        int line = Math.Clamp(_viewState.Caret.Line, 1, _buffer.LineCount);
        return Innermost(_regions.Where(r => !r.IsFolded && r.StartLine <= line && line <= r.EndLine));
    }

    private FoldRegion? RegionToUnfold()
    {
        int line = _viewState.Caret.Line, caret = CaretOffset;
        return Innermost(_regions.Where(r => r.IsFolded && (r.StartLine == line || r.EndOffset == caret)));
    }

    private static FoldRegion? Innermost(IEnumerable<FoldRegion> candidates) =>
        candidates.OrderByDescending(r => r.StartOffset).ThenBy(r => r.EndOffset).FirstOrDefault();

    /// <summary>IDE-ED-054: (a), (b), (g) and multi-line comments not inside a routine, type, block or uses clause.</summary>
    private bool IsFoldAllKind(FoldRegion region) =>
        FoldAllKinds.Contains(region.Kind)
        && (region.Kind != FoldKind.Comment || !_regions.Any(c => c != region && Containers.Contains(c.Kind)
            && c.StartOffset <= region.StartOffset && region.EndOffset <= c.EndOffset));

    private void SetFoldedCore(FoldRegion region, bool folded)
    {
        region.IsFolded = folded;
        region.Anchor = folded ? _buffer.CreateAnchor(_buffer.GetLine(region.StartLine).Offset, movesWithInsertionBefore: true) : null;
        if (!folded)
            return;

        // A caret or selection end the fold would hide moves to the end of the start line's text.
        if (_viewState.SelectionAnchor is { } anchor && Hides(region, OffsetOf(anchor)))
            _viewState.SelectionAnchor = null;
        if (Hides(region, CaretOffset))
        {
            _viewState.SelectionAnchor = null;
            _viewState.Caret = _buffer.GetPosition(region.StartOffset);
        }
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        // IDE-ED-055: an edit inside a folded region, or typed right after its box, unfolds it.
        foreach (var region in _regions)
        {
            if (region.IsFolded && region.StartOffset < e.Offset && e.Offset <= region.EndOffset)
            {
                region.IsFolded = false;
                region.Anchor = null;
            }
        }

        Recompute();
    }

    /// <summary>
    /// Regions of the current text: at once, or for a buffer larger than <see cref="Languages.WholeTextClassifier.BackgroundThreshold"/>
    /// from a snapshot on the thread pool (IDE-ARCH-081, I-159); until that result arrives the previous regions stay.
    /// </summary>
    private void Recompute()
    {
        if (_provider is { } provider && _buffer.Length > Languages.WholeTextClassifier.BackgroundThreshold)
            StartBackground(provider);
        else
            Apply(_provider?.GetRegions(_buffer).ToList() ?? []);
    }

    private volatile bool _computing;

    /// <summary>Starts a background computation (the thread pool; tests replace it to decide when a run ends, I-204).</summary>
    internal Func<Func<List<FoldRegion>>, Task<List<FoldRegion>>> RunInBackground { get; set; } = work => Task.Run(work);

    /// <summary>A background run is in progress or its result has not been applied yet (tests drain it, I-179).</summary>
    internal bool IsComputing => _computing;

    /// <summary>
    /// One run at a time over a <see cref="StringTextBuffer"/> snapshot; a result for an outdated version (or another
    /// provider) is dropped and the current text is folded again (IDE-ARCH-083). The result is applied on the
    /// synchronization context that started the run (the UI thread). I-204: the run counts as running from before it
    /// starts until its continuation begins, so a run that ends at once cannot let a second one start beside the next.
    /// </summary>
    private void StartBackground(IFoldingProvider provider)
    {
        if (_computing)
            return; // the running result will be outdated: it re-runs when it ends
        _computing = true;
        var version = _buffer.Version;
        var text = _buffer.Text;
        var context = SynchronizationContext.Current;
        _ = RunInBackground(() => provider.GetRegions(new StringTextBuffer(text)).ToList()).ContinueWith(
            t =>
            {
                if (version == _buffer.Version && ReferenceEquals(provider, _provider))
                {
                    Apply(t.Status == TaskStatus.RanToCompletion ? t.Result : []); // a provider must never throw
                    _computing = false; // after the regions: "not computing" never precedes them
                }
                else
                {
                    _computing = false;
                    Recompute();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            context is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>The new regions replace the old ones; folded regions stay folded where they still are (IDE-ED-056).</summary>
    private void Apply(List<FoldRegion> regions)
    {
        var folded = _regions.Where(r => r.IsFolded).ToList();
        foreach (var region in regions)
        {
            var same = folded.FirstOrDefault(old => old.Kind == region.Kind && old.StartLineText == region.StartLineText
                && StartLineNow(old) == region.StartLine);
            if (same is null)
                continue;
            region.IsFolded = true;
            region.Anchor = same.Anchor;
            folded.Remove(same);
        }

        _regions = regions;
        if (_pendingFolds is { } pending && pending.Version == _buffer.Version)
        {
            _pendingFolds = null;
            Refold(pending.Folds);
        }

        Publish();
    }

    /// <summary>Where a folded region's start line is now: its anchor's line, or (text replaced as a whole) its old line.</summary>
    private int StartLineNow(FoldRegion old) =>
        old.Anchor is { IsDeleted: false } anchor ? _buffer.GetPosition(Math.Min(anchor.Offset, _buffer.Length)).Line : old.StartLine;

    private void Publish()
    {
        _writingViewState = true;
        try
        {
            var lines = _regions.Where(r => r.IsFolded).Select(r => r.StartLine).Distinct().Order().ToList();
            if (!lines.SequenceEqual(_viewState.FoldedLines))
                _viewState.FoldedLines = lines;
        }
        finally
        {
            _writingViewState = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EditorViewState.FoldedLines) when !_writingViewState:
                var lines = _viewState.FoldedLines.ToHashSet();
                foreach (var region in _regions)
                {
                    if (region.IsFolded != lines.Contains(region.StartLine))
                        SetFoldedCore(region, !region.IsFolded);
                }

                Publish();
                break;
            case nameof(EditorViewState.Caret):
                Reveal(CaretOffset);
                break;
            case nameof(EditorViewState.SelectionAnchor) when _viewState.SelectionAnchor is { } anchor:
                Reveal(OffsetOf(anchor));
                break;
        }
    }
}
