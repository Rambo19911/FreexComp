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
public class TextDocumentViewModel : DocumentViewModel, ICommandTarget
{
    public const string EncodingInfoBarKind = "Encoding";
    public const string MixedLineEndingsInfoBarKind = "MixedLineEndings";
    public const string ReadOnlyInfoBarKind = "ReadOnly";

    private string? _filePath;
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

    /// <summary>The encoding of the next save (IDE-P1-074).</summary>
    public FileEncoding Encoding => _encoding;

    /// <summary>Document line-ending kind incl. Mixed (IDE-P1-075…077); handles <c>edit.convertLineEndings</c>.</summary>
    public LineEndingModel LineEnding { get; }

    public EditorViewState ViewState { get; } = new();

    /// <summary>The editor settings its view draws with; the document service gives every document its shared instance.</summary>
    public EditorOptions Options { get; internal set; } = EditorOptions.Default;

    /// <summary>Glyph-margin glyphs and line backgrounds (editor.md §3): bookmarks, diagnostics, breakpoints add providers.</summary>
    public EditorDecorations Decorations { get; } = new();

    /// <summary>Change-bar states per line (UI-ED-040): the document's <see cref="LineChangeTracker"/> (tests may replace it).</summary>
    public ILineChangeSource LineChanges
    {
        get => _lineChanges;
        set => SetProperty(ref _lineChanges, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Fold regions and their folded state (editor-features.md §5); handles the <c>edit.fold*</c> commands.</summary>
    public FoldingModel Folding { get; }

    /// <summary>The name of an untitled document (<c>Unit1.pas</c>); null once it has a path.</summary>
    public string? UntitledName => _filePath is null ? _untitledName : null;

    public override string Title => _filePath is null ? _untitledName! : Path.GetFileName(_filePath);

    public override string? FilePath => _filePath;

    /// <summary>
    /// IDE-ARCH-022: the buffer differs from its save point, or the encoding / line-ending kind changed since then.
    /// An untitled document is always modified (IDE-P1-033).
    /// </summary>
    public override bool IsModified =>
        _filePath is null || !Buffer.IsAtSavePoint || _encoding != _savedEncoding || LineEnding.Kind != _savedLineEnding;

    public override bool IsReadOnly => Buffer.IsReadOnly;

    /// <summary>
    /// The document itself: <c>edit.undo</c> / <c>edit.redo</c> through <see cref="ITextBuffer.Undo"/> /
    /// <see cref="ITextBuffer.Redo"/> (never the editor's own undo stack, IDE-ARCH-030), the fold commands to
    /// <see cref="Folding"/>, everything else to
    /// <see cref="LineEnding"/>. The editor view adds the commands that need its selection.
    /// </summary>
    public override ICommandTarget? CommandTarget => this;

    bool ICommandTarget.CanExecute(string commandId, string? argument) => commandId switch
    {
        UndoCommandId => !Buffer.IsReadOnly && Buffer.CanUndo,
        RedoCommandId => !Buffer.IsReadOnly && Buffer.CanRedo,
        FoldCommandId => Folding.CanFold,
        UnfoldCommandId => Folding.CanUnfold,
        FoldAllCommandId => Folding.CanFoldAll,
        UnfoldAllCommandId => Folding.CanUnfoldAll,
        _ => LineEnding.CanExecute(commandId, argument),
    };

    Task<bool> ICommandTarget.ExecuteAsync(string commandId, string? argument)
    {
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
            return LineEnding.ExecuteAsync(commandId, argument);
        if (!((ICommandTarget)this).CanExecute(commandId, argument))
            return Task.FromResult(false);
        if (commandId == UndoCommandId)
            Buffer.Undo();
        else
            Buffer.Redo();
        return Task.FromResult(true);
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
        _lineChangeTracker.Reset(); // IDE-ED-045
        ViewState.Caret = Buffer.GetPosition(Buffer.GetOffset(Math.Clamp(caret.Line, 1, Buffer.LineCount), caret.Column));
        ShowLoadInfoBars(encodingFallback: false);
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>The status-bar fields R2–R8 of this document (main-window.md §5).</summary>
    public EditorStatus GetStatus(bool insertSpaces, int indentSize)
    {
        var caret = ViewState.Caret;
        int chars = 0, lines = 0;
        if (ViewState.SelectionAnchor is { } anchor && anchor != caret)
        {
            chars = Math.Abs(Buffer.GetOffset(caret.Line, caret.Column) - Buffer.GetOffset(anchor.Line, anchor.Column));
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
            // "Reopen with Encoding…" joins when its dialog exists (dialogs.md §18).
            ShowInfoBar(new InfoBarViewModel(EncodingInfoBarKind, InfoBarSeverity.Warning,
                string.Format(CultureInfo.CurrentCulture, Strings.Msg_File_EncodingFallback, FileEncodings.DisplayName(_encoding)),
                [new InfoBarAction("ConvertToUtf8", Strings.Button_ConvertToUtf8, () => { SetEncoding(FileEncoding.Utf8); return Task.CompletedTask; })],
                bar => RemoveInfoBar(bar.Kind)));
        }
    }

    /// <summary>A successful save to <paramref name="path"/>: the current state becomes the save point.</summary>
    internal void MarkSaved(string path, ILanguageDefinition language)
    {
        var renamed = !string.Equals(_filePath, path, StringComparison.Ordinal);
        _filePath = path;
        _language = language;
        Buffer.MarkSavePoint();
        _lineChangeTracker.MarkSaved();
        _savedEncoding = _encoding;
        _savedLineEnding = LineEnding.Kind;
        if (renamed)
        {
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(UntitledName));
            OnPropertyChanged(nameof(Language));
            Folding.SetProvider(language.CreateFoldingProvider());
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
