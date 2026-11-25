using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Build;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using IdeFileSystem = FreexComp.Ide.Core.Platform.IFileSystem;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>A page of the Project Options list (UI-DLG-080); <see cref="HasError"/> shows the <c>status.error</c> dot (UI-DLG-086).</summary>
public sealed class ProjectOptionsPage(string id, string title, bool perConfiguration) : ObservableObject
{
    private bool _hasError;

    public string Id { get; } = id;

    public string Title { get; } = title;

    /// <summary>The page has <b>[cfg]</b> fields: the configuration bar is enabled (UI-DLG-082).</summary>
    public bool PerConfiguration { get; } = perConfiguration;

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }
}

/// <summary>An entry of the <c>AppType</c> combo (dialogs.md §8.1): disabled items stay visible.</summary>
public sealed record AppTypeItem(string Value, string Title, bool IsEnabled);

/// <summary>
/// A row of the <c>SearchPaths</c> list; <see cref="IsMissing"/> shows the warning glyph (§8.3). <see cref="Origin"/> is
/// set for a read-only row a configuration inherits: <c>Project</c> or the name of the configuration that holds the entry.
/// </summary>
public sealed record SearchPathRow(string Path, bool IsMissing, string? Origin = null)
{
    public string? ToolTip => IsMissing ? Strings.Msg_ProjectOptions_SearchPathMissing : null;

    /// <summary>Read-only: the entry belongs to the project level or to an ancestor.</summary>
    public bool IsInherited => Origin is not null;
}

/// <summary>
/// The Project Options dialog (dialogs.md §8, UI-DLG-080…086), 760 × 520, resizable. It edits a working copy of the
/// open project's definition; <c>Apply</c>/<c>OK</c> call <see cref="IProjectService.ApplyOptionsAsync"/> (keys not shown
/// are never touched: the copy is made with <c>with</c> from the definition). Pages: Application, Compiler, Search Paths,
/// Conditional Defines, Output, Run, Build Events (read-only until P8+).
/// </summary>
public sealed partial class ProjectOptionsViewModel : DialogViewModel<bool>
{
    public const string DialogName = "ProjectOptions";

    private static readonly Regex ConfigNamePattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly Regex SymbolPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>CMP-1153: as the first entry of a configuration's defines or search paths it discards the inherited ones.</summary>
    private const string ResetMarker = "!";

    private readonly IProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _pickers;
    private readonly IdeFileSystem _fileSystem;
    private readonly ILayoutService _layout;
    private readonly List<ConfigState> _configs = [];
    private ImmutableArray<string> _projectSearchPaths;
    private bool _searchPathsAllConfigurations = true;
    private Project _baseline;
    private ProjectOptionsPage _selectedPage;
    private string _selectedConfiguration;
    private string _name;
    private string _appType;
    private string _mainSource;
    private string _projectDefines;
    private bool _changed;

