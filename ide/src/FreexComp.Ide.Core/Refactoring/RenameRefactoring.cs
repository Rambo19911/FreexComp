using System.Globalization;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Text;
using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.Refactoring;

/// <summary>The way a rename target is renamed (IDE-REFAC-042).</summary>
public enum RenameRoute
{
    /// <summary>A unit, program or library name: the Project Manager's file rename (IDE-REFAC-056, I-382).</summary>
    Unit,

    /// <summary>A local whose occurrences are all in the document: the inline session (IDE-REFAC-050, I-381).</summary>
    Inline,

    /// <summary>Everything else: the name entry, then the plan's workspace edit (IDE-REFAC-044).</summary>
    Dialog,
}

/// <summary>
/// Rename Symbol (IDE-REFAC-040…046; F13-001, I-379): the target at the caret is found by the language service, refused
/// where it cannot be renamed, routed, named by <c>enterName</c> (the dialog of I-380; tests pass a function) and renamed
/// by the engine as one operation with the plan's workspace edit.
/// </summary>
public sealed class RenameRefactoring : IRefactoring
{
    /// <summary>The refactoring's id.</summary>
    public const string RefactoringId = "rename-symbol";

    /// <summary>The command it handles once it is available (I-380).</summary>
    public const string RenameCommandId = "refactor.rename";

    private readonly IRefactoringService _engine;
    private readonly RenamePlanner _planner;
    private readonly CodeModelService _codeModel;
    private readonly IProjectService _projects;
    private readonly ILibrarySourceService _library;
    private readonly IToolchainLocator _toolchain;
    private readonly Func<RenameInfo, Document, int, Task<RenameDialogResult?>> _enterName;
    private readonly InlineRenameController? _inline;

    /// <param name="enterName">
    /// The name entry: the facts of the target, the snapshot and the position; it plans the names it is given (with the
    /// planner) and answers the chosen one with its plan, or null when the user cancels. Called without the engine's
    /// working text.
    /// </param>
    /// <param name="inline">The inline sessions (IDE-REFAC-050, I-381); null: a local target also takes the name entry.</param>
    public RenameRefactoring(IRefactoringService engine, RenamePlanner planner, CodeModelService codeModel, IProjectService projects,
        ILibrarySourceService library, IToolchainLocator toolchain, Func<RenameInfo, Document, int, Task<RenameDialogResult?>> enterName,
        InlineRenameController? inline = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(codeModel);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(toolchain);
        ArgumentNullException.ThrowIfNull(enterName);
        _engine = engine;
        _planner = planner;
        _codeModel = codeModel;
        _projects = projects;
        _library = library;
        _toolchain = toolchain;
        _enterName = enterName;
        _inline = inline;
    }

    public string Id => RefactoringId;

    public string Title => Strings.Refactoring_Rename;

    public string? CommandId => RenameCommandId;

    /// <summary>
    /// IDE-REFAC-040: an Object Pascal document, a selection within one line, and a name at the caret — the longest run of
    /// identifier characters (one <c>&amp;</c> right before it included) that holds the selection's start or ends at it and
    /// holds the whole selection; it starts with a letter or an underscore, is not in a comment, a string or other text
    /// the lexer does not read as code, and is not a reserved word (an <c>&amp;</c>-name is no reserved word).
    /// </summary>
    public bool IsApplicable(RefactoringContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var document = context.Document;
        if (document.Language.Id != "pascal")
            return false;
        var buffer = document.Buffer;
        var selection = context.Selection;
        if (selection.End > buffer.Length || buffer.GetPosition(selection.Start).Line != buffer.GetPosition(selection.End).Line)
            return false;
        if (NameAt(buffer.Text, selection) is not { } name)
            return false;
        return !CompletionRules.Suppressed(CompletionRules.ClassAt(document, name.Start));
    }

