using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.ViewModels;

/// <summary>
/// The Welcome page (welcome-page.md): start actions, the recent projects and recent files of <c>recent.json</c>
/// (IDE-WEL-024) and the start-up check box. Local content only (IDE-WEL-021). UI thread only.
/// </summary>
public sealed class WelcomeDocumentViewModel : DocumentViewModel
{
    /// <summary>The filter box appears above this many entries of both lists together (welcome-page.md §1, IDE-WEL-024).</summary>
    public const int FilterThreshold = 5;

    private readonly IRecentItemsService _recent;
    private readonly ICommandService _commands;
    private readonly ISettingsService _settings;
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private string _filterText = "";
    private bool _isCheckingMissing;
    private int _missingCheck;

    private readonly Build.IIdeState? _state;

    public WelcomeDocumentViewModel(IRecentItemsService recent, ICommandService commands, ISettingsService settings, IFileSystem fileSystem, IClock clock,
        Build.IIdeState? state = null)
    {
        _state = state;
        state?.Changed += (_, _) => OnPropertyChanged(nameof(RowsEnabled));
        _recent = recent;
        _commands = commands;
        _settings = settings;
        _fileSystem = fileSystem;
        _clock = clock;
        var version = typeof(WelcomeDocumentViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        VersionText = string.Format(CultureInfo.CurrentCulture, Strings.WelcomePage_Version, version);
        _recent.Changed += OnRecentChanged;
        _settings.Changed += OnSettingChanged;
        Rebuild();
        _ = CheckMissingAsync();
    }

    public override string Title => Strings.WelcomePage_Title;

    public override string? FilePath => null;

    public override bool IsModified => false;

    public override bool IsReadOnly => false;

    /// <summary>No editing commands apply (IDE-WEL-022).</summary>
    public override ICommandTarget? CommandTarget => null;

    public string VersionText { get; }

    public IdeCommand NewProject => _commands.Get("file.newProject");

    public IdeCommand OpenProject => _commands.Get("file.openProject");

    public IdeCommand OpenFile => _commands.Get("file.open");

    /// <summary>The rows shown: filtered, pinned first, then newest first (IDE-WEL-001).</summary>
    public ObservableCollection<RecentProjectRow> Projects { get; } = [];

    /// <summary>IDE-WEL-024: the recent files, filtered by the same box, pinned first, then newest first.</summary>
    public ObservableCollection<RecentProjectRow> Files { get; } = [];

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? ""))
                Rebuild();
        }
    }

    /// <summary>More than <see cref="FilterThreshold"/> recent projects and recent files together (IDE-WEL-024).</summary>
    public bool ShowFilter => _recent.Projects.Count + _recent.Files.Count > FilterThreshold;

    public bool HasRows => Projects.Count > 0;

    /// <summary>IDE-WEL-024: the "Recent files" section shows while it has a row (none recorded or none matching: hidden).</summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>IDE-WEL-007: <c>Msg.Welcome.NoRecent</c> or <c>Msg.Welcome.FilterEmpty</c>; null while rows are shown.</summary>
    public string? EmptyText => _recent.Projects.Count == 0 ? Strings.Msg_Welcome_NoRecent
        : Projects.Count == 0 ? string.Format(CultureInfo.CurrentCulture, Strings.Msg_Welcome_FilterEmpty, FilterText)
        : null;

    public string? EmptySecondaryText => _recent.Projects.Count == 0 ? Strings.Msg_Welcome_NoRecent_Secondary : null;

    /// <summary><c>Welcome.ShowOnStartup</c> ↔ <c>environment.showWelcomeOnStartup</c>, applied at once.</summary>
    public bool ShowOnStartup
    {
        get => _settings.Get(SettingsCatalog.EnvironmentShowWelcomeOnStartup);
        set => _settings.Set(SettingsCatalog.EnvironmentShowWelcomeOnStartup, value);
    }

    /// <summary>The background existence check of IDE-WEL-004 is running.</summary>
    public bool IsCheckingMissing
    {
        get => _isCheckingMissing;
        private set => SetProperty(ref _isCheckingMissing, value);
    }

    /// <summary>
    /// IDE-WEL-005: <c>file.openRecentProject:&lt;path&gt;</c>; IDE-WEL-024: <c>file.openRecentFile:&lt;path&gt;</c> (a missing
    /// entry asks <c>Msg.File.RecentNotFound</c>).
    /// </summary>
    public Task OpenAsync(RecentProjectRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Kind == RecentKind.File)
            return _commands.ExecuteAsync("file.openRecentFile", row.Path);
        return RowsEnabled ? _commands.ExecuteAsync("file.openRecentProject", row.Path) : Task.CompletedTask;
    }

    /// <summary>IDE-WEL-005: the recent-project rows are disabled while the IDE is not Idle.</summary>
    public bool RowsEnabled => _state?.Current.State is null or Models.IdeState.Idle;

    /// <summary>IDE-WEL-006: toggles the pin; returns the row to focus (the same entry, re-sorted).</summary>
    public RecentProjectRow? TogglePin(RecentProjectRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _recent.SetPinned(row.Kind, row.Path, !row.IsPinned);
        return RowsOf(row.Kind).FirstOrDefault(p => SamePath(p.Path, row.Path));
    }

    /// <summary>IDE-WEL-006: removes the entry without confirmation; returns the nearest remaining row, or null.</summary>
    public RecentProjectRow? Remove(RecentProjectRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rows = RowsOf(row.Kind);
        var index = rows.IndexOf(row);
        _recent.Remove(row.Kind, row.Path);
        return rows.Count == 0 ? null : rows[Math.Clamp(index, 0, rows.Count - 1)];
    }

    /// <summary>
    /// IDE-WEL-008: the row's context menu — Open (<c>file.openRecentProject</c>), Open Containing Folder
    /// (<c>file.showInExplorer</c> with the path), Pin/Unpin and Remove from List. The view passes its own pin and
    /// remove so focus stays on the nearest row (IDE-WEL-006).
    /// </summary>
    public IReadOnlyList<MenuItemModel> RowContextMenu(RecentProjectRow row, Action<RecentProjectRow> togglePin, Action<RecentProjectRow> remove)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            MenuItemModel.ForCommand(Strings.WelcomePage_Menu_Open,
                _commands.Get(row.Kind == RecentKind.File ? "file.openRecentFile" : "file.openRecentProject"), row.Path, MenuToggleKind.None),
            MenuItemModel.ForCommand(Strings.WelcomePage_Menu_OpenFolder, _commands.Get("file.showInExplorer"), row.Path, MenuToggleKind.None),
            row.IsPinned
                ? MenuItemModel.ForAction("welcome.unpin", Strings.WelcomePage_Menu_Unpin, () => togglePin(row))
                : MenuItemModel.ForAction("welcome.pin", Strings.WelcomePage_Menu_Pin, () => togglePin(row)),
            MenuItemModel.ForAction("welcome.remove", Strings.WelcomePage_Menu_Remove, () => remove(row)),
        ];
    }

    /// <summary>IDE-WEL-004: the main window was activated; projects may have appeared or gone.</summary>
    public void OnWindowActivated() => _ = CheckMissingAsync();

    /// <summary>IDE-WEL-004: checks every entry's project file on a background thread; stale results are dropped.</summary>
    public Task CheckMissingAsync()
    {
        var check = CheckMissingCoreAsync();
        MissingChecks = MissingChecks.IsCompleted ? check : Task.WhenAll(MissingChecks, check);
        return check;
    }

    /// <summary>Completes once every existence check started so far has applied (or dropped) its result (I-221).</summary>
    public Task MissingChecks { get; private set; } = Task.CompletedTask;

    private async Task CheckMissingCoreAsync()
    {
        var check = ++_missingCheck;
        var paths = _recent.Projects.Concat(_recent.Files).Select(p => p.Path).ToList();
        IsCheckingMissing = true;
        bool[] exists;
        try
        {
            exists = await Task.Run(() => paths.Select(_fileSystem.FileExists).ToArray());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            exists = [.. paths.Select(_ => true)];
        }

        if (check != _missingCheck)
            return;
        _missing.Clear();
        for (var i = 0; i < paths.Count; i++)
        {
            if (!exists[i])
                _missing.Add(paths[i]);
        }

        foreach (var row in Projects.Concat(Files))
            row.IsMissing = _missing.Contains(row.Path);
        IsCheckingMissing = false;
    }

    /// <summary>The tab was closed: stop following the services.</summary>
    internal void Detach()
    {
        _recent.Changed -= OnRecentChanged;
        _settings.Changed -= OnSettingChanged;
        _missingCheck++;
    }

    private void OnRecentChanged(object? sender, EventArgs e)
    {
        if (!ShowFilter)
            _filterText = ""; // the hidden box no longer filters
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(ShowFilter));
        Rebuild();
        _ = CheckMissingAsync();
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key == SettingsCatalog.EnvironmentShowWelcomeOnStartup.Name)
            OnPropertyChanged(nameof(ShowOnStartup));
    }

    /// <summary>IDE-WEL-001/007/009/024: the service's order, filtered; existing row objects are reused.</summary>
    private void Rebuild()
    {
        var now = _clock.Now.ToLocalTime().DateTime;
        Sync(Projects, RecentKind.Project, _recent.Projects, now);
        Sync(Files, RecentKind.File, _recent.Files, now);
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(EmptySecondaryText));
    }

    private void Sync(ObservableCollection<RecentProjectRow> rows, RecentKind kind, IReadOnlyList<RecentItem> items, DateTime now)
    {
        var wanted = items
            .Where(p => Matches(kind, p.Path))
            .Select(p => new RecentProjectRow(kind, p.Path, p.Pinned,
                RecentProjectRow.FormatDate(p.LastOpened.ToLocalTime().DateTime, now, CultureInfo.CurrentCulture)) { IsMissing = _missing.Contains(p.Path) })
            .ToList();
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Any(w => w.Path == rows[i].Path))
                rows.RemoveAt(i);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var at = IndexOf(rows, wanted[i].Path);
            if (at < 0)
            {
                rows.Insert(i, wanted[i]);
                continue;
            }

            rows[at].Update(wanted[i]);
            if (at != i)
                rows.Move(at, i);
        }
    }

    private ObservableCollection<RecentProjectRow> RowsOf(RecentKind kind) => kind == RecentKind.File ? Files : Projects;

    private static int IndexOf(ObservableCollection<RecentProjectRow> rows, string path)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Path == path)
                return i;
        }

        return -1;
    }

    private bool Matches(RecentKind kind, string path) =>
        _filterText.Length == 0
        || RecentProjectRow.NameOf(kind, path).Contains(_filterText, StringComparison.CurrentCultureIgnoreCase)
        || path.Contains(_filterText, StringComparison.CurrentCultureIgnoreCase);

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One row of the recent-projects list (IDE-WEL-002) or of the recent-files list (IDE-WEL-024).</summary>
public sealed class RecentProjectRow : ObservableObject
{
    private bool _isPinned;
    private bool _isMissing;
    private string _dateText;

