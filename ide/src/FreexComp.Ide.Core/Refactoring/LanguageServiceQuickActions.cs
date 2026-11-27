using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.CodeModel;
using FreexComp.LanguageService;
using IFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Refactoring;

/// <summary>
/// IDE-REFAC-033: the code actions of the language service as quick actions. For a context with a source it asks
/// <see cref="CodeActionService.GetCodeActions"/> on the code-model worker for the selected range (a caret is an empty
/// range); every code action becomes a fix with the same id and title whose changes are the service's edits as a
/// workspace edit (<see cref="WorkspaceEdit.FromLanguageService"/>). What the language service offers later appears here
/// without a change.
/// </summary>
internal sealed class LanguageServiceQuickActions(CodeModelService codeModel, IFileSystem fileSystem) : IQuickActionProvider
{
    /// <summary>The worker's request kind: a newer request replaces one that has not started.</summary>
    private const string RequestKind = "quickActions";

    public async Task<IReadOnlyList<QuickAction>> GetActionsAsync(RefactoringContext context, CancellationToken ct)
    {
        if (context.Source is not { } source)
            return [];
        var length = source.Text.Text.Length;
        var start = Math.Clamp(context.Selection.Start, 0, length);
        var range = new TextSpan(start, Math.Clamp(context.Selection.End, start, length) - start);
        var actions = await codeModel.Worker.RunAsync(RequestKind, token => CodeActionService.GetCodeActions(source, range, token), ct);
        return [.. actions.Select(action => new QuickAction(action.Id, action.Title, QuickActionKind.Fix,
            async token => await WorkspaceEdit.FromLanguageService(action.Title, action.Edits, source.Compilation, fileSystem, token)))];
    }
}
