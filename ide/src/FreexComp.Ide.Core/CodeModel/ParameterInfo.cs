using System.Globalization;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// The parameter-info popup's model for one document (IDE-ED-090, I-116): the callee's signatures from the language
/// service, the selected overload and the current parameter. While open, every caret move asks again; no result (the
/// caret left the argument list, or a closing parenthesis was typed at depth 0) closes it.
/// </summary>
public sealed class ParameterInfoSession
{
    /// <summary>Asks the language service for the signature help at an offset; null = not inside an argument list.</summary>
    public delegate Task<SignatureHelp?> Fetch(int offset, CancellationToken ct);

    private readonly TextDocumentViewModel _document;
    private readonly Fetch _fetch;
    private readonly Func<bool> _autoTrigger;
    private CancellationTokenSource? _request;
    private bool _userSelected;

    public ParameterInfoSession(TextDocumentViewModel document, Fetch fetch, Func<bool> autoTrigger)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _fetch = fetch;
        _autoTrigger = autoTrigger;
        document.ViewState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewState.Caret) && IsOpen)
                Pending = RequestAsync();
        };
    }

    /// <summary>Opened, closed, or the signature or parameter changed.</summary>
    public event EventHandler? Changed;

    public bool IsOpen { get; private set; }

    /// <summary>The last result while open.</summary>
    public SignatureHelp? Help { get; private set; }

    public IReadOnlyList<SignatureInfo> Signatures => IsOpen && Help is { } help ? help.Signatures : [];

    public int SelectedIndex { get; private set; }

    public SignatureInfo? SelectedSignature => SelectedIndex < Signatures.Count ? Signatures[SelectedIndex] : null;

    /// <summary>The number of top-level commas between the opening parenthesis and the caret.</summary>
    public int ActiveParameter => Help?.ActiveParameter ?? 0;

    /// <summary>The parameter drawn strong (UI-ED-122); null past the last parameter.</summary>
    public ParameterInfo? CurrentParameter =>
        SelectedSignature is { } signature && ActiveParameter < signature.Parameters.Length ? signature.Parameters[ActiveParameter] : null;

    /// <summary><c>Msg.Editor.OverloadCounter</c> with several overloads, else null.</summary>
    public string? CounterText => Signatures.Count > 1
        ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_OverloadCounter, SelectedIndex + 1, Signatures.Count)
        : null;

    /// <summary>The running or last request; completes when its result is applied (tests await it).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    private int Caret => _document.CaretOffset; // I-210

    /// <summary><c>edit.parameterInfo</c>: asks at the caret; the result says whether it opened.</summary>
    public Task InvokeAsync()
    {
        Pending = RequestAsync();
        return Pending;
    }

    /// <summary>After typed text was inserted: <c>(</c> after an identifier, <c>)</c> or <c>]</c>, or <c>,</c>, opens it (IDE-ED-090).</summary>
    internal void AfterTyped(string text)
    {
        if (IsOpen || text.Length != 1 || !_autoTrigger())
            return;
        var at = Caret - 1;
        var buffer = _document.Buffer;
        if (at < 0 || at >= buffer.Length || buffer.GetText(at, 1)[0] != text[0] || CompletionRules.Suppressed(CompletionRules.ClassAt(_document, at)))
            return;
        var before = at > 0 ? buffer.GetText(at - 1, 1)[0] : '\n';
        if (text[0] == ',' || (text[0] == '(' && (CompletionRules.IsIdentifierChar(before) || before is ')' or ']')))
            Pending = RequestAsync();
    }

    /// <summary>Keys while open: Up/Down cycle the overloads (when there are several), Escape closes.</summary>
    public bool HandleKey(string gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (!IsOpen)
            return false;
        switch (gesture)
        {
            case "Up" or "Down" when Signatures.Count > 1:
                SelectedIndex = (SelectedIndex + (gesture == "Down" ? 1 : Signatures.Count - 1)) % Signatures.Count;
                _userSelected = true;
                Changed?.Invoke(this, EventArgs.Empty);
                return true;
            case "Escape":
                Close();
                return true;
            default:
                return false;
        }
    }

    public void Close()
    {
        _request?.Cancel();
        _request = null;
        _userSelected = false;
        if (!IsOpen)
            return;
        IsOpen = false;
        Help = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RequestAsync()
    {
        _request?.Cancel();
        var request = _request = new CancellationTokenSource();
        var version = _document.Buffer.Version;
        SignatureHelp? help;
        try
        {
            help = await _fetch(Caret, request.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (request.IsCancellationRequested || !ReferenceEquals(request, _request) || _document.Buffer.Version != version)
            return; // superseded, or a stale buffer version
        _request = null;
        if (help is null || help.Signatures.IsEmpty)
        {
            Close();
            return;
        }

        var keep = IsOpen && _userSelected && Help is { } old && old.Signatures.Length == help.Signatures.Length;
        Help = help;
        if (!keep)
        {
            SelectedIndex = Math.Clamp(help.ActiveSignature, 0, help.Signatures.Length - 1);
            _userSelected = false;
        }

        IsOpen = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// What the language service says about a symbol: its declaration, origin and doc comment (IDE-ED-091);
/// <see cref="HasSourceDeclaration"/>: the symbol is declared in a source file, so the tooltip offers Go to Declaration (UI-ED-134).
/// </summary>
public sealed record HoverContent(int Offset, int Length, string Declaration, string? Origin, string? Documentation)
{
    public bool HasSourceDeclaration { get; init; }
}

/// <summary>A shown hover: the hovered range, the symbol content (null = markers only) and the markers there (UI-ED-131).</summary>
public sealed record HoverInfo(int Offset, int Length, HoverContent? Content, IReadOnlyList<TextMarker> Markers, bool AtCaret)
{
    /// <summary>UI-ED-130, from P6 while paused: the value of the hovered variable, shown as <c>= value</c> first; null = none.</summary>
    public string? Value => ValueNode?.Info?.Value;

    /// <summary>DBG-258: the evaluated designator as an expandable node; null = no value.</summary>
    public Debugging.HoverValueNode? ValueNode { get; init; }
}

/// <summary>
/// The hover tooltip's model for one document (IDE-ED-091, UI-ED-130): the pointer resting on an identifier for
/// <c>editor.hover.delayMs</c> asks the language service; <c>edit.parameterInfo</c> outside an argument list shows the
/// same at the caret (with the error rows of a marker there, IDE-ED-093).
/// </summary>
public sealed class HoverSession
{
    /// <summary>Asks the language service for the symbol at an offset; null = no identifier with a symbol there.</summary>
    public delegate Task<HoverContent?> Fetch(int offset, CancellationToken ct);

    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(200); // UI-ED-130

    private readonly TextDocumentViewModel _document;
    private readonly Fetch _fetch;
    private readonly IClock _clock;
    private readonly Func<bool> _enabled;
    private readonly Func<TimeSpan> _delay;

    /// <summary>
    /// Asks the debugger for the value under an offset while the program is paused (UI-ED-130, DBG-258,
    /// <c>debugger.evaluateTooltips</c>): the hovered token's range and the value node; null = none. Null before Phase 6.
    /// </summary>
    public Func<int, Task<Debugging.HoverValue?>>? ValueFetch { get; set; }

    /// <summary>UI-ED-134: Go to Declaration (<c>search.goToDeclaration</c>) at a text offset of the document; null = no link.</summary>
    public Func<int, Task>? GoToDeclaration { get; set; }

    private IDisposable? _timer;
    private IDisposable? _closing;
    private CancellationTokenSource? _request;

    public HoverSession(TextDocumentViewModel document, Fetch fetch, IClock clock, Func<bool> enabled, Func<TimeSpan> delay)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _fetch = fetch;
        _clock = clock;
        _enabled = enabled;
        _delay = delay;
    }

    public event EventHandler? Changed;

    /// <summary>The hover shown; null = none.</summary>
    public HoverInfo? Current { get; private set; }

    /// <summary>The running or last request (tests await it).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The pointer is over text offset <paramref name="offset"/> (null = it left the text): resting there for the delay
    /// asks; leaving closes after the grace time; moving within the shown word keeps it.
    /// </summary>
    public void PointerAt(int? offset)
    {
        CancelTimer();
        if (offset is not { } at)
        {
            if (Current is { AtCaret: false } && _closing is null)
                _closing = _clock.Schedule(Grace, () =>
                {
                    _closing = null;
                    Close();
                });
            return;
        }

        if (Current is { AtCaret: false } current && current.Offset <= at && at < current.Offset + current.Length)
        {
            KeepOpen();
            return;
        }

        if (Current is { AtCaret: false })
            Close();
        if (!_enabled())
            return;
        _timer = _clock.Schedule(_delay(), () =>
        {
            _timer = null;
            Pending = RequestAsync(at, atCaret: false);
        });
    }

    /// <summary>The pointer entered the tooltip: a pending grace close is dropped.</summary>
    public void KeepOpen()
    {
        _closing?.Dispose();
        _closing = null;
    }

    /// <summary>The keyboard equivalent at the caret.</summary>
    public Task ShowAtCaretAsync()
    {
        CancelTimer();
        Pending = RequestAsync(_document.CaretOffset, atCaret: true); // I-210
        return Pending;
    }

    /// <summary>UI-ED-134: the shown symbol has a source declaration and the session can go there (the tooltip shows the link).</summary>
    public bool CanGoToDeclaration => Current is { Content.HasSourceDeclaration: true } && GoToDeclaration is not null;

    /// <summary>
    /// UI-ED-134: the link was clicked — the tooltip closes, the caret moves to the hovered word and Go to Declaration runs
    /// there as F12 would (its navigation-history entry departs from the hovered word).
    /// </summary>
    public Task GoToDeclarationAsync()
    {
        if (!CanGoToDeclaration)
            return Task.CompletedTask;
        var offset = Current!.Offset;
        Close();
        _document.Selection = Ide.Core.Text.TextSelection.At(offset);
        return GoToDeclaration!(offset);
    }

    /// <summary>Closes (a key press, scroll, focus loss); a running request is dropped.</summary>
    public void Close()
    {
        CancelTimer();
        KeepOpen();
        _request?.Cancel();
        _request = null;
        if (Current is null)
            return;
        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task RequestAsync(int offset, bool atCaret)
    {
        _request?.Cancel();
        var request = _request = new CancellationTokenSource();
        var version = _document.Buffer.Version;
        HoverContent? content;
        try
        {
            content = await _fetch(offset, request.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (request.IsCancellationRequested || !ReferenceEquals(request, _request) || _document.Buffer.Version != version)
            return;
        var value = ValueFetch is { } fetch ? await fetch(offset) : null;
        if (request.IsCancellationRequested || !ReferenceEquals(request, _request) || _document.Buffer.Version != version)
            return;
        _request = null;
        var markers = _document.Decorations.MarkersAt(offset);
        if (content is null && value is null && (!atCaret || markers.Count == 0))
        {
            Close();
            return;
        }

        KeepOpen();
        Current = content is not null ? new HoverInfo(content.Offset, content.Length, content, markers, atCaret) { ValueNode = value?.Node }
            : value is { } evaluated ? new HoverInfo(evaluated.Offset, evaluated.Length, null, markers, atCaret) { ValueNode = evaluated.Node }
            : new HoverInfo(markers[0].Offset, Math.Max(1, markers[0].Length), null, markers, atCaret);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// I-116: gives every open Pascal document its <see cref="ParameterInfoSession"/> and <see cref="HoverSession"/> over the
/// project <c>Compilation</c> (requests on the <see cref="CodeModelWorker"/>) and handles <c>edit.parameterInfo</c>
/// (Ctrl+Shift+Space): parameter info inside an argument list, else the hover at the caret.
/// </summary>
public sealed class ParameterInfoController
{
    public const string CommandId = "edit.parameterInfo";

    private readonly IDocumentService _documents;
    private readonly IProjectService _projects;
    private readonly CodeModelService _codeModel;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;

    public ParameterInfoController(IDocumentService documents, IProjectService projects, CodeModelService codeModel, ISettingsService settings, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
        _projects = projects;
        _codeModel = codeModel;
        _settings = settings;
        _clock = clock;
        documents.DocumentOpened += (_, e) => Attach(e.Document);
        foreach (var document in documents.Documents)
            Attach(document);
    }

    /// <summary>
    /// Phase 6 (UI-ED-130): the debugger's value of the identifier at an offset of a document while the program is paused;
    /// set before documents open. Null = no value line.
    /// </summary>
    public Func<TextDocumentViewModel, int, Task<Debugging.HoverValue?>>? DebugValue { get; set; }

    /// <summary>UI-ED-134: Go to Declaration at an offset of a document (the hover link); null = no link.</summary>
    public Func<TextDocumentViewModel, int, Task>? GoToDeclaration { get; set; }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler(CommandId, new Handler(this));
    }

    // I-302; IDE-REFAC-051: no parameter info in an inline rename session.
    private bool CanShow => _documents.ActiveDocument?.VisibleText is { ParameterInfo: not null, RenameSession: null } && _projects.Project is not null;

    private void Attach(DocumentViewModel document)
    {
        if (document is not TextDocumentViewModel { FilePath: not null } text || text.Language.Id != "pascal" || text.ParameterInfo is not null)
            return;
        text.ParameterInfo = new ParameterInfoSession(text, (offset, ct) => SignatureAsync(text, offset, ct),
            () => _settings.Get(SettingsCatalog.EditorParameterInfoAutoTrigger));
        text.Hover = new HoverSession(text, (offset, ct) => HoverAsync(text, offset, ct), _clock,
            () => _settings.Get(SettingsCatalog.EditorHoverEnabled),
            () => TimeSpan.FromMilliseconds(_settings.Get(SettingsCatalog.EditorHoverDelayMs)))
        {
            ValueFetch = DebugValue is null ? null : offset => DebugValue(text, offset),
            GoToDeclaration = GoToDeclaration is null ? null : offset => GoToDeclaration(text, offset),
        };
    }

    private Document? Source(TextDocumentViewModel document) =>
        document.FilePath is { } path && _codeModel.Synchronize() is not null ? _codeModel.Workspace.GetDocument(path) : null;

    private Task<SignatureHelp?> SignatureAsync(TextDocumentViewModel document, int offset, CancellationToken ct) =>
        Source(document) is { } source
            ? _codeModel.Worker.RunAsync<SignatureHelp?>("signatureHelp", token => SignatureHelpService.GetSignatureHelp(source, offset, token), ct)
            : Task.FromResult<SignatureHelp?>(null);

    private Task<HoverContent?> HoverAsync(TextDocumentViewModel document, int offset, CancellationToken ct) =>
        Source(document) is { } source
            ? _codeModel.Worker.RunAsync("hover", token => Content(source, offset, token), ct)
            : Task.FromResult<HoverContent?>(null);

    /// <summary>The quick info at <paramref name="offset"/> with the origin line <c>Msg.Editor.HoverOrigin</c> of the symbol's first declaration.</summary>
    private static HoverContent? Content(Document source, int offset, CancellationToken ct)
    {
        if (QuickInfoService.GetQuickInfo(source, offset, ct) is not { } quick)
            return null;
        var model = source.SemanticModel;
        var token = model.SyntaxTree.FindToken(quick.Span.Start);
        Symbol? symbol = token.Parent is null ? null : model.GetDeclaredSymbol(token, ct) ?? model.GetSymbolInfo(token.Parent, ct).Symbol;
        string? origin = null;
        var inSource = symbol is { Locations: { IsDefaultOrEmpty: false } locations } && locations[0].Path.Length > 0;
        if (quick.DeclaredIn?.Split('.')[0] is { } unit && inSource)
            origin = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_HoverOrigin, unit, symbol!.Locations[0].LineSpan.Start.Line + 1);
        // UI-ED-133: the view lays out and cuts the rendered text (DocumentationLayout).
        var documentation = quick.Documentation?.ReplaceLineEndings("\n");
        return new HoverContent(quick.Span.Start, quick.Span.Length, quick.Signature, origin, documentation) { HasSourceDeclaration = inSource };
    }

    private sealed class Handler(ParameterInfoController owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner.CanShow;

        public async Task ExecuteAsync(string? argument)
        {
            if (owner._documents.ActiveDocument?.VisibleText is not { ParameterInfo: { } info } document)
                return;
            await info.InvokeAsync();
            if (!info.IsOpen && document.Hover is { } hover)
                await hover.ShowAtCaretAsync(); // IDE-ED-091: outside an argument list, the hover at the caret
        }
    }
}
