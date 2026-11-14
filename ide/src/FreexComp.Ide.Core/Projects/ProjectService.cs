using System.Globalization;
using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;
using FreexComp.Ide.Core.ViewModels;
using IClock = FreexComp.Ide.Core.Platform.IClock;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Projects;

/// <inheritdoc />
public sealed class ProjectService : IProjectService
{
    private const string LogCategory = "project";

    private readonly IdeFileSystem _fileSystem;
    private readonly IDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ILog _log;
    private readonly FilePrompts _prompts;
    private readonly ISettingsService _settings;
    private readonly ILayoutService _layout;
    private readonly IClock _clock;
    private readonly HashSet<string> _members = new(StringComparer.OrdinalIgnoreCase);
    private ICommandService? _commands;
    private Project? _project;

    public ProjectService(IdeFileSystem fileSystem, IDocumentService documents, IDialogService dialogs, IStatusService status,
        ILog log, FilePrompts prompts, ISettingsService settings, IReadOnlyList<ProjectTemplate> templates, ILayoutService layout,
        IClock clock)
    {
        _layout = layout;
        _clock = clock;
        _fileSystem = fileSystem;
        _documents = documents;
        _dialogs = dialogs;
        _status = status;
        _log = log;
        _prompts = prompts;
        _settings = settings;
        Templates = templates;
    }

    public ProjectModel? Project { get; private set; }

    public IReadOnlyList<ProjectTemplate> Templates { get; }

