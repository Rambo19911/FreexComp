using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Build;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The fields of dialogs.md §22, shared by the Run Parameters dialog and the <c>Run</c> page of Project Options (§8.6):
/// <c>Arguments</c> (verbatim), <c>WorkingDir</c> (invalid syntax → <c>Msg.Project.LocationInvalid</c>, blocking; a missing
/// folder → the warning <c>Msg.ProjectOptions.SearchPathMissing</c>), <c>HostApplication</c> (IDE-TGT-012: enabled for
/// library projects only; invalid syntax → <c>Msg.Project.LocationInvalid</c>) and the live <c>CommandLine</c> preview.
/// </summary>
public sealed class RunFields : ObservableObject
{
    private readonly string _projectDirectory;
    private readonly Func<string> _executable;
    private readonly IdeFileSystem _fileSystem;
    private readonly Func<bool> _isLibrary;
    private string _arguments;
    private string _workingDir;
    private string _hostApplication;

    /// <param name="isLibrary">The application type of the working copy is <c>library</c>.</param>
    public RunFields(string projectDirectory, Func<string> executable, IdeFileSystem fileSystem, RunSettings run, Func<bool> isLibrary)
    {
        ArgumentNullException.ThrowIfNull(run);
        _projectDirectory = projectDirectory;
        _executable = executable;
        _fileSystem = fileSystem;
        _isLibrary = isLibrary;
        _arguments = run.Arguments;
        _workingDir = run.WorkingDir;
        _hostApplication = run.HostApplication;
        Validate();
    }

    /// <summary>
    /// <c>run.hostApplication</c> (IDE-TGT-012): the program that loads the library; relative to the project directory
    /// with <c>/</c> or absolute. Kept unchanged while the field is disabled.
    /// </summary>
    public string HostApplication
    {
        get => _hostApplication;
        set
        {
            if (!SetProperty(ref _hostApplication, value ?? ""))
                return;
            OnPropertyChanged(nameof(HostApplicationError));
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(CommandLine));
        }
    }

    /// <summary>The field is enabled only for a library project.</summary>
    public bool HostApplicationEnabled => _isLibrary();

    /// <summary>The message under <c>HostApplication</c> (<c>Error.HostApplication</c>): invalid path syntax; null when none.</summary>
    public string? HostApplicationError =>
        HostApplicationEnabled && HostApplication.Trim().Length > 0 && Resolve(_projectDirectory, HostApplication) is null
            ? Strings.Msg_Project_LocationInvalid
            : null;

    public string Arguments
    {
        get => _arguments;
        set
        {
            if (SetProperty(ref _arguments, value ?? ""))
                OnPropertyChanged(nameof(CommandLine));
        }
    }

    public string WorkingDir
    {
        get => _workingDir;
        set
        {
            if (SetProperty(ref _workingDir, value ?? ""))
                Validate();
        }
    }

    /// <summary>The message under <c>WorkingDir</c> (<c>Error.WorkingDir</c>); null when none.</summary>
    public string? WorkingDirMessage { get; private set; }

    /// <summary>The message is the blocking error (otherwise it is the warning).</summary>
    public bool WorkingDirIsError { get; private set; }

    public bool IsValid => !WorkingDirIsError && HostApplicationError is null;

    /// <summary><c>&lt;exe of the active configuration&gt; &lt;arguments&gt;</c>; for a library the host application.</summary>
    public string CommandLine => (Arguments.Length == 0 ? _executable() : _executable() + " " + Arguments).TrimStart();

    /// <summary>The settings as stored: a relative working folder and host application with <c>/</c>.</summary>
    public RunSettings ToSettings()
    {
        static string Stored(string path) => Path.IsPathFullyQualified(path) ? path : path.Replace('\\', '/');
        return new RunSettings(Arguments, Stored(WorkingDir.Trim())) { HostApplication = Stored(HostApplication.Trim()) };
    }

    /// <summary>The picked folder, relative with <c>/</c> when it lies inside the project directory, else absolute.</summary>
    public static string StoredFolder(string projectDirectory, string folder)
    {
        var relative = Path.GetRelativePath(projectDirectory, folder);
        return relative == "." ? "." : relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative)
            ? folder
            : relative.Replace('\\', '/');
    }

    /// <summary>The full path of a stored folder (relative to the project directory); null when the syntax is invalid.</summary>
    public static string? Resolve(string projectDirectory, string folder) =>
        NewProjectValidation.NormalizeLocation(Path.Combine(projectDirectory, folder.Trim().Replace('/', '\\')));

    /// <summary>The executable or the application type of the working copy changed.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(HostApplicationEnabled));
        OnPropertyChanged(nameof(HostApplicationError));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(CommandLine));
    }

    private void Validate()
    {
        var dir = WorkingDir.Trim();
        string? message = null;
        var error = false;
        if (dir.Length > 0)
        {
            if (Resolve(_projectDirectory, dir) is not { } full)
            {
                message = Strings.Msg_Project_LocationInvalid;
                error = true;
            }
            else if (!_fileSystem.DirectoryExists(full))
            {
                message = Strings.Msg_ProjectOptions_SearchPathMissing;
            }
        }

        WorkingDirMessage = message;
        WorkingDirIsError = error;
        OnPropertyChanged(nameof(WorkingDirMessage));
        OnPropertyChanged(nameof(WorkingDirIsError));
        OnPropertyChanged(nameof(IsValid));
    }
}

