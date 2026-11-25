using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// A row of the <c>Projects</c> check list (IDE-PM-176): another member of the group, or an entry of the project's
/// <c>dependencies</c> that is not one (<see cref="IsOutside"/>: greyed, with <see cref="Suffix"/>; it can only be
/// unticked, IDE-PM-179).
/// </summary>
public sealed class DependencyRow : ObservableObject
{
    private bool _isChecked;

    internal DependencyRow(string name, string fullPath, string displayPath, string? storedText, bool isOutside, bool isChecked)
    {
        Name = name;
        FullPath = fullPath;
        DisplayPath = displayPath;
        StoredText = storedText;
        IsOutside = isOutside;
        _isChecked = isChecked;
    }

    /// <summary>The project name; the file name without extension when the file cannot be read.</summary>
    public string Name { get; }

    public string FullPath { get; }

    /// <summary>The path as <c>dependencies</c> has it or would get it: relative to the project's directory, <c>/</c> separators.</summary>
    public string DisplayPath { get; }

    /// <summary>The entry's text in the file; null for a project that is not a dependency yet.</summary>
    internal string? StoredText { get; }

    public bool IsOutside { get; }

    /// <summary><c>Msg.Dependencies.Outside</c> for a dependency outside the group; null otherwise.</summary>
    public string? Suffix => IsOutside ? Strings.Msg_Dependencies_Outside : null;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
                Changed?.Invoke();
        }
    }

    internal Action? Changed { get; set; }
}

/// <summary>
/// The dialog <c>ProjectDependencies</c> (project-manager.md IDE-PM-175…180, UI-DLG-310…315), 520 × 360, resizable: the
/// other available members of the group as a check list, then the dependencies outside the group. After every change
/// the selection is checked with <see cref="ProjectDependencies.FindCycle"/>; a cycle shows <see cref="Error"/> and
/// disables <c>OK</c>. <c>OK</c> writes the <c>dependencies</c> key and nothing else — through
/// <see cref="IProjectService.ApplyOptionsAsync"/> for the loaded project, through the serializer for a listed member.
/// </summary>
public sealed class ProjectDependenciesViewModel : DialogViewModel<bool>
{
    public const string DialogName = "ProjectDependencies";

    /// <summary>Joins the project names of a cycle (UI-DLG-312).</summary>
    public const string CycleSeparator = " → ";

    private readonly IProjectGroupService _group;
    private readonly IProjectService _projects;
    private readonly IdeFileSystem _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly ILayoutService? _layout;
    private readonly ProjectGroupMember _target;
    private readonly ImmutableArray<string> _original;
    private string? _error;

