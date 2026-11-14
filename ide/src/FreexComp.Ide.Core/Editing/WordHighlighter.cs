using System.ComponentModel;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// Word-occurrence highlight (IDE-ED-074, UI-ED-054): <see cref="Delay"/> after the caret or selection last changed,
/// <see cref="Highlights"/> become the other occurrences of the identifier at the caret (<see cref="CodeTokens.Occurrences"/>);
/// any caret, selection or text change clears them at once. Off while <c>editor.highlightWordOccurrences</c> is off.
/// A large buffer whose tokens are still being scanned is highlighted when they arrive.
/// </summary>
public sealed class WordHighlighter
{
    /// <summary>The caret rests this long before occurrences are shown.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    private readonly ITextBuffer _buffer;
    private readonly EditorViewState _viewState;
    private readonly CodeTokenIndex _tokens;
    private readonly Func<IClock?> _clock;
    private readonly Func<bool> _enabled;
    private IDisposable? _timer;
    private bool _resting;

    public WordHighlighter(ITextBuffer buffer, EditorViewState viewState, CodeTokenIndex tokens, Func<IClock?> clock, Func<bool> enabled)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _viewState = viewState ?? throw new ArgumentNullException(nameof(viewState));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        buffer.Changed += (_, _) => Restart();
        viewState.PropertyChanged += OnViewStateChanged;
        tokens.Changed += (_, _) =>
        {
            if (_resting)
                Update();
        };
    }

    /// <summary>The highlighted identifiers (not the one at the caret), sorted by offset.</summary>
    public IReadOnlyList<CodeToken> Highlights { get; private set; } = [];

    public event EventHandler? Changed;

    /// <summary><c>editor.highlightWordOccurrences</c> changed.</summary>
    public void OptionChanged() => Restart();

    private void OnViewStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewState.Caret) or nameof(EditorViewState.SelectionAnchor))
            Restart();
    }

    private void Restart()
    {
        _timer?.Dispose();
        _timer = null;
        _resting = false;
        Set([]);
        if (!_enabled())
            return;
        if (_clock() is { } clock)
            _timer = clock.Schedule(Delay, Rest);
        else
            Rest();
    }

    private void Rest()
    {
        _timer = null;
        _resting = true;
        Update();
    }

    private void Update()
    {
        if (!_enabled() || _tokens.Current is not { } tokens)
            return; // a background scan in progress: Changed comes back here
        var caret = Offset(_viewState.Caret);
        var selection = _viewState.SelectionAnchor is { } anchor ? new TextSelection(Offset(anchor), caret) : TextSelection.At(caret);
        Set(tokens.Occurrences(caret, selection));
    }

    private int Offset(TextPosition position) => _buffer.GetOffset(Math.Clamp(position.Line, 1, _buffer.LineCount), position.Column);

    private void Set(IReadOnlyList<CodeToken> highlights)
    {
        if (highlights.Count == 0 && Highlights.Count == 0)
            return;
        Highlights = highlights;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
