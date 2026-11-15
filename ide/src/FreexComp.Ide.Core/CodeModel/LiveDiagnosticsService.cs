using System.Collections.Immutable;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// IDE-ED-092/093 (I-111): every open Pascal document of the project gets a <see cref="LiveDiagnostics"/> decoration
/// provider. <c>editor.liveDiagnostics.delayMs</c> after the last edit (and after opening) the document is analysed in the
/// project <c>Compilation</c> with every open buffer; the result becomes squiggles and error glyphs, never Messages rows.
/// </summary>
public sealed class LiveDiagnosticsService
{
    private readonly CodeModelService _codeModel;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<TextDocumentViewModel, LiveDiagnostics> _providers = [];

    public LiveDiagnosticsService(IDocumentService documents, CodeModelService codeModel, ISettingsService settings, IClock clock, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(settings);
        _codeModel = codeModel;
        _settings = settings;
        _clock = clock;
        _dispatcher = dispatcher;
        documents.DocumentOpened += (_, e) => Attach(e.Document);
        documents.DocumentClosed += (_, e) => Detach(e.Document);
        foreach (var document in documents.Documents)
            Attach(document);
        settings.Changed += (_, e) =>
        {
            if (e.Key != SettingsCatalog.EditorLiveDiagnostics.Name)
                return;
            foreach (var provider in _providers.Values)
                provider.Restart();
        };
    }

    internal bool IsEnabled => _settings.Get(SettingsCatalog.EditorLiveDiagnostics);

    internal TimeSpan Delay => TimeSpan.FromMilliseconds(_settings.Get(SettingsCatalog.EditorLiveDiagnosticsDelayMs));

    internal IClock Clock => _clock;

    internal IUiDispatcher Dispatcher => _dispatcher;

    internal CodeModelService CodeModel => _codeModel;

    private void Attach(DocumentViewModel document)
    {
        if (document is not TextDocumentViewModel { FilePath: not null } text || text.Language.Id != "pascal" || _providers.ContainsKey(text))
            return;
        var provider = new LiveDiagnostics(this, text);
        _providers.Add(text, provider);
        text.Decorations.Add(provider);
        provider.Restart();
    }

    private void Detach(DocumentViewModel document)
    {
        if (document is TextDocumentViewModel text && _providers.Remove(text, out var provider))
        {
            provider.Dispose();
            text.Decorations.Remove(provider);
        }
    }
}

/// <summary>The live diagnostics of one document (IDE-ED-092): markers follow edits until the next analysis replaces them.</summary>
public sealed class LiveDiagnostics : IEditorDecorationProvider, IDisposable
{
    private readonly LiveDiagnosticsService _service;
    private readonly TextDocumentViewModel _document;
    private IDisposable? _timer;
    private int _generation;
    private IReadOnlyList<TextMarker> _markers = [];

    internal LiveDiagnostics(LiveDiagnosticsService service, TextDocumentViewModel document)
    {
        _service = service;
        _document = document;
        document.Buffer.Changed += OnBufferChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<TextMarker> GetMarkers() => _markers;

    /// <summary>The error glyph on every line with an error marker; its tooltip is the first error's message.</summary>
    public IReadOnlyList<LineGlyph> GetLineGlyphs(int line)
    {
        foreach (var marker in _markers)
        {
            if (marker.Severity == MarkerSeverity.Error && marker.Offset <= _document.Buffer.Length && _document.Buffer.GetPosition(marker.Offset).Line == line)
                return [new LineGlyph(LineGlyphKind.Error, ToolTip: marker.Message)];
        }

        return [];
    }

    public void Dispose()
    {
        _document.Buffer.Changed -= OnBufferChanged;
        _timer?.Dispose();
        _timer = null;
        _generation++;
    }

    /// <summary>Re-reads the setting: off clears the markers, on analyses after the delay.</summary>
    internal void Restart()
    {
        if (_service.IsEnabled)
        {
            Schedule();
            return;
        }

        _timer?.Dispose();
        _timer = null;
        _generation++;
        SetMarkers([]);
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        if (_markers.Count > 0)
            SetMarkers(Shift(_markers, e));
        if (_service.IsEnabled)
            Schedule();
    }

    /// <summary>Markers after the edit move with it; one the edit touches is dropped.</summary>
    private static List<TextMarker> Shift(IReadOnlyList<TextMarker> markers, TextChangedEventArgs e)
    {
        var delta = e.InsertedText.Length - e.RemovedLength;
        var end = e.Offset + e.RemovedLength;
        var shifted = new List<TextMarker>(markers.Count);
        foreach (var m in markers)
        {
            if (m.DrawnEnd <= e.Offset)
                shifted.Add(m);
            else if (m.Offset >= end)
                shifted.Add(m with { Offset = m.Offset + delta });
        }

        return shifted;
    }

    private void Schedule()
    {
        _timer?.Dispose();
        _generation++;
        _timer = _service.Clock.Schedule(_service.Delay, Analyse);
    }

    private void Analyse()
    {
        _timer?.Dispose();
        _timer = null;
        var generation = ++_generation;
        if (_service.CodeModel.Synchronize() is not { } compilation || _document.FilePath is not { } path)
        {
            SetMarkers([]);
            return;
        }

        var text = _document.Buffer.Text;
        _ = AnalyseAsync(generation, compilation, path, text);
    }

    private async Task AnalyseAsync(int generation, Compilation compilation, string path, string text)
    {
        ImmutableArray<Diagnostic> diagnostics;
        try
        {
            diagnostics = await _service.CodeModel.Worker.RunAsync("liveDiagnostics:" + path, token => compilation.GetDiagnostics(path, token));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var normalized = PathUtil.Normalize(path);
        var markers = diagnostics
            .Where(d => string.Equals(PathUtil.Normalize(d.Location.Path), normalized, StringComparison.OrdinalIgnoreCase))
            .Select(d => ToMarker(d, text))
            .ToList();
        _service.Dispatcher.Post(() =>
        {
            if (generation == _generation)
                SetMarkers(markers); // otherwise an edit or a newer analysis came in between
        });
    }

    /// <summary>The diagnostic span; an empty one at the end of a line marks the line's last character (IDE-ED-092).</summary>
    private static TextMarker ToMarker(Diagnostic d, string text)
    {
        var offset = Math.Clamp(d.Location.Span.Start, 0, text.Length);
        var length = Math.Clamp(d.Location.Span.Length, 0, text.Length - offset);
        if (length == 0 && offset > 0 && (offset == text.Length || text[offset] is '\r' or '\n') && text[offset - 1] is not ('\r' or '\n'))
            (offset, length) = (offset - 1, 1);
        var severity = d.Severity switch
        {
            DiagnosticSeverity.Error => MarkerSeverity.Error,
            DiagnosticSeverity.Warning => MarkerSeverity.Warning,
            _ => MarkerSeverity.Hint,
        };
        return new TextMarker(offset, length, severity, d.Code, d.Message);
    }

    private void SetMarkers(IReadOnlyList<TextMarker> markers)
    {
        if (markers.Count == 0 && _markers.Count == 0)
            return;
        _markers = markers;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
