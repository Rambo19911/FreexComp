using System.Globalization;
using FreexComp.Build;
using FreexComp.Ide.Core.Layout;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// A row of the <c>Configurations</c> list (IDE-CFG-011): <see cref="Level"/> = number of ancestors, shown as
/// <see cref="Indent"/> (16 px per level, at most six levels, UI-DLG-272); <see cref="ParentText"/> = the parent's name
/// or <c>—</c> for a root configuration.
/// </summary>
public sealed record ConfigurationRow(string Name, int Level, bool IsActive, string ParentText)
{
    public const double IndentPerLevel = 16;

    /// <summary>The <c>Inherits from</c> cell of a root configuration.</summary>
    public const string NoParentCell = "—";

    public const int MaxIndentLevels = 6;

    public double Indent => IndentPerLevel * Math.Min(Level, MaxIndentLevels);

    /// <summary>The name, with the suffix " (active)" for the active configuration.</summary>
    public string AutomationName => IsActive ? string.Format(CultureInfo.CurrentCulture, Strings.ProjectOptions_ActiveSuffix, Name) : Name;
}

/// <summary>
/// The Configuration Manager (phase8-configurations.md §4, UI-DLG-271…276), 560 × 420, resizable: the configurations in
/// inheritance order with New, Rename, Delete, Set Active, the parent combo and the own-values line. Every operation
/// changes a working copy of the project (IDE-CFG-018); <c>OK</c> applies it with
/// <see cref="IProjectService.ApplyOptionsAsync"/> when something changed, <c>Cancel</c> discards it (IDE-CFG-010).
/// </summary>
public sealed class ConfigurationManagerViewModel : DialogViewModel<bool>
{
    public const string DialogName = "ConfigurationManager";

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly IProjectService _projects;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ILayoutService? _layout;
    private readonly Project _original;
    private Project _work;
    private string _selected;
    private (string From, string To)? _renamed;