    public ProjectOptionsViewModel(IProjectService projects, IDialogService dialogs, IFileDialogService pickers, IdeFileSystem fileSystem,
        ILayoutService layout)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(layout);
        _projects = projects;
        _dialogs = dialogs;
        _pickers = pickers;
        _fileSystem = fileSystem;
        _layout = layout;
        _baseline = projects.Definition ?? throw new InvalidOperationException("No project is open.");
        _name = _baseline.Name;
        _appType = _baseline.AppType;
        _mainSource = _baseline.MainSource;
        _projectDefines = string.Join(";", _baseline.Defines);
        foreach (var name in _baseline.ConfigurationNames)
            _configs.Add(ConfigState.From(name, _baseline.Configurations[name]));
        _selectedConfiguration = _configs.Any(c => c.Name == _baseline.ActiveConfiguration) ? _baseline.ActiveConfiguration : _configs[0].Name;
        _projectSearchPaths = _baseline.SearchPaths;
        _projectEvents = new EventsState(_baseline.Events);
        SearchPaths = new ObservableCollection<SearchPathRow>(_projectSearchPaths.Select(p => Row(p)));
        Run = new RunFields(_baseline.Directory, () => RunParametersViewModel.ExecutablePath(Build(), ActiveName), fileSystem, _baseline.Run,
            () => AppType == "library");
        Run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RunFields.Arguments) or nameof(RunFields.WorkingDir) or nameof(RunFields.HostApplication))
                MarkChanged();
        };

        Pages =
        [
            new("Application", Strings.ProjectOptions_Page_Application, false),
            new("Compiler", Strings.ProjectOptions_Page_Compiler, true),
            new("SearchPaths", Strings.ProjectOptions_Page_SearchPaths, true),
            new("ConditionalDefines", Strings.ProjectOptions_Page_ConditionalDefines, true),
            new("Output", Strings.ProjectOptions_Page_Output, true),
            new("Run", Strings.ProjectOptions_Page_Run, false),
            new("BuildEvents", Strings.ProjectOptions_Page_BuildEvents, true),
        ];
        _selectedPage = Pages.FirstOrDefault(p => p.Id == layout.GetDialogValue(DialogName, "page")) ?? Pages[0];

        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, () => CommitAsync(close: true)) { IsDefault = true, IsAccent = true };
        Apply = new DialogButtonViewModel("Apply", Strings.Button_Apply, () => CommitAsync(close: false)) { IsEnabled = false };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(false)) { IsCancel = true }, Apply];
        Validate();
    }

    public override string Name => DialogName;

    public override string Title => string.Format(CultureInfo.CurrentCulture, Strings.ProjectOptions_Title, _baseline.Name);

    public override double Width => 760;

    public override double? Height => 520;

    public override bool CanResize => true;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    public DialogButtonViewModel Apply { get; }

    public IReadOnlyList<ProjectOptionsPage> Pages { get; }

    /// <summary>The shown page; remembered in UI state <c>dialogs.ProjectOptions.page</c> (UI-DLG-080).</summary>
    public ProjectOptionsPage SelectedPage
    {
        get => _selectedPage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedPage, value))
                return;
            _layout.SetDialogValue(DialogName, "page", value.Id);
            OnPropertyChanged(nameof(ConfigurationBarEnabled));
            OnPropertyChanged(nameof(HasAllConfigurationsChoice));
            OnPropertyChanged(nameof(ConfigurationSelected));
            OnPropertyChanged(nameof(CanDeleteConfiguration));
        }
    }

    // ---- Configuration bar (UI-DLG-082…085) ----

    /// <summary>The configuration whose [cfg] fields are shown.</summary>
    public string SelectedConfiguration
    {
        get => _selectedConfiguration;
        set
        {
            if (value is null || !_configs.Any(c => c.Name == value) || !SetProperty(ref _selectedConfiguration, value))
                return;
            LoadSearchPaths();
            OnConfigurationFieldsChanged();
        }
    }

    public IReadOnlyList<string> ConfigurationNames => [.. _configs.Select(c => c.Name)];

    /// <summary>The active configuration (the first remaining one when the active one was deleted, UI-DLG-084).</summary>
    public string ActiveName => _configs.Any(c => c.Name == _baseline.ActiveConfiguration) ? _baseline.ActiveConfiguration : _configs[0].Name;

    /// <summary>Combo text of a configuration: <c>Debug (active)</c> for the active one.</summary>
    public string ConfigurationTitle(string name) =>
        name == ActiveName ? string.Format(CultureInfo.CurrentCulture, Strings.ProjectOptions_ActiveSuffix, name) : name;

    public bool ConfigurationBarEnabled => SelectedPage.PerConfiguration;

    /// <summary>
    /// The Search Paths and Build Events pages: the selector starts with <c>All configurations</c>, the project-level
    /// values (UI-DLG-082, IDE-EVT-011).
    /// </summary>
    public bool HasAllConfigurationsChoice => SelectedPage.Id is "SearchPaths" or "BuildEvents";

    /// <summary>The page shows the values of <see cref="SelectedConfiguration"/>: <c>New…</c> and <c>Delete</c> apply.</summary>
    public bool ConfigurationSelected => ConfigurationBarEnabled && !(HasAllConfigurationsChoice && SearchPathsAllConfigurations);

    /// <summary>UI-DLG-084, IDE-CFG-014: not the last configuration and not one another configuration inherits from.</summary>
    public bool CanDeleteConfiguration => ConfigurationSelected && DeleteConfigurationToolTip is null;

    /// <summary>Why <c>DeleteConfiguration</c> is disabled (its tooltip); null when the selected configuration can be deleted.</summary>
    public string? DeleteConfigurationToolTip =>
        _configs.Count <= 1 ? Strings.Msg_ProjectOptions_ConfigDeleteLast
        : _configs.FirstOrDefault(c => c.Inherits == SelectedConfiguration) is { } child
            ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ConfigDeleteInherited, SelectedConfiguration, child.Name)
            : null;

    /// <summary>UI-DLG-085, IDE-CFG-020: "(Debug)" after the label of a [cfg] field; "(Release-Demo, inherits Release)" for a derived configuration.</summary>
    public string ConfigurationNote => Selected.Inherits is { } parent
        ? string.Format(CultureInfo.CurrentCulture, Strings.ProjectOptions_ConfigNoteDerived, SelectedConfiguration, parent)
        : "(" + SelectedConfiguration + ")";

    /// <summary>The selected configuration inherits from another one: its fields have the states inherited / own (IDE-CFG-021).</summary>
    public bool IsDerived => Selected.Inherits is not null;

    /// <summary>UI-DLG-083: the <c>NewConfiguration</c> sub-dialog; the new configuration is appended and selected.</summary>
    public async Task NewConfigurationAsync()
    {
        var sub = new NewConfigurationViewModel(ConfigurationNames, SelectedConfiguration);
        if (await _dialogs.ShowAsync(sub) is not { } created)
            return;
        var source = _configs.First(c => c.Name == created.CopyFrom);
        if (created.Inherit)
        {
            // IDE-CFG-006: derived, no own value, its own output folder.
            _configs.Add(new ConfigState(created.Name, null, 0, false, "", ConfigurationEdits.DefaultOutputDir(created.Name), source.Name, false, false));
        }
        else
        {
            // IDE-CFG-007: the edited values of the source; the entry of a loaded source also brings its link, the values
            // this dialog does not show and its unknown keys.
            string? original = null;
            if (source.Original is { } loaded)
            {
                _baseline = ConfigurationEdits.AddCopy(_baseline, created.Name, loaded);
                original = created.Name;
            }

            var folder = source.OutputDir.Trim().Replace('\\', '/') == ConfigurationEdits.DefaultOutputDir(source.Name)
                ? ConfigurationEdits.DefaultOutputDir(created.Name)
                : source.OutputDir;
            _configs.Add(source with { Name = created.Name, Original = original, OutputDir = folder });
        }

        OnPropertyChanged(nameof(ConfigurationNames));
        OnPropertyChanged(nameof(CanDeleteConfiguration));
        SelectedConfiguration = created.Name;
        MarkChanged();
    }

    /// <summary>UI-DLG-084: asks <c>Msg.ProjectOptions.ConfigDeleteConfirm</c>; never deletes the last one.</summary>
    public async Task DeleteConfigurationAsync()
    {
        if (DeleteConfigurationToolTip is not null)
            return;
        var name = SelectedConfiguration;
        if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.ProjectOptions.ConfigDeleteConfirm", [name])) != "Delete")
            return;
        var index = _configs.FindIndex(c => c.Name == name);
        if (_configs[index].Original is { } loaded)
            _baseline = ConfigurationEdits.Remove(_baseline, loaded); // no entry of it survives for a later configuration of that name
        _configs.RemoveAt(index);
        _selectedConfiguration = _configs[Math.Min(index, _configs.Count - 1)].Name;
        OnPropertyChanged(nameof(ConfigurationNames));
        OnPropertyChanged(nameof(SelectedConfiguration));
        OnPropertyChanged(nameof(CanDeleteConfiguration));
        LoadSearchPaths();
        OnConfigurationFieldsChanged();
        MarkChanged();
    }

    // ---- Application (§8.1) ----

    public string ProjectName
    {
        get => _name;
        set => Edit(ref _name, value ?? "", nameof(OutputFile));
    }

    public string? ProjectNameError { get; private set; }

    public IReadOnlyList<AppTypeItem> AppTypes { get; } =
    [
        new("console", Strings.ProjectOptions_AppType_Console, true),
        new("gui", Strings.ProjectOptions_AppType_Gui, false), // enabled from P4
        new("library", Strings.ProjectOptions_AppType_Library, true), // IDE-TGT-010
        new("package", Strings.ProjectOptions_AppType_Package, false),
    ];

    public string AppType
    {
        get => _appType;
        set
        {
            if (AppTypes.Any(a => a.Value == value && a.IsEnabled))
                Edit(ref _appType, value, nameof(OutputFile));
        }
    }

    /// <summary>The project files with kind <c>unit</c>, as relative paths.</summary>
    public IReadOnlyList<string> UnitFiles =>
        [.. _baseline.Files.Where(f => f.EffectiveKind == "unit").Select(f => f.Path).Prepend(_baseline.MainSource).Distinct(StringComparer.OrdinalIgnoreCase)];

    public string MainSource
    {
        get => _mainSource;
        set
        {
            if (value is not null && UnitFiles.Contains(value, StringComparer.OrdinalIgnoreCase))
                Edit(ref _mainSource, value);
        }
    }

    public string LanguageText => Strings.ProjectOptions_LanguagePascal;

    /// <summary>
    /// IDE-PLAT-016: the listed platforms by label (UI-TB-022), comma-separated, the active one followed by
    /// <c> (active)</c>. Read-only: platforms are edited in the Project Manager.
    /// </summary>
    public string PlatformText
    {
        get
        {
            var active = FreexComp.Build.ProjectOptions.GetEffectivePlatform(_baseline);
            return string.Join(", ", _baseline.Platforms.Select(p =>
                PlatformLabels.Label(p, _projects.PlatformCatalog) + (p == active ? " " + Strings.ProjectOptions_PlatformActiveSuffix : "")));
        }
    }

    public string ProjectFile => _baseline.FilePath;

    // ---- Compiler (§8.2, [cfg]) ----

    /// <summary>The effective value (IDE-CFG-021); a change makes it an own value of the selected configuration (IDE-CFG-022).</summary>
    public int Optimization
    {
        get => Selected.OwnOptimization ? Selected.Optimization : Effective.Optimization;
        set
        {
            if (value is >= 0 and <= 2 && value != Optimization)
                Replace(Selected with { Optimization = value, OwnOptimization = true });
        }
    }

    /// <summary>The effective value (IDE-CFG-021); a change makes it an own value of the selected configuration (IDE-CFG-022).</summary>
    public bool DebugInfo
    {
        get => Selected.OwnDebugInfo ? Selected.DebugInfo : Effective.DebugInfo;
        set
        {
            if (value != DebugInfo)
                Replace(Selected with { DebugInfo = value, OwnDebugInfo = true });
        }
    }

    /// <summary>State <i>own</i> of <see cref="Optimization"/> in a derived configuration: bold label and <c>Revert</c> (IDE-CFG-021).</summary>
    public bool OptimizationIsOwn => IsDerived && Selected.OwnOptimization;

    public bool DebugInfoIsOwn => IsDerived && Selected.OwnDebugInfo;

    /// <summary>Automation help text of the field: where the value comes from; null for a root configuration.</summary>
    public string? OptimizationHelpText => StateText(Selected.OwnOptimization, c => c.OwnOptimization);

    public string? DebugInfoHelpText => StateText(Selected.OwnDebugInfo, c => c.OwnDebugInfo);

    /// <summary>IDE-CFG-023 <c>Optimization.Revert</c>: removes the own value; the parent's effective value shows again.</summary>
    public void RevertOptimization()
    {
        if (OptimizationIsOwn)
            Replace(Selected with { Optimization = 0, OwnOptimization = false });
    }

    /// <summary>IDE-CFG-023 <c>DebugInfo.Revert</c>.</summary>
    public void RevertDebugInfo()
    {
        if (DebugInfoIsOwn)
            Replace(Selected with { DebugInfo = false, OwnDebugInfo = false });
    }

    // ---- Search Paths (§8.3; per configuration: phase8-configurations.md § "Decision 2026-11-24") ----

    /// <summary>
    /// The editable rows: the project-level list under <c>All configurations</c>, else the own entries of the selected
    /// configuration (without the reset marker).
    /// </summary>
    public ObservableCollection<SearchPathRow> SearchPaths { get; }

    /// <summary>
    /// <c>All configurations</c> is chosen on the Search Paths page (the state when the dialog opens): the project-level
    /// list is edited. False: the list of <see cref="SelectedConfiguration"/>.
    /// </summary>
    public bool SearchPathsAllConfigurations
    {
        get => _searchPathsAllConfigurations;
        set
        {
            if (!SetProperty(ref _searchPathsAllConfigurations, value))
                return;
            LoadSearchPaths();
            foreach (var name in (string[])[nameof(InheritedSearchPaths), nameof(SearchPathsDoNotInherit), nameof(CanSearchPathsDoNotInherit),
                         nameof(ConfigurationSelected), nameof(CanDeleteConfiguration)])
                OnPropertyChanged(name);
            OnEventsChanged();
            Validate();
        }
    }

    /// <summary>
    /// The read-only rows shown above the own ones, in resolution order (CMP-1160): the project-level entries, then the
    /// entries of each ancestor from the root on. An ancestor that does not inherit discards the rows before it. Empty
    /// under <c>All configurations</c> and while <see cref="SearchPathsDoNotInherit"/> is checked.
    /// </summary>
    public IReadOnlyList<SearchPathRow> InheritedSearchPaths
    {
        get
        {
            if (SearchPathsAllConfigurations || Selected.ResetSearchPaths)
                return [];
            var chain = new List<ConfigState>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { SelectedConfiguration };
            for (var ancestor = _configs.FirstOrDefault(c => c.Name == Selected.Inherits);
                 ancestor is not null && seen.Add(ancestor.Name);
                 ancestor = _configs.FirstOrDefault(c => c.Name == ancestor.Inherits))
                chain.Insert(0, ancestor);
            var rows = _projectSearchPaths.Select(p => Row(p, Strings.ProjectOptions_SearchPathOriginProject)).ToList();
            foreach (var ancestor in chain)
            {
                if (ancestor.ResetSearchPaths)
                    rows.Clear();
                rows.AddRange(ancestor.SearchPaths.Select(p => Row(p, ancestor.Name)));
            }

            return rows;
        }
    }

    /// <summary>The check box <c>Do not inherit</c> applies: a configuration is chosen.</summary>
    public bool CanSearchPathsDoNotInherit => !SearchPathsAllConfigurations;

    /// <summary>
    /// <c>SearchPaths.DoNotInherit</c>: the configuration's list starts with the reset marker <c>"!"</c> (CMP-1153), which
    /// discards the project-level and ancestor entries; the inherited rows are hidden.
    /// </summary>
    public bool SearchPathsDoNotInherit
    {
        get => CanSearchPathsDoNotInherit && Selected.ResetSearchPaths;
        set
        {
            if (CanSearchPathsDoNotInherit && value != Selected.ResetSearchPaths)
                Replace(Selected with { ResetSearchPaths = value });
        }
    }

    /// <summary>An entry that is empty or has invalid syntax (<c>Msg.Project.LocationInvalid</c>); null when all are valid.</summary>
    public string? SearchPathsError { get; private set; }

    /// <summary><c>SearchPaths.Add</c>: the folder picker; inside the project directory the entry is relative with <c>/</c>.</summary>
    public async Task AddSearchPathAsync()
    {
        var picked = await _pickers.PickFolderAsync(new FilePickerRequest(DialogName + ".SearchPaths", Strings.ProjectOptions_SearchPathPicker, [],
            _baseline.Directory));
        if (picked is not null)
            AddSearchPath(RunFields.StoredFolder(_baseline.Directory, picked));
    }

    /// <summary>Appends an entry; a duplicate (case-insensitive after normalization) is silently dropped.</summary>
    public void AddSearchPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (SearchPaths.Any(r => SamePath(r.Path, path)))
            return;
        SearchPaths.Add(Row(path));
        SearchPathsEdited();
    }

    /// <summary>In-place edit of a row (F2, double-click, <c>SearchPaths.Edit</c>); a duplicate of another row drops the row.</summary>
    public void EditSearchPath(int index, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (index < 0 || index >= SearchPaths.Count || SearchPaths[index].Path == path)
            return;
        if (SearchPaths.Where((_, i) => i != index).Any(r => SamePath(r.Path, path)))
            SearchPaths.RemoveAt(index);
        else
            SearchPaths[index] = Row(path);
        SearchPathsEdited();
    }

    public void RemoveSearchPath(int index)
    {
        if (index < 0 || index >= SearchPaths.Count)
            return;
        SearchPaths.RemoveAt(index);
        SearchPathsEdited();
    }

    /// <summary>Move Up (-1) / Move Down (+1); no-op at the ends.</summary>
    public void MoveSearchPath(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= SearchPaths.Count || target < 0 || target >= SearchPaths.Count)
            return;
        SearchPaths.Move(index, target);
        SearchPathsEdited();
    }

    // ---- Conditional Defines (§8.4) ----

    public string ProjectDefines
    {
        get => _projectDefines;
        set => Edit(ref _projectDefines, value ?? "", nameof(EffectiveDefines));
    }

    public string? ProjectDefinesError { get; private set; }

    public string ConfigDefines
    {
        get => Selected.DefinesText;
        set
        {
            if ((value ?? "") != Selected.DefinesText)
                Replace(Selected with { DefinesText = value ?? "" });
        }
    }

    public string? ConfigDefinesError { get; private set; }

    /// <summary>IDE-CFG-024: a derived configuration has own entries (bold label and <c>Revert</c>).</summary>
    public bool ConfigDefinesIsOwn => IsDerived && ParseDefines(ConfigDefines).Count > 0;

    /// <summary>Automation help text of <c>ConfigDefines</c>; inherited defines come from the nearest ancestor that has some, else from the project.</summary>
    public string? ConfigDefinesHelpText => StateText(ParseDefines(ConfigDefines).Count > 0, c => ParseDefines(c.DefinesText).Count > 0, baseLevel: true);

    /// <summary>IDE-CFG-024 <c>ConfigDefines.Revert</c>: empties the own list.</summary>
    public void RevertConfigDefines()
    {
        if (ConfigDefinesIsOwn)
            Replace(Selected with { DefinesText = "" });
    }

    /// <summary>The hint under <c>ConfigDefines</c> for a derived configuration; null for a root configuration.</summary>
    public string? DefinesHint => IsDerived ? Strings.ProjectOptions_DefinesInheritHint : null;

    /// <summary>
    /// <c>InheritedDefines</c> (derived configurations only): the project-level and ancestor defines in effect before this
    /// configuration, joined with <c>; </c>; <c>—</c> when there are none.
    /// </summary>
    public string InheritedDefines =>
        Selected.Inherits is { } parent && ConfigurationResolver.Resolve(Build(), parent).Defines is { IsEmpty: false } defines
            ? string.Join("; ", defines.Distinct(StringComparer.OrdinalIgnoreCase))
            : "—";

    /// <summary>The resolved list of the selected configuration (CMP-1160), shown without repeated symbols.</summary>
    public string EffectiveDefines => string.Join("; ", Effective.Defines.Distinct(StringComparer.OrdinalIgnoreCase));

    // ---- Output (§8.5, [cfg]) ----

    public string OutputDir
    {
        get => Selected.OutputDir;
        set
        {
            if ((value ?? "") != Selected.OutputDir)
                Replace(Selected with { OutputDir = value ?? "" });
        }
    }

    public string? OutputDirError { get; private set; }

    /// <summary>
    /// IDE-CFG-026: another configuration of the working copy has the same effective output folder (normalised full paths,
    /// ignoring case). A warning: it does not disable <c>OK</c>.
    /// </summary>
    public string? OutputDirWarning
    {
        get
        {
            if (OutputDirError is not null || RunFields.Resolve(_baseline.Directory, OutputDir) is not { } folder)
                return null;
            var other = _configs.FirstOrDefault(c => c.Name != SelectedConfiguration
                && RunFields.Resolve(_baseline.Directory, c.OutputDir) is { } theirs && SameFolder(theirs, folder));
            return other is null ? null : string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_OutputDirShared, other.Name);
        }
    }

    /// <summary>IDE-TGT-011: <c>&lt;name&gt;.dll</c> for a library, else <c>&lt;name&gt;.exe</c>.</summary>
    public string OutputFileName => ProjectName.Trim() + (AppType == "library" ? ".dll" : ".exe");

    /// <summary>The hint under <c>Name</c>: "output file name (&lt;name&gt;.dll)".</summary>
    public string NameHint => string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.ProjectOptions_NameHint, OutputFileName);

    public string OutputFile => Path.Combine(RunFields.Resolve(_baseline.Directory, OutputDir) ?? _baseline.Directory,
        ProjectName + (AppType == "library" ? ".dll" : ".exe"));

    public string ObjDir => Path.Combine(_baseline.Directory, "obj", SelectedConfiguration) + "\\";

    /// <summary><c>OutputDir.Browse</c>.</summary>
    public async Task BrowseOutputDirAsync()
    {
        var picked = await _pickers.PickFolderAsync(new FilePickerRequest(DialogName + ".OutputDir", Strings.ProjectOptions_OutputDirPicker, [],
            RunFields.Resolve(_baseline.Directory, OutputDir) ?? _baseline.Directory));
        if (picked is not null)
            OutputDir = RunFields.StoredFolder(_baseline.Directory, picked);
    }

    // ---- Run (§8.6) and Build Events (§8.7) ----

    public RunFields Run { get; }

    /// <summary><c>HostApplication.Browse</c> of the Run page (IDE-TGT-012).</summary>
    public Task BrowseHostAsync() => RunParametersViewModel.BrowseHostAsync(_pickers, DialogName, _baseline.Directory, Run);

    /// <summary><c>WorkingDir.Browse</c> of the Run page.</summary>
    public async Task BrowseWorkingDirAsync()
    {
        var picked = await _pickers.PickFolderAsync(new FilePickerRequest(DialogName + ".WorkingDir", Strings.RunParameters_WorkingDirPicker, [],
            RunFields.Resolve(_baseline.Directory, Run.WorkingDir) ?? _baseline.Directory));
        if (picked is not null)
            Run.WorkingDir = RunFields.StoredFolder(_baseline.Directory, picked);
    }

    /// <summary>UI-DLG-086: no page holds an invalid field.</summary>
    public bool IsValid => Pages.All(p => !p.HasError);

    /// <summary>The working copy as it would be applied (valid fields only are meaningful).</summary>
    public Project Build()
    {
        var configurations = ImmutableDictionary.CreateBuilder<string, ProjectConfiguration>(StringComparer.Ordinal);
        foreach (var c in _configs)
        {
            var defines = ImmutableArray.CreateRange(ParseDefines(c.DefinesText));
            var outputDir = c.OutputDir.Trim().Replace('\\', '/');
            var template = c.Original is { } loaded ? _baseline.Configurations[loaded] : new ProjectConfiguration(0, false, [], outputDir);
            // CMP-1156: a derived configuration holds 0 / false for a value it inherits.
            configurations[c.Name] = template with
            {
                Inherits = c.Inherits,
                InheritsOptimization = !c.OwnOptimization,
                InheritsDebugInfo = !c.OwnDebugInfo,
                Optimization = c.OwnOptimization ? c.Optimization : 0,
                DebugInfo = c.OwnDebugInfo && c.DebugInfo,
                Defines = SameOr(template.Defines, defines),
                SearchPaths = SameOr(template.SearchPaths, c.ResetSearchPaths ? c.SearchPaths.Insert(0, ResetMarker) : c.SearchPaths),
                OutputDir = outputDir,
                Events = c.Events?.Value,
            };
        }

        return _baseline with
        {
            Name = ProjectName.Trim(),
            AppType = AppType,
            MainSource = MainSource,
            SearchPaths = SameOr(_baseline.SearchPaths, _projectSearchPaths),
            Defines = SameOr(_baseline.Defines, ImmutableArray.CreateRange(ParseDefines(ProjectDefines))),
            Configurations = configurations.Count == _baseline.Configurations.Count
                && configurations.All(kv => _baseline.Configurations.TryGetValue(kv.Key, out var b) && b == kv.Value)
                    ? _baseline.Configurations
                    : configurations.ToImmutable(),
            ActiveConfiguration = ActiveName,
            Run = Run.ToSettings() == _baseline.Run ? _baseline.Run : Run.ToSettings(),
            Events = _projectEvents.Value,
        };
    }

    private ConfigState Selected => _configs.First(c => c.Name == SelectedConfiguration);

    /// <summary>The effective values of the selected configuration in the working copy (IDE-CFG-001).</summary>
    private EffectiveConfiguration Effective => ConfigurationResolver.Resolve(Build(), SelectedConfiguration);

    private async Task CommitAsync(bool close)
    {
        if (!IsValid)
            return;
        if (_changed)
        {
            if (!await _projects.ApplyOptionsAsync(Build()))
                return; // reported by the save
            _baseline = _projects.Definition ?? _baseline;
            // The lists are the stored ones again: what was typed is now what the file holds (IDE-EVT-012).
            for (var i = 0; i < _configs.Count; i++)
            {
                var stored = _baseline.Configurations.TryGetValue(_configs[i].Name, out var saved) ? saved.Events : null;
                _configs[i] = _configs[i] with { Original = _configs[i].Name, Events = stored is null ? null : new EventsState(stored) };
            }

            _projectEvents = new EventsState(_baseline.Events);
            OnEventsChanged();
            _changed = false;
            Apply.IsEnabled = false;
        }

        if (close)
            Close(true);
    }

    private void Replace(ConfigState state)
    {
        var index = _configs.FindIndex(c => c.Name == state.Name);
        _configs[index] = state;
        OnConfigurationFieldsChanged();
        MarkChanged();
    }

    private void OnConfigurationFieldsChanged()
    {
        foreach (var name in (string[])[nameof(Optimization), nameof(DebugInfo), nameof(ConfigDefines), nameof(EffectiveDefines), nameof(OutputDir),
                     nameof(OutputFile), nameof(ObjDir), nameof(ConfigurationNote), nameof(IsDerived), nameof(OptimizationIsOwn), nameof(DebugInfoIsOwn),
                     nameof(ConfigDefinesIsOwn), nameof(OptimizationHelpText), nameof(DebugInfoHelpText), nameof(ConfigDefinesHelpText), nameof(DefinesHint),
                     nameof(InheritedDefines), nameof(OutputDirWarning), nameof(CanDeleteConfiguration), nameof(DeleteConfigurationToolTip),
                     nameof(InheritedSearchPaths), nameof(SearchPathsDoNotInherit)])
            OnPropertyChanged(name);
        OnEventsChanged();
        Validate();
        Run.Refresh();
    }

    private void Edit(ref string field, string value, string? dependent = null)
    {
        if (field == value)
            return;
        field = value;
        OnPropertyChanged(string.Empty);
        if (dependent is not null)
            OnPropertyChanged(dependent);
        MarkChanged();
    }

    /// <summary>Stores the edited rows in the list they belong to: the project level or the selected configuration.</summary>
    private void SearchPathsEdited()
    {
        var paths = ImmutableArray.CreateRange(SearchPaths.Select(r => r.Path));
        if (SearchPathsAllConfigurations)
        {
            _projectSearchPaths = paths;
            OnPropertyChanged(nameof(InheritedSearchPaths));
            MarkChanged();
        }
        else
        {
            Replace(Selected with { SearchPaths = paths });
        }
    }

    /// <summary>Shows the rows of the list now edited (the project level or the selected configuration).</summary>
    private void LoadSearchPaths()
    {
        var paths = SearchPathsAllConfigurations ? _projectSearchPaths : Selected.SearchPaths;
        if (SearchPaths.Select(r => r.Path).SequenceEqual(paths, StringComparer.Ordinal))
            return;
        SearchPaths.Clear();
        foreach (var path in paths)
            SearchPaths.Add(Row(path));
    }

    private void MarkChanged()
    {
        _changed = true;
        Validate();
        Apply.IsEnabled = IsValid;
        Run.Refresh();
    }

    private void Validate()
    {
        var name = NewProjectValidation.ValidateName(ProjectName.Trim());
        ProjectNameError = name.Success ? null : NewProjectValidation.MessageText(name);
        SearchPathsError = SearchPaths.Any(r => !ValidSearchPath(r.Path)) ? Strings.Msg_Project_LocationInvalid : null;
        ProjectDefinesError = DefineError(ProjectDefines);
        ConfigDefinesError = DefineError(ConfigDefines, allowReset: true);
        OutputDirError = OutputDirMessage(OutputDir);
        Pages[0].HasError = ProjectNameError is not null;
        Pages[1].HasError = false;
        Pages[2].HasError = !_projectSearchPaths.All(ValidSearchPath) || _configs.Any(c => !c.SearchPaths.All(ValidSearchPath));
        Pages[3].HasError = ProjectDefinesError is not null || _configs.Any(c => DefineError(c.DefinesText, allowReset: true) is not null);
        Pages[4].HasError = _configs.Any(c => OutputDirMessage(c.OutputDir) is not null);
        Pages[5].HasError = !Run.IsValid;
        Pages[6].HasError = EventsHaveError();
        foreach (var property in (string[])[nameof(ProjectNameError), nameof(SearchPathsError), nameof(ProjectDefinesError), nameof(ConfigDefinesError),
                     nameof(OutputDirError), nameof(OutputDirWarning), nameof(PreBuildError), nameof(PostBuildError), nameof(IsValid)])
            OnPropertyChanged(property);
        Ok.IsEnabled = IsValid;
        Apply.IsEnabled = _changed && IsValid;
    }

    /// <summary>Non-empty with valid path syntax; the reset marker is written by <c>Do not inherit</c> only, never typed.</summary>
    private bool ValidSearchPath(string path) =>
        path.Trim().Length > 0 && path.Trim() != ResetMarker && RunFields.Resolve(_baseline.Directory, path) is not null;

    private string? OutputDirMessage(string text) =>
        text.Trim().Length == 0 || RunFields.Resolve(_baseline.Directory, text) is null ? Strings.Msg_ProjectOptions_OutputDirInvalid : null;

    /// <summary><c>DefineInvalid</c> for the first entry that is no symbol; a configuration's list may start with the reset marker (IDE-CFG-024).</summary>
    private static string? DefineError(string text, bool allowReset = false) =>
        ParseDefines(text).Where((s, i) => !(allowReset && i == 0 && s == ResetMarker)).FirstOrDefault(s => !SymbolPattern.IsMatch(s)) is { } bad
            ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_DefineInvalid, bad)
            : null;

    /// <summary>
    /// IDE-CFG-021: the automation help text of a field of a derived configuration — <c>ValueOwn</c>, or <c>ValueInherited</c>
    /// with the nearest ancestor for which <paramref name="supplies"/> holds (a root configuration supplies every scalar),
    /// else the project (<paramref name="baseLevel"/>). Null for a root configuration.
    /// </summary>
    private string? StateText(bool own, Func<ConfigState, bool> supplies, bool baseLevel = false)
    {
        if (!IsDerived)
            return null;
        if (own)
            return Strings.Msg_ProjectOptions_ValueOwn;
        var seen = new HashSet<string>(StringComparer.Ordinal) { SelectedConfiguration };
        for (var ancestor = _configs.FirstOrDefault(c => c.Name == Selected.Inherits);
             ancestor is not null && seen.Add(ancestor.Name);
             ancestor = _configs.FirstOrDefault(c => c.Name == ancestor.Inherits))
        {
            if (supplies(ancestor) || (!baseLevel && ancestor.Inherits is null))
                return string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ValueInherited, ancestor.Name);
        }

        return string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ValueInherited, Strings.ProjectOptions_BaseLevel);
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>§8.4: split on <c>;</c>, trim, drop empties and case-insensitive duplicates, typed order.</summary>
    public static IReadOnlyList<string> ParseDefines(string text) =>
        [.. (text ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static ImmutableArray<string> SameOr(ImmutableArray<string> original, ImmutableArray<string> value) =>
        original.SequenceEqual(value, StringComparer.Ordinal) ? original : value;

    private SearchPathRow Row(string path, string? origin = null) =>
        new(path, RunFields.Resolve(_baseline.Directory, path) is { } full && !_fileSystem.DirectoryExists(full), origin);

    private bool SamePath(string a, string b) =>
        string.Equals(RunFields.Resolve(_baseline.Directory, a) ?? a, RunFields.Resolve(_baseline.Directory, b) ?? b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The validated name of the NewConfiguration sub-dialog (UI-DLG-083).</summary>
    public static string? ConfigNameError(string name, IEnumerable<string> existing)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!ConfigNamePattern.IsMatch(name))
            return Strings.Msg_ProjectOptions_ConfigNameInvalid;
        return existing.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ConfigNameExists, name)
            : null;
    }

    /// <summary>
    /// Edited state of one configuration; <see cref="Original"/> = the entry of the baseline it started from (null for a new
    /// derived one). <see cref="Inherits"/> null = root configuration, whose values are all its own; for a derived one
    /// <see cref="OwnOptimization"/> / <see cref="OwnDebugInfo"/> tell whether the value is set here (IDE-CFG-021).
    /// </summary>
    private sealed record ConfigState(string Name, string? Original, int Optimization, bool DebugInfo, string DefinesText, string OutputDir,
        string? Inherits, bool OwnOptimization, bool OwnDebugInfo)
    {
        /// <summary>The own search paths without the reset marker (CMP-1152).</summary>
        public ImmutableArray<string> SearchPaths { get; init; } = [];

        /// <summary>The own list starts with the reset marker: nothing is inherited (CMP-1153).</summary>
        public bool ResetSearchPaths { get; init; }

        /// <summary>The configuration's own <c>events</c>; null = it has no such key and is built with inherited commands.</summary>
        public EventsState? Events { get; init; }

        public static ConfigState From(string name, ProjectConfiguration c)
        {
            var reset = c.SearchPaths is [ResetMarker, ..];
            return new(name, name, c.Optimization, c.DebugInfo, string.Join(";", c.Defines), c.OutputDir, c.Inherits,
                c.Inherits is null || !c.InheritsOptimization, c.Inherits is null || !c.InheritsDebugInfo)
            {
                SearchPaths = reset ? c.SearchPaths.RemoveAt(0) : c.SearchPaths,
                ResetSearchPaths = reset,
                Events = c.Events is { } events ? new EventsState(events) : null,
            };
        }
    }
}

