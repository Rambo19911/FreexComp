using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// I-112/I-113 (phase1-shell.md §5): every open Pascal document of the project gets its <see cref="SemanticLayer"/> filled
/// <c>editor.liveDiagnostics.delayMs</c> after opening and after the last edit: the tree-based syntactic classes of the
/// project's binding parse (so <c>{$IFDEF}</c> follows the active configuration's defines) with the semantic kinds
/// (<c>syntax.type</c>, <c>syntax.constant</c>, <c>syntax.routine</c> …) on the identifiers. Loose files keep the lexical
/// colours (IDE-ED-094). The work runs on the <see cref="CodeModelWorker"/>; a result for an outdated buffer is dropped.
/// </summary>
public sealed class SemanticHighlightService
{
    private readonly CodeModelService _codeModel;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<TextDocumentViewModel, Highlighter> _highlighters = [];
    private string? _defines;

    public SemanticHighlightService(IDocumentService documents, CodeModelService codeModel, ISettingsService settings, IClock clock, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _codeModel = codeModel;
        _settings = settings;
        _clock = clock;
        _dispatcher = dispatcher;
        documents.DocumentOpened += (_, e) => Attach(e.Document);
        documents.DocumentClosed += (_, e) => Detach(e.Document);
        foreach (var document in documents.Documents)
            Attach(document);
    }

    /// <summary>
    /// IDE-CFG-002: a project change that alters the effective defines of the active configuration — an edit of the
    /// configuration, of one of its ancestors or of the project-level list — colours the open documents again.
    /// IDE-PLAT-006: so does a change of the active platform (its predefined symbols) or of that platform's defines.
    /// </summary>
    internal void OnProjectChanged(FreexComp.Build.Project? definition)
    {
        var defines = definition is null ? null : DefinesKey(definition);
        if (defines == _defines)
            return;
        _defines = defines;
        foreach (var highlighter in _highlighters.Values)
            highlighter.Schedule();
    }

    /// <summary>The effective defines of the active configuration, the active platform and that platform's defines, as one text.</summary>
    internal static string DefinesKey(FreexComp.Build.Project definition)
    {
        var platform = FreexComp.Build.ProjectOptions.GetEffectivePlatform(definition);
        return string.Join(";", Projects.ConfigurationEdits.Effective(definition, definition.ActiveConfiguration)?.Defines ?? [])
            + "|" + platform + "|" + string.Join(";", definition.PlatformOptions.GetValueOrDefault(platform)?.Defines ?? []);
    }

    /// <summary>The analysis of <paramref name="document"/> that is running or last ran (tests await it).</summary>
    internal Task PendingFor(TextDocumentViewModel document) =>
        _highlighters.TryGetValue(document, out var h) ? h.Pending : Task.CompletedTask;

    /// <summary>
    /// The layer's spans of <paramref name="document"/> (<paramref name="path"/> in <paramref name="compilation"/>): the
    /// syntactic classes of the semantic model's own tree, an identifier replaced by its semantic kind. Colourless kinds are left out.
    /// </summary>
    internal static List<TokenClassSpan> Compute(Document document, CancellationToken ct)
    {
        var model = document.SemanticModel;
        var range = new TextSpan(0, model.SyntaxTree.Text.Text.Length);
        var semantic = new Dictionary<int, ClassificationKind>();
        foreach (var span in Classifier.GetSemanticClassifications(document, range, ct))
        {
            if (span.Kind != ClassificationKind.DeprecatedSymbol)
                semantic[span.Span.Start] = span.Kind;
        }

        var spans = new List<TokenClassSpan>();
        IReadOnlyList<ClassifiedSpan>? lexical = null;
        foreach (var span in Classifier.GetSyntacticClassifications(model.SyntaxTree, range))
        {
            if (span.Kind == ClassificationKind.Invalid)
            {
                // The tree classifier marks tokens the parser skipped as Invalid; only lexer errors are Invalid (CMP-710),
                // so inside such a span the lexical classes are used.
                lexical ??= Classifier.GetLexicalClassifications(Compiler.Syntax.Lexer.Tokenize(model.SyntaxTree.Text), range);
                foreach (var l in lexical.Where(l => l.Span.Start < span.Span.End && l.Span.End > span.Span.Start))
                {
                    var color = Classifier.GetColorToken(l.Kind);
                    int start = Math.Max(l.Span.Start, span.Span.Start), end = Math.Min(l.Span.End, span.Span.End);
                    if (color.Length > 0 && end > start)
                        spans.Add(new TokenClassSpan(start, end - start, color));
                }

                continue;
            }

            var kind = span.Kind == ClassificationKind.Identifier && semantic.TryGetValue(span.Span.Start, out var k) ? k : span.Kind;
            var token = Classifier.GetColorToken(kind);
            if (token.Length > 0)
                spans.Add(new TokenClassSpan(span.Span.Start, span.Span.Length, token));
        }

        return spans;
    }

    private TimeSpan Delay => TimeSpan.FromMilliseconds(_settings.Get(SettingsCatalog.EditorLiveDiagnosticsDelayMs));

    private void Attach(DocumentViewModel document)
    {
        if (document is not TextDocumentViewModel { FilePath: not null } text || text.Language.Id != "pascal" || _highlighters.ContainsKey(text))
            return;
        var highlighter = new Highlighter(this, text);
        _highlighters.Add(text, highlighter);
        highlighter.Schedule();
    }

    private void Detach(DocumentViewModel document)
    {
        if (document is TextDocumentViewModel text && _highlighters.Remove(text, out var highlighter))
            highlighter.Dispose();
    }

    private sealed class Highlighter : IDisposable
    {
        private readonly SemanticHighlightService _service;
        private readonly TextDocumentViewModel _document;
        private IDisposable? _timer;
        private bool _disposed;

        public Highlighter(SemanticHighlightService service, TextDocumentViewModel document)
        {
            _service = service;
            _document = document;
            document.Buffer.Changed += OnBufferChanged;
        }

        public Task Pending { get; private set; } = Task.CompletedTask;

        public void Schedule()
        {
            _timer?.Dispose();
            _timer = _service._clock.Schedule(_service.Delay, Analyse);
        }

        public void Dispose()
        {
            _disposed = true;
            _document.Buffer.Changed -= OnBufferChanged;
            _timer?.Dispose();
            _timer = null;
        }

        private void OnBufferChanged(object? sender, TextChangedEventArgs e) => Schedule();

        private void Analyse()
        {
            _timer?.Dispose();
            _timer = null;
            if (_disposed || _document.FilePath is not { } path || !_service._codeModel.IsProjectFile(path) || _service._codeModel.Synchronize() is null)
            {
                _document.Semantic.Clear();
                return;
            }

            var version = _document.Buffer.Version;
            var source = _service._codeModel.Workspace.GetDocument(path);
            Pending = AnalyseAsync(source, version);
        }

        private async Task AnalyseAsync(Document source, long version)
        {
            List<TokenClassSpan> spans;
            try
            {
                spans = await _service._codeModel.Worker.RunAsync("semanticHighlight:" + source.Path, ct => Compute(source, ct));
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var done = new TaskCompletionSource();
            _service._dispatcher.Post(() =>
            {
                if (!_disposed)
                    _document.Semantic.Set(version, spans); // dropped when the buffer changed meanwhile
                done.TrySetResult();
            });
            await done.Task;
        }
    }
}
