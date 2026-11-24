using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Settings;

namespace FreexComp.Ide.Core.Menus;

/// <summary>A toolbar button: command, icon and tooltip <c>Title (Shortcut)</c> (UI-TB-003).</summary>
public sealed class ToolBarButtonModel : ObservableObject
{
    public ToolBarButtonModel(IdeCommand command, string iconName)
    {
        Command = command;
        IconName = iconName;
        command.PropertyChanged += OnCommandChanged;
    }

    public IdeCommand Command { get; }

    public string IconName { get; }

    public bool IsToggle => Command.Definition.IsToggle;

    /// <summary>Automation name (UI-TB-003).</summary>
    public string Title => Command.Title;

    public string Tooltip => Command.GestureText.Length == 0 ? Command.Title : string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.Strings.Format_TitleWithShortcut, Command.Title, Command.GestureText);

    private void OnCommandChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IdeCommand.Title) or nameof(IdeCommand.GestureText))
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Tooltip));
        }
    }
}

/// <summary>
/// A toolbar combo box (UI-TB-020/021): its items, the selected one and whether it is enabled come from its provider;
/// without one it stays empty and disabled. Choosing an item calls <see cref="Choose"/>.
/// </summary>
public sealed class ToolBarComboModel(string name, double width) : ObservableObject
{
    private IReadOnlyList<string> _items = [];
    private string? _selected;
    private bool _isEnabled;

    public string Name { get; } = name;

    public double Width { get; } = width;

    public IReadOnlyList<string> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    public string? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>The user picked <c>item</c>; null = the combo has no action.</summary>
    public Action<string>? Choose { get; set; }
}

/// <summary>
/// UI-TB-020/021, IDE-PM-090: the Build toolbar's Configuration and Platform combos follow the open project and are
/// enabled only with a project while Idle; picking a configuration runs <c>project.setConfiguration</c>.
/// </summary>
public static class ProjectCombos
{
    public static void Attach(IReadOnlyList<ToolBarModel> toolBars, Projects.IProjectService projects, Build.IIdeState state, ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(toolBars);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commands);
        var combos = toolBars.SelectMany(t => t.Items.OfType<ToolBarComboModel>()).ToList();
        var configuration = combos.FirstOrDefault(c => c.Name == "Configuration");
        var platform = combos.FirstOrDefault(c => c.Name == "Platform");
        configuration?.Choose = name => _ = commands.ExecuteAsync("project.setConfiguration", name);
        void Update()
        {
            var project = projects.Project;
            var enabled = project is not null && state.Current.State == Models.IdeState.Idle;
            if (configuration is not null)
            {
                configuration.Items = project?.Configurations ?? [];
                configuration.Selected = project?.ActiveConfiguration;
                configuration.IsEnabled = enabled;
            }

            if (platform is not null)
            {
                platform.Items = project is null ? [] : [.. project.Platforms.Select(PlatformLabel)];
                platform.Selected = project is { Platforms.Count: > 0 } ? PlatformLabel(project.Platforms[0]) : null;
                platform.IsEnabled = enabled;
            }
        }

        projects.ProjectChanged += (_, _) => Update();
        state.Changed += (_, _) => Update();
        Update();
    }

    /// <summary><c>win64</c> → <c>Win64</c> (main-window.md R9).</summary>
    public static string PlatformLabel(string platform) =>
        string.IsNullOrEmpty(platform) ? platform : char.ToUpperInvariant(platform[0]) + platform[1..];
}

/// <summary>A separator inside a toolbar.</summary>
public sealed record ToolBarSeparatorModel
{
    public static readonly ToolBarSeparatorModel Instance = new();
}