    internal RecentProjectRow(RecentKind kind, string path, bool isPinned, string dateText)
    {
        Kind = kind;
        Path = path;
        Name = NameOf(kind, path);
        Detail = kind == RecentKind.File ? System.IO.Path.GetDirectoryName(path) ?? path : path;
        _isPinned = isPinned;
        _dateText = dateText;
    }

    /// <summary>Which list the row belongs to.</summary>
    public RecentKind Kind { get; }

    /// <summary>
    /// The absolute path (row AutomationId <c>Welcome.Recent.Row.&lt;path&gt;</c>, recent files
    /// <c>Welcome.RecentFiles.Row.&lt;path&gt;</c>).
    /// </summary>
    public string Path { get; }

    /// <summary>A project: the file name without extension; a file: the file name with extension (IDE-WEL-024).</summary>
    public string Name { get; }

    /// <summary>The second line: a project's full path; a file's folder (IDE-WEL-024).</summary>
    public string Detail { get; }

    public string DateText
    {
        get => _dateText;
        private set => SetProperty(ref _dateText, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        private set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(PinGlyph));
                OnPropertyChanged(nameof(PinToolTip));
            }
        }
    }

    public bool IsMissing
    {
        get => _isMissing;
        internal set
        {
            if (SetProperty(ref _isMissing, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>Filled star when pinned, outline otherwise.</summary>
    public string PinGlyph => IsPinned ? "★" : "☆";

    public string PinToolTip => IsPinned ? Strings.WelcomePage_Unpin : Strings.WelcomePage_Pin;

    public static string RemoveToolTip => Strings.WelcomePage_Remove;

    /// <summary>The name, with <c> (missing)</c> when the project file does not exist (IDE-WEL-004).</summary>
    public string DisplayName => IsMissing ? Name + " " + Strings.Welcome_Missing : Name;

    /// <summary>
    /// IDE-WEL-003 (local times): same calendar day → <c>today HH:mm</c>; previous day → <c>yesterday</c>; same year →
    /// <c>d MMM</c>; else <c>d MMM yyyy</c>.
    /// </summary>
    public static string FormatDate(DateTime lastOpened, DateTime now, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (lastOpened.Date == now.Date)
            return string.Format(culture, Strings.WelcomePage_Today, lastOpened.ToString("HH:mm", culture));
        if (lastOpened.Date == now.Date.AddDays(-1))
            return Strings.WelcomePage_Yesterday;
        return lastOpened.ToString(lastOpened.Year == now.Year ? "d MMM" : "d MMM yyyy", culture);
    }

    internal static string NameOf(RecentKind kind, string path) =>
        kind == RecentKind.File ? System.IO.Path.GetFileName(path) : System.IO.Path.GetFileNameWithoutExtension(path);

    internal void Update(RecentProjectRow from)
    {
        IsPinned = from.IsPinned;
        DateText = from.DateText;
        IsMissing = from.IsMissing;
    }
}