    public event EventHandler? ProjectChanged;

    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("file.newProject", new Handler(_ => Templates.Count > 0, _ => NewProjectAsync()));
        commands.RegisterHandler("file.openProject", new Handler(_ => true, _ => PickAndOpenAsync()));
        commands.RegisterHandler("file.closeProject", new Handler(_ => Project is not null, _ => CloseAsync()));
        commands.RegisterHandler("project.viewSource", new Handler(_ => Project is not null, _ => ViewSourceAsync()));
    }

    public async Task<bool> OpenAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        if (_project is not null && string.Equals(_project.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            return true;

        // Steps 1-2 off the UI thread (IDE-ARCH-081) with the status "Opening <name>…".
        ProjectLoadResult load;
        using (_status.ShowStickyMessage(string.Format(CultureInfo.CurrentCulture, Strings.Status_OpeningProject, Path.GetFileNameWithoutExtension(fullPath))))
        {
            load = await Task.Run(() => ProjectFileAccess.Load(fullPath, _fileSystem));
        }

        var error = load.Project is { } loaded ? Validate(loaded) : load.Error;
        if (!error.Success)
        {
            if (await _dialogs.ShowMessageAsync(new MessageRequest(error.MessageId!, error.Args)) == "OpenAsText")
                await OpenDocumentAsync(fullPath);
            return false;
        }

        if (!await CloseAsync()) // IDE-PM-010: canceled → nothing changes
            return false;

        var project = load.Project!;
        if (!project.Configurations.ContainsKey(project.ActiveConfiguration))
            project = project with { ActiveConfiguration = project.ConfigurationNames[0] }; // IDE-PM-012: in memory
        SetProject(project);

        // Step 4: the session (I-073) restores documents; without one the main source opens. A missing main source is
        // reported by the missing-files check (step 7, UI-TW-107), not by a message box.
        var main = await _documents.OpenAsync(Resolve(project.MainSource));
        if (!main.Success)
            _log.Warning(LogCategory, $"{project.FilePath}: main source not opened ({main.MessageId})");
        return true;
    }

    public async Task<bool> CloseAsync()
    {
        if (_project is null)
            return true;

        // UI-DLG-063: the project file first, then the member documents in tab order.
        var members = _documents.Documents.Where(d => IsMember(d.FilePath)).ToList();
        var ordered = members.Where(IsProjectFile).Concat(members.Where(d => !IsProjectFile(d))).ToList();
        if (!await _documents.PromptSaveChangesAsync(ordered))
            return false;

        // The session write (I-073), Messages/Structure clearing (P3) and the file watcher (I-072) join here.
        foreach (var document in ordered)
        {
            _documents.Close(document);
        }

        SetProject(null);
        return true;
    }

    public bool IsMember(string? path) => path is not null && _members.Contains(Path.GetFullPath(path));

    public async Task<bool> ViewSourceAsync() =>
        _project is not null && await OpenDocumentAsync(Resolve(_project.MainSource));

    public async Task<NewProjectOutcome> CreateProjectAsync(NewProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = Templates.FirstOrDefault(t => t.Id == request.TemplateId)
            ?? throw new ArgumentException($"Unknown template '{request.TemplateId}'.", nameof(request));
        if (!await CloseAsync()) // UI-DLG-031: the unsaved-changes flow of the current project first
            return NewProjectOutcome.Canceled;

        var target = Path.Combine(NewProjectValidation.NormalizeLocation(request.Location) ?? request.Location.Trim(), request.Name);
        var check = NewProjectValidation.ValidateName(request.Name);
        if (check.Success)
            check = NewProjectValidation.ValidateLocation(request.Location, request.Name, template, _fileSystem);
        string? projectFile = null;
        var reason = check.Success ? CreateFiles(template, request.Name, target, out projectFile) : NewProjectValidation.MessageText(check);
        if (reason is not null)
        {
            await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Project.CreateFailed", [target, reason]));
            return NewProjectOutcome.Failed;
        }

        if (!await OpenAsync(projectFile!))
            return NewProjectOutcome.Failed; // reported by the open
        if (_documents.ActiveDocument is TextDocumentViewModel main && IsMainSource(main.FilePath))
            main.ViewState.Caret = CaretAfterBegin(main.Buffer);
        return NewProjectOutcome.Created;
    }

    /// <summary>
    /// IDE-PM-023: writes every template file into <paramref name="target"/>; null on success, else the OS reason after
    /// removing every file and folder this call created.
    /// </summary>
    private string? CreateFiles(ProjectTemplate template, string name, string target, out string? projectFile)
    {
        projectFile = null;
        var folders = new List<string>(); // created by this call, outermost first
        for (var d = target; d is not null && !_fileSystem.DirectoryExists(d); d = Path.GetDirectoryName(d))
            folders.Insert(0, d);
        var files = new List<string>();
        try
        {
            _fileSystem.CreateDirectory(target);
            foreach (var file in template.Files)
            {
                var path = Path.Combine(target, ProjectTemplate.Expand(file.Target, name));
                var text = ProjectTemplate.Expand(template.ReadSource(file.Source), name).Replace("\r", "", StringComparison.Ordinal);
                files.Add(path);
                if (path.EndsWith(".pproj", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = ProjectSerializer.ParseProject(path, text);
                    if (parsed.Value is not { } project || !parsed.Diagnostics.IsEmpty)
                        throw new InvalidDataException($"Template {template.Id}: {parsed.Diagnostics.FirstOrDefault()?.Message}");
                    var saved = ProjectFileAccess.Save(project, _fileSystem);
                    if (!saved.Success)
                        throw new IOException(saved.Args[^1]);
                    projectFile = path;
                }
                else if (path.EndsWith(".form", StringComparison.OrdinalIgnoreCase))
                {
                    AtomicFileWriter.Write(_fileSystem, path, FileEncodings.Encode(text, FileEncoding.Utf8));
                }
                else
                {
                    if (_settings.Get(SettingsCatalog.FilesDefaultLineEnding) != "lf")
                        text = text.Replace("\n", "\r\n", StringComparison.Ordinal);
                    var encoding = FileEncodings.FromId(_settings.Get(SettingsCatalog.FilesDefaultEncoding)) ?? FileEncoding.Utf8;
                    AtomicFileWriter.Write(_fileSystem, path, FileEncodings.Encode(text, encoding));
                }
            }

            return projectFile is null ? throw new InvalidDataException($"Template {template.Id} has no project file.") : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            RollBack(files, folders);
            return e.Message;
        }
    }

    private void RollBack(List<string> files, List<string> folders)
    {
        try
        {
            foreach (var file in files)
                _fileSystem.DeleteFile(file);
            for (var i = folders.Count - 1; i >= 0; i--)
                _fileSystem.DeleteDirectory(folders[i]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, "new project not fully removed", e);
        }
    }

    /// <summary>The first line after the first <c>begin</c> token, at its first non-blank character (IDE-PM-023).</summary>
    private static TextPosition CaretAfterBegin(ITextBuffer buffer)
    {
        var text = buffer.Text;
        var begins = Lexer.Tokenize(SourceText.From(@"C:\Untitled.pas", text)).Where(t => t.Kind == SyntaxKind.BeginKeyword).Take(1).ToList();
        if (begins.Count == 0)
            return new TextPosition(1, 1);
        var line = buffer.GetPosition(begins[0].Span.Start).Line + 1;
        if (line > buffer.LineCount)
            return new TextPosition(line - 1, 1);
        var start = buffer.GetOffset(line, 1);
        var indent = 0;
        while (start + indent < text.Length && text[start + indent] is ' ' or '\t')
            indent++;
        return new TextPosition(line, indent + 1);
    }

    private bool IsMainSource(string? path) =>
        _project is not null && path is not null && string.Equals(path, Resolve(_project.MainSource), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>file.newProject</c> (dialogs.md §3): the dialog, then <see cref="CreateProjectAsync"/>; after a failure the
    /// dialog opens again with the same values (UI-DLG-031). The location used is remembered
    /// (<c>dialogs.NewProject.lastLocation</c>).
    /// </summary>
    private async Task NewProjectAsync()
    {
        NewProjectRequest? previous = null;
        while (true)
        {
            var location = _layout.GetDialogValue(NewProjectViewModel.DialogName, "lastLocation") ?? _prompts.DefaultProjectsDirectory;
            var dialog = new NewProjectViewModel(Templates, _fileSystem, _clock, location, previous,
                current => _prompts.PickFolderAsync("Location", current));
            if (await _dialogs.ShowAsync(dialog) is not { } request)
                return;
            _layout.SetDialogValue(NewProjectViewModel.DialogName, "lastLocation", request.Location);
            if (await CreateProjectAsync(request) != NewProjectOutcome.Failed)
                return;
            previous = request;
        }
    }

    private async Task PickAndOpenAsync()
    {
        if (await _prompts.OpenProjectAsync() is { } path)
            await OpenAsync(path);
    }

    /// <summary>IDE-PM-012 beyond the serializer's checks.</summary>
    private static OperationResult Validate(Project project)
    {
        if (!IsIdentifier(project.Name))
            return OperationResult.Fail("Msg.Project.OpenFailed", project.FilePath, Strings.Msg_Project_NameInvalid);
        if (project.Configurations.IsEmpty)
            return OperationResult.Fail("Msg.Project.OpenFailed", project.FilePath, Strings.Reason_NoConfigurations);
        return OperationResult.Ok;
    }

    /// <summary><c>^[A-Za-z_][A-Za-z0-9_]*$</c> (IDE-PM-021's identifier rule).</summary>
    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private void SetProject(Project? project)
    {
        _project = project;
        _members.Clear();
        Project = project is null ? null : BuildModel(project);
        if (project is not null)
        {
            _members.Add(project.FilePath);
            foreach (var file in Project!.Files)
            {
                _members.Add(Resolve(file.Path));
                if (file.Form is { } form)
                    _members.Add(Resolve(form));
            }
        }

        ProjectChanged?.Invoke(this, EventArgs.Empty);
        _commands?.InvalidateCanExecute();
    }

    /// <summary>IDE-PM-012: the main source is listed first when missing; later duplicates are ignored with a warning.</summary>
    private ProjectModel BuildModel(Project project)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ProjectFileItem>();
        if (!project.Files.Any(f => string.Equals(Resolve(f.Path), Resolve(project.MainSource), StringComparison.OrdinalIgnoreCase)))
            project = project with { Files = project.Files.Insert(0, new ProjectFile(project.MainSource, null, null)) };

        foreach (var file in project.Files)
        {
            if (string.IsNullOrEmpty(file.Path))
            {
                // Lead decision: ignored here, kept in the Project (and so in the file on the next save).
                _log.Warning(LogCategory, $"{project.FilePath}: file entry with an empty path ignored");
                continue;
            }

            if (!seen.Add(Resolve(file.Path)))
            {
                _log.Warning(LogCategory, $"{project.FilePath}: duplicate file entry '{file.Path}' ignored");
                continue;
            }

            files.Add(new ProjectFileItem(file.Path, file.Form, file.EffectiveKind));
        }

        return new ProjectModel(project.FilePath, project.Directory, project.Name, project.Language, project.AppType,
            project.MainSource, files, project.ConfigurationNames, project.ActiveConfiguration, project.Platforms,
            isModified: false);
    }

    /// <summary>A project path ('/', relative to the project directory or absolute) as an absolute path.</summary>
    private string Resolve(string projectPath) =>
        Path.GetFullPath(Path.Combine(_project!.Directory, projectPath.Replace('/', Path.DirectorySeparatorChar)));

    private bool IsProjectFile(DocumentViewModel document) =>
        string.Equals(document.FilePath, _project?.FilePath, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> OpenDocumentAsync(string path)
    {
        var result = await _documents.OpenAsync(path);
        if (!result.Success)
            await _dialogs.ShowMessageAsync(new MessageRequest(result.MessageId!, result.Args));
        return result.Success;
    }

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