    /// <summary>The span of the name of IDE-REFAC-040 at <paramref name="selection"/> of <paramref name="text"/> (without the classifier's judgement), or null.</summary>
    internal static TextSpan? NameAt(string text, TextSelection selection)
    {
        var start = selection.Start;
        if (start < text.Length && text[start] == '&' && (start == 0 || !CompletionRules.IsIdentifierChar(text[start - 1])))
            start++; // the caret before the & of an &-name is in that name
        var from = start;
        while (from > 0 && CompletionRules.IsIdentifierChar(text[from - 1]))
            from--;
        var to = start;
        while (to < text.Length && CompletionRules.IsIdentifierChar(text[to]))
            to++;
        if (from == to || selection.End > to || !(char.IsLetter(text[from]) || text[from] == '_'))
            return null;
        var escaped = from > 0 && text[from - 1] == '&';
        if (!escaped && SyntaxFacts.IsReservedWord(text[from..to]))
            return null;
        return escaped ? new TextSpan(from - 1, to - from + 1) : new TextSpan(from, to - from);
    }

    /// <summary>
    /// IDE-REFAC-042, first match: a unit name → <see cref="RenameRoute.Unit"/>; a local whose every span reads the current
    /// name (ignoring case) in <paramref name="text"/>, without a block selection or several carets → <see cref="RenameRoute.Inline"/>;
    /// else <see cref="RenameRoute.Dialog"/>.
    /// </summary>
    public static RenameRoute Route(RenameInfo info, string text, bool blockOrMultiCaret)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(text);
        if (info.Kind == CompletionItemKind.Unit)
            return RenameRoute.Unit;
        if (info.Scope == RenameScope.Local && !info.LocalSpans.IsEmpty && !blockOrMultiCaret
            && info.LocalSpans.All(s => s.End <= text.Length && string.Equals(text.Substring(s.Start, s.Length), info.CurrentName, StringComparison.OrdinalIgnoreCase)))
            return RenameRoute.Inline;
        return RenameRoute.Dialog;
    }

    /// <summary>
    /// IDE-REFAC-041…044: the target and its refusals (in the order of IDE-REFAC-041, before anything is asked), the route,
    /// the name entry, and the plan's workspace edit for the engine to apply as one operation. Null: the user cancelled.
    /// </summary>
    public async Task<WorkspaceEdit?> ComputeAsync(RefactoringContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var source = context.Source ?? throw new RefactoringException(Strings.Reason_Rename_NoProject);
        if (_toolchain.Resolve().RtlDir is null)
            throw new RefactoringException(Strings.Reason_Rename_NoLibrary);
        var buffer = context.Document.Buffer;
        if (!string.Equals(source.Text.Text, buffer.Text, StringComparison.Ordinal))
        {
            // The buffer changed after the context was taken (an inline session was cancelled, IDE-REFAC-054): anew.
            if (_codeModel.Synchronize() is null || context.Document.FilePath is not { } path)
                throw new RefactoringException(Strings.Reason_Rename_NoProject);
            source = _codeModel.Workspace.GetDocument(path);
        }

        var position = context.Selection.Start;
        RenameInfo info;
        try
        {
            info = await _planner.GetInfoAsync(source, position, ct);
        }
        catch (IOException e)
        {
            throw new RefactoringException(string.Format(CultureInfo.CurrentCulture, Strings.Reason_Rename_NoCodeModel, e.Message));
        }

        if (!info.CanRename)
            throw new RefactoringException(info.Reason ?? "");
        if (info.DeclarationPaths.FirstOrDefault(p => _library.IsLibrarySource(p) && !_projects.IsMember(p)) is { } library)
            throw new RefactoringException(string.Format(CultureInfo.CurrentCulture, Strings.Reason_Rename_Library, info.CurrentName, Path.GetFileName(library)));

        var route = Route(info, buffer.Text, context.Document.HasBlockSelection);
        if (route == RenameRoute.Unit)
            throw new RefactoringException(Strings.Reason_Rename_UnitInProjectManager); // until I-382
        if (route == RenameRoute.Inline && _inline is { } inline)
        {
            // IDE-REFAC-050: the inline session; the engine is not busy during it.
            inline.Start(context.Document, source, position, info);
            return null;
        }

        var answer = await _engine.WithoutWorkingTextAsync(() => _enterName(info, source, position));
        if (answer is null)
            return null;
        return answer.Plan.Edit ?? throw new RefactoringException(answer.Plan.Result.Error ?? "");
    }
}
