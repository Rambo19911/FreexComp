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
/// A symbol without source (a built-in, a <c>.pcu</c> symbol without its file) opens the generated read-only declaration view
/// of its unit (<c>NavigationService.GetGeneratedDeclaration</c>, IDE-ED-148, I-323) with the name selected.
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

    /// <summary>IDE-ED-149 (I-322): every open Pascal document gets its Ctrl+hover session.</summary>
    public void AttachDeclarationLinks()
    {
        documents.DocumentOpened += (_, e) => AttachLink(e.Document);
        foreach (var document in documents.Documents)
            AttachLink(document);
    }

    private void AttachLink(DocumentViewModel document)
    {
        if (document is TextDocumentViewModel { FilePath: not null, DeclarationLink: null } text && text.Language.Id == "pascal")
            text.DeclarationLink = new DeclarationLinkSession(text, (offset, ct) => LinkAsync(text, offset, ct));
    }

    /// <summary>
    /// IDE-ED-149: the identifier token at <paramref name="offset"/> when Go to Declaration would find a target for it
    /// (the same <c>GoToDefinition</c> query, else <c>GetGeneratedDeclaration</c>, on the worker, cancellable); null on
    /// keywords, comments, strings and identifiers without a declaration.
    /// </summary>
    private async Task<(int Offset, int Length)?> LinkAsync(TextDocumentViewModel document, int offset, CancellationToken ct)
    {
        if (document.FilePath is not { } path || projects.Project is null || codeModel.Synchronize() is null)
            return null;
        var source = codeModel.Workspace.GetDocument(path);
        var token = source.SyntaxTree.FindToken(offset);
        if (token.Kind != SyntaxKind.IdentifierToken || token.IsMissing || offset < token.Span.Start || offset >= token.Span.End)
            return null;
        var found = await codeModel.Worker.RunAsync("declarationLink", t =>
            !LsNavigation.GoToDefinition(source, token.Span.Start, t).IsEmpty || LsNavigation.GetGeneratedDeclaration(source, token.Span.Start, t) is not null, ct);
        return found ? (token.Span.Start, token.Span.Length) : null;
    }

    private bool CanGo => documents.ActiveDocument?.VisibleText is { FilePath: not null } d && d.Language.Id == "pascal" && projects.Project is not null; // I-302

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
        if (targets.IsEmpty && await codeModel.Worker.RunAsync("generatedDeclaration",
                ct => LsNavigation.GetGeneratedDeclaration(source, identifier.Span.Start, ct)) is { } generated)
        {
            // IDE-ED-148: the generated view of the symbol's unit (its tab reused), the name selected; activating it records
            // the departure from this document in the navigation history.
            var view = documents.ShowGeneratedDeclaration(generated.UnitName, generated.Text);
            var end = Math.Min(generated.NameSpan.End, view.Buffer.Length);
            view.Folding.Reveal(end);
            view.Selection = new Ide.Core.Text.TextSelection(Math.Min(generated.NameSpan.Start, end), end);
            view.RevealCaret(third: true);
            return true;
        }

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
        if (documents.ActiveDocument?.VisibleText is not { } document)
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
