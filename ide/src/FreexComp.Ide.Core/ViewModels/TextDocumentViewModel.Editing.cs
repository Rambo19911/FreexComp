using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// The editor operations of the document (editor-features.md §2 and §4): commands and keys act on the selection in
/// <see cref="EditorViewState"/>, apply one <see cref="EditorOperations"/> result as one undo step through
/// <see cref="TryEdit"/> (refused while read-only) and put the resulting selection back into the view state.
/// </summary>
public partial class TextDocumentViewModel
{
    /// <summary>The editor commands this document handles itself (commands.md, Editor scope).</summary>
    public static readonly IReadOnlyList<string> EditingCommandIds =
    [
        "edit.duplicateLine", "edit.deleteLine", "edit.moveLineUp", "edit.moveLineDown", "edit.toggleLineComment",
        "edit.toggleBlockComment", "edit.upperCase", "edit.lowerCase", "edit.trimTrailingWhitespace", "edit.indent", "edit.outdent",
        ToggleOverwriteCommandId, GoToMatchingBracketCommandId,
    ];

    private const string GoToMatchingBracketCommandId = "search.goToMatchingBracket";

    /// <summary>
    /// IDE-ED-072: the caret moves to the partner of the bracket or keyword at the caret, else to the nearest enclosing
    /// opener; none → <c>Msg.Editor.BracketNotFound</c>. Also in read-only documents (menus.md "text"). A large buffer's
    /// tokens are awaited, not scanned on the UI thread.
    /// </summary>
    private async Task<bool> GoToMatchingBracketAsync()
    {
        var version = Buffer.Version;
        var tokens = await Tokens.GetAsync();
        if (Buffer.Version != version)
            return false; // edited meanwhile
        if (tokens.MatchingBracketTarget(Selection.Caret) is not { } target)
        {
            StatusMessage?.Invoke(this, Strings.Msg_Editor_BracketNotFound);
            return true;
        }

        var from = ViewState.Caret;
        Folding.Reveal(target);
        Selection = TextSelection.At(target);
        RevealCaretRequested?.Invoke(this, EventArgs.Empty);
        OnJumped(from); // IDE-ED-072: more than 10 lines → navigation history
        return true;
    }

    /// <summary>A status-bar message for the user (an operation that could not be done), already formatted.</summary>
    public event EventHandler<string>? StatusMessage;

    /// <summary>
    /// The caret as an offset, clamped to the buffer (I-210): the view state may lag behind an edit, so its line can be past
    /// the end of the text.
    /// </summary>
    public int CaretOffset => OffsetOf(ViewState.Caret);

    /// <summary>The current selection as offsets (from <see cref="ViewState"/>).</summary>
    public TextSelection Selection
    {
        get
        {
            var caret = OffsetOf(ViewState.Caret);
            return ViewState.SelectionAnchor is { } anchor ? new TextSelection(OffsetOf(anchor), caret) : TextSelection.At(caret);
        }
        set
        {
            ViewState.RectangularSelection = false;
            ViewState.SelectionAnchor = value.IsEmpty ? null : Buffer.GetPosition(value.Anchor);
            ViewState.Caret = Buffer.GetPosition(value.Caret);
        }
    }

    private int OffsetOf(TextPosition position) =>
        Buffer.GetOffset(Math.Clamp(position.Line, 1, Buffer.LineCount), position.Column);

    private static bool IsEditingCommand(string commandId) => EditingCommandIds.Contains(commandId);

    private bool CanExecuteEditing(string commandId)
    {
        if (commandId == ToggleOverwriteCommandId)
            return CanToggleOverwrite;
        if (commandId == GoToMatchingBracketCommandId)
            return true;
        if (Buffer.IsReadOnly)
            return false; // menus.md: "text and writable"
        var selection = Selection;
        return commandId switch
        {
            "edit.moveLineUp" => EditorOperations.TouchedLines(Buffer, selection).First > 1,
            "edit.moveLineDown" => EditorOperations.TouchedLines(Buffer, selection).Last < Buffer.LineCount,
            "edit.toggleLineComment" => _language.LineComment is not null,
            "edit.toggleBlockComment" => _language.BlockComment is not null,
            // IDE-ED-036: the selection, else the word under the caret.
            "edit.upperCase" or "edit.lowerCase" => !selection.IsEmpty || EditorOperations.WordAt(Buffer, selection.Caret) is var (s, e) && s < e,
            _ => true,
        };
    }

