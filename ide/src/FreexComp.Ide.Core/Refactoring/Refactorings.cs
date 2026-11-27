using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Refactoring;

/// <summary>
/// Where a refactoring or a quick action is asked for (IDE-REFAC-031): the text document, its selection (a caret is an
/// empty selection) and the language-service document of the file, taken after the code model was brought up to date.
/// <paramref name="Source"/> is null without a project, for a file without a path, for a language other than Object
/// Pascal — and always inside <see cref="IRefactoring.IsApplicable"/>.
/// </summary>
public sealed record RefactoringContext(TextDocumentViewModel Document, TextSelection Selection, FreexComp.LanguageService.Document? Source);

/// <summary>
/// A refusal of a refactoring: <paramref name="message"/> is a sentence for the user that says why it is not possible
/// here (shown by <c>Msg.Refactoring.Refused</c>).
/// </summary>
public sealed class RefactoringException(string message) : Exception(message);

/// <summary>
/// A file of a workspace edit could not be read while the edit was built (IDE-REFAC-003): <see cref="Path"/> and, as
/// the message, the system's reason. The engine reports it as a failed operation, not as an internal error.
/// </summary>
public sealed class WorkspaceEditReadException(string path, Exception inner) : IOException(inner.Message, inner)
{
    public string Path { get; } = path;
}

/// <summary>
/// A refactoring (IDE-REFAC-030): registered with <see cref="IRefactoringService.Register(IRefactoring)"/>, it is offered
/// as a quick action where it is applicable and handles its catalogued command.
/// </summary>
public interface IRefactoring
{
    /// <summary>Kebab-case, unique among the registered refactorings.</summary>
    string Id { get; }

    /// <summary>The operation's name, without a trailing ellipsis.</summary>
    string Title { get; }

    /// <summary>The catalogued command this refactoring handles, or null.</summary>
    string? CommandId { get; }

    /// <summary>
    /// Whether the refactoring can start at <paramref name="context"/> (whose source is null). Asked on the UI thread
    /// whenever commands are judged: synchronous, no I/O, no language-service query (UI-MENU-006).
    /// </summary>
    bool IsApplicable(RefactoringContext context);

    /// <summary>
    /// The changes of the refactoring at <paramref name="context"/>; it may show a dialog of its own and uses the
    /// code-model worker for language-service work. Null: the user cancelled. A <see cref="RefactoringException"/>: it is
    /// not possible here, with the reason.
    /// </summary>
    Task<WorkspaceEdit?> ComputeAsync(RefactoringContext context, CancellationToken ct);
}

/// <summary>The two groups of quick actions (IDE-REFAC-032): fixes of a diagnostic come first, then refactorings.</summary>
public enum QuickActionKind
{
    Fix,
    Refactoring,
}

/// <summary>
/// An action offered at a position (IDE-REFAC-032): <paramref name="ComputeAsync"/> gives its changes — null when the
/// user cancelled, a <see cref="RefactoringException"/> when it is not possible after all.
/// </summary>
public sealed record QuickAction(string Id, string Title, QuickActionKind Kind, Func<CancellationToken, Task<WorkspaceEdit?>> ComputeAsync);

/// <summary>A source of quick actions (IDE-REFAC-032), registered with <see cref="IRefactoringService.Register(IQuickActionProvider)"/>.</summary>
public interface IQuickActionProvider
{
    /// <summary>The actions offered at <paramref name="context"/>, in the order they are shown. Called on the UI thread.</summary>
    Task<IReadOnlyList<QuickAction>> GetActionsAsync(RefactoringContext context, CancellationToken ct);
}
