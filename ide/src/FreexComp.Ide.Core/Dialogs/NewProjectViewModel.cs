using System.Globalization;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Projects;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Dialogs;

/// <summary>
/// The New Project dialog (dialogs.md §3): template, name, location, the live target path. Fields are validated
/// (IDE-PM-021/022) 200 ms after the last change on a background thread; while a check is pending or a field is
/// invalid, <c>Create</c> is disabled (UI-DLG-006, UI-DLG-030). <c>Create</c> returns the request with the location
/// normalized (UI-DLG-011), <c>Cancel</c> null.
/// </summary>
public sealed class NewProjectViewModel : DialogViewModel<NewProjectRequest>
{
    public const string DialogName = "NewProject";

    public static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(200);

    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly Func<string?, Task<string?>> _pickFolder;
    private ProjectTemplate _selectedTemplate;
    private string _name;
    private string _location;
    private bool _nameTouched;
    private bool _locationTouched;
    private bool _isChecking;
    private string? _nameError;
    private string? _locationError;
    private IDisposable? _pending;
    private int _version;

    /// <param name="templates">At least one; the first is selected unless <paramref name="previous"/> names another.</param>
    /// <param name="defaultLocation">The initial location (last location used, else the default projects folder).</param>
    /// <param name="previous">The values to show again after a failed creation (UI-DLG-031).</param>
    /// <param name="pickFolder">The folder picker of the Location field, given the field's value; null = canceled.</param>
    public NewProjectViewModel(IReadOnlyList<ProjectTemplate> templates, IFileSystem fileSystem, IClock clock,
        string defaultLocation, NewProjectRequest? previous, Func<string?, Task<string?>> pickFolder)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(defaultLocation);
        if (templates.Count == 0)
            throw new ArgumentException("No project template.", nameof(templates));
        Templates = templates;
        _fileSystem = fileSystem;
        _clock = clock;
        _pickFolder = pickFolder;
        _selectedTemplate = templates.FirstOrDefault(t => t.Id == previous?.TemplateId) ?? templates[0];
        _location = previous?.Location ?? defaultLocation;
        _name = previous?.Name ?? DefaultName(fileSystem, _location);

