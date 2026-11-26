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
/// §3, IDE-LIB-026…038): copies a library unit (with the companion form and the include files it names) into the
/// project directory and adds it to the project, so that this project is built from the copy. Nothing is ever
/// overwritten; all or nothing.
/// </summary>
public sealed class LibraryCopyService
{
    public const string CommandId = "file.copyLibraryUnitToProject";

    /// <summary>The AutomationId part of the bar action (<c>InfoBar.LibrarySource.Action.CopyToProject</c>, UI-ED-147).</summary>
    public const string ActionName = "CopyToProject";

    private const string LogCategory = "LibraryCopy";
    private const string IncludeNotFound = "E1202";   // the compiler's "include file not found" (LANG-875)
    private const string IncludeUnreadable = "E0003"; // the compiler's "cannot read" (path, system text)
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
        var includes = new List<(string Source, string Relative, byte[] Bytes)>();
        try
        {
            bytes = _fileSystem.ReadAllBytes(source);
            var tree = SyntaxTree.Parse(SourceText.From(PathUtil.Normalize(source), EncodingDetector.Detect(bytes).Text),
                fs: new CompilerFileSystemAdapter(_fileSystem));

            // IDE-LIB-029: the include files as the compiler resolves them; a missing one → nothing is copied.
            if (tree.Diagnostics.FirstOrDefault(d => d.Code == IncludeNotFound) is { } missing)
            {
                Report(Format(Strings.Msg_File_LibraryCopyIncludeMissing, name, Convert.ToString(missing.Arguments[0], CultureInfo.InvariantCulture)!));
                return;
            }

            if (tree.Diagnostics.FirstOrDefault(d => d.Code == IncludeUnreadable) is { } unreadable)
            {
                await ReportFailureAsync(name, Convert.ToString(unreadable.Arguments[^1], CultureInfo.InvariantCulture)!);
                return;
            }

            var unitDirectory = Path.GetDirectoryName(PathUtil.Normalize(source))!;
            foreach (var include in tree.IncludedTrees.Select(t => t.Text.Path).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // Only what lies in the unit's folder moves with it; a file elsewhere (absolute name) is found where it is.
                var relative = Path.GetRelativePath(unitDirectory, include);
                if (relative == ".." || relative.StartsWith(@"..\", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                    continue;
                includes.Add((include, relative, _fileSystem.ReadAllBytes(include)));
            }

            var namedForm = FormOf(source, tree);
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

        // IDE-LIB-029: nor is an existing include file; the whole action is refused.
        if (includes.FirstOrDefault(i => _fileSystem.FileExists(Path.Combine(project.Directory, i.Relative))) is { Relative: { } existingInclude })
        {
            Report(Format(Strings.Msg_File_LibraryCopyAlreadyInProject, existingInclude));
            return;
        }

        // IDE-LIB-031, 032: copy, then add; whatever fails, nothing the action created stays behind.
        var created = new List<string>();
        var createdDirectories = new List<string>();
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

            foreach (var include in includes)
            {
                var includeTarget = Path.Combine(project.Directory, include.Relative);
                CreateDirectoryOf(includeTarget, createdDirectories);
                created.Add(includeTarget);
                _fileSystem.WriteAllBytes(includeTarget, include.Bytes);
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
            foreach (var directory in Enumerable.Reverse(createdDirectories))
                DeleteDirectoryQuietly(directory);
            await ReportFailureAsync(name, failure);
            return;
        }

        var withIncludes = string.Concat(includes.Select(i => $", '{i.Source}' to '{Path.Combine(project.Directory, i.Relative)}'"));
        _log.Info(LogCategory, $"{CommandId}: '{source}' copied to '{target}'{withIncludes}."); // IDE-LIB-036
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

    /// <summary>Creates the missing folders of <paramref name="file"/>'s directory, outermost first, and records them.</summary>
    private void CreateDirectoryOf(string file, List<string> created)
    {
        var missing = new List<string>();
        for (var directory = Path.GetDirectoryName(file); directory is not null && !_fileSystem.DirectoryExists(directory);
             directory = Path.GetDirectoryName(directory))
            missing.Insert(0, directory);
        foreach (var directory in missing)
        {
            _fileSystem.CreateDirectory(directory);
            created.Add(directory);
        }
    }

    private void DeleteDirectoryQuietly(string path)
    {
        try
        {
            _fileSystem.DeleteDirectory(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, $"{path}: not removed after a failed copy", e);
        }
    }

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
    /// The sibling <c>&lt;base&gt;.form</c> path when a <c>{$FORM}</c> directive of the unit names it (IDE-PM-051; directive
    /// trivia of the compiler's tree, so a directive inside a comment does not count).
    /// </summary>
    private static string? FormOf(string unitPath, SyntaxTree tree)
    {
        var formPath = Path.ChangeExtension(unitPath, ".form");
        var formName = Path.GetFileName(formPath);
        return tree.Directives.Any(d => d.Name == "FORM" && string.Equals(d.Argument.Trim().Trim('\''), formName, StringComparison.OrdinalIgnoreCase))
            ? formPath
            : null;
    }

    private static string Format(string text, params string[] args) => string.Format(CultureInfo.CurrentCulture, text, args);

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
