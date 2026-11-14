using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Editing;

/// <summary>
/// The <see cref="CodeTokens"/> of a document's current text (bracket matching, indent guides, word highlight).
/// Scanned once per buffer version; a buffer larger than <see cref="WholeTextClassifier.BackgroundThreshold"/> is scanned
/// on the thread pool (IDE-ARCH-081, the classifier's rule): until the result for the current version arrives
/// <see cref="Current"/> is null, and <see cref="Changed"/> announces it. Used on the UI thread.
/// </summary>
public sealed class CodeTokenIndex
{
    private readonly ITextBuffer _buffer;
    private ILanguageDefinition _language;
    private CodeTokens? _tokens;
    private Task<CodeTokens>? _running;

    public CodeTokenIndex(ITextBuffer buffer, ILanguageDefinition language)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _language = language ?? throw new ArgumentNullException(nameof(language));
    }

    /// <summary>A background scan ended (raised on the synchronization context that started it): ask <see cref="Current"/> again.</summary>
    public event EventHandler? Changed;

    /// <summary>A background scan is in progress or its result has not been delivered yet (tests drain it, I-179).</summary>
    internal bool IsScanning => _running is { IsCompleted: false };

    /// <summary>The tokens of the buffer's current version, or null while a large buffer is being scanned.</summary>
    public CodeTokens? Current
    {
        get
        {
            if (_tokens?.Version == _buffer.Version)
                return _tokens;
            if (_buffer.Length <= WholeTextClassifier.BackgroundThreshold)
                return _tokens = _language.ScanCodeTokens(_buffer.Text).WithVersion(_buffer.Version);
            StartBackground();
            return null;
        }
    }

    /// <summary>The tokens of the current version, waiting for a background scan without blocking.</summary>
    public async Task<CodeTokens> GetAsync()
    {
        while (true)
        {
            if (Current is { } current)
                return current;
            await _running!.ConfigureAwait(true);
        }
    }

    /// <summary>The document's language changed (Save As with another extension).</summary>
    public void SetLanguage(ILanguageDefinition language)
    {
        _language = language ?? throw new ArgumentNullException(nameof(language));
        _tokens = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One scan at a time; an outdated result is dropped and the next <see cref="Current"/> scans again (IDE-ARCH-083).</summary>
    private void StartBackground()
    {
        if (_running is { IsCompleted: false })
            return;
        var version = _buffer.Version;
        var text = _buffer.Text;
        var language = _language;
        var context = SynchronizationContext.Current;
        _running = Task.Run(() => language.ScanCodeTokens(text).WithVersion(version)).ContinueWith(
            t =>
            {
                if (version == _buffer.Version && language == _language)
                    _tokens = t.Result;
                Changed?.Invoke(this, EventArgs.Empty); // an outdated result: the next Current scans the new text
                return t.Result;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            context is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext());
    }
}