    private bool ExecuteEditing(string commandId)
    {
        if (commandId == ToggleOverwriteCommandId)
            return ToggleOverwrite();
        var selection = Selection;
        if (commandId == "edit.outdent")
            return Outdent(selection, IndentOptions);
        var tabWidth = Options.TabWidth;
        string? refused = null;
        var result = commandId switch
        {
            "edit.duplicateLine" => EditorOperations.DuplicateLine(Buffer, selection, LineEnding.NewLine),
            "edit.deleteLine" => EditorOperations.DeleteLine(Buffer, selection, tabWidth),
            "edit.moveLineUp" or "edit.moveLineDown" => EditorOperations.MoveLines(Buffer, selection, commandId == "edit.moveLineUp",
                FoldedLineRanges(), LineEnding.NewLine),
            "edit.toggleLineComment" => EditorOperations.ToggleLineComment(Buffer, selection, _language.LineComment!, tabWidth),
            "edit.toggleBlockComment" => EditorOperations.ToggleBlockComment(Buffer, selection, out refused),
            "edit.upperCase" => EditorOperations.ChangeCase(Buffer, selection, upper: true),
            "edit.lowerCase" => EditorOperations.ChangeCase(Buffer, selection, upper: false),
            "edit.trimTrailingWhitespace" => EditorOperations.TrimTrailingWhitespace(Buffer, selection),
            // shortcuts.md: Tab = edit.indent; without a selection spanning lines it inserts indentation (IDE-ED-001).
            "edit.indent" => EditorOperations.Tab(Buffer, selection, IndentOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(commandId)),
        };
        if (refused is not null)
            StatusMessage?.Invoke(this, Strings.ResourceManager.GetString(refused, Strings.Culture) ?? refused);
        if (commandId is "edit.moveLineUp" or "edit.moveLineDown" && result is not null
            && EditorOperations.MoveRanges(Buffer, selection, commandId == "edit.moveLineUp", FoldedLineRanges()) is var (first, last, otherFirst, otherLast))
        {
            // IDE-ED-033: folded regions in the moved block, and a folded region moved over, stay folded.
            int block = last - first + 1, other = otherLast - otherFirst + 1, sign = commandId == "edit.moveLineUp" ? -1 : 1;
            return Command(() => Folding.KeepFoldedThroughMove([(first, last, sign * other), (otherFirst, otherLast, -sign * block)], () => Apply(result)));
        }

        return Command(() => Apply(result));
    }

    private List<(int StartLine, int EndLine)> FoldedLineRanges() => [.. Folding.Regions.Where(r => r.IsFolded).Select(r => (r.StartLine, r.EndLine))];

    /// <summary>Applies <paramref name="result"/> as one undo step (refused while read-only) and selects its selection.</summary>
    public bool Apply(EditResult? result)
    {
        if (result is null)
            return false;
        if (result.Edits.Count > 0 && !TryEdit(result.Edits))
            return false;
        Selection = result.Selection;
        return true;
    }

    /// <summary>
    /// Shift+Tab / <c>edit.outdent</c> (IDE-ED-003). When there is nothing to outdent, or the document is read-only,
    /// and an info bar is shown, focus moves to the top-most info bar instead (UI-ED-094).
    /// </summary>
    private bool Outdent(TextSelection selection, IndentOptions options)
    {
        var result = EditorOperations.Outdent(Buffer, selection, options);
        if ((Buffer.IsReadOnly || result is null || result.Edits.Count == 0) && RequestInfoBarFocus())
            return true;
        return Command(() => Apply(result));
    }

    /// <summary>
    /// A command edit from outside (find/replace): <paramref name="action"/> runs as one undo step of its own that
    /// restores the selection on undo (IDE-P1-092); refused edits inside report as <see cref="TryEdit"/> does.
    /// </summary>
    internal bool ApplyCommandEdit(Func<bool> action) => Command(action);

    /// <summary>The indentation settings (editor-features.md §2) of <see cref="Options"/>.</summary>
    public IndentOptions IndentOptions => new(Math.Max(1, Options.TabWidth), Options.InsertSpaces, Options.AutoIndent, Options.SmartHome);

    /// <summary>
    /// A key the editor view passes on when the keymap did not take it (gesture text of <c>KeyGesture</c>): Tab,
    /// Shift+Tab, Backspace, Enter and Home with the indentation rules of editor-features.md §2. True when handled;
    /// on a read-only document an edit is refused with <see cref="ReadOnlyEditRefused"/>.
    /// </summary>
    public bool HandleKey(string gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        if (Completion?.HandleKey(gesture) == true)
            return true; // the open completion popup takes its keys first (IDE-ED-084)
        if (ParameterInfo?.HandleKey(gesture) == true)
            return true; // then parameter info: Up/Down overloads, Escape (IDE-ED-090)
        if (HandleFieldKey(gesture))
            return true; // then template field mode (IDE-ED-065)
        var selection = Selection;
        var options = IndentOptions;
        switch (gesture)
        {
            case "Tab" when TryTabExpand(selection): // IDE-ED-064
                return true;
            case "Tab":
                Command(() => Apply(EditorOperations.Tab(Buffer, selection, options)));
                return true;
            case "Shift+Tab":
                Outdent(selection, options);
                return true;
            case "Backspace" or "Shift+Backspace":
                if (Block is { } block)
                    Command(() => ReplaceBlock(block.Left < block.Right ? block : block with { Left = Math.Max(0, block.Left - 1) }, ""));
                else if (!selection.IsEmpty || _pairs.Exists(p => p.Opener == selection.Caret - 1 && p.Closer == selection.Caret))
                    Command(() => PairDelete(selection) ?? Apply(EditorOperations.Backspace(Buffer, selection, options)));
                else if (EditorOperations.Backspace(Buffer, selection, options) is { } backspace)
                    DeleteKey(EditKind.Backspace, Buffer.GetText(backspace.Edits[0].Offset, backspace.Edits[0].Length), () => Apply(backspace));
                return true;
            case "Enter" or "Shift+Enter":
                Command(() =>
                {
                    var enter = EditorOperations.Enter(Buffer, selection, options, _language.IndentRules, LineEnding.NewLine);
                    // IDE-ED-024: the closer goes in with the line break, as one undo step.
                    var completed = Options.AutoCloseBeginEnd && _language.Id == "pascal"
                        ? Editing.BlockCompletion.Extend(Buffer, selection, enter, FilePath ?? Title, LineEnding.NewLine)
                        : null;
                    return Apply(completed ?? enter);
                });
                return true;
            case "Home" or "Shift+Home":
                Selection = EditorOperations.Home(Buffer, selection, options, extend: gesture == "Shift+Home");
                return true;
            default:
                return false;
        }
    }
}