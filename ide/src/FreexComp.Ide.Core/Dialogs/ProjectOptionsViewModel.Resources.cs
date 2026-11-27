using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Build;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// A row of the grid <c>ResourceList</c> (UI-DLG-332): an entry of the <c>.pproj</c> key <c>resources</c>. <see cref="Name"/> and
/// <see cref="Type"/> are edited in place; <see cref="NameError"/> is the cell's validation message (CMP-1428).
/// </summary>
public sealed class ResourceRow : ObservableObject
{
    private readonly Action _edited;
    private string _name;
    private string _type;
    private string? _nameError;

    internal ResourceRow(ProjectResource entry, Action edited)
    {
        Path = entry.Path;
        _name = entry.Name;
        _type = entry.Type;
        _edited = edited;
    }

    /// <summary>The file as stored: relative to the project folder with <c>/</c>, or absolute.</summary>
    public string Path { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, (value ?? "").Trim()))
                _edited();
        }
    }

    /// <summary>One of <see cref="ProjectResources.Types"/>; other values are ignored.</summary>
    public string Type
    {
        get => _type;
        set
        {
            if (value is not null && ProjectResources.Types.Contains(value) && SetProperty(ref _type, value))
                _edited();
        }
    }

    public string? NameError
    {
        get => _nameError;
        internal set => SetProperty(ref _nameError, value);
    }

    internal ProjectResource ToEntry() => new(Path, Type, Name);
}

/// <summary>
/// The field <c>Icon</c> of the page <c>Application</c> (UI-DLG-331), the page <c>Resources</c> (UI-DLG-332, 333) and the page
/// <c>Version Info</c> (UI-DLG-334): the <c>.pproj</c> keys <c>icon</c>, <c>resources</c> and <c>versionInfo</c> (CMP-1427). The build
/// turns them into the generated resource script (CMP-1429); the Project Manager shows none of them (UI-DLG-335).
/// </summary>
public sealed partial class ProjectOptionsViewModel
{
    private string _icon = "";
    private ResourceImage? _iconImage;
    private ResourceRow? _selectedResource;
    private ResourcePreview _preview = ResourcePreview.Empty;
    private bool _versionInfoEnabled;
    private string _fileVersion = "";
    private string _productVersion = "";
    private string _companyName = "";
    private string _fileDescription = "";
    private string _productName = "";
    private string _legalCopyright = "";
    private string _comments = "";

    // ---- Application: Icon (UI-DLG-331) ----

    /// <summary>The application icon as stored (relative to the project folder when inside it); empty = no <c>icon</c> key.</summary>
    public string Icon
    {
        get => _icon;
        set
        {
            if (value is null || value == _icon)
                return;
            _icon = value;
            _iconImage = ReadIcon();
            OnPropertyChanged(string.Empty);
            MarkChanged();
        }
    }

    /// <summary><c>Msg.ProjectOptions.IconInvalid</c> for a set icon whose file is missing or is no icon file.</summary>
    public string? IconError { get; private set; }

    /// <summary>The largest frame of the icon for the 32 × 32 preview; null = the empty frame.</summary>
    public ResourceImage? IconImage => _iconImage;

    /// <summary><c>Icon.Browse</c>: the file picker filtered to <c>*.ico</c>.</summary>
    public async Task BrowseIconAsync()
    {
        var current = Icon.Trim().Length > 0 ? RunFields.Resolve(_baseline.Directory, Icon) : null;
        var picked = await _pickers.PickOpenFilesAsync(new FilePickerRequest(DialogName + ".Icon", Strings.ProjectOptions_IconPicker,
            [new FileFilter(Strings.ProjectOptions_IconFilter, ["*.ico"])], current is null ? _baseline.Directory : Path.GetDirectoryName(current)));
        if (picked.Count > 0)
            Icon = RunFields.StoredFolder(_baseline.Directory, picked[0]);
    }

