using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Ide.Core.Build;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// The component metadata of the IDE (tool-palette.md §2): finds the design folders (DSG-TP-001), loads the catalog
/// through the file system and hands it to the document service; <c>component.reloadMetadata</c> (DSG-TP-008) reloads it
/// and re-binds every open form document. <see cref="Load"/> is the start-up wiring point (D-088).
/// </summary>
public sealed class ComponentMetadataService(DocumentService documents, IFileSystem fileSystem, ISettingsService settings,
    IStatusService status, ILog log, ToolchainEnvironment environment)
{
    private const string LogCategory = "Designer";
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(5); // UI-MSG-003 "Status"

    /// <summary>The current catalog (empty until <see cref="Load"/>).</summary>
    public ComponentCatalog Catalog { get; private set; } = ComponentCatalog.Empty;

    /// <summary>The catalog was replaced (the Tool Palette refreshes, DSG-TP-008).</summary>
    public event EventHandler<ComponentCatalog>? CatalogChanged;

    /// <summary>Registers <c>component.reloadMetadata</c>.</summary>
    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.RegisterHandler("component.reloadMetadata", new ReloadHandler(this));
    }

    /// <summary>
    /// Loads the metadata of the framework (and, when present, the data-access) design folder; every problem is logged.
    /// The catalog becomes the one new form documents are read with.
    /// </summary>
    public ComponentCatalog Load()
    {
        var indexes = new List<string>();
        foreach (var (key, relative, required) in ((SettingKey<string>, string, bool)[])[
            (SettingsCatalog.ToolchainFrameworkDesignDir, DesignFolderResolver.FrameworkFolder, true),
            (SettingsCatalog.ToolchainDataDesignDir, DesignFolderResolver.DataFolder, false)])
        {
            var folder = DesignFolderResolver.Resolve(settings.Get(key), environment.FreexCompHome, environment.IdeDirectory, relative,
                fileSystem.FileExists);
            if (folder is not null && fileSystem.FileExists(Path.Combine(folder, "index.json")))
            {
                indexes.Add(Path.Combine(folder, "index.json"));
            }
            else if (required)
            {
                log.Warning(LogCategory, "No component metadata found" + (folder is null ? "." : " in " + folder + "."));
            }
        }

        var result = CatalogLoader.Load(indexes, path => fileSystem.FileExists(path) ? fileSystem.ReadAllBytes(path) : null);
        foreach (var problem in result.Problems)
        {
            log.Warning(LogCategory, problem.ToString() ?? "");
        }

        Catalog = result.Catalog;
        documents.DesignCatalog = Catalog;
        return Catalog;
    }

    /// <summary>DSG-TP-008: reload, re-bind every open form document, refresh the palette, report the count.</summary>
    public void Reload()
    {
        var catalog = Load();
        foreach (var form in documents.Documents.OfType<FormDocumentViewModel>())
        {
            form.Designer.Rebind(catalog);
        }

        CatalogChanged?.Invoke(this, catalog);
        var count = catalog.All.Count(c => c.ClassName != ComponentCatalog.RootClassName);
        status.ShowMessage(string.Format(CultureInfo.CurrentCulture, Strings.Msg_Designer_MetadataReloaded, count), StatusDuration);
    }

    private sealed class ReloadHandler(ComponentMetadataService owner) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            owner.Reload();
            return Task.CompletedTask;
        }
    }
}
