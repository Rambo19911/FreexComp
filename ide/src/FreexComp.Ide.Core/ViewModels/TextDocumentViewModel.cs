using System.Globalization;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Languages;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// One text file, or an untitled text document (architecture.md §5). Owns the document-level line-ending state
/// (<see cref="LineEnding"/>) and the encoding of the next save. Created and saved only by the document service
/// (IDE-ARCH-023).
/// </summary>
public partial class TextDocumentViewModel : DocumentViewModel, ICommandTarget
{
    public const string EncodingInfoBarKind = "Encoding";
    public const string MixedLineEndingsInfoBarKind = "MixedLineEndings";
    public const string ReadOnlyInfoBarKind = "ReadOnly";
    public const string LibrarySourceInfoBarKind = "LibrarySource";
    public const string ExternalChangeInfoBarKind = "ExternalChange";
    public const string ExternalDeleteInfoBarKind = "ExternalDelete";
    public const string RecoveredInfoBarKind = "Recovered";

    private string? _filePath;
    private bool _forcedModified;
    private readonly string? _untitledName;
    private ILanguageDefinition _language;
    private FileEncoding _encoding;
    private FileEncoding _savedEncoding;
    private LineEndingKind _savedLineEnding;
    private readonly LineChangeTracker _lineChangeTracker;
    private ILineChangeSource _lineChanges;

    /// <param name="filePath">Absolute normalized path; null for an untitled document named <paramref name="untitledName"/>.</param>
    public TextDocumentViewModel(ITextBuffer buffer, string? filePath, string? untitledName, ILanguageDefinition language,
        FileEncoding encoding, LineEndingKind defaultLineEnding)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(language);
        if ((filePath is null) == (untitledName is null))
            throw new ArgumentException("Exactly one of filePath and untitledName is required.");
        Buffer = buffer;
        _filePath = filePath;
        _untitledName = untitledName;
        _language = language;
        _encoding = _savedEncoding = encoding;
        LineEnding = new LineEndingModel(buffer, defaultLineEnding);
        _savedLineEnding = LineEnding.Kind;
        _lineChanges = _lineChangeTracker = new LineChangeTracker(buffer);
        Folding = new FoldingModel(buffer, ViewState, language.CreateFoldingProvider());
        Tokens = new CodeTokenIndex(buffer, language);
        Semantic = new SemanticLayer(buffer);
        Decorations.Add(new LexerErrorMarkers(buffer, () => _language)); // UI-ED-068
        WordHighlights = new WordHighlighter(buffer, ViewState, Tokens, () => Clock, () => Options.HighlightWordOccurrences && !_showsFindMatches);
        InitTyping();
        InitTemplates();