    public ProjectDependenciesViewModel(IProjectGroupService group, IProjectService projects, IdeFileSystem fileSystem, IDialogService dialogs,
        ILayoutService? layout, ProjectGroupMember target)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(target);
        _group = group;
        _projects = projects;
        _fileSystem = fileSystem;
        _dialogs = dialogs;
        _layout = layout;
        _target = target;
        _original = (target.Definition ?? throw new ArgumentException("The project is not available.", nameof(target))).Dependencies;
        Rows = BuildRows();
        foreach (var row in Rows)
            row.Changed = Check;
        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, CommitAsync) { IsDefault = true, IsAccent = true };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(false)) { IsCancel = true }];
        Check();
    }

    public override string Name => DialogName;

    public override string Title => string.Format(CultureInfo.CurrentCulture, Strings.ProjectDependencies_Title, _target.Name);

    public override double Width => 520;

    public override double? Height => 360;

    public override bool CanResize => true;

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    /// <summary>The label of the list, with its mnemonic as <c>_</c>.</summary>
    public string ProjectsLabel => string.Format(CultureInfo.CurrentCulture, Strings.ProjectDependencies_Projects, _target.Name);

    /// <summary>IDE-PM-176: the other members in group order, then the outside entries in the order of the file.</summary>
    public IReadOnlyList<DependencyRow> Rows { get; }

    /// <summary><c>Msg.Dependencies.NoOtherProjects</c> when there is no row; null otherwise.</summary>
    public string? Placeholder => Rows.Count == 0 ? Strings.Msg_Dependencies_NoOtherProjects : null;

    /// <summary>IDE-PM-177: <c>Msg.Dependencies.Cycle</c> with the project names of the cycle; null without a cycle.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>The <c>dependencies</c> as <c>OK</c> would write them (IDE-PM-178).</summary>
    public ImmutableArray<string> Entries => [.. Rows.Where(r => r.IsChecked).Select(r => r.StoredText ?? r.DisplayPath)];

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private string Directory => Path.GetDirectoryName(_target.FilePath)!;

    private string Resolve(string stored) => Path.GetFullPath(Path.Combine(Directory, stored.Replace('/', Path.DirectorySeparatorChar)));

    private List<DependencyRow> BuildRows()
    {
        var stored = _original.Select(text => (Text: text, Full: Resolve(text))).ToList();
        var rows = new List<DependencyRow>();
        foreach (var member in _group.Members.Where(m => m.IsAvailable && !Same(m.FilePath, _target.FilePath)))
        {
            var entry = stored.FirstOrDefault(s => Same(s.Full, member.FilePath));
            rows.Add(new DependencyRow(member.Name, member.FilePath, Path.GetRelativePath(Directory, member.FilePath).Replace('\\', '/'),
                entry.Text, isOutside: false, isChecked: entry.Text is not null));
        }

        // Every other entry of the file: shown, so that OK loses none of them.
        foreach (var (text, full) in stored.Where(s => rows.All(r => !Same(r.FullPath, s.Full))))
            rows.Add(new DependencyRow(NameOf(full), full, text, text, isOutside: true, isChecked: true));
        return rows;
    }

    /// <summary>The name of the project at <paramref name="fullPath"/>: a member's, the file's, else the file name.</summary>
    private string NameOf(string fullPath) =>
        _group.Members.FirstOrDefault(m => Same(m.FilePath, fullPath))?.Name
        ?? ProjectFileAccess.Load(fullPath, _fileSystem).Project?.Name
        ?? Path.GetFileNameWithoutExtension(fullPath);

    private void Check()
    {
        var cycle = ProjectDependencies.FindCycle(new CompilerFileSystemAdapter(_fileSystem), _target.FilePath, Entries);
        Error = cycle is { } paths
            ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_Dependencies_Cycle, string.Join(CycleSeparator, paths.Select(NameOf)))
            : null;
        Ok.IsEnabled = Error is null; // a cycle cannot be saved
    }

    private async Task CommitAsync()
    {
        var entries = Entries;
        if (entries.SequenceEqual(_original, StringComparer.Ordinal))
        {
            Close(true); // IDE-PM-178: no change, nothing is written
            return;
        }

        if (Same(_projects.Project?.FilePath, _target.FilePath) && _projects.Definition is { } loaded)
        {
            // The loaded project: saved at once, as Project Options (IDE-PM-004).
            if (!await _projects.ApplyOptionsAsync(loaded with { Dependencies = entries }, ShowSaveFailedAsync))
                return;
        }
        else if (await WriteListedAsync(entries) is { } reason)
        {
            await ShowSaveFailedAsync(reason);
            return;
        }

        _group.Refresh();
        Close(true);
    }

    /// <summary>A listed member: load, <c>with</c>, save (unknown keys survive). Returns the reason of a failure, else null.</summary>
    private Task<string?> WriteListedAsync(ImmutableArray<string> entries)
    {
        var load = ProjectFileAccess.Load(_target.FilePath, _fileSystem);
        if (load.Project is not { } project)
            return Task.FromResult<string?>(load.Diagnostic?.Message ?? load.Error.Args[^1]);
        var saved = ProjectFileAccess.Save(project with { Dependencies = entries }, _fileSystem);
        return Task.FromResult(saved.Success ? null : saved.Args[^1]);
    }

    /// <summary>UI-DLG-313: the message box over the dialog, which stays open.</summary>
    private Task ShowSaveFailedAsync(string reason) =>
        _dialogs.ShowMessageAsync(new MessageRequest("Msg.Dependencies.SaveFailed", [_target.Name, reason]));

    /// <summary>
    /// <c>project.dependencies</c> (IDE-PM-175): the target is the argument (a project path), else
    /// <paramref name="selectedRoot"/> — the root row selected in the focused Project Manager —, else the active project;
    /// enabled when it is an available member (the Idle state is the rule of <c>IdeCommandAvailability</c>).
    /// </summary>
    public static void RegisterCommand(ICommandService commands, IProjectGroupService group, IProjectService projects, IdeFileSystem fileSystem,
        IDialogService dialogs, ILayoutService? layout, Func<string?> selectedRoot)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(selectedRoot);
        ProjectGroupMember? Target(string? argument)
        {
            var path = argument ?? selectedRoot() ?? group.ActiveMember?.FilePath;
            return path is null ? null : group.Members.FirstOrDefault(m => m.IsAvailable && Same(m.FilePath, Path.GetFullPath(path)));
        }

        commands.RegisterHandler("project.dependencies", new Handler(
            argument => Target(argument) is not null,
            argument => Target(argument) is { } target
                ? dialogs.ShowAsync(new ProjectDependenciesViewModel(group, projects, fileSystem, dialogs, layout, target))
                : Task.CompletedTask));
    }

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);
    }
}