    public ConfigurationManagerViewModel(IProjectService projects, IDialogService dialogs, IStatusService status, ILayoutService? layout)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(status);
        _projects = projects;
        _dialogs = dialogs;
        _status = status;
        _layout = layout;
        _original = _work = projects.Definition ?? throw new InvalidOperationException("No project is open.");
        _selected = _work.Configurations.ContainsKey(_work.ActiveConfiguration) ? _work.ActiveConfiguration : _work.ConfigurationNames[0];
        Rows = BuildRows();
        Ok = new DialogButtonViewModel("Ok", Strings.Button_OK, CommitAsync) { IsDefault = true, IsAccent = true };
        Buttons = [Ok, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(false)) { IsCancel = true }];
    }

    /// <summary>The first item of the parent combo: the configuration inherits from nothing.</summary>
    public static string NoParent => Strings.ConfigurationManager_ParentNone;

    public override string Name => DialogName;

    public override string Title => string.Format(CultureInfo.CurrentCulture, Strings.ConfigurationManager_Title, _original.Name);

    public override double Width => 560;

    public override double? Height => 420;

    public override bool CanResize => true;

    public override (double Width, double Height)? StoredSize
    {
        get => StoredDialogSize.Read(_layout, DialogName);
        set => StoredDialogSize.Write(_layout, DialogName, value);
    }

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Ok { get; }

    /// <summary>The project as <c>OK</c> would apply it.</summary>
    public Project WorkingCopy => _work;

    /// <summary>IDE-CFG-011: root configurations in file order, each followed by its descendants depth-first, children in file order.</summary>
    public IReadOnlyList<ConfigurationRow> Rows { get; private set; }

    /// <summary>The selected configuration (single selection; initially the active one).</summary>
    public string SelectedName
    {
        get => _selected;
        set
        {
            if (value is null || !_work.Configurations.ContainsKey(value) || !SetProperty(ref _selected, value))
                return;
            OnSelectionChanged();
        }
    }

    // ---- Buttons of the list (IDE-CFG-012…015) ----

    /// <summary><c>New</c>: the NewConfiguration sub-dialog with the selected row as the source; the new row is selected.</summary>
    public async Task NewAsync()
    {
        var sub = new NewConfigurationViewModel(_work.ConfigurationNames, SelectedName);
        if (await _dialogs.ShowAsync(sub) is not { } created)
            return;
        Apply(created.Inherit
            ? ConfigurationEdits.AddDerived(_work, created.Name, created.CopyFrom)
            : ConfigurationEdits.AddCopy(_work, created.Name, created.CopyFrom), created.Name);
    }

    /// <summary><c>Rename</c>: the RenameConfiguration sub-dialog; children, the active marker and the position follow (CMP-1159).</summary>
    public async Task RenameAsync()
    {
        var old = SelectedName;
        var sub = new RenameConfigurationViewModel(_work.ConfigurationNames, old);
        if (await _dialogs.ShowAsync(sub) is not { } name || name == old)
            return;
        _renamed = (_renamed is { } earlier && earlier.To == old ? earlier.From : old, name);
        Apply(ProjectConfigurations.Rename(_work, old, name), name);
    }

    /// <summary>IDE-CFG-014: not the last configuration and not one another configuration inherits from.</summary>
    public bool CanDelete => DeleteToolTip is null;

    /// <summary>Why <c>Delete</c> is disabled; null when the selected configuration can be deleted.</summary>
    public string? DeleteToolTip =>
        _work.Configurations.Count <= 1 ? Strings.Msg_ProjectOptions_ConfigDeleteLast
        : ProjectConfigurations.ChildrenOf(_work, SelectedName) is [var child, ..]
            ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ConfigDeleteInherited, SelectedName, child)
            : null;

    /// <summary><c>Delete</c>: asks <c>Msg.ProjectOptions.ConfigDeleteConfirm</c>; the neighbouring row is selected.</summary>
    public async Task DeleteAsync()
    {
        if (!CanDelete)
            return;
        var name = SelectedName;
        if (await _dialogs.ShowMessageAsync(new MessageRequest("Msg.ProjectOptions.ConfigDeleteConfirm", [name])) != "Delete")
            return;
        var index = Rows.ToList().FindIndex(r => r.Name == name);
        var work = ConfigurationEdits.Remove(_work, name);
        var remaining = Rows.Where(r => r.Name != name).ToList();
        Apply(work, remaining[Math.Min(index, remaining.Count - 1)].Name);
    }

    /// <summary>IDE-CFG-015: disabled for the active row.</summary>
    public bool CanSetActive => SelectedName != _work.ActiveConfiguration;

    /// <summary><c>SetActive</c>: marks the selected configuration active in the working copy.</summary>
    public void SetActive()
    {
        if (CanSetActive)
            Apply(_work with { ActiveConfiguration = SelectedName }, SelectedName);
    }

    // ---- Parent combo and own values (IDE-CFG-016, 017) ----

    /// <summary><c>(none)</c>, then every configuration except the selected one and its descendants, in file order.</summary>
    public IReadOnlyList<string> ParentItems
    {
        get
        {
            var excluded = ProjectConfigurations.DescendantsOf(_work, SelectedName);
            return [NoParent, .. _work.ConfigurationNames.Where(n => n != SelectedName && !excluded.Contains(n))];
        }
    }

    /// <summary>The parent of the selected configuration, or <see cref="NoParent"/>. Setting it re-parents or detaches.</summary>
    public string SelectedParent
    {
        get => _work.Configurations[SelectedName].Inherits ?? NoParent;
        set
        {
            if (value is null || value == SelectedParent || !ParentItems.Contains(value))
                return;
            Apply(value == NoParent ? ProjectConfigurations.Detach(_work, SelectedName) : ConfigurationEdits.SetParent(_work, SelectedName, value), SelectedName);
        }
    }

    /// <summary>The hint under the parent combo.</summary>
    public string ParentHint => Strings.ConfigurationManager_ParentHint;

    /// <summary>
    /// IDE-CFG-017: for a derived configuration the keys it sets itself, in the fixed order optimization, debug information,
    /// defines, search paths, output folder, RTTI, or <c>none</c>; for a root configuration <c>all (no parent)</c>.
    /// </summary>
    public string OwnValues
    {
        get
        {
            var c = _work.Configurations[SelectedName];
            if (c.Inherits is null)
                return Strings.ConfigurationManager_OwnAll;
            var keys = new List<string>();
            if (!c.InheritsOptimization)
                keys.Add(Strings.ConfigKey_Optimization);
            if (!c.InheritsDebugInfo)
                keys.Add(Strings.ConfigKey_DebugInfo);
            if (!c.Defines.IsDefaultOrEmpty)
                keys.Add(Strings.ConfigKey_Defines);
            if (!c.SearchPaths.IsDefaultOrEmpty)
                keys.Add(Strings.ConfigKey_SearchPaths);
            if (c.OutputDir.Replace('\\', '/') != ConfigurationEdits.DefaultOutputDir(SelectedName))
                keys.Add(Strings.ConfigKey_OutputDir);
            if (c.Rtti is not null)
                keys.Add(Strings.ConfigKey_Rtti);
            return keys.Count == 0 ? Strings.ConfigurationManager_OwnNone : string.Join(", ", keys);
        }
    }

    private void Apply(Project work, string selected)
    {
        _work = work;
        _selected = selected;
        Rows = BuildRows();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(SelectedName));
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        foreach (var name in (string[])[nameof(CanDelete), nameof(DeleteToolTip), nameof(CanSetActive), nameof(ParentItems), nameof(SelectedParent), nameof(OwnValues)])
            OnPropertyChanged(name);
    }

    private List<ConfigurationRow> BuildRows()
    {
        var rows = new List<ConfigurationRow>();
        void Add(string name, int level)
        {
            var parent = _work.Configurations[name].Inherits;
            rows.Add(new ConfigurationRow(name, level, name == _work.ActiveConfiguration, parent ?? ConfigurationRow.NoParentCell));
            foreach (var child in ProjectConfigurations.ChildrenOf(_work, name))
                Add(child, level + 1);
        }

        foreach (var name in _work.ConfigurationNames.Where(n => _work.Configurations[n].Inherits is null))
            Add(name, 0);
        return rows;
    }

    private async Task CommitAsync()
    {
        // IDE-CFG-010: OK with no change applies nothing and does not mark the project modified.
        if (_work != _original)
        {
            if (!await _projects.ApplyOptionsAsync(_work))
                return; // reported by the save
            if (_renamed is { } renamed && renamed.From != renamed.To)
                _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Project_ConfigRenamed, renamed.From, renamed.To), StatusDuration);
        }

        Close(true);
    }
}

