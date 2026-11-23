using FreexComp.Designer.Surface;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Documents;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Designer;

/// <summary>
/// The IDE around the Design view from Phase 5 on (D-080, I-130): the <c>designer.*</c> command handlers, which act on
/// the active document's Design view (toolbar, menus, context menu; the surface's own keys stay with the surface), the
/// toggles bound to the <c>designer.*</c> settings, the design-surface context menu, the <c>Design</c> layout profile
/// with automatic switching (UI-WIN-012, <c>layout.autoSwitch</c>) and <c>window.layout.design</c>, and whether a Design
/// view is active (the Designer toolbar's context, UI-TB-004).
/// </summary>
public sealed class DesignerShell
{
    private static readonly Dictionary<string, SettingKey<bool>> Toggles = new(StringComparer.Ordinal)
    {
        ["designer.lockControls"] = SettingsCatalog.DesignerLockControls,
        ["designer.toggleGrid"] = SettingsCatalog.DesignerShowGrid,
        ["designer.toggleSnapToGrid"] = SettingsCatalog.DesignerSnapToGrid,
        ["designer.toggleGuidelines"] = SettingsCatalog.DesignerUseGuidelines,
    };

    private readonly ICommandService _commands;
    private readonly IDocumentService _documents;
    private readonly ISettingsService _settings;
    private readonly LayoutViewModel _layout;
    private FormDesignerViewModel? _followed;
    private bool _isDesignActive;

    public DesignerShell(ICommandService commands, IDocumentService documents, ISettingsService settings, LayoutViewModel layout, int implementedPhase)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(layout);
        _commands = commands;
        _documents = documents;
        _settings = settings;
        _layout = layout;

        commands.RegisterHandler("window.layout.design", new Handler(() => true, () =>
        {
            layout.SwitchProfile(LayoutProfiles.Design);
            return Task.CompletedTask;
        }, () => layout.ActiveProfile == LayoutProfiles.Design));
        foreach (var definition in commands.Definitions.Where(d => d.Scope == CommandScope.Designer && d.Phase <= implementedPhase && !commands.IsHandled(d.Id)))
        {
            var id = definition.Id;
            commands.RegisterHandler(id, Toggles.TryGetValue(id, out var key)
                ? new Handler(() => ActiveDesigner is not null, () =>
                {
                    settings.Set(key, !settings.Get(key));
                    return Task.CompletedTask;
                }, () => settings.Get(key))
                : new Handler(
                    () => ActiveDesigner is { } d && new DesignSurfaceCommands(d).CanExecute(id, null),
                    () => ActiveDesigner is { } d ? new DesignSurfaceCommands(d).ExecuteAsync(id, null) : Task.CompletedTask,
                    () => id == FreexComp.Designer.Surface.Commands.TabOrder && ActiveDesigner?.Surface?.State == InteractionState.TabOrder));
        }

        settings.Changed += (_, e) =>
        {
            if (Toggles.Values.Any(k => k.Name == e.Key))
            {
                foreach (var form in documents.Documents.OfType<FormDocumentViewModel>())
                {
                    form.Designer.RefreshDisplay();
                }

                commands.InvalidateCanExecute();
            }
        };
        foreach (var document in documents.Documents)
        {
            Attach(document);
        }

        documents.DocumentOpened += (_, e) => Attach(e.Document);
        documents.ActiveDocumentChanged += (_, _) => Update();
        _isDesignActive = ActiveDesigner is not null;
    }

    /// <summary>The designer of the active document while its Design view is shown; null otherwise.</summary>
    public FormDesignerViewModel? ActiveDesigner =>
        _documents.ActiveDocument is FormDocumentViewModel { ActiveView: DocumentView.Design } form ? form.Designer : null;

    /// <summary>A Design view is the active document view.</summary>
    public bool IsDesignActive => _isDesignActive;

    /// <summary><see cref="IsDesignActive"/> changed.</summary>
    public event EventHandler? IsDesignActiveChanged;

    private void Attach(DocumentViewModel document)
    {
        if (document is not FormDocumentViewModel form)
        {
            return;
        }

        form.Designer.Settings = new SettingsDesignerSettings(_settings);
        form.Designer.ContextMenu = () => DesignerContextMenu.Build(_commands);
        form.ActiveViewChanged += (_, _) =>
        {
            if (_documents.ActiveDocument == form)
            {
                Update();
            }
        };
    }

    /// <summary>The active document or its view changed.</summary>
    private void Update()
    {
        var designer = ActiveDesigner;
        Follow(designer);
        if (_settings.Get(SettingsCatalog.LayoutAutoSwitch))
        {
            _layout.SwitchProfile(designer is not null ? LayoutProfiles.Design : LayoutProfiles.Edit); // UI-WIN-012 (a)
        }

        if (_isDesignActive != (designer is not null))
        {
            _isDesignActive = designer is not null;
            IsDesignActiveChanged?.Invoke(this, EventArgs.Empty);
        }

        _commands.InvalidateCanExecute();
    }

    /// <summary>The command states follow the selection, the model and the tab-order mode of the active Design view.</summary>
    private void Follow(FormDesignerViewModel? designer)
    {
        if (_followed == designer)
        {
            return;
        }

        if (_followed is not null)
        {
            _followed.SelectionChanged -= OnDesignerChanged;
            _followed.ModelChanged -= OnDesignerChanged;
            _followed.DesignInfoBars.CollectionChanged -= OnDesignerChanged;
        }

        _followed = designer;
        if (designer is not null)
        {
            designer.SelectionChanged += OnDesignerChanged;
            designer.ModelChanged += OnDesignerChanged;
            designer.DesignInfoBars.CollectionChanged += OnDesignerChanged;
        }
    }

    private void OnDesignerChanged(object? sender, EventArgs e) => _commands.InvalidateCanExecute();

    private sealed class Handler(Func<bool> canExecute, Func<Task> execute, Func<bool> isChecked) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument) => execute();

        public bool IsChecked(string? argument) => isChecked();
    }
}

/// <summary>
/// The designer settings over the settings service: the four settings the Designer toolbar and the context menu toggle
/// (<c>designer.showGrid</c>, <c>snapToGrid</c>, <c>useGuidelines</c>, <c>lockControls</c>); the others keep the surface
/// defaults until the Designer options page binds them (I-132).
/// </summary>
public sealed class SettingsDesignerSettings(ISettingsService settings) : IDesignerSettings
{
    private static readonly DefaultDesignerSettings Defaults = new();

    public bool ShowComponentCaptions => Defaults.ShowComponentCaptions;

    public int HandleSize => Defaults.HandleSize;

    public bool ShowGrid => settings.Get(SettingsCatalog.DesignerShowGrid);

    public bool SnapToGrid => settings.Get(SettingsCatalog.DesignerSnapToGrid);

    public int GridSizeX => Defaults.GridSizeX;

    public int GridSizeY => Defaults.GridSizeY;

    public bool UseGuidelines => settings.Get(SettingsCatalog.DesignerUseGuidelines);

    public int GuidelineSnapDistance => Defaults.GuidelineSnapDistance;

    public int GuidelineMargin => Defaults.GuidelineMargin;

    public bool LockControls => settings.Get(SettingsCatalog.DesignerLockControls);
}
