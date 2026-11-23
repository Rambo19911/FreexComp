using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Editing;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using TextChange = FreexComp.Compiler.Syntax.TextChange;

namespace FreexComp.Ide.Core.Designer.CodeSync;

/// <summary>
/// The unit side of a form document for code sync: its text buffer and the form class name (the root's type name). Every
/// edit parses the current buffer text afresh (DSG-CS-001) and applies the compiler's text changes through
/// <see cref="ITextBuffer.ApplyEdits"/> (DSG-CS-002).
/// </summary>
public sealed class FormUnit(ITextBuffer buffer, string formTypeName, string path = FormClassLocator.PlaceholderPath)
{
    public ITextBuffer Buffer { get; } = buffer;

    /// <summary>The root object's type name (e.g. <c>TMainForm</c>); follows <see cref="FieldEdits.RenameFormClassAndVariable"/>.</summary>
    public string FormTypeName { get; set; } = formTypeName;

    public string Path { get; } = path;

    /// <summary>Setting <c>editor.rightMargin</c> (default 120; 0 = no limit), used by DSG-CS-052.</summary>
    public int RightMargin { get; set; } = 120;

    /// <summary>A fresh parse of the buffer and the form class in it (code-sync.md §2).</summary>
    public FormClassLocation Locate() => FormClassLocator.Locate(Buffer.Text, FormTypeName, Path);

    /// <summary>DSG-CS-003: edits are possible only in state <c>Ok</c>.</summary>
    internal FormClassInfo RequireFormClass() =>
        Locate() is { State: CodeSyncState.Ok, FormClass: { } info }
            ? info
            : throw new InvalidOperationException("Code sync edits need the state Ok.");

    /// <summary>DSG-CS-005 line ending, DSG-CS-006 indentation unit, the right margin.</summary>
    internal EditOptions Options(FormClassInfo info) =>
        new(Buffer.LineEnding == LineEndingKind.Lf ? "\n" : "\r\n", IndentUnit(info), RightMargin);

    /// <summary>
    /// DSG-CS-006: the leading whitespace of the first member line of the class minus that of the line with its
    /// <c>class</c> keyword, when longer and starting with it; else two spaces, or a tab when the unit indents with tabs.
    /// </summary>
    public static string IndentUnit(FormClassInfo info)
    {
        var text = info.Tree.Text;
        var first = info.Class.Members.SelectMany(m => m is VisibilitySectionSyntax s ? s.Members : (IEnumerable<SyntaxNode>)[m])
            .OfType<MemberDeclarationSyntax>().FirstOrDefault();
        if (first is not null)
        {
            var member = LeadingWhitespace(text, text.Lines.GetLinePosition(first.Span.Start).Line);
            var heading = LeadingWhitespace(text, text.Lines.GetLinePosition(info.Class.ClassKeyword.Span.Start).Line);
            if (member.Length > heading.Length && member.StartsWith(heading, StringComparison.Ordinal))
            {
                return member[heading.Length..];
            }
        }

        for (var line = 0; line < text.Lines.LineCount; line++)
        {
            if (LeadingWhitespace(text, line) is ['\t', ..])
            {
                return "\t";
            }
        }

        return "  ";
    }

    /// <summary>Applies non-overlapping changes of one tree as one undo step; returns them as records in application order.</summary>
    internal IReadOnlyList<TextEditRecord> Apply(IEnumerable<TextChange> changes)
    {
        var ordered = changes.Where(c => !c.IsEmpty).OrderByDescending(c => c.Span.Start).ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var records = ordered.Select(c => new TextEditRecord(c.Span.Start, Buffer.GetText(c.Span.Start, c.Span.Length), c.NewText)).ToList();
        Buffer.ApplyEdits([.. ordered.Select(c => new TextEdit(c.Span.Start, c.Span.Length, c.NewText))]);
        return records;
    }

    /// <summary>One code-editor undo group around several parse-and-apply steps (DSG-CS-001, 002).</summary>
    internal IDisposable UndoGroup() => Buffer.OpenUndoGroup();

    private static string LeadingWhitespace(FreexComp.Compiler.SourceText text, int line)
    {
        var span = text.Lines.GetLineSpan(line);
        var end = span.Start;
        while (end < span.End && text.Text[end] is ' ' or '\t')
        {
            end++;
        }

        return text.Text[span.Start..end];
    }
}