/// <summary>
/// The <c>RenameConfiguration</c> sub-dialog (UI-DLG-275), 360 × auto: <c>Name</c>, pre-filled with the current name and
/// validated as in the NewConfiguration sub-dialog. The unchanged name is valid but disables <c>Rename</c>; a change of
/// letter case only is a rename (IDE-CFG-013). The result is the new name.
/// </summary>
public sealed class RenameConfigurationViewModel : DialogViewModel<string>
{
    public const string DialogName = "RenameConfiguration";

    private readonly List<string> _others;
    private string _configName;
    private string? _error;

    public RenameConfigurationViewModel(IReadOnlyList<string> existing, string current)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(current);
        CurrentName = current;
        _configName = current;
        _others = [.. existing.Where(n => n != current)];
        Rename = new DialogButtonViewModel("Ok", Strings.RenameConfiguration_Rename, () =>
        {
            if (Error is null && ConfigName != CurrentName)
                Close(ConfigName);
        })
        { IsDefault = true, IsAccent = true, IsEnabled = false };
        Buttons = [Rename, new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true }];
    }

    public override string Name => DialogName;

    public override string Title => Strings.RenameConfiguration_Title;

    public override double Width => 360;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Rename { get; }

    /// <summary>The name before the rename.</summary>
    public string CurrentName { get; }

    public string ConfigName
    {
        get => _configName;
        set
        {
            if (!SetProperty(ref _configName, (value ?? "").Trim()))
                return;
            Error = ProjectOptionsViewModel.ConfigNameError(_configName, _others);
            Rename.IsEnabled = Error is null && _configName != CurrentName;
        }
    }

    /// <summary>Shown under <c>Name</c> (<c>Dialog.RenameConfiguration.Error.Name</c>).</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }
}
