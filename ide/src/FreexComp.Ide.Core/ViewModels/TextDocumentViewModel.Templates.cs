using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// Code templates in the document (editor-features.md §6, I-118): expansion (IDE-ED-060), Tab expansion (IDE-ED-064)
/// and field mode (IDE-ED-065) — the fields follow the edits, typing in a field updates its mirrors in the same undo
/// step, and all field edits until field mode ends form one undo step after the expansion's.
/// </summary>
public partial class TextDocumentViewModel
{
    private TemplateFieldMode? _fieldMode;
    private bool _movingInFields;

    /// <summary>The code templates of this document's language (set by the template controller); null = none.</summary>
    public CodeTemplateService? Templates { get; internal set; }

    /// <summary><c>editor.templates.expandOnTab</c> (set with <see cref="Templates"/>).</summary>
    internal Func<bool>? ExpandOnTab { get; set; }

    public bool InTemplateFieldMode => _fieldMode is { IsActive: true };

    /// <summary>The field occurrences while in field mode (drawn per editor-features.md IDE-ED-065).</summary>
    public IReadOnlyList<(int Start, int End)> TemplateFieldRanges => _fieldMode is { IsActive: true } mode ? [.. mode.AllOccurrences] : [];

    /// <summary>Field mode started, ended, or its ranges moved.</summary>
    public event EventHandler? TemplateFieldsChanged;