    /// <summary><c>Icon.Clear</c>: no application icon.</summary>
    public void ClearIcon() => Icon = "";

    // ---- Resources (UI-DLG-332, 333) ----

    /// <summary>The entries in <c>.pproj</c> order.</summary>
    public ObservableCollection<ResourceRow> ResourceList { get; } = [];

    /// <summary>The choices of the <c>Type</c> cell.</summary>
    public IReadOnlyList<string> ResourceTypes => ProjectResources.Types;

    /// <summary>The row the preview shows (the grid's current row).</summary>
    public ResourceRow? SelectedResource
    {
        get => _selectedResource;
        set
        {
            if (SetProperty(ref _selectedResource, value is not null && ResourceList.Contains(value) ? value : null))
                RefreshPreview();
        }
    }

    /// <summary>UI-DLG-333: the preview of <see cref="SelectedResource"/>, read when the row, its type or its name changes.</summary>
    public ResourcePreview Preview => _preview;

    /// <summary>The first cell error of the grid, shown under it (null when every row is valid).</summary>
    public string? ResourceListError => ResourceList.Select(r => r.NameError).FirstOrDefault(e => e is not null);

    /// <summary><c>Add…</c>: the file picker (multi-select); each file becomes a row with type and name of UI-DLG-332.</summary>
    public async Task AddResourcesAsync()
    {
        var picked = await _pickers.PickOpenFilesAsync(new FilePickerRequest(DialogName + ".Resources", Strings.ProjectOptions_ResourcePicker,
            [new FileFilter(Strings.FilePrompt_Filter_All, ["*.*"])], _baseline.Directory, AllowMultiple: true));
        AddResources(picked);
    }

    /// <summary>Appends one row per file (full paths) and selects the last one.</summary>
    public void AddResources(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        ResourceRow? last = null;
        foreach (var file in files)
        {
            var type = TypeOf(file);
            last = Row(new ProjectResource(RunFields.StoredFolder(_baseline.Directory, file), type, NewName(file, type)));
            ResourceList.Add(last);
        }

        if (last is null)
            return;
        ResourcesEdited();
        SelectedResource = last;
    }

    /// <summary><c>Remove</c>: the given rows, without confirmation (the dialog has Cancel).</summary>
    public void RemoveResources(IEnumerable<ResourceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var removed = rows.ToList();
        var index = SelectedResource is { } selected ? ResourceList.IndexOf(selected) : -1;
        if (removed.Count(ResourceList.Remove) == 0)
            return;
        ResourcesEdited();
        SelectedResource = ResourceList.Count == 0 ? null : ResourceList[Math.Clamp(index, 0, ResourceList.Count - 1)];
        RefreshPreview();
    }

