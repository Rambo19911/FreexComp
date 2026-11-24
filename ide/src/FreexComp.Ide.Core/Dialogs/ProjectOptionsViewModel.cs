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

/// <summary>A row of the <c>SearchPaths</c> list; <see cref="IsMissing"/> shows the warning glyph (§8.3).</summary>
public sealed record SearchPathRow(string Path, bool IsMissing)
{
    public string? ToolTip => IsMissing ? Strings.Msg_ProjectOptions_SearchPathMissing : null;
}

/// <summary>
/// The Project Options dialog (dialogs.md §8, UI-DLG-080…086), 760 × 520, resizable. It edits a working copy of the
/// open project's definition; <c>Apply</c>/<c>OK</c> call <see cref="IProjectService.ApplyOptionsAsync"/> (keys not shown
/// are never touched: the copy is made with <c>with</c> from the definition). Pages: Application, Compiler, Search Paths,
/// Conditional Defines, Output, Run, Build Events (read-only until P8+).
/// </summary>
public sealed class ProjectOptionsViewModel : DialogViewModel<bool>
{
    public const string DialogName = "ProjectOptions";

    private static readonly Regex ConfigNamePattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly Regex SymbolPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    private readonly IProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _pickers;
    private readonly IdeFileSystem _fileSystem;
    private readonly ILayoutService _layout;
    private readonly List<ConfigState> _configs = [];
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
        SearchPaths = new ObservableCollection<SearchPathRow>(_baseline.SearchPaths.Select(Row));
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
            new("SearchPaths", Strings.ProjectOptions_Page_SearchPaths, false),
            new("ConditionalDefines", Strings.ProjectOptions_Page_ConditionalDefines, true),
            new("Output", Strings.ProjectOptions_Page_Output, true),
            new("Run", Strings.ProjectOptions_Page_Run, false),
            new("BuildEvents", Strings.ProjectOptions_Page_BuildEvents, false),
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

    public bool CanDeleteConfiguration => ConfigurationBarEnabled && _configs.Count > 1;

    /// <summary>UI-DLG-085: "(Debug)" after the label of a [cfg] field.</summary>
    public string ConfigurationNote => "(" + SelectedConfiguration + ")";

    /// <summary>UI-DLG-083: the <c>NewConfiguration</c> sub-dialog; the new configuration is appended and selected.</summary>
    public async Task NewConfigurationAsync()
    {
        var sub = new NewConfigurationViewModel(ConfigurationNames, SelectedConfiguration);
        if (await _dialogs.ShowAsync(sub) is not { } created)
            return;
        var source = _configs.First(c => c.Name == created.CopyFrom);
        _configs.Add(source with { Name = created.Name });
        OnPropertyChanged(nameof(ConfigurationNames));
        OnPropertyChanged(nameof(CanDeleteConfiguration));
        SelectedConfiguration = created.Name;
        MarkChanged();
    }

