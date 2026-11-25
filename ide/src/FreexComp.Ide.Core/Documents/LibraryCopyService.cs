using System.Globalization;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using IFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Documents;

/// <summary>
/// <c>file.copyLibraryUnitToProject</c> and the <c>Copy to Project</c> action of the library bar (library-local-copy.md
/// §3, IDE-LIB-026…038): copies a library unit (and the companion form it names) into the project directory and adds
/// it to the project, so that this project is built from the copy. Nothing is ever overwritten; all or nothing.
/// </summary>
public sealed class LibraryCopyService
{
    public const string CommandId = "file.copyLibraryUnitToProject";

    /// <summary>The AutomationId part of the bar action (<c>InfoBar.LibrarySource.Action.CopyToProject</c>, UI-ED-147).</summary>
    public const string ActionName = "CopyToProject";

    private const string LogCategory = "LibraryCopy";
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IFileSystem _fileSystem;
    private readonly IDocumentService _documents;
    private readonly ProjectService _projects;
    private readonly ILibrarySourceService _library;
    private readonly IIdeState _state;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ILog _log;

    public LibraryCopyService(IFileSystem fileSystem, IDocumentService documents, ProjectService projects, ILibrarySourceService library,
        IIdeState state, IDialogService dialogs, IStatusService status, ILog log)
    {
        _fileSystem = fileSystem;
        _documents = documents;
        _projects = projects;
        _library = library;
        _state = state;
        _dialogs = dialogs;
        _status = status;
        _log = log;
        // IDE-LIB-027: what the bar offers follows the project, its members and the IDE state.
        _projects.ProjectChanged += (_, _) => RefreshBarActions();
        _state.Changed += (_, _) => RefreshBarActions();
    }

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler(CommandId, new Handler(
            _ => _documents.ActiveDocument is TextDocumentViewModel document && IsOffered(document) && IsIdle,
            _ => CopyAsync((TextDocumentViewModel)_documents.ActiveDocument!)));
    }

    private bool IsIdle => _state.Current.State == IdeState.Idle;

    /// <summary>The <c>Copy to Project</c> action of <paramref name="document"/>'s library bar (IDE-LIB-037).</summary>
    public InfoBarAction CreateBarAction(TextDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var action = new InfoBarAction(ActionName, Strings.InfoBar_Action_CopyToProject, () => CopyAsync(document));
        Update(action, document);
        return action;
    }

    /// <summary>
    /// IDE-LIB-027 without the IDE state: a project is open, the document's file is a <c>.pas</c> library source that is
    /// not a member of the project, and the copy would not land under a library root (IDE-LIB-035).
    /// </summary>
    private bool IsOffered(TextDocumentViewModel document) =>
        _projects.Project is { } project && document.FilePath is { } path
        && Path.GetExtension(path).Equals(".pas", StringComparison.OrdinalIgnoreCase)
        && _library.IsLibrarySource(path) && !_projects.IsMember(path)
        && !_library.IsLibrarySource(Path.Combine(project.Directory, Path.GetFileName(path)));

    private void Update(InfoBarAction action, TextDocumentViewModel document)
    {
        action.IsVisible = IsOffered(document);
        action.IsEnabled = IsIdle;
    }

    private void RefreshBarActions()
    {
        foreach (var document in _documents.Documents.OfType<TextDocumentViewModel>())
        {
            var action = document.InfoBars.FirstOrDefault(bar => bar.Kind == TextDocumentViewModel.LibrarySourceInfoBarKind)?
                .Actions.FirstOrDefault(a => a.Name == ActionName);
            if (action is not null)
                Update(action, document);
        }
    }

    private async Task CopyAsync(TextDocumentViewModel document)
    {
        if (!IsOffered(document) || !IsIdle || _projects.Project is not { } project)
            return;
        var source = document.FilePath!;
        var name = Path.GetFileName(source);
        var target = Path.Combine(project.Directory, name);

        // IDE-LIB-031: the file on disk is copied, never the editor buffer.
        if (document.IsModified)
        {
            Report(Format(Strings.Msg_File_LibraryCopyUnsaved, name));
            return;
        }

        // IDE-LIB-030: an existing target is never overwritten.
        if (_fileSystem.FileExists(target))
        {
            if (_projects.IsMember(target))
            {
                if (await OpenAsync(target) is not null)
                    Report(Format(Strings.Msg_File_LibraryCopyAlreadyInProject, name));
                return;
            }

            if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.LibraryCopyExists", [name])) != "AddAndOpen")
                return;
            var existing = _projects.AddExistingFileQuietly(target);
            if (!existing.Success)
            {
                await ReportFailureAsync(name, ReasonOf(existing));
                return;
            }

            _log.Info(LogCategory, $"{CommandId}: the existing '{target}' was added to the project instead of copying '{source}'.");
            await OpenAsync(target);
            return;
        }

        byte[] bytes;
        byte[]? formBytes = null;
        string? form;
        try
        {
            bytes = _fileSystem.ReadAllBytes(source);
            var (hasInclude, namedForm) = Directives(source, EncodingDetector.Detect(bytes).Text);
            if (hasInclude)
            {
                // IDE-LIB-029: an include file is looked up beside the including file, so the copy would not compile.
                Report(Format(Strings.Msg_File_LibraryCopyHasIncludes, name));
                return;
            }

            form = namedForm is not null && _fileSystem.FileExists(namedForm) ? namedForm : null;
            if (form is not null)
                formBytes = _fileSystem.ReadAllBytes(form);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await ReportFailureAsync(name, e.Message);
            return;
        }

        var targetForm = form is null ? null : Path.Combine(project.Directory, Path.GetFileName(form));
        if (targetForm is not null && _fileSystem.FileExists(targetForm))
        {
            // IDE-LIB-031: an existing form file is never overwritten; the whole action is refused.
            Report(Format(Strings.Msg_File_LibraryCopyAlreadyInProject, Path.GetFileName(targetForm)));
            return;
        }

        // IDE-LIB-031, 032: copy, then add; whatever fails, nothing the action created stays behind.
        var created = new List<string>();
        string? failure = null;
        try
        {
            // New files: the library file's read-only attribute is not carried over.
            created.Add(target);
            _fileSystem.WriteAllBytes(target, bytes);
            if (targetForm is not null)
            {
                created.Add(targetForm);
                _fileSystem.WriteAllBytes(targetForm, formBytes!);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failure = e.Message;
        }

        if (failure is null && _projects.AddExistingFileQuietly(target) is { Success: false } notAdded)
            failure = ReasonOf(notAdded);
        if (failure is not null)
        {
            foreach (var path in created)
                DeleteQuietly(path);
            await ReportFailureAsync(name, failure);
            return;
        }

        _log.Info(LogCategory, $"{CommandId}: '{source}' copied to '{target}'."); // IDE-LIB-036
        if (await OpenAsync(target) is { } copy)
        {
            // IDE-LIB-033: the same caret position and vertical scroll position as the library document.
            copy.ViewState.Caret = document.ViewState.Caret;
            copy.ViewState.TopLine = document.ViewState.TopLine;
            copy.ViewState.VerticalOffset = document.ViewState.VerticalOffset;
            copy.RevealCaret(); // I-273: whatever the view has done with the position by now, the caret line is shown
        }

        Report(Format(Strings.Msg_File_LibraryCopyCreated, name));
    }

    /// <summary>UI-ED-149: a refusal or confirmation is a status-bar text shown for the usual time and written to the log.</summary>
    private void Report(string text)
    {
        _status.ShowMessage(text, StatusDuration);
        _log.Info(LogCategory, text);
    }

    /// <summary>Opens (or activates) <paramref name="path"/>; a failure is shown as the open's own message.</summary>
    private async Task<TextDocumentViewModel?> OpenAsync(string path)
    {
        var opened = await _documents.OpenAsync(path);
        if (opened.NeedsMessage)
            await _dialogs.ShowMessageAsync(new MessageRequest(opened.MessageId!, opened.Args));
        return opened.Value as TextDocumentViewModel;
    }

    private Task ReportFailureAsync(string name, string reason)
    {
        _log.Warning(LogCategory, $"{CommandId}: {name} was not copied: {reason}");
        return _dialogs.ShowMessageAsync(new MessageRequest("Msg.File.LibraryCopyFailed", [name, reason]));
    }

    /// <summary>The text of a failed project operation: the system's text when it has one, else the message itself.</summary>
    private static string ReasonOf(OperationResult result) =>
        result.MessageId == "Msg.File.SaveFailed" && result.Args.Count > 0
            ? result.Args[^1]
            : Format(Strings.ResourceManager.GetString(result.MessageId!.Replace('.', '_'), CultureInfo.CurrentUICulture) ?? result.MessageId,
                [.. result.Args]);

    private void DeleteQuietly(string path)
    {
        try
        {
            if (_fileSystem.FileExists(path))
                _fileSystem.DeleteFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, $"{path}: not removed after a failed copy", e);
        }
    }

    /// <summary>
    /// The directive trivia of the unit (compiler lexer, so a directive inside a comment does not count): whether it has
    /// an include directive (IDE-LIB-029; the switches <c>{$I+}</c> / <c>{$I-}</c> are none) and the sibling
    /// <c>&lt;base&gt;.form</c> path when a <c>{$FORM}</c> directive names it (IDE-PM-051).
    /// </summary>
    private static (bool HasInclude, string? Form) Directives(string unitPath, string text)
    {
        var formPath = Path.ChangeExtension(unitPath, ".form");
        var formName = Path.GetFileName(formPath);
        var hasInclude = false;
        string? form = null;
        foreach (var token in Lexer.Tokenize(SourceText.From(unitPath, text)))
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                // The lexer gives an include directive its own trivia kind ({$I+} / {$I-} are ordinary directives).
                if (trivia.Kind == SyntaxKind.IncludeDirectiveTrivia)
                    hasInclude = true;
                else if (trivia.Kind == SyntaxKind.DirectiveTrivia && trivia.Directive is { Name: "FORM" } directive
                    && string.Equals(directive.Argument.Trim().Trim('\''), formName, StringComparison.OrdinalIgnoreCase))
                    form = formPath;
            }
        }

        return (hasInclude, form);
    }

    private static string Format(string text, params string[] args) => string.Format(CultureInfo.CurrentCulture, text, args);

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
