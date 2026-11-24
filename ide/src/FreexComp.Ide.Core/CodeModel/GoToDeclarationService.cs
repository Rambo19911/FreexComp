using System.Globalization;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.ViewModels;
using LsNavigation = FreexComp.LanguageService.NavigationService;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// IDE-ED-101 <c>search.goToDeclaration</c> (F12, Ctrl+click; I-117): resolves the identifier at the caret in the project
/// <c>Compilation</c> (<c>NavigationService.GoToDefinition</c> of the language service, which also toggles between a
/// declaration heading and its implementation) and moves the caret to the target's name, opening the file if needed —
/// a library source outside the project opens as a protected document (IDE-LIB-002, decided by the document service).
/// </summary>
public sealed class GoToDeclarationService(IDocumentService documents, IProjectService projects, CodeModelService codeModel,
    INavigationService navigation, IStatusService status)
{
    public const string CommandId = "search.goToDeclaration";

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler(CommandId, new Handler(this));
    }

    private bool CanGo => documents.ActiveDocument is TextDocumentViewModel { FilePath: not null } d && d.Language.Id == "pascal" && projects.Project is not null;

    /// <summary>Goes to the declaration of the identifier at <paramref name="offset"/> of <paramref name="document"/>; false when there is none.</summary>
    public async Task<bool> GoAsync(TextDocumentViewModel document, int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FilePath is not { } path || codeModel.Synchronize() is null)
            return false;
        var source = codeModel.Workspace.GetDocument(path);
        if (IdentifierAt(source.SyntaxTree, offset) is not { } identifier)
        {
            status.ShowMessage(Strings.Msg_Editor_NotAnIdentifier, StatusDuration);
            return false;
        }

        var targets = await codeModel.Worker.RunAsync("goToDeclaration", ct => LsNavigation.GoToDefinition(source, identifier.Span.Start, ct));
        if (targets.IsEmpty)
        {
            status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Editor_NoDeclaration, identifier.Text), StatusDuration);
            return false;
        }

        var target = targets[0];
        var opened = PathUtil.AreEqual(target.Path, path) ? document : (await documents.OpenAsync(target.Path)).Value as TextDocumentViewModel;
        if (opened is null)
            return false;
        var start = target.LineSpan.Start;
        navigation.GoToLine(opened, start.Line + 1, start.Column + 1);
        return true;
    }

    private Task GoFromCaretAsync()
    {
        if (documents.ActiveDocument is not TextDocumentViewModel document)
            return Task.CompletedTask;
        return GoAsync(document, document.CaretOffset); // I-210
    }

    /// <summary>The identifier token at the caret (or ending at it).</summary>
    private static SyntaxToken? IdentifierAt(SyntaxTree tree, int offset)
    {
        foreach (var at in (int[])[offset, offset - 1])
        {
            if (at < 0)
                continue;
            var token = tree.FindToken(at);
            if (token.Kind == SyntaxKind.IdentifierToken && !token.IsMissing && token.Span.Start <= at && at < token.Span.End)
                return token;
        }

        return null;
    }

    private sealed class Handler(GoToDeclarationService owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner.CanGo;

        public Task ExecuteAsync(string? argument) => owner.GoFromCaretAsync();
    }
}