    /// <summary>UI-DLG-084: asks <c>Msg.ProjectOptions.ConfigDeleteConfirm</c>; never deletes the last one.</summary>
    public async Task DeleteConfigurationAsync()
    {
        if (_configs.Count <= 1)
            return;
        var name = SelectedConfiguration;
        if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.ProjectOptions.ConfigDeleteConfirm", [name])) != "Delete")
            return;
        var index = _configs.FindIndex(c => c.Name == name);
        _configs.RemoveAt(index);
        _selectedConfiguration = _configs[Math.Min(index, _configs.Count - 1)].Name;
        OnPropertyChanged(nameof(ConfigurationNames));
        OnPropertyChanged(nameof(SelectedConfiguration));
        OnPropertyChanged(nameof(CanDeleteConfiguration));
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

    public string PlatformText => Strings.ProjectOptions_PlatformWin64;

    public string ProjectFile => _baseline.FilePath;

    // ---- Compiler (§8.2, [cfg]) ----

    public int Optimization
    {
        get => Selected.Optimization;
        set
        {
            if (value is >= 0 and <= 2 && value != Selected.Optimization)
                Replace(Selected with { Optimization = value });
        }
    }

    public bool DebugInfo
    {
        get => Selected.DebugInfo;
        set
        {
            if (value != Selected.DebugInfo)
                Replace(Selected with { DebugInfo = value });
        }
    }

    // ---- Search Paths (§8.3) ----

    public ObservableCollection<SearchPathRow> SearchPaths { get; }

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

    public string EffectiveDefines =>
        string.Join("; ", ParseDefines(ProjectDefines).Concat(ParseDefines(ConfigDefines)).Distinct(StringComparer.OrdinalIgnoreCase));

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

    public string PreBuild => string.Join(Environment.NewLine, _baseline.Events.PreBuild);

    public string PostBuild => string.Join(Environment.NewLine, _baseline.Events.PostBuild);

    /// <summary>UI-DLG-086: no page holds an invalid field.</summary>
    public bool IsValid => Pages.All(p => !p.HasError);

    /// <summary>The working copy as it would be applied (valid fields only are meaningful).</summary>
    public Project Build()
    {
        var configurations = ImmutableDictionary.CreateBuilder<string, ProjectConfiguration>(StringComparer.Ordinal);
        foreach (var c in _configs)
        {
            var original = _baseline.Configurations.GetValueOrDefault(c.Original ?? c.Name);
            var defines = ImmutableArray.CreateRange(ParseDefines(c.DefinesText));
            var outputDir = c.OutputDir.Trim().Replace('\\', '/');
            configurations[c.Name] = original is not null
                ? original with { Optimization = c.Optimization, DebugInfo = c.DebugInfo, Defines = SameOr(original.Defines, defines), OutputDir = outputDir }
                : new ProjectConfiguration(c.Optimization, c.DebugInfo, defines, outputDir, c.Rtti);
        }

        var searchPaths = ImmutableArray.CreateRange(SearchPaths.Select(r => r.Path));
        return _baseline with
        {
            Name = ProjectName.Trim(),
            AppType = AppType,
            MainSource = MainSource,
            SearchPaths = SameOr(_baseline.SearchPaths, searchPaths),
            Defines = SameOr(_baseline.Defines, ImmutableArray.CreateRange(ParseDefines(ProjectDefines))),
            Configurations = configurations.Count == _baseline.Configurations.Count
                && configurations.All(kv => _baseline.Configurations.TryGetValue(kv.Key, out var b) && b == kv.Value)
                    ? _baseline.Configurations
                    : configurations.ToImmutable(),
            ActiveConfiguration = ActiveName,
            Run = Run.ToSettings() == _baseline.Run ? _baseline.Run : Run.ToSettings(),
        };
    }

    private ConfigState Selected => _configs.First(c => c.Name == SelectedConfiguration);

    private async Task CommitAsync(bool close)
    {
        if (!IsValid)
            return;
        if (_changed)
        {
            if (!await _projects.ApplyOptionsAsync(Build()))
                return; // reported by the save
            _baseline = _projects.Definition ?? _baseline;
            for (var i = 0; i < _configs.Count; i++)
                _configs[i] = _configs[i] with { Original = _configs[i].Name };
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
                     nameof(OutputFile), nameof(ObjDir), nameof(ConfigurationNote)])
            OnPropertyChanged(name);
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

    private void SearchPathsEdited()
    {
        Validate();
        MarkChanged();
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
        SearchPathsError = SearchPaths.Any(r => RunFields.Resolve(_baseline.Directory, r.Path) is null || r.Path.Trim().Length == 0)
            ? Strings.Msg_Project_LocationInvalid
            : null;
        ProjectDefinesError = DefineError(ProjectDefines);
        ConfigDefinesError = DefineError(ConfigDefines);
        OutputDirError = OutputDirMessage(OutputDir);
        Pages[0].HasError = ProjectNameError is not null;
        Pages[1].HasError = false;
        Pages[2].HasError = SearchPathsError is not null;
        Pages[3].HasError = ProjectDefinesError is not null || _configs.Any(c => DefineError(c.DefinesText) is not null);
        Pages[4].HasError = _configs.Any(c => OutputDirMessage(c.OutputDir) is not null);
        Pages[5].HasError = !Run.IsValid;
        foreach (var property in (string[])[nameof(ProjectNameError), nameof(SearchPathsError), nameof(ProjectDefinesError), nameof(ConfigDefinesError),
                     nameof(OutputDirError), nameof(IsValid)])
            OnPropertyChanged(property);
        Ok.IsEnabled = IsValid;
        Apply.IsEnabled = _changed && IsValid;
    }

    private string? OutputDirMessage(string text) =>
        text.Trim().Length == 0 || RunFields.Resolve(_baseline.Directory, text) is null ? Strings.Msg_ProjectOptions_OutputDirInvalid : null;

    private static string? DefineError(string text) =>
        ParseDefines(text).FirstOrDefault(s => !SymbolPattern.IsMatch(s)) is { } bad
            ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_DefineInvalid, bad)
            : null;

    /// <summary>§8.4: split on <c>;</c>, trim, drop empties and case-insensitive duplicates, typed order.</summary>
    public static IReadOnlyList<string> ParseDefines(string text) =>
        [.. (text ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static ImmutableArray<string> SameOr(ImmutableArray<string> original, ImmutableArray<string> value) =>
        original.SequenceEqual(value, StringComparer.Ordinal) ? original : value;

    private SearchPathRow Row(string path) =>
        new(path, RunFields.Resolve(_baseline.Directory, path) is { } full && !_fileSystem.DirectoryExists(full));

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

    /// <summary>Edited state of one configuration; <see cref="Original"/> = the name it was loaded under (null for a new one).</summary>
    private sealed record ConfigState(string Name, string? Original, int Optimization, bool DebugInfo, string DefinesText, string OutputDir, string? Rtti)
    {
        public static ConfigState From(string name, ProjectConfiguration c) =>
            new(name, name, c.Optimization, c.DebugInfo, string.Join(";", c.Defines), c.OutputDir, c.Rtti);
    }
}

/// <summary>The result of the NewConfiguration sub-dialog.</summary>
public sealed record NewConfigurationResult(string Name, string CopyFrom);

/// <summary>The <c>NewConfiguration</c> sub-dialog (UI-DLG-083), 360 × auto: <c>Name</c> and <c>CopyFrom</c>.</summary>
public sealed class NewConfigurationViewModel : DialogViewModel<NewConfigurationResult>
{
    public const string DialogName = "NewConfiguration";

    private readonly IReadOnlyList<string> _existing;
    private string _configName = "";
    private string _copyFrom;
    private string? _error;

    public NewConfigurationViewModel(IReadOnlyList<string> existing, string copyFrom)
    {
        _existing = existing;
        _copyFrom = copyFrom;
        Create = new DialogButtonViewModel("Ok", Strings.NewConfiguration_Create, () =>
        {
            if (Error is null)
                Close(new NewConfigurationResult(ConfigName, CopyFrom));
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

    /// <summary>Shown under <c>Name</c> once something was typed (<c>Dialog.NewConfiguration.Error.Name</c>).</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }
}