    private void InitTemplates()
    {
        Buffer.Changed += (_, e) =>
        {
            if (_fieldMode is not { IsActive: true } mode)
                return;
            if (mode.OnChanged(e.Offset, e.RemovedLength, e.InsertedText.Length))
                TemplateFieldsChanged?.Invoke(this, EventArgs.Empty);
            else
                EndFieldMode(); // IDE-ED-065: an edit outside all fields
        };
        ViewState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewState.Caret) && _fieldMode is { IsActive: true } mode && !_movingInFields && _actionDepth == 0
                && !mode.Contains(OffsetOf(ViewState.Caret)))
                EndFieldMode(); // the caret left all fields
        };
    }

    /// <summary>
    /// Expands <paramref name="template"/> in place of <c>[start, start + length)</c> as one undo step (IDE-ED-060): later
    /// lines get the line's indentation, a leading tab one indent unit; then field mode, or the caret at <c>$end$</c>.
    /// Template functions (IDE-ED-152) are computed on the document's syntax tree at <paramref name="start"/>.
    /// </summary>
    /// <param name="selection">The text of <c>$selection$</c>.</param>
    public bool ExpandTemplate(CodeTemplate template, int start, int length, string selection = "")
    {
        ArgumentNullException.ThrowIfNull(template);
        EndFieldMode();
        var lineText = EditorOperations.LineText(Buffer, Buffer.GetPosition(start).Line);
        var lineStart = Buffer.GetLine(Buffer.GetPosition(start).Line).Offset;
        var indent = new string([.. lineText.TakeWhile(c => c is ' ' or '\t').Take(start - lineStart)]);
        var unit = Options.InsertSpaces ? new string(' ', Math.Max(1, Options.TabWidth)) : "\t";
        var expansion = CodeTemplateExpander.Expand(template.Body, indent, unit, LineEnding.NewLine, selection, TemplateFunction(start));
        var done = Command(() =>
        {
            if (!TryInsertText(start, length, expansion.Text))
                return false;
            Selection = TextSelection.At(start + expansion.EndOffset);
            return true;
        });
        if (!done || expansion.Fields.Count == 0)
            return done;
        _fieldMode = new TemplateFieldMode(start, expansion);
        SelectField(0);
        TemplateFieldsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>IDE-ED-152: the template functions at <paramref name="offset"/>; the document is parsed once, when a function needs it.</summary>
    private Func<string, string> TemplateFunction(int offset)
    {
        FreexComp.Compiler.Syntax.SyntaxTree? tree = null;
        FreexComp.Compiler.Syntax.SyntaxTree Tree() => tree ??= FreexComp.Compiler.Syntax.SyntaxTree.Parse(
            FreexComp.Compiler.SourceText.From(FreexComp.Compiler.Text.PathUtil.Normalize(FilePath ?? ""), Buffer.Text));
        var now = (Clock?.Now ?? DateTimeOffset.UnixEpoch).LocalDateTime;
        return function => function switch
        {
            "UnitName" => TemplateFunctions.UnitName(Tree(), FilePath),
            "ClassName" => TemplateFunctions.ClassName(Tree(), offset),
            "Date" => TemplateFunctions.Date(now),
            "Time" => TemplateFunctions.Time(now),
            _ => "",
        };
    }

    /// <summary>IDE-ED-064: Tab right after an abbreviation, without a selection, outside strings and comments.</summary>
    private bool TryTabExpand(TextSelection selection)
    {
        if (Templates is not { } templates || ExpandOnTab?.Invoke() != true || !selection.IsEmpty || Block is not null)
            return false;
        var caret = selection.Caret;
        var start = caret;
        while (start > 0 && CodeModel.CompletionRules.IsIdentifierChar(Buffer.GetText(start - 1, 1)[0]))
            start--;
        if (start == caret || (caret < Buffer.Length && CodeModel.CompletionRules.IsIdentifierChar(Buffer.GetText(caret, 1)[0])))
            return false;
        if (templates.Find(Buffer.GetText(start, caret - start), _language.Id) is not { } template || InStringOrComment(caret))
            return false;
        return ExpandTemplate(template, start, caret - start);
    }

    /// <summary>Tab / Shift+Tab / Enter / Escape in field mode (IDE-ED-065); false = not in field mode or not its key.</summary>
    private bool HandleFieldKey(string gesture)
    {
        if (_fieldMode is not { IsActive: true } mode)
            return false;
        switch (gesture)
        {
            case "Tab":
                if (mode.Current < mode.Count - 1)
                    SelectField(mode.Current + 1);
                else
                    GoToTemplateEnd();
                return true;
            case "Shift+Tab":
                SelectField(Math.Max(0, mode.Current - 1));
                return true;
            case "Enter" when mode.Current == mode.Count - 1:
                GoToTemplateEnd();
                return true;
            case "Escape":
                EndFieldMode();
                return true;
            default:
                return false;
        }
    }

    private void SelectField(int index)
    {
        if (_fieldMode is not { } mode)
            return;
        mode.Current = index;
        var (start, end) = mode.Primary(index);
        _movingInFields = true;
        try
        {
            Selection = new TextSelection(start, end);
        }
        finally
        {
            _movingInFields = false;
        }
    }

    private void GoToTemplateEnd()
    {
        if (_fieldMode is not { } mode)
            return;
        var end = mode.End;
        EndFieldMode();
        Selection = TextSelection.At(end);
    }

    private void EndFieldMode()
    {
        if (_fieldMode is null)
            return;
        _fieldMode.Deactivate();
        _fieldMode = null;
        TemplateFieldsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Inside a user edit's undo group: copies an edited field occurrence to its mirrors (IDE-ED-065).</summary>
    private void SyncTemplateMirrors()
    {
        if (_fieldMode is not { IsActive: true } mode || mode.Edited is not { } edited)
            return;
        mode.Edited = null;
        var occurrences = mode.Occurrences(edited.Field);
        var (start, end) = occurrences[edited.Occurrence];
        var text = Buffer.GetText(start, end - start);
        var edits = occurrences.Where((o, i) => i != edited.Occurrence && Buffer.GetText(o.Start, o.End - o.Start) != text)
            .Select(o => new TextEdit(o.Start, o.End - o.Start, text)).ToList();
        if (edits.Count > 0)
        {
            var caret = OffsetOf(ViewState.Caret);
            var anchor = ViewState.SelectionAnchor is { } a ? OffsetOf(a) : caret;
            int Shift(int offset) => offset + edits.Where(e => e.Offset + e.Length <= offset).Sum(e => e.NewText.Length - e.Length);
            mode.Mirroring = true;
            try
            {
                Buffer.ApplyEdits(edits);
            }
            finally
            {
                mode.Mirroring = false;
            }

            Selection = new TextSelection(Shift(anchor), Shift(caret));
        }

        mode.LastEditVersion = Buffer.Version;
    }

    /// <summary>A field edit joins the previous field edit's undo step while field mode lasts.</summary>
    private bool ContinuesFieldEdits => _fieldMode is { IsActive: true, LastEditVersion: var v } && v == Buffer.Version;
}
