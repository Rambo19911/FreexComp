using System.Collections.Immutable;
using System.Globalization;
using FreexComp.Build;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>How the question about a project's commands ended (IDE-TRUST-006…009); the default is <see cref="Cancel"/>.</summary>
public enum TrustPromptOutcome
{
    Cancel,
    BuildWithoutEvents,
    Trusted,
}

public enum TrustRowKind
{
    /// <summary>The group row of a project: its full path.</summary>
    Project,

    /// <summary>UI-DLG-293: the note of a project whose commands changed since it was trusted.</summary>
    Changed,

    /// <summary>One command, exactly as stored in the project file.</summary>
    Command,
}

/// <summary>
/// One row of the command tree (UI-DLG-293…295). <paramref name="Project"/> is the zero-based index of the project in the
/// dialog; <paramref name="Index"/> the zero-based index of a command among the commands of its project (before-build
/// commands first); <paramref name="ListName"/> is "Before build" / "After build" for a command row.
/// </summary>
public sealed record TrustRow(TrustRowKind Kind, int Project, int Index, string ListName, string Text)
{
    /// <summary>UI-DLG-295: what Ctrl+C copies for this row (a project: its path; a command: its text, unaltered).</summary>
    public string CopyText => Text;

    /// <summary>UI-DLG-295: the automation name; a command row: <c>&lt;list name&gt;: &lt;command&gt;</c>, never shortened.</summary>
    public string AutomationName => Kind == TrustRowKind.Command ? ListName + ": " + Text : Text;

    /// <summary>The line of this row in "copy all" (UI-DLG-295); null for the note row.</summary>
    public string? CopyAllLine => Kind switch
    {
        TrustRowKind.Project => Text,
        TrustRowKind.Command => "  " + ListName + ": " + Text,
        _ => null,
    };
}

/// <summary>
/// The dialog <c>TrustProject</c> (dialogs.md §29, UI-DLG-290…296; IDE-TRUST-005…010): the commands of every project of the
/// build that is not trusted, and the four answers. 640 × 420, resizable.
/// </summary>
public sealed class TrustProjectViewModel : DialogViewModel<TrustPromptOutcome>
{
    public const string DialogName = "TrustProject";

    private readonly ITrustStore _store;
    private readonly Func<Task<ImmutableArray<UntrustedProject>>> _recheck;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _pickers;
    private readonly IClipboardService _clipboard;
    private readonly ILayoutService? _layout;
    private readonly string _projectName;
    private readonly DialogButtonViewModel _trustProject;
    private ImmutableArray<UntrustedProject> _projects;
    private IReadOnlyList<TrustRow> _rows = [];
    private string? _error;