        buffer.Changed += (_, _) => OnPropertyChanged(nameof(IsModified));
        LineEnding.KindChanged += (_, _) =>
        {
            if (LineEnding.Kind != LineEndingKind.Mixed)
                RemoveInfoBar(MixedLineEndingsInfoBarKind); // IDE-P1-077
            OnPropertyChanged(nameof(LineEnding));
            OnPropertyChanged(nameof(IsModified));
        };
    }

    public ITextBuffer Buffer { get; }

    public ILanguageDefinition Language => _language;

    /// <summary>The P3 tree-based and semantic classes over the lexical colours (I-112/I-113); filled by the code model.</summary>
    public SemanticLayer Semantic { get; }

    /// <summary>The classifier the editor view draws with: the language's lexical classifier with <see cref="Semantic"/> applied.</summary>
    public ITokenClassifier CreateClassifier() => new LayeredClassifier(_language.CreateClassifier(), Semantic);

    /// <summary>Code completion of this document (I-114); null when the code model does not complete it.</summary>
    public CodeModel.CompletionSession? Completion { get; internal set; }

    /// <summary>Parameter info of this document (I-116); null when the code model does not provide it.</summary>
    public CodeModel.ParameterInfoSession? ParameterInfo { get; internal set; }

    /// <summary>The hover tooltip of this document (I-116); null when the code model does not provide it.</summary>
    public CodeModel.HoverSession? Hover { get; internal set; }

    /// <summary>The Ctrl+hover underline of this document (I-322, IDE-ED-149); null when the code model does not provide it.</summary>
    public CodeModel.DeclarationLinkSession? DeclarationLink { get; internal set; }

    /// <summary>The encoding of the next save (IDE-P1-074).</summary>
    public FileEncoding Encoding => _encoding;

    /// <summary>Document line-ending kind incl. Mixed (IDE-P1-075…077); handles <c>edit.convertLineEndings</c>.</summary>
    public LineEndingModel LineEnding { get; }

    public EditorViewState ViewState { get; } = new();

    /// <summary>The editor settings its view draws with; the document service gives every document its shared instance.</summary>
    public EditorOptions Options { get; internal set; } = EditorOptions.Default;

    /// <summary>Glyph-margin glyphs and line backgrounds (editor.md §3): bookmarks, diagnostics, breakpoints add providers.</summary>
    public EditorDecorations Decorations { get; } = new();

    /// <summary>UI-ED-022: the gutter context menu for a 1-based line (set by the document service); null = none.</summary>
    public Func<int, IReadOnlyList<Menus.MenuItemModel>>? GutterMenu { get; internal set; }

    /// <summary>UI-ED-012 (P6): a left click in the glyph margin on the 1-based line toggles a breakpoint; null = no effect.</summary>
    public Action<int>? GlyphMarginClick { get; internal set; }

    /// <summary>Runs a global command (set by the document service): the view's Ctrl+click goes to <c>search.goToDeclaration</c> (I-196).</summary>
    public Func<string, Task>? ExecuteCommand { get; internal set; }

    /// <summary>The text-area context menu (menus.md §12), built when it opens; null without a command service.</summary>
    public Func<IReadOnlyList<Menus.MenuItemModel>>? TextAreaMenu { get; internal set; }

    /// <summary>Change-bar states per line (UI-ED-040): the document's <see cref="LineChangeTracker"/> (tests may replace it).</summary>
    public ILineChangeSource LineChanges
    {
        get => _lineChanges;
        set => SetProperty(ref _lineChanges, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Fold regions and their folded state (editor-features.md §5); handles the <c>edit.fold*</c> commands.</summary>
    public FoldingModel Folding { get; }

    /// <summary>Brackets, block keywords and identifiers of the current text (editor-features.md §7).</summary>
    public CodeTokenIndex Tokens { get; }

    /// <summary>Other occurrences of the identifier at the caret (IDE-ED-074); the view calls <see cref="WordHighlighter.OptionChanged"/>.</summary>
    public WordHighlighter WordHighlights { get; }

    private bool _showsFindMatches;

    /// <summary>The find state of this document (its panel, matches), from <c>IFindService.For</c>; null outside the IDE.</summary>
    public FindSession? Find => FindSource?.Invoke(this);

    /// <summary>Set by the find service when the document opens.</summary>
    internal Func<TextDocumentViewModel, FindSession>? FindSource { get; set; }

    /// <summary>UI-ED-054: no word-occurrence highlight while the find panel shows matches.</summary>
    internal bool ShowsFindMatches
    {
        get => _showsFindMatches;
        set
        {
            if (_showsFindMatches == value)
                return;
            _showsFindMatches = value;
            WordHighlights.OptionChanged();
        }
    }

    /// <summary>
    /// Asks the view to scroll the caret into view (a find navigation), or (<paramref name="third"/>, Go to Line,
    /// UI-DLG-120) to put the caret line at one third of the viewport.
    /// </summary>
    internal void RevealCaret(bool third = false)
    {
        // I-273: a view that cannot scroll yet (it has just been given the document, or none shows it) finds the request
        // here when it can; a stronger request (one third) is not replaced by a weaker one.
        _pendingReveal = third || _pendingReveal == true;
        if (third)
            RevealCaretAtThirdRequested?.Invoke(this, EventArgs.Empty);
        else
            RevealCaretRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The view should scroll the caret line to one third of the viewport (UI-DLG-120).</summary>
    public event EventHandler? RevealCaretAtThirdRequested;

    private bool? _pendingReveal;

    /// <summary>
    /// The request to show the caret that no view has carried out yet, and forgets it: true = the caret line at one third
    /// of the viewport, false = scrolled just into view, null = none. A view calls this when it scrolls for a request, and
    /// when it has measured a document it was given (instead of restoring the stored scroll position).
    /// </summary>
    public bool? TakePendingReveal()
    {
        var pending = _pendingReveal;
        _pendingReveal = null;
        return pending;
    }

    /// <summary>
    /// IDE-ED-100 / IDE-ED-072: the caret jumped away from the given position far enough for the navigation history (a
    /// bracket or find jump of more than <see cref="FarJumpLines"/> lines, a find wrap-around). The navigation service records it.
    /// </summary>
    public event EventHandler<TextPosition>? Jumped;

    /// <summary>A jump of more lines than this is a navigation-history entry (IDE-ED-072, IDE-ED-100).</summary>
    public const int FarJumpLines = 10;

    /// <summary>Raises <see cref="Jumped"/> when the caret moved more than <see cref="FarJumpLines"/> lines from <paramref name="from"/>, or always.</summary>
    internal void OnJumped(TextPosition from, bool always = false)
    {
        if (always || Math.Abs(ViewState.Caret.Line - from.Line) > FarJumpLines)
            Jumped?.Invoke(this, from);
    }

    /// <summary>The name of an untitled document (<c>Unit1.pas</c>); null once it has a path.</summary>
    public string? UntitledName => _filePath is null ? _untitledName : null;

    public override string Title => _filePath is null ? _untitledName! : Path.GetFileName(_filePath);

    public override string? FilePath => _filePath;

    /// <summary>
    /// IDE-ED-148 (I-323): the virtual path <c>generated:/&lt;Unit&gt;.pas</c> of a generated read-only declaration view
    /// (no file, never saved, never modified, not in the session or recent files); null for every other document.
    /// </summary>
    public string? GeneratedPath { get; init; }

    /// <summary>
    /// IDE-ARCH-022: the buffer differs from its save point, or the encoding / line-ending kind changed since then.
    /// An untitled document is always modified (IDE-P1-033); a generated declaration view never is (IDE-ED-148).
    /// </summary>
    public override bool IsModified => GeneratedPath is null &&
        (_filePath is null || _forcedModified || !Buffer.IsAtSavePoint || _encoding != _savedEncoding || LineEnding.Kind != _savedLineEnding);

    /// <summary>
    /// The file as last loaded, saved or reloaded (IDE-P1-110); null when unknown (untitled, or the file could not be
    /// examined after a save). Kept by the document and file-change services.
    /// </summary>
    internal Models.DiskStamp? DiskStamp { get; set; }

    /// <summary>
    /// Marks the document modified without changing its text (Keep in Editor of a deleted file, IDE-P1-115; a recovered
    /// copy, IDE-P1-123) until it is saved or reloaded.
    /// </summary>
    internal void MarkModified()
    {
        if (_forcedModified)
            return;
        _forcedModified = true;
        OnPropertyChanged(nameof(IsModified));
    }

    public override bool IsReadOnly => Buffer.IsReadOnly;

    /// <summary>
    /// IDE-LIB-003: the document is a protected library source — read-only with the <c>LibrarySource</c> info bar — until
    /// <c>Edit Anyway</c> (IDE-LIB-004) or the document is closed.
    /// </summary>
    public bool IsLibraryProtected => InfoBars.Any(bar => bar.Kind == LibrarySourceInfoBarKind);

    /// <summary>
    /// The document itself: <c>edit.undo</c> / <c>edit.redo</c> through <see cref="ITextBuffer.Undo"/> /
    /// <see cref="ITextBuffer.Redo"/> (never the editor's own undo stack, IDE-ARCH-030), the fold commands to
    /// <see cref="Folding"/>, everything else to
    /// <see cref="LineEnding"/>. The editor view adds the commands that need its selection.
    /// </summary>
    public override ICommandTarget? CommandTarget => this;

    /// <summary>A text document shows its own text.</summary>
    public override TextDocumentViewModel? VisibleText => this;

    bool ICommandTarget.CanExecute(string commandId, string? argument) => commandId switch
    {
        UndoCommandId => !Buffer.IsReadOnly && Buffer.CanUndo,
        RedoCommandId => !Buffer.IsReadOnly && Buffer.CanRedo,
        FoldCommandId => Folding.CanFold,
        UnfoldCommandId => Folding.CanUnfold,
        FoldAllCommandId => Folding.CanFoldAll,
        UnfoldAllCommandId => Folding.CanUnfoldAll,
        _ when IsEditingCommand(commandId) => CanExecuteEditing(commandId),
        _ when IsClipboardCommand(commandId) => CanExecuteClipboard(commandId),
        _ when ViewCommandIds.Contains(commandId) => CanExecuteView(commandId),
        _ => LineEnding.CanExecute(commandId, argument) && !IsCurrentLineEnding(commandId, argument),
    };

    /// <summary>IDE-ED-040: the check mark of Word Wrap is this document's state.</summary>
    bool ICommandTarget.IsChecked(string commandId) => commandId == WordWrapCommandId && ViewState.WordWrap;

    /// <summary>menus.md: "Line Endings to CRLF/LF" is disabled while the document already has that kind.</summary>
    private bool IsCurrentLineEnding(string commandId, string? argument) =>
        commandId == LineEndingModel.ConvertCommandId
        && (argument, LineEnding.Kind) is ("crlf", LineEndingKind.CrLf) or ("lf", LineEndingKind.Lf);

    Task<bool> ICommandTarget.ExecuteAsync(string commandId, string? argument)
    {
        if (IsClipboardCommand(commandId))
            return ExecuteClipboardAsync(commandId);
        if (ViewCommandIds.Contains(commandId))
            return Task.FromResult(CanExecuteView(commandId) && ExecuteView(commandId));
        if (commandId == GoToMatchingBracketCommandId)
            return GoToMatchingBracketAsync();
        if (IsEditingCommand(commandId))
            return Task.FromResult(CanExecuteEditing(commandId) && ExecuteEditing(commandId));
        switch (commandId)
        {
            case FoldCommandId:
                return Task.FromResult(Folding.Fold());
            case UnfoldCommandId:
                return Task.FromResult(Folding.Unfold());
            case FoldAllCommandId:
                return Task.FromResult(Folding.FoldAll());
            case UnfoldAllCommandId:
                return Task.FromResult(Folding.UnfoldAll());
        }

        if (commandId is not (UndoCommandId or RedoCommandId))
            return ExecuteLineEndingAsync(commandId, argument);
        if (!((ICommandTarget)this).CanExecute(commandId, argument))
            return Task.FromResult(false);
        if (commandId == RedoCommandId)
            RedoStep();
        else if (OperationUndo?.Invoke(this) is { } operation)
            return UndoOperationAsync(operation);
        else
            UndoStep();
        return Task.FromResult(true);
    }

    /// <summary>
    /// IDE-REFAC-024: asked on <c>edit.undo</c>. Null — the document's own undo runs, as always; otherwise the undo of a
    /// whole multi-file operation that edited this document last, whose result says whether the document's own undo is to
    /// run after all (that undo did not go through, nothing changed, and the user chose <c>Undo in This File</c>).
    /// <c>edit.redo</c> never asks.
    /// </summary>
    internal Func<TextDocumentViewModel, Task<bool>?>? OperationUndo { get; set; }

    private async Task<bool> UndoOperationAsync(Task<bool> operation)
    {
        if (await operation && ((ICommandTarget)this).CanExecute(UndoCommandId, null))
            UndoStep();
        return true;
    }

    /// <summary><c>edit.convertLineEndings</c> (one undo step, IDE-P1-077) keeps the selections around it for undo/redo.</summary>
    private async Task<bool> ExecuteLineEndingAsync(string commandId, string? argument)
    {
        var before = CurrentSelection;
        var version = Buffer.Version;
        var result = await LineEnding.ExecuteAsync(commandId, argument);
        if (Buffer.Version != version)
            RecordStep(before, version, continued: false);
        EndEdit(EditKind.None, whitespace: false);
        return result;
    }

    private const string UndoCommandId = "edit.undo";
    private const string RedoCommandId = "edit.redo";
    private const string FoldCommandId = "edit.fold";
    private const string UnfoldCommandId = "edit.unfold";
    private const string FoldAllCommandId = "edit.foldAll";
    private const string UnfoldAllCommandId = "edit.unfoldAll";

    /// <summary>Changes only the encoding used for the next save; the document becomes modified (IDE-P1-074).</summary>
    public void SetEncoding(FileEncoding encoding)
    {
        if (_encoding == encoding)
            return;
        _encoding = encoding;
        RemoveInfoBar(EncodingInfoBarKind);
        OnPropertyChanged(nameof(Encoding));
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>Raised when an edit was refused because the document is read-only (IDE-P1-040).</summary>
    public event EventHandler? ReadOnlyEditRefused;

    /// <summary>
    /// A user edit (typing, paste): applied as one undo step, or refused with <see cref="ReadOnlyEditRefused"/> while
    /// the document is read-only (IDE-P1-040). The editor view routes its input through here.
    /// </summary>
    public bool TryEdit(IReadOnlyList<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (Buffer.IsReadOnly)
        {
            ReadOnlyEditRefused?.Invoke(this, EventArgs.Empty);
            return false;
        }

        Buffer.ApplyEdits(edits);
        return true;
    }

    /// <summary>
    /// Typed or pasted text replacing <paramref name="replacedLength"/> units at <paramref name="offset"/>: its line
    /// breaks become the document's (<see cref="LineEndingModel.Normalize"/>, IDE-P1-076), then <see cref="TryEdit"/>.
    /// </summary>
    public bool TryInsertText(int offset, int replacedLength, string text) =>
        TryEdit([new TextEdit(offset, replacedLength, LineEnding.Normalize(text))]);

    /// <summary>Sets <see cref="IsReadOnly"/>; becoming writable removes the <c>ReadOnly</c> info bar.</summary>
    internal void SetReadOnly(bool readOnly)
    {
        if (!readOnly)
            RemoveInfoBar(ReadOnlyInfoBarKind);
        if (Buffer.IsReadOnly == readOnly)
            return;
        Buffer.IsReadOnly = readOnly;
        OnPropertyChanged(nameof(IsReadOnly));
    }

    /// <summary>
    /// Revert (IDE-P1-039): <paramref name="text"/> (read with the current encoding) replaces the buffer, the undo
    /// history is cleared and the result is the save point; the caret keeps its line/column, clamped.
    /// </summary>
    internal void Reload(string text)
    {
        var caret = ViewState.Caret;
        ViewState.SelectionAnchor = null; // before the text changes: the old selection may lie past the new end
        var readOnly = Buffer.IsReadOnly;
        Buffer.IsReadOnly = false;
        try
        {
            Buffer.ReplaceAll(text, resetUndo: true);
        }
        finally
        {
            Buffer.IsReadOnly = readOnly;
        }

        _savedEncoding = _encoding;
        _savedLineEnding = LineEnding.Kind;
        _forcedModified = false;
        // The text is the disk version now: bars about another version are obsolete (IDE-P1-114, IDE-P1-123).
        RemoveInfoBar(ExternalChangeInfoBarKind);
        RemoveInfoBar(RecoveredInfoBarKind);
        _lineChangeTracker.Reset(); // IDE-ED-045
        ForgetSelections(); // the undo history is gone
        EndEdit(EditKind.None, whitespace: false);
        ViewState.Caret = Buffer.GetPosition(Buffer.GetOffset(Math.Clamp(caret.Line, 1, Buffer.LineCount), caret.Column));
        ShowLoadInfoBars(encodingFallback: false);
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>
    /// IDE-HIST-020: <paramref name="text"/> replaces the buffer as one undo step <paramref name="undoTitle"/> (only the
    /// differing middle is replaced, so marks before and after it stay); the caret keeps its line, clamped. The document
    /// becomes modified; nothing is written.
    /// </summary>
    internal void ReplaceText(string text, string undoTitle)
    {
        ArgumentNullException.ThrowIfNull(text);
        var current = Buffer.Text;
        if (string.Equals(current, text, StringComparison.Ordinal))
            return;
        var caret = ViewState.Caret;
        var prefix = 0;
        var max = Math.Min(current.Length, text.Length);
        while (prefix < max && current[prefix] == text[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < max - prefix && current[^(suffix + 1)] == text[^(suffix + 1)])
            suffix++;
        ViewState.SelectionAnchor = null; // the old selection may lie past the new end
        Buffer.ApplyEdits([new TextEdit(prefix, current.Length - prefix - suffix, text[prefix..^suffix])], undoTitle);
        var line = Math.Clamp(caret.Line, 1, Buffer.LineCount);
        ViewState.Caret = Buffer.GetPosition(Buffer.GetOffset(line, caret.Column));
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>The status-bar fields R2–R8 of this document (main-window.md §5).</summary>
    public EditorStatus GetStatus(bool insertSpaces, int indentSize)
    {
        var caret = ViewState.Caret;
        int chars = 0, lines = 0;
        if (ViewState.SelectionAnchor is { } anchor && anchor != caret)
        {
            // Clamped: during an edit the view state may still describe the text before it.
            chars = Math.Abs(OffsetOf(caret) - OffsetOf(anchor));
            lines = Math.Abs(caret.Line - anchor.Line) + 1;
        }

        return new EditorStatus(caret.Line, caret.Column, chars, lines, ViewState.Overwrite, insertSpaces, indentSize,
            LineEnding.Kind, FileEncodings.ToId(_encoding), _language.DisplayName);
    }

    /// <summary>Info bars of a freshly loaded file (IDE-P1-071, IDE-P1-075).</summary>
    internal void ShowLoadInfoBars(bool encodingFallback)
    {
        if (LineEnding.Kind == LineEndingKind.Mixed)
        {
            var counts = LineEndingDetector.Count(Buffer.Text);
            ShowInfoBar(new InfoBarViewModel(MixedLineEndingsInfoBarKind, InfoBarSeverity.Info,
                string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_MixedLineEndings, counts.CrLf, counts.Lf),
                [
                    new InfoBarAction("ConvertToCrlf", Strings.Button_ConvertToCrlf, () => Convert(LineEndingKind.CrLf)),
                    new InfoBarAction("ConvertToLf", Strings.Button_ConvertToLf, () => Convert(LineEndingKind.Lf)),
                    new InfoBarAction("LeaveAsIs", Strings.Button_LeaveAsIs, () => Remove(MixedLineEndingsInfoBarKind)),
                ],
                bar => RemoveInfoBar(bar.Kind)));
        }

        if (encodingFallback)
        {
            var convert = new InfoBarAction("ConvertToUtf8", Strings.Button_ConvertToUtf8, () => { SetEncoding(FileEncoding.Utf8); return Task.CompletedTask; });
            ShowInfoBar(new InfoBarViewModel(EncodingInfoBarKind, InfoBarSeverity.Warning,
                string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_EncodingFallback, FileEncodings.DisplayName(_encoding)),
                ReopenWithEncodingHandler is { } reopen
                    ? [new InfoBarAction("ReopenWithEncoding", Strings.Button_ReopenWithEncoding, reopen), convert]
                    : [convert],
                bar => RemoveInfoBar(bar.Kind)));
        }
    }

    /// <summary>"Reopen with Encoding…" of the <c>Encoding</c> info bar (dialogs.md section 18); set by the document service.</summary>
    internal Func<Task>? ReopenWithEncodingHandler { get; set; }

    /// <summary>
    /// Reopen with Encoding (IDE-P1-074): <paramref name="text"/> was read from disk in <paramref name="encoding"/>, which
    /// becomes the save encoding; then as <see cref="Reload"/> (undo cleared, new save point).
    /// </summary>
    internal void ReloadWithEncoding(string text, FileEncoding encoding)
    {
        _encoding = encoding;
        RemoveInfoBar(EncodingInfoBarKind);
        OnPropertyChanged(nameof(Encoding));
        Reload(text);
    }

    /// <summary>A successful save to <paramref name="path"/>: the current state becomes the save point.</summary>
    internal void MarkSaved(string path, ILanguageDefinition language)
    {
        var renamed = !string.Equals(_filePath, path, StringComparison.Ordinal);
        _filePath = path;
        _language = language;
        Buffer.MarkSavePoint();
        _lineChangeTracker.MarkSaved();
        _forcedModified = false;
        _savedEncoding = _encoding;
        _savedLineEnding = LineEnding.Kind;
        if (renamed)
        {
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(UntitledName));
            OnPropertyChanged(nameof(Language));
            Folding.SetProvider(language.CreateFoldingProvider());
            Tokens.SetLanguage(language);
        }

        OnPropertyChanged(nameof(IsModified));
    }

    private Task Convert(LineEndingKind kind)
    {
        LineEnding.Convert(kind);
        return Task.CompletedTask;
    }

    private Task Remove(string kind)
    {
        RemoveInfoBar(kind);
        return Task.CompletedTask;
    }
}