/// <summary>The result of the NewConfiguration sub-dialog: <see cref="Inherit"/> = derived from <see cref="CopyFrom"/>, else a copy of it.</summary>
public sealed record NewConfigurationResult(string Name, string CopyFrom, bool Inherit);

/// <summary>
/// The <c>NewConfiguration</c> sub-dialog (UI-DLG-083, UI-DLG-270), 360 × auto: <c>Name</c>, <c>CopyFrom</c> ("Based on") and the
/// check box <c>Inherit</c>, checked by default (IDE-CFG-005).
/// </summary>
public sealed class NewConfigurationViewModel : DialogViewModel<NewConfigurationResult>
{
    public const string DialogName = "NewConfiguration";

    private readonly IReadOnlyList<string> _existing;
    private string _configName = "";
    private string _copyFrom;
    private bool _inherit = true;
    private string? _error;

    public NewConfigurationViewModel(IReadOnlyList<string> existing, string copyFrom)
    {
        _existing = existing;
        _copyFrom = copyFrom;
        Create = new DialogButtonViewModel("Ok", Strings.NewConfiguration_Create, () =>
        {
            if (Error is null)
                Close(new NewConfigurationResult(ConfigName, CopyFrom, Inherit));
        })
        { IsDefault = true, IsAccent = true, IsEnabled = false };
        Buttons = [Create, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true }];
        _error = ProjectOptionsViewModel.ConfigNameError("", existing);
    }

    public override string Name => DialogName;

    public override string Title => Strings.NewConfiguration_Title;

    public override double Width => 360;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Create { get; }

    public IReadOnlyList<string> Existing => _existing;

    public string ConfigName
    {
        get => _configName;
        set
        {
            if (!SetProperty(ref _configName, (value ?? "").Trim()))
                return;
            Error = ProjectOptionsViewModel.ConfigNameError(_configName, _existing);
            Create.IsEnabled = Error is null;
        }
    }

    public string CopyFrom
    {
        get => _copyFrom;
        set
        {
            if (value is not null && _existing.Contains(value))
                SetProperty(ref _copyFrom, value);
        }
    }

    /// <summary>Checked: the new configuration inherits from <see cref="CopyFrom"/> and has no own value; unchecked: a copy.</summary>
    public bool Inherit
    {
        get => _inherit;
        set => SetProperty(ref _inherit, value);
    }

    /// <summary>Shown under <c>Name</c> once something was typed (<c>Dialog.NewConfiguration.Error.Name</c>).</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }
}
