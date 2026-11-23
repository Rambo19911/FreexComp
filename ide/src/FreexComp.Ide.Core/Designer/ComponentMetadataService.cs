using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Designer;

/// <summary>Where the component metadata load stands (tool-palette.md DSG-TP-003, DSG-TP-004).</summary>
public enum MetadataLoadState
{
    /// <summary>Not loaded yet (the start-up sequence has not started it; tests set a catalog directly).</summary>
    NotLoaded,

    /// <summary>Being read on a background thread; form documents opened now wait for it.</summary>
    Loading,

    /// <summary>The catalog of the found index files.</summary>
    Loaded,

    /// <summary>No framework index was found: <see cref="ComponentCatalog.Empty"/>, no Design view.</summary>
    Missing,

    /// <summary>
    /// The load itself failed (an unexpected error, logged): <see cref="ComponentCatalog.Empty"/>, no Design view, as
    /// <see cref="Missing"/>; a terminal state, so no form keeps waiting (DSG-TP-003).
    /// </summary>
    Failed,
}

/// <summary>
/// The component metadata of the IDE (tool-palette.md §2): finds the design folders (DSG-TP-001), reads the catalog on a
/// background thread (DSG-TP-003, started by the start-up sequence) and hands it to the document service; form documents
/// opened while it loads wait for it. Without an index (DSG-TP-004) the catalog is <see cref="ComponentCatalog.Empty"/>
/// and a Design view of a form read with it cannot be opened (<c>Msg.Designer.MetadataMissing</c> once per session).
/// <c>component.reloadMetadata</c> (DSG-TP-008) reloads it and re-binds every open form document.
/// </summary>
public sealed class ComponentMetadataService
{
    private const string LogCategory = "Designer";
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    private readonly DocumentService _documents;
    private readonly IFileSystem _fileSystem;
    private readonly ISettingsService _settings;
    private readonly IStatusService _status;
    private readonly ILog _log;
    private readonly ToolchainEnvironment _environment;
    private readonly IDialogService _dialogs;
    private ICommandService? _commands;
    private bool _missingShown;

    /// <summary>The Output whose <c>designer</c> channel receives the metadata problems (DSG-TP-005); null = log only.</summary>
    public Build.IOutputService? Output { get; set; }

