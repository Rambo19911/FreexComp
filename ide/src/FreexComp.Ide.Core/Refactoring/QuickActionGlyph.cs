using System.ComponentModel;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Refactoring;

/// <summary>The quick-action glyphs of the open text documents (IDE-REFAC-035).</summary>
public sealed partial class RefactoringService
{
    private readonly Dictionary<TextDocumentViewModel, QuickActionGlyph> _glyphs = [];

    private void AttachGlyph(TextDocumentViewModel document)
    {
        var glyph = new QuickActionGlyph(this, document, _clock);
        _glyphs.Add(document, glyph);
        document.Decorations.Add(glyph);
    }

    private void DetachGlyph(TextDocumentViewModel document)
    {
        if (!_glyphs.Remove(document, out var glyph))
            return;
        glyph.Dispose();
        document.Decorations.Remove(glyph);
    }

    /// <summary>The view in front shows its document again: the quick actions of its caret position are asked for.</summary>
    private void OnActiveDocumentChanged()
    {
        if (ActiveWritableText() is { } document && _glyphs.TryGetValue(document, out var glyph))
            glyph.Restart();
    }

    /// <summary>
    /// The glyph of <paramref name="document"/> is asked for: <c>edit.quickActions</c> is available in this composition and
    /// the document is the writable text view in front.
    /// </summary>
    internal bool AsksForGlyph(TextDocumentViewModel document) =>
        IsAvailable(QuickActionsCommandId) && ReferenceEquals(ActiveWritableText(), document);

    /// <summary>A request for the quick actions of <paramref name="document"/> is collecting (IDE-REFAC-032: one per document).</summary>
    internal bool IsCollecting(TextDocumentViewModel document) => _collecting.ContainsKey(document);

    /// <summary>
    /// UI-ED-150: <c>Msg.QuickActions.Available</c> with the gesture <c>edit.quickActions</c> has now; a command without a
    /// gesture is named by its title.
    /// </summary>
    internal string GlyphToolTip()
    {
        var command = _commands!.Get(QuickActionsCommandId);
        return Format(Strings.Msg_QuickActions_Available, command.GestureText.Length > 0 ? command.GestureText : command.Title);
    }
}

/// <summary>
/// The quick-action glyph of one text document (IDE-REFAC-035, UI-ED-150): <see cref="RestDelay"/> after the caret came
/// to rest — no caret move, no text change, no selection change in that time — the quick actions of the caret position
/// are asked for (an empty selection; one request in flight, cancelled by the next change), and when the answer has at
/// least one fix the caret line shows the glyph. It is removed at once when the caret leaves the line, the text changes,
/// the document becomes read only or closes. Refactorings alone never show it. Asking does not make the engine busy.
/// </summary>
internal sealed class QuickActionGlyph : IEditorDecorationProvider, IDisposable
{
    /// <summary>The caret rests this long before the quick actions of its position are asked for.</summary>
    internal static readonly TimeSpan RestDelay = TimeSpan.FromMilliseconds(500);

    private readonly RefactoringService _owner;
    private readonly TextDocumentViewModel _document;
    private readonly IClock _clock;
    private IDisposable? _timer;
    private CancellationTokenSource? _request; // the question that is awaited
    private int _line; // the 1-based line that shows the glyph; 0 = none
    private bool _disposed;

    internal QuickActionGlyph(RefactoringService owner, TextDocumentViewModel document, IClock clock)
    {
        _owner = owner;
        _document = document;
        _clock = clock;
        document.Buffer.Changed += OnBufferChanged;
        document.ViewState.PropertyChanged += OnViewStateChanged;
        document.PropertyChanged += OnDocumentChanged;
        Restart();
    }

    public event EventHandler? Changed;

    /// <summary>The tooltip is made when it is asked for: it names the gesture the command has at that time.</summary>
    public IReadOnlyList<LineGlyph> GetLineGlyphs(int line) =>
        line == _line ? [new LineGlyph(LineGlyphKind.QuickAction, ToolTip: _owner.GlyphToolTip())] : [];

    /// <summary>The 500 ms start again; the answer to a question that is awaited is no longer wanted.</summary>
    internal void Restart()
    {
        _timer?.Dispose();
        _request?.Cancel();
        _request = null;
        _timer = _disposed ? null : _clock.Schedule(RestDelay, Rest);
    }

    public void Dispose()
    {
        _disposed = true;
        Restart();
        _document.Buffer.Changed -= OnBufferChanged;
        _document.ViewState.PropertyChanged -= OnViewStateChanged;
        _document.PropertyChanged -= OnDocumentChanged;
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        Show(0);
        Restart();
    }

    private void OnViewStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(EditorViewState.Caret) or nameof(EditorViewState.SelectionAnchor)))
            return;
        if (_document.ViewState.Caret.Line != _line)
            Show(0);
        Restart();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        // VisibleText: a form document switched between its views.
        if (e.PropertyName is not (nameof(TextDocumentViewModel.IsReadOnly) or nameof(TextDocumentViewModel.VisibleText)))
            return;
        if (_document.IsReadOnly)
            Show(0);
        Restart();
    }

    /// <summary>The caret has rested.</summary>
    private void Rest()
    {
        _timer = null;
        if (!_owner.AsksForGlyph(_document))
            return; // read only or not the view in front: asked when that changes
        if (_owner.IsBusy || _owner.IsCollecting(_document))
            _timer = _clock.Schedule(RestDelay, Rest); // an operation runs, or edit.quickActions is asking for this document: afterwards
        else
            Ask();
    }

    /// <summary>
    /// Asks for the quick actions of the caret position. Nothing awaits it: an unexpected exception reaches the UI
    /// thread's handler (IDE-ARCH-091).
    /// </summary>
    private async void Ask()
    {
        var request = _request = new CancellationTokenSource();
        var version = _document.Buffer.Version;
        var caret = _document.Selection.Caret;
        bool fix;
        try
        {
            var actions = await _owner.GetQuickActionsAsync(_document, TextSelection.At(caret), request.Token);
            // IDE-ARCH-083: the answer to an older text or caret position is dropped.
            if (request.IsCancellationRequested || version != _document.Buffer.Version || caret != _document.Selection.Caret)
                return;
            fix = actions.Any(a => a.Kind == QuickActionKind.Fix);
        }
        catch (OperationCanceledException)
        {
            // Not cancelled here: a newer request for the quick actions took its place (edit.quickActions). Asked again later.
            if (!request.IsCancellationRequested && !_disposed)
                _timer ??= _clock.Schedule(RestDelay, Rest);
            return;
        }
        finally
        {
            if (ReferenceEquals(_request, request))
                _request = null;
            request.Dispose();
        }

        Show(fix ? _document.ViewState.Caret.Line : 0);
    }

    private void Show(int line)
    {
        if (_line == line)
            return;
        _line = line;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