        Create = new DialogButtonViewModel("Ok", Strings.NewProject_Create, Commit)
        {
            IsDefault = true,
            IsAccent = true,
            IsEnabled = false,
            AccessLabel = "_" + Strings.NewProject_Create,
        };
        var cancel = new DialogButtonViewModel("Cancel", Strings.Button_Cancel, () => Close(null)) { IsCancel = true };
        Buttons = [Create, cancel];
        ScheduleCheck();
    }

    public override string Name => DialogName;

    public override string Title => Strings.NewProject_Title;

    public override double Width => 640;

    public override double? Height => 400;

    public override bool CanResize => true;

    public override IReadOnlyList<DialogButtonViewModel> Buttons { get; }

    public DialogButtonViewModel Create { get; }

    public IReadOnlyList<ProjectTemplate> Templates { get; }

    public ProjectTemplate SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedTemplate, value))
            {
                OnPropertyChanged(nameof(LanguageText));
                ScheduleCheck();
            }
        }
    }

    /// <summary><c>Language: &lt;language display name&gt;</c> of the selected template.</summary>
    public string LanguageText => string.Format(CultureInfo.CurrentCulture, Strings.NewProject_Language, LanguageName(SelectedTemplate.Language));

    public string ProjectName
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _name, value))
            {
                _nameTouched = true;
                OnPropertyChanged(nameof(TargetPath));
                ScheduleCheck();
            }
        }
    }

    public string Location
    {
        get => _location;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _location, value))
            {
                _locationTouched = true;
                OnPropertyChanged(nameof(TargetPath));
                ScheduleCheck();
            }
        }
    }

    /// <summary><c>Will be created in:</c> <c>&lt;Location&gt;\&lt;Name&gt;</c>, updated live.</summary>
    public string TargetPath =>
        Path.Combine(NewProjectValidation.NormalizeLocation(Location) ?? Location.Trim(), ProjectName);

    /// <summary>Message under Name (<c>Dialog.NewProject.Error.Name</c>); null when none is shown.</summary>
    public string? NameError
    {
        get => _nameError;
        private set => SetProperty(ref _nameError, value);
    }

    /// <summary>Message under Location (<c>Dialog.NewProject.Error.Location</c>); null when none is shown.</summary>
    public string? LocationError
    {
        get => _locationError;
        private set => SetProperty(ref _locationError, value);
    }

    /// <summary>A check is scheduled or running (UI-DLG-030).</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set => SetProperty(ref _isChecking, value);
    }

    /// <summary>A field lost focus: from now on an empty value shows its message (UI-DLG-006).</summary>
    public void FieldLeft(string fieldId)
    {
        if (fieldId == "Name")
            _nameTouched = true;
        else if (fieldId == "Location")
            _locationTouched = true;
        ScheduleCheck();
    }

    /// <summary>The Browse button: the folder picker starting at the field's value; a picked folder replaces it.</summary>
    public async Task BrowseAsync()
    {
        if (await _pickFolder(Location) is { } folder)
            Location = folder;
    }

    /// <summary><c>Project&lt;n&gt;</c>: the smallest n ≥ 1 for which <c>&lt;location&gt;\Project&lt;n&gt;</c> does not exist.</summary>
    public static string DefaultName(IFileSystem fileSystem, string location)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var folder = NewProjectValidation.NormalizeLocation(location ?? "");
        for (var n = 1; ; n++)
        {
            var name = "Project" + n.ToString(CultureInfo.InvariantCulture);
            if (folder is null || !(fileSystem.DirectoryExists(Path.Combine(folder, name)) || fileSystem.FileExists(Path.Combine(folder, name))))
                return name;
        }
    }

    private static string LanguageName(string language) =>
        Strings.ResourceManager.GetString("Language_" + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(language), CultureInfo.CurrentCulture) ?? language;

    private void Commit()
    {
        if (IsChecking || NameError is not null || LocationError is not null || !Create.IsEnabled)
            return;
        Close(new NewProjectRequest(SelectedTemplate.Id, ProjectName, NewProjectValidation.NormalizeLocation(Location) ?? Location));
    }

    private void ScheduleCheck()
    {
        _pending?.Dispose();
        var version = ++_version;
        IsChecking = true;
        Create.IsEnabled = false;
        _pending = _clock.Schedule(CheckDelay, () => _ = CheckAsync(version));
    }

    /// <summary>Validates the current values off the UI thread; a result for older values is dropped.</summary>
    private async Task CheckAsync(int version)
    {
        var (name, location, template) = (ProjectName, Location, SelectedTemplate);
        (OperationResult Name, OperationResult Location) result;
        try
        {
            result = await Task.Run(() =>
            {
                var nameResult = NewProjectValidation.ValidateName(name);
                var locationResult = nameResult.Success
                    ? NewProjectValidation.ValidateLocation(location, name, template, _fileSystem)
                    : NewProjectValidation.ValidateLocationSyntax(location);
                return (nameResult, locationResult);
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result = (OperationResult.Ok, OperationResult.Fail("Msg.Project.LocationInvalid"));
        }

        if (version != _version || IsClosed)
            return;
        NameError = Shown(result.Name, name, _nameTouched);
        LocationError = Shown(result.Location, location, _locationTouched);
        IsChecking = false;
        Create.IsEnabled = result.Name.Success && result.Location.Success;
    }

    /// <summary>An untouched field that is merely empty shows no message, but still blocks Create (UI-DLG-006).</summary>
    private static string? Shown(OperationResult result, string value, bool touched) =>
        result.Success || (value.Length == 0 && !touched) ? null : NewProjectValidation.MessageText(result);
}