    /// <summary><c>Move Up</c> (-1) / <c>Move Down</c> (+1) of one row.</summary>
    public void MoveResource(ResourceRow row, int delta)
    {
        ArgumentNullException.ThrowIfNull(row);
        var index = ResourceList.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ResourceList.Count)
            return;
        ResourceList.Move(index, target);
        ResourcesEdited();
    }

    // ---- Version Info (UI-DLG-334) ----

    /// <summary><c>Include version information</c>: on = the key <c>versionInfo</c> is present; off removes it on apply.</summary>
    public bool VersionInfoEnabled
    {
        get => _versionInfoEnabled;
        set
        {
            if (_versionInfoEnabled == value)
                return;
            _versionInfoEnabled = value;
            if (value && FileVersion.Trim().Length == 0)
                _fileVersion = "1.0.0.0";
            OnPropertyChanged(string.Empty);
            MarkChanged();
        }
    }

    public string FileVersion
    {
        get => _fileVersion;
        set => Edit(ref _fileVersion, value ?? "");
    }

    /// <summary>Empty = the same as <see cref="FileVersion"/>: the key <c>productVersion</c> is left out (the empty string is E3123).</summary>
    public string ProductVersion
    {
        get => _productVersion;
        set => Edit(ref _productVersion, value ?? "");
    }

    public string CompanyName
    {
        get => _companyName;
        set => Edit(ref _companyName, value ?? "");
    }

    public string FileDescription
    {
        get => _fileDescription;
        set => Edit(ref _fileDescription, value ?? "");
    }

    public string ProductName
    {
        get => _productName;
        set => Edit(ref _productName, value ?? "");
    }

    public string LegalCopyright
    {
        get => _legalCopyright;
        set => Edit(ref _legalCopyright, value ?? "");
    }

    public string Comments
    {
        get => _comments;
        set => Edit(ref _comments, value ?? "");
    }

    public string? FileVersionError => VersionError(FileVersion);

    public string? ProductVersionError => VersionError(ProductVersion);

    /// <summary>Read-only line <c>Internal name</c>: the project name.</summary>
    public string InternalName => ProjectName.Trim();

    /// <summary>Read-only line <c>Original file name</c>: the output file.</summary>
    public string OriginalFileName => OutputFileName;

    // ---- Working copy ----

    /// <summary>Loads the three keys of <see cref="_baseline"/> into the fields (constructor).</summary>
    private void LoadResourceKeys()
    {
        _icon = _baseline.Icon ?? "";
        _iconImage = ReadIcon();
        ResourceList.Clear();
        foreach (var entry in _baseline.Resources)
            ResourceList.Add(Row(entry));
        var info = _baseline.VersionInfo;
        _versionInfoEnabled = info is not null;
        _fileVersion = info?.FileVersion ?? (info is null ? "" : "1.0.0.0");
        _productVersion = info?.ProductVersion ?? "";
        _companyName = info?.CompanyName ?? "";
        _fileDescription = info?.FileDescription ?? "";
        _productName = info?.ProductName ?? "";
        _legalCopyright = info?.LegalCopyright ?? "";
        _comments = info?.Comments ?? "";
    }

    /// <summary>The working copy of <paramref name="project"/> with the three keys of the fields; unchanged values keep their form.</summary>
    private Project WithResourceKeys(Project project)
    {
        var entries = ImmutableArray.CreateRange(ResourceList.Select(r => r.ToEntry()));
        var icon = Icon.Trim();
        return project with
        {
            Icon = icon.Length == 0 ? null : icon == _baseline.Icon?.Trim() ? _baseline.Icon : icon,
            Resources = entries.SequenceEqual(_baseline.Resources) ? _baseline.Resources : entries,
            VersionInfo = VersionInfoEnabled ? VersionInfo(_baseline.VersionInfo ?? new ProjectVersionInfo()) : null,
        };
    }

    /// <summary>
    /// The edited <c>versionInfo</c>: an empty version field is left out; an empty text keeps an empty key that was there and is
    /// left out otherwise.
    /// </summary>
    private ProjectVersionInfo VersionInfo(ProjectVersionInfo stored)
    {
        static string? Text(string? was, string text) => text.Length == 0 ? (was == "" ? "" : null) : text;
        static string? Version(string text) => text.Trim().Length == 0 ? null : text.Trim();
        return stored with
        {
            FileVersion = Version(FileVersion),
            ProductVersion = Version(ProductVersion),
            CompanyName = Text(stored.CompanyName, CompanyName),
            FileDescription = Text(stored.FileDescription, FileDescription),
            ProductName = Text(stored.ProductName, ProductName),
            LegalCopyright = Text(stored.LegalCopyright, LegalCopyright),
            Comments = Text(stored.Comments, Comments),
        };
    }

    /// <summary>The errors of the three pages (part of <see cref="Validate"/>).</summary>
    private void ValidateResources()
    {
        IconError = Icon.Trim().Length > 0 && IconImage is null ? Strings.Msg_ProjectOptions_IconInvalid : null;
        var icon = Icon.Trim();
        var seen = new Dictionary<(string Type, string Name), string>();
        foreach (var row in ResourceList)
        {
            string? error = null;
            if (!ProjectResources.IsValidName(row.Name))
                error = Strings.Msg_ProjectOptions_ResourceNameInvalid;
            else if (row.Type == "ICON" && icon.Length > 0 && Canonical(row.Name) == "1")
                error = string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ResourceDuplicate, row.Name, icon); // CMP-1431
            else if (!seen.TryAdd((row.Type, Canonical(row.Name)), row.Path))
                error = string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ResourceDuplicate, row.Name, seen[(row.Type, Canonical(row.Name))]);
            row.NameError = error;
        }

        OnPropertyChanged(nameof(IconError));
        OnPropertyChanged(nameof(ResourceListError));
    }

    private bool ResourcesHaveError() => ResourceList.Any(r => r.NameError is not null);

    private bool VersionInfoHasError() => VersionInfoEnabled && (FileVersionError is not null || ProductVersionError is not null);

    private void ResourcesEdited()
    {
        RefreshPreview();
        MarkChanged();
    }

    private void RefreshPreview()
    {
        _preview = SelectedResource is { } row ? ResourcePreviews.Create(_fileSystem, Full(row.Path), row.Type) : ResourcePreview.Empty;
        OnPropertyChanged(nameof(Preview));
    }

    private ResourceRow Row(ProjectResource entry) => new(entry, ResourcesEdited);

    private string? Full(string stored) => RunFields.Resolve(_baseline.Directory, stored);

    private ResourceImage? ReadIcon()
    {
        try
        {
            return Icon.Trim().Length > 0 && Full(Icon.Trim()) is { } path && _fileSystem.FileExists(path)
                ? ResourcePreviews.ImageOf(_fileSystem.ReadAllBytes(path), "ICON")
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? VersionError(string text) =>
        text.Trim().Length > 0 && !ProjectResources.IsValidVersion(text.Trim()) ? Strings.Msg_ProjectOptions_VersionInvalid : null;

    /// <summary>UI-DLG-332: <c>.ico</c> ICON, <c>.bmp</c> BITMAP, <c>.cur</c> CURSOR, anything else RCDATA.</summary>
    private static string TypeOf(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".ico" => "ICON",
        ".bmp" => "BITMAP",
        ".cur" => "CURSOR",
        _ => "RCDATA",
    };

    /// <summary>
    /// UI-DLG-332: an ICON gets the next free number from 2; any other type the file's base name upper-cased, characters other than
    /// letters, digits and <c>_</c> as <c>_</c> (a leading <c>_</c> when that is no valid name, at most 64 characters), and
    /// <c>_2</c>, <c>_3</c>, … when an entry of the type already has the name.
    /// </summary>
    private string NewName(string file, string type)
    {
        bool Used(string name) => ResourceList.Any(r => r.Type == type && ProjectResources.IsValidName(r.Name) && Canonical(r.Name) == Canonical(name));
        if (type == "ICON")
        {
            var number = 2;
            while (Used(number.ToString(CultureInfo.InvariantCulture)))
                number++;
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var stem = new string([.. Path.GetFileNameWithoutExtension(file).ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_')]);
        if (!ProjectResources.IsValidName(stem))
            stem = "_" + stem; // empty, or digits that are no number 1…65535
        stem = stem[..Math.Min(stem.Length, 64)];
        if (!Used(stem))
            return stem;
        if (char.IsAsciiDigit(stem[0]))
            stem = "_" + stem;
        for (var n = 2; ; n++)
        {
            var suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
            var name = stem[..Math.Min(stem.Length, 64 - suffix.Length)] + suffix;
            if (!Used(name))
                return name;
        }
    }

    /// <summary>CMP-1428: identifiers compare case-insensitively, numbers by value (<c>01</c> = <c>1</c>).</summary>
    private static string Canonical(string name) =>
        name.Length > 0 && char.IsAsciiDigit(name[0]) && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : name.ToUpperInvariant();
}