/// <summary>
/// The Run Parameters dialog (dialogs.md §22, UI-DLG-220), 560 × auto: <c>OK</c> writes <c>run.arguments</c> and
/// <c>run.workingDir</c> through <see cref="IProjectService.ApplyOptionsAsync"/> and closes; nothing is started.
/// </summary>
public sealed class RunParametersViewModel : DialogViewModel<bool>
{
    public const string DialogName = "RunParameters";

    private readonly IProjectService _projects;
    private readonly IFileDialogService _pickers;
    private readonly Project _project;

    public RunParametersViewModel(IProjectService projects, IFileDialogService pickers, IdeFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(projects);
        _projects = projects;
        _pickers = pickers;
        _project = projects.Definition ?? throw new InvalidOperationException("No project is open.");
        Fields = new RunFields(_project.Directory,
            () => ExecutablePath(_project with { Run = Fields!.ToSettings() }, _project.ActiveConfiguration), fileSystem, _project.Run,
            () => _project.AppType == "library");
        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, CommitAsync) { IsDefault = true, IsAccent = true };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(false)) { IsCancel = true }];
        Fields.PropertyChanged += (_, _) => Ok.IsEnabled = Fields.IsValid;
        Ok.IsEnabled = Fields.IsValid;
    }

    public override string Name => DialogName;

    public override string Title => string.Format(CultureInfo.CurrentCulture, Strings.RunParameters_Title, _project.Name);

    public override double Width => 560;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    public RunFields Fields { get; }

    /// <summary>
    /// What Run starts: <c>&lt;project dir&gt;\&lt;outputDir&gt;\&lt;name&gt;.exe</c> of a configuration (dialogs.md §8.5); for a
    /// library project the host application (IDE-TGT-013), empty when none is set.
    /// </summary>
    public static string ExecutablePath(Project project, string configuration)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.AppType == "library")
        {
            var host = project.Run.HostApplication.Trim();
            return host.Length == 0 ? "" : RunFields.Resolve(project.Directory, host) ?? host;
        }

        // IDE-PLAT-005: the output directory of the configuration on the project's active platform (CMP-1366).
        if (ConfigurationEdits.Effective(project, configuration) is null)
            return Path.Combine(project.Directory, project.Name + ".exe");
        string dir;
        try
        {
            dir = Path.GetFullPath(ProjectOptions.GetOutputDirectory(project, configuration, ProjectOptions.GetEffectivePlatform(project)));
        }
        catch (ArgumentException)
        {
            dir = project.Directory; // an output folder that is no path (still being typed in Project Options)
        }

        return Path.Combine(dir, project.Name + ".exe");
    }

    /// <summary>The <c>WorkingDir.Browse</c> button: the folder picker, stored relative inside the project directory.</summary>
    public async Task BrowseAsync()
    {
        var initial = RunFields.Resolve(_project.Directory, Fields.WorkingDir) ?? _project.Directory;
        var picked = await _pickers.PickFolderAsync(new FilePickerRequest(DialogName + ".WorkingDir", Strings.RunParameters_WorkingDirPicker, [], initial));
        if (picked is not null)
            Fields.WorkingDir = RunFields.StoredFolder(_project.Directory, picked);
    }

    /// <summary>The <c>HostApplication.Browse</c> button (IDE-TGT-012).</summary>
    public Task BrowseHostAsync() => BrowseHostAsync(_pickers, DialogName, _project.Directory, Fields);

    /// <summary>The file picker filtered to <c>*.exe</c>; the choice is stored relative with <c>/</c> inside the project directory.</summary>
    internal static async Task BrowseHostAsync(IFileDialogService pickers, string dialogName, string projectDirectory, RunFields fields)
    {
        var current = RunFields.Resolve(projectDirectory, fields.HostApplication);
        var initial = fields.HostApplication.Trim().Length > 0 && current is not null ? Path.GetDirectoryName(current) : projectDirectory;
        var picked = await pickers.PickOpenFilesAsync(new FilePickerRequest(dialogName + ".HostApplication", Strings.RunParameters_HostApplicationPicker,
            [new FileFilter(Strings.FilePrompt_Filter_Programs, ["*.exe"])], initial));
        if (picked.Count > 0)
            fields.HostApplication = RunFields.StoredFolder(projectDirectory, picked[0]);
    }

    private async Task CommitAsync()
    {
        if (!Fields.IsValid)
            return;
        var current = _projects.Definition ?? _project;
        if (await _projects.ApplyOptionsAsync(current with { Run = Fields.ToSettings() }))
            Close(true);
    }
}
