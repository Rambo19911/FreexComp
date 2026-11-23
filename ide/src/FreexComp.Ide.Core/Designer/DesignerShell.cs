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
    private readonly Services.IDialogService? _dialogs;
    private readonly Layout.ILayoutService? _uiState;
    private FormDesignerViewModel? _followed;
    private bool _isDesignActive;

    /// <param name="dialogs">The modal dialogs of the component editors the designer verbs open (DSG-SUR-195, 196).</param>
    /// <param name="uiState">The UI state the component editors remember their size in (UI-DLG-252).</param>
    public DesignerShell(ICommandService commands, IDocumentService documents, ISettingsService settings, LayoutViewModel layout, int implementedPhase,
        Services.IDialogService? dialogs = null, Layout.ILayoutService? uiState = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(layout);
        _commands = commands;
        _documents = documents;
        _settings = settings;
        _layout = layout;
        _dialogs = dialogs;
        _uiState = uiState;

        commands.RegisterHandler("window.layout.design", new Handler(_ => true, _ =>
        {
            layout.SwitchProfile(LayoutProfiles.Design);
            return Task.CompletedTask;
        }, () => layout.ActiveProfile == LayoutProfiles.Design));
        foreach (var definition in commands.Definitions.Where(d => d.Scope == CommandScope.Designer && d.Phase <= implementedPhase && !commands.IsHandled(d.Id)))
        {
            var id = definition.Id;
            commands.RegisterHandler(id, Toggles.TryGetValue(id, out var key)
                ? new Handler(_ => ActiveDesigner is not null, _ =>
                {
                    settings.Set(key, !settings.Get(key));
                    return Task.CompletedTask;
                }, () => settings.Get(key))
                : new Handler(
                    argument => ActiveDesigner is { } d && new DesignSurfaceCommands(d).CanExecute(id, argument),
                    argument => ActiveDesigner is { } d ? new DesignSurfaceCommands(d).ExecuteAsync(id, argument) : Task.CompletedTask,
                    () => id == FreexComp.Designer.Surface.Commands.TabOrder && ActiveDesigner?.Surface?.State == InteractionState.TabOrder));
        }

        settings.Changed += (_, e) =>
        {
            if (e.Key.StartsWith("designer.", StringComparison.Ordinal)) // every designer.* setting is read live (I-132)
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

    /// <summary>UI-WIN-012 (b): true while a debug session holds the <c>Debug</c> profile, which overrides rule (a).</summary>
    public Func<bool>? AutoSwitchSuspended { get; set; }

    /// <summary><see cref="IsDesignActive"/> changed.</summary>
    public event EventHandler? IsDesignActiveChanged;

    /// <summary>The active document or its view changed: <see cref="ActiveDesigner"/> may be another (the Object Inspector follows it, DSG-OI-001).</summary>
    public event EventHandler? ActiveDesignerChanged;

    /// <summary>DSG-FD-071 <c>Show Problems</c> of every form document: the IDE lists them in Messages.</summary>
    public Action<FormDocumentViewModel, IReadOnlyList<FreexComp.Designer.Text.FormDiagnostic>>? ShowProblems { get; set; }

    private void Attach(DocumentViewModel document)
    {
        if (document is not FormDocumentViewModel form)
        {
            return;
        }

        form.Designer.Settings = new SettingsDesignerSettings(_settings);
        form.Designer.Dialogs = _dialogs;
        form.Designer.UiState = _uiState;
        form.Designer.ContextMenu = () => DesignerContextMenu.Build(_commands, form.Designer.Surface?.Verbs);
        form.Designer.ShowProblems = problems => ShowProblems?.Invoke(form, problems);
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
        ActiveDesignerChanged?.Invoke(this, EventArgs.Empty);
        if (_settings.Get(SettingsCatalog.LayoutAutoSwitch) && AutoSwitchSuspended?.Invoke() != true)
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

    private sealed class Handler(Func<string?, bool> canExecute, Func<string?, Task> execute, Func<bool> isChecked) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute(argument);

        public Task ExecuteAsync(string? argument) => execute(argument);

        public bool IsChecked(string? argument) => isChecked();
    }
}

/// <summary>
/// The designer settings over the settings service (<c>designer.*</c>, I-132): every value is read live, so a change in
/// Tools › Options › Designer or through a toolbar toggle takes effect at the next read.
/// </summary>
public sealed class SettingsDesignerSettings(ISettingsService settings) : IDesignerSettings
{
    public bool ShowComponentCaptions => settings.Get(SettingsCatalog.DesignerShowComponentCaptions);

    public int HandleSize => settings.Get(SettingsCatalog.DesignerHandleSize);

    public bool ShowGrid => settings.Get(SettingsCatalog.DesignerShowGrid);

    public bool SnapToGrid => settings.Get(SettingsCatalog.DesignerSnapToGrid);

    public int GridSizeX => settings.Get(SettingsCatalog.DesignerGridSizeX);

    public int GridSizeY => settings.Get(SettingsCatalog.DesignerGridSizeY);

    public bool UseGuidelines => settings.Get(SettingsCatalog.DesignerUseGuidelines);

    public int GuidelineSnapDistance => settings.Get(SettingsCatalog.DesignerGuidelineSnapDistance);

    public int GuidelineMargin => settings.Get(SettingsCatalog.DesignerGuidelineMargin);

    public bool LockControls => settings.Get(SettingsCatalog.DesignerLockControls);
}