/// <summary>
/// One toolbar (UI-TB-004/005): shown when its phase is reached, its setting is on, and — Debug and Designer only —
/// its context exists (a debug session or the Debug profile; an active Design view). Those contexts do not exist
/// before Phases 5 and 6, so those toolbars stay hidden until then.
/// </summary>
public sealed class ToolBarModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly bool _phaseReached;
    private bool _hasContext;

    public ToolBarModel(ToolBarDefinition definition, ICommandService commands, ISettingsService settings, int implementedPhase)
    {
        Definition = definition;
        _settings = settings;
        _phaseReached = Composition.Phase8Features.IsAvailable("toolbar:" + definition.Name, definition.Phase, implementedPhase);
        Items = [.. definition.Items.Select(item => item switch
        {
            ToolBarButton b => (object)new ToolBarButtonModel(commands.Get(b.CommandId), b.IconName),
            ToolBarCombo c => new ToolBarComboModel(c.Name, c.Width),
            _ => ToolBarSeparatorModel.Instance,
        })];
        settings.Changed += (_, e) =>
        {
            if (e.Key == definition.Visibility.Name)
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        };
    }

    public ToolBarDefinition Definition { get; }

    public string Name => Definition.Name;

    /// <summary><see cref="ToolBarButtonModel"/>, <see cref="ToolBarComboModel"/> or <see cref="ToolBarSeparatorModel"/>.</summary>
    public IReadOnlyList<object> Items { get; }

    public bool IsVisible => _phaseReached && _settings.Get(Definition.Visibility) && ContextAllowsIt;

    /// <summary>
    /// The context of a Debug or Designer toolbar exists (UI-TB-004): set by the IDE — for Designer while a Design view is
    /// the active document view (I-130). Ignored by the other toolbars.
    /// </summary>
    public bool HasContext
    {
        get => _hasContext;
        set
        {
            if (SetProperty(ref _hasContext, value))
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }

    /// <summary>UI-TB-004: Debug needs a session or the Debug profile (P6), Designer an active Design view (P5).</summary>
    private bool ContextAllowsIt => Definition.Name is not ("Debug" or "Designer") || _hasContext;
}

/// <summary>The toolbar row (UI-TB-001) and the <c>view.toolbar.*</c> handlers.</summary>
public static class ToolBarRowModel
{
    public static IReadOnlyList<ToolBarModel> Build(ICommandService commands, ISettingsService settings, int implementedPhase) =>
        [.. ToolBarDefinitions.All.Select(d => new ToolBarModel(d, commands, settings, implementedPhase))];

    /// <summary>
    /// Pure overflow rule (UI-TB-006): how many toolbars, from the left, stay in a row of <paramref name="available"/>
    /// width. When not all fit, the overflow button's width is reserved. Gaps between toolbars count.
    /// </summary>
    public static int FittingCount(IReadOnlyList<double> widths, double gap, double available, double overflowButtonWidth)
    {
        var total = widths.Sum() + (gap * Math.Max(0, widths.Count - 1));
        if (total <= available)
        {
            return widths.Count;
        }

        var used = overflowButtonWidth;
        for (var i = 0; i < widths.Count; i++)
        {
            used += widths[i] + (i > 0 ? gap : 0);
            if (used > available)
            {
                return i;
            }
        }

        return widths.Count;
    }

    /// <summary>UI-TB-004: the toolbar row's context menu lists the four <c>view.toolbar.*</c> toggles, in row order.</summary>
    public static IReadOnlyList<MenuItemModel> BuildContextMenu(ICommandService commands) =>
        [.. ToolBarDefinitions.All.Select(t => MenuItemModel.ForCommand(
            MenuLabels.For(new CommandMenuNode(t.ToggleCommandId)), commands.Get(t.ToggleCommandId), null, MenuToggleKind.Check))];

    /// <summary>Registers the <c>view.toolbar.*</c> toggles of the toolbars whose phase is reached (UI-TB-004).</summary>
    public static void RegisterCommands(ICommandService commands, ISettingsService settings, int implementedPhase)
    {
        foreach (var toolbar in ToolBarDefinitions.All.Where(t => Composition.Phase8Features.IsAvailable("toolbar:" + t.Name, t.Phase, implementedPhase)))
        {
            commands.RegisterHandler(toolbar.ToggleCommandId, new ToggleSettingHandler(settings, toolbar.Visibility));
        }

        settings.Changed += (_, e) =>
        {
            if (ToolBarDefinitions.All.Any(t => t.Visibility.Name == e.Key))
            {
                commands.InvalidateCanExecute(); // moves the check mark
            }
        };
    }

    private sealed class ToggleSettingHandler(ISettingsService settings, SettingKey<bool> key) : ICommandHandler
    {
        public bool CanExecute(string? argument) => true;

        public Task ExecuteAsync(string? argument)
        {
            settings.Set(key, !settings.Get(key));
            return Task.CompletedTask;
        }

        public bool IsChecked(string? argument) => settings.Get(key);
    }
}