    /// <param name="projectName">Name of the project the build command was started for (UI-DLG-290).</param>
    /// <param name="projects">The projects to list, in build order; not empty.</param>
    /// <param name="recheck">IDE-TRUST-008: the check of IDE-TRUST-004 again, after a folder was trusted.</param>
    public TrustProjectViewModel(string projectName, ImmutableArray<UntrustedProject> projects, ITrustStore store,
        Func<Task<ImmutableArray<UntrustedProject>>> recheck, IDialogService dialogs, IFileDialogService pickers,
        IClipboardService clipboard, ILayoutService? layout = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(recheck);
        if (projects.IsDefaultOrEmpty)
            throw new ArgumentException("The dialog lists at least one project.", nameof(projects));
        _projectName = projectName;
        _store = store;
        _recheck = recheck;
        _dialogs = dialogs;
        _pickers = pickers;
        _clipboard = clipboard;
        _layout = layout;
        _trustProject = new DialogButtonViewModel("TrustProject", "", TrustProjectsAsync);
        Buttons =
        [
            // UI-DLG-291: the answer that stores nothing is the default; the trust buttons are ordinary buttons.
            Button("BuildWithoutEvents", Strings.Dialog_TrustProject_BuildWithoutEvents, () => Close(TrustPromptOutcome.BuildWithoutEvents), isDefault: true),
            _trustProject,
            Button("TrustFolder", Strings.Dialog_TrustProject_TrustFolder, TrustFolderAsync),
            new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(TrustPromptOutcome.Cancel)) { IsCancel = true },
        ];
        SetProjects(projects);
    }

    public override string Name => DialogName;

    public override string Title => string.Format(CultureInfo.CurrentCulture, Strings.Dialog_TrustProject_Title, _projectName);

    public override double Width => 640;

    public override double? Height => 420;

    public override bool CanResize => true;

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public string Intro => Strings.Msg_Trust_Intro;

    /// <summary>The listed projects, in build order (a dependency before the project that needs it).</summary>
    public ImmutableArray<UntrustedProject> Projects => _projects;

    /// <summary>The rows of the tree, top to bottom; replaced as a whole when the list of projects changes.</summary>
    public IReadOnlyList<TrustRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    /// <summary>The inline message under the tree (<c>Msg.Trust.FolderDoesNotCover</c>); null = hidden.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>
    /// UI-DLG-295: Ctrl+C. One selected row: its text (a project: its path; a command: its text, unaltered). Several rows
    /// (all of them after Ctrl+A): the lines <c>&lt;project path&gt;</c> and <c>  &lt;list name&gt;: &lt;command&gt;</c>, in row order.
    /// </summary>
    public Task CopyAsync(IReadOnlyList<TrustRow> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        return selected.Count switch
        {
            0 => Task.CompletedTask,
            1 => _clipboard.SetTextAsync(selected[0].CopyText),
            _ => _clipboard.SetTextAsync(string.Join(Environment.NewLine, Rows.Where(selected.Contains).Select(r => r.CopyAllLine).OfType<string>())),
        };
    }

    private static DialogButtonViewModel Button(string id, string resource, Action execute, bool isDefault = false) =>
        new(id, WithoutMnemonic(resource), execute) { AccessLabel = resource, IsDefault = isDefault, IsAccent = isDefault };

    private static DialogButtonViewModel Button(string id, string resource, Func<Task> execute) =>
        new(id, WithoutMnemonic(resource), execute) { AccessLabel = resource };

    /// <summary>The resource text marks its mnemonic with <c>_</c>.</summary>
    private static string WithoutMnemonic(string resource) => resource.Replace("_", "", StringComparison.Ordinal);

    private void SetProjects(ImmutableArray<UntrustedProject> projects)
    {
        _projects = projects;
        var rows = new List<TrustRow>();
        for (var i = 0; i < projects.Length; i++)
        {
            var project = projects[i];
            rows.Add(new TrustRow(TrustRowKind.Project, i, -1, "", project.ProjectPath));
            if (project.State == TrustState.Changed)
                rows.Add(new TrustRow(TrustRowKind.Changed, i, -1, "", Strings.Msg_Trust_Changed));
            var j = 0;
            foreach (var command in Commands(project.PreBuild))
                rows.Add(new TrustRow(TrustRowKind.Command, i, j++, Strings.Dialog_TrustProject_BeforeBuild, command));
            foreach (var command in Commands(project.PostBuild))
                rows.Add(new TrustRow(TrustRowKind.Command, i, j++, Strings.Dialog_TrustProject_AfterBuild, command));
        }

        // UI-DLG-292: singular / plural.
        var resource = projects.Length == 1 ? Strings.Dialog_TrustProject_TrustProject : Strings.Dialog_TrustProject_TrustProjects;
        _trustProject.Label = WithoutMnemonic(resource);
        _trustProject.AccessLabel = resource;
        Rows = rows;
        OnPropertyChanged(nameof(Projects));
    }

    private static ImmutableArray<string> Commands(ImmutableArray<string> list) => list.IsDefault ? [] : list;

    /// <summary>IDE-TRUST-006: every listed project is trusted with the hash of the commands that are shown.</summary>
    private async Task TrustProjectsAsync()
    {
        try
        {
            foreach (var project in _projects)
            {
                var shown = new Project { Events = new ProjectEvents(Commands(project.PreBuild), Commands(project.PostBuild)) };
                _store.TrustProject(project.ProjectPath, ProjectTrust.CommandsHash(shown));
            }
        }
        catch (IOException e)
        {
            await ShowSaveFailedAsync(e);
            return;
        }

        Close(TrustPromptOutcome.Trusted);
    }

    /// <summary>IDE-TRUST-008: the folder picker, then the check again.</summary>
    private async Task TrustFolderAsync()
    {
        var initial = Path.GetDirectoryName(_projects[0].ProjectPath);
        var folder = await _pickers.PickFolderAsync(new FilePickerRequest(DialogName + ".Folder", Strings.FilePrompt_Title_folder, [], initial));
        if (folder is null)
            return; // the dialog stays as it was
        try
        {
            _store.TrustFolder(folder);
        }
        catch (IOException e)
        {
            await ShowSaveFailedAsync(e);
            return;
        }

        var left = await _recheck();
        if (left.IsEmpty)
        {
            Close(TrustPromptOutcome.Trusted);
            return;
        }

        SetProjects(left);
        Error = string.Format(CultureInfo.CurrentCulture, Strings.Msg_Trust_FolderDoesNotCover, folder);
    }

    /// <summary>IDE-TRUST-010: the message on top of the prompt, which stays open and unchanged.</summary>
    private Task ShowSaveFailedAsync(IOException e) => _dialogs.ShowMessageAsync(new MessageRequest("Msg.Trust.SaveFailed", [e.Message]));
}