    public ComponentMetadataService(DocumentService documents, IFileSystem fileSystem, ISettingsService settings, IStatusService status,
        ILog log, ToolchainEnvironment environment, IDialogService dialogs)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        documents.DocumentOpened += (_, e) =>
        {
            if (e.Document is FormDocumentViewModel form)
                Attach(form);
        };
    }

    /// <summary>The current catalog (<see cref="ComponentCatalog.Empty"/> until loaded, and when the index is missing).</summary>
    public ComponentCatalog Catalog { get; private set; } = ComponentCatalog.Empty;

    public MetadataLoadState State { get; private set; }

    /// <summary>The index path a <see cref="MetadataLoadState.Missing"/> load expected (the argument of <c>Msg.Designer.MetadataMissing</c>).</summary>
    public string? MissingIndexPath { get; private set; }

    /// <summary>The error a <see cref="MetadataLoadState.Failed"/> load ended with (the argument of <c>Msg.Designer.MetadataMissing</c>).</summary>
    public string? FailureReason { get; private set; }

    /// <summary>The running (or last) load; completed before the first one starts.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The <c>Msg.Designer.MetadataMissing</c> message and its <c>Open Options</c> action, once shown.</summary>
    public Task MissingMessage { get; private set; } = Task.CompletedTask;

    /// <summary>The catalog was replaced (the Tool Palette refreshes, DSG-TP-008).</summary>
    public event EventHandler<ComponentCatalog>? CatalogChanged;

    /// <summary>Registers <c>component.reloadMetadata</c>.</summary>
    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("component.reloadMetadata", new ReloadHandler(this));
    }

    /// <summary>
    /// DSG-TP-003: loads the metadata of the framework (and, when present, the data-access) design folder on a background
    /// thread; every problem is logged. The catalog becomes the one form documents are read with, and the form documents
    /// opened meanwhile are read against it. A load already running is returned.
    /// </summary>
    public Task LoadAsync()
    {
        if (State == MetadataLoadState.Loading)
            return Loading;
        State = MetadataLoadState.Loading;
        var folders = ((SettingKey<string>, string, bool)[])[
            (SettingsCatalog.ToolchainFrameworkDesignDir, DesignFolderResolver.FrameworkFolder, true),
            (SettingsCatalog.ToolchainDataDesignDir, DesignFolderResolver.DataFolder, false)];
        var settings = folders.Select(f => (_settings.Get(f.Item1), f.Item2, f.Item3)).ToList(); // read on this (UI) thread
        Loading = LoadCoreAsync(settings);
        return Loading;
    }

    private async Task LoadCoreAsync(IReadOnlyList<(string Setting, string Relative, bool Required)> folders)
    {
        CatalogLoadResult result;
        string? missing;
        try
        {
            (result, missing) = await Task.Run(() => Read(folders));
        }
        catch (Exception ex)
        {
            // DSG-TP-003: never an endless wait; the load ends in the terminal Failed state with the empty catalog.
            _log.Error(LogCategory, "component metadata could not be loaded: " + ex.Message, ex);
            MissingIndexPath = null;
            FailureReason = ex.Message;
            Finish(ComponentCatalog.Empty, MetadataLoadState.Failed);
            return;
        }

        foreach (var problem in result.Problems)
        {
            _log.Warning(LogCategory, problem.ToString() ?? "");
            // DSG-TP-005: every problem on the Output's designer channel.
            Output?.Append(Build.OutputChannels.Designer, string.Format(System.Globalization.CultureInfo.CurrentCulture,
                Resources.Strings.Msg_Designer_MetadataError, problem.File, problem.Line, problem.Column, problem.Message), Build.OutputKind.Error);
        }
        MissingIndexPath = missing;
        FailureReason = null;
        if (missing is null)
            _log.Info(LogCategory, $"component metadata loaded: {result.Catalog.All.Count} classes, {result.Catalog.All.Count(c => c.Palette is not null && !c.IsAbstract)} on the palette");
        Finish(missing is null ? result.Catalog : ComponentCatalog.Empty, missing is null ? MetadataLoadState.Loaded : MetadataLoadState.Missing);
    }

    /// <summary>The end of a load: the catalog and state, then the waiting form documents re-bind to it.</summary>
    private void Finish(ComponentCatalog catalog, MetadataLoadState state)
    {
        Catalog = catalog;
        State = state;
        _documents.DesignCatalog = Catalog;
        foreach (var form in _documents.Documents.OfType<FormDocumentViewModel>().Where(d => d.Designer.IsMetadataLoading))
            form.Designer.Rebind(Catalog);
        CatalogChanged?.Invoke(this, Catalog);
    }

    /// <summary>The background part: folder resolution (DSG-TP-001) and the catalog; the missing index path, if any.</summary>
    private (CatalogLoadResult Result, string? MissingIndex) Read(IReadOnlyList<(string Setting, string Relative, bool Required)> folders)
    {
        var indexes = new List<string>();
        string? missing = null;
        foreach (var (setting, relative, required) in folders)
        {
            var folder = DesignFolderResolver.Resolve(setting, _environment.FreexCompHome, _environment.IdeDirectory, relative, _fileSystem.FileExists);
            var index = Path.Combine(folder ?? Path.Combine(_environment.IdeDirectory, relative.Replace('/', Path.DirectorySeparatorChar)), "index.json");
            if (folder is not null && _fileSystem.FileExists(index))
            {
                indexes.Add(index);
            }
            else if (required)
            {
                _log.Warning(LogCategory, "No component metadata found: " + index);
                missing = index;
            }
        }

        return (CatalogLoader.Load(indexes, ReadFile), missing);
    }

    /// <summary>A file of the metadata, or null when it cannot be read (the loader reports it).</summary>
    private byte[]? ReadFile(string path)
    {
        try
        {
            return _fileSystem.FileExists(path) ? _fileSystem.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(LogCategory, path + ": " + ex.Message);
            return null;
        }
    }

    /// <summary>DSG-TP-008: reload, re-bind every open form document, refresh the palette, report the count.</summary>
    public async Task ReloadAsync()
    {
        await LoadAsync();
        var catalog = Catalog;
        foreach (var form in _documents.Documents.OfType<FormDocumentViewModel>().Where(d => !ReferenceEquals(d.Designer.Catalog, catalog)))
            form.Designer.Rebind(catalog);
        var count = catalog.All.Count(c => c.ClassName != ComponentCatalog.RootClassName);
        _status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_MetadataReloaded, count), StatusDuration);
    }

    /// <summary>A form document opened while loading waits (DSG-TP-003); every form asks before its Design view opens (DSG-TP-004).</summary>
    private void Attach(FormDocumentViewModel form)
    {
        var designer = form.Designer;
        designer.DesignRefused = () => RefuseDesign(designer);
        if (State == MetadataLoadState.Loading)
            designer.BeginMetadataWait();
        else if (form.ActiveView == DocumentView.Design && RefuseDesign(designer))
            form.ShowView(DocumentView.Text); // a form without unit opens in its Design view; there is none without metadata
    }

    /// <summary>DSG-TP-004: true when the form was read with the empty catalog of a missing index; the first time, the message.</summary>
    private bool RefuseDesign(FormDesignerViewModel designer)
    {
        if (State is not (MetadataLoadState.Missing or MetadataLoadState.Failed) || !ReferenceEquals(designer.Catalog, ComponentCatalog.Empty))
            return false;
        if (!_missingShown)
        {
            _missingShown = true;
            MissingMessage = ShowMissingAsync();
        }

        return true;
    }

    private async Task ShowMissingAsync()
    {
        var answer = await _dialogs.ShowMessageAsync(new MessageRequest("Msg.Designer.MetadataMissing", [MissingIndexPath ?? FailureReason ?? ""]));
        if (answer == "OpenOptions" && _commands is { } commands)
            await commands.ExecuteAsync("tools.options", OptionsViewModel.ToolchainPathsPage);
    }

    private sealed class ReloadHandler(ComponentMetadataService owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => owner.State != MetadataLoadState.Loading;

        public Task ExecuteAsync(string? argument) => owner.ReloadAsync();
    }
}
