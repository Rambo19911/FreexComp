using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Ide.Core.Commands;
using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Models;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.ToolWindows;
using FreexComp.Ide.Core.ViewModels;

namespace FreexComp.Ide.Core.Designer;

/// <summary>One component of the Tool Palette: its class and the name shown (the class without the leading <c>T</c>).</summary>
public sealed record PaletteItem(string ClassName, string DisplayName, string Unit, string? Icon)
{
    /// <summary>DSG-TP-061: the IDE item <c>Frames…</c> (not from metadata): it opens the frame picker.</summary>
    public bool IsFrames { get; init; }
}

/// <summary>A row of the frame picker (UI-DSG-138): the frame class, its name without the leading <c>T</c>, its unit.</summary>
public sealed record FramePickerRow(string ClassName, string DisplayName, string UnitName);

/// <summary>A page of the Tool Palette as shown (DSG-TP-020 … 022): expanded or collapsed, with its visible items.</summary>
public sealed record PalettePage(string Name, bool IsExpanded, IReadOnlyList<PaletteItem> Items);

/// <summary>
/// A row of the palette as the keyboard walks it (tool-palette.md §6): a page header or an item of an expanded page.
/// <see cref="Key"/> is the AutomationId suffix (<c>Page.&lt;PageName&gt;</c>, <c>Item.&lt;ClassName&gt;</c>, UI-TW-353).
/// </summary>
public sealed record PaletteRow(PalettePage Page, PaletteItem? Item)
{
    public bool IsPage => Item is null;

    public string Key => Item is null ? "Page." + Page.Name : "Item." + Item.ClassName;
}

/// <summary>
/// The Tool Palette tool window (tool-palette.md §3–§7, ui/designer.md §12): the catalog's palette pages and items in
/// their order, the search box, the keyboard-current row, arming an item for placement on the active form document's
/// Design view (single-shot or sticky), placing at the default position, the palette drag onto the surface, the list /
/// icon view and the collapsed pages (settings <c>toolPalette.*</c>) and the enablement rules. No Avalonia types.
/// </summary>
public sealed class ToolPaletteViewModel : ToolWindowViewModel
{
    /// <summary>The tool window's name (tool-windows.md §5, IDE-ARCH-100).</summary>
    public const string ToolName = "ToolPalette";

    private const string LogCategory = "Designer";

    /// <summary>DSG-TP-061: the class key of the <c>Frames…</c> item (AutomationId <c>Tool.ToolPalette.Item.Frames</c>).</summary>
    public const string FramesKey = "Frames";

    /// <summary>DSG-TP-061: the page the <c>Frames…</c> item starts.</summary>
    public const string FramesPage = "Standard";
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly HashSet<string> _collapsed = new(Names);
    private readonly HashSet<string> _iconFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISettingsService? _settings;
    private readonly IFileSystem? _fileSystem;
    private readonly ILog? _log;
    private ComponentCatalog _catalog;
    private List<PalettePage> _all = [];
    private FormDesignerViewModel? _designer;
    private ICommandService? _commands;
    private string _searchText = "";
    private string? _currentKey;
    private bool _iconView;
    private IReadOnlyList<FramePickerRow>? _pickerRows;

    /// <param name="settings">The <c>toolPalette.*</c> settings (view, collapsed pages); null: in-memory state.</param>
    /// <param name="fileSystem">Reads the palette icons (DSG-TP-009); null: every item shows the generic glyph.</param>
    /// <param name="log">The one log line of an icon that cannot be read.</param>
    public ToolPaletteViewModel(ComponentCatalog catalog, ISettingsService? settings = null, IFileSystem? fileSystem = null, ILog? log = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _settings = settings;
        _fileSystem = fileSystem;
        _log = log;
        if (settings is not null)
        {
            _collapsed.UnionWith(settings.Get(SettingsCatalog.ToolPaletteCollapsedPages));
            settings.Changed += (_, e) =>
            {
                if (e.Key == SettingsCatalog.ToolPaletteView.Name)
                {
                    OnPropertyChanged(nameof(IsIconView));
                }
            };
        }

        Build();
    }

    /// <summary>The Tool Palette tool window in the right lower group from Phase 5 (UI-WIN-011).</summary>
    public ToolWindowDefinition Definition => new(ToolName, "Cmd_view_toolPalette", ToolGroupId.RightLower, 5, () => this);

    /// <summary>The pages shown: all pages, or with a search text the pages with matches (expanded), DSG-TP-031.</summary>
    public IReadOnlyList<PalettePage> Pages
    {
        get
        {
            var words = Words;
            if (words.Length == 0)
            {
                return [.. AllPages().Select(p => p with { IsExpanded = !_collapsed.Contains(p.Name) })];
            }

            var pages = new List<PalettePage>();
            foreach (var page in AllPages())
            {
                var matches = page.Items.Where(i => words.All(w => i.ClassName.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
                if (matches.Count == 0)
                {
                    continue;
                }

                // DSG-TP-032: the items starting with the text (with or without the T) first, each group in palette order.
                var first = words[0];
                pages.Add(page with
                {
                    IsExpanded = true,
                    Items = [.. matches.Where(i => StartsWith(i, first)), .. matches.Where(i => !StartsWith(i, first))],
                });
            }

            return pages;
        }
    }

    /// <summary>The rows the keyboard walks (§6): each page header, followed by its items when expanded.</summary>
    public IReadOnlyList<PaletteRow> Rows =>
        [.. Pages.SelectMany(p => (IEnumerable<PaletteRow>)[new PaletteRow(p, null), .. p.IsExpanded ? p.Items.Select(i => new PaletteRow(p, i)) : []])];

    /// <summary>The keyboard-current row (focus ring, Enter, Space); null when none is current.</summary>
    public PaletteRow? Current => _currentKey is { } key ? Rows.FirstOrDefault(r => r.Key == key) : null;

    /// <summary>The collapsed pages (DSG-TP-022; persisted in <c>toolPalette.collapsedPages</c>).</summary>
    public IReadOnlySet<string> CollapsedPages => _collapsed;

    /// <summary>The search box (DSG-TP-030): words separated by spaces, each a substring of the class name.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
            {
                // DSG-TP-032: the first match is the keyboard-current item.
                if (CurrentItem is { } first)
                {
                    _currentKey = "Item." + first.ClassName;
                }

                Changed();
            }
        }
    }

    /// <summary>The item Enter in the search box places: with a search text the first match (DSG-TP-032); null otherwise.</summary>
    public PaletteItem? CurrentItem => Words.Length == 0 ? null : Pages.SelectMany(p => p.Items).FirstOrDefault();

    /// <summary>A search text matches nothing: <see cref="FilterEmptyText"/> is shown.</summary>
    public bool IsFilterEmpty => Words.Length > 0 && Pages.Count == 0;

    public string FilterEmptyText => string.Format(CultureInfo.CurrentCulture, Strings.Msg_Palette_FilterEmpty, SearchText.Trim());

    /// <summary>UI-DSG-120: the placeholder of the search box.</summary>
    public string SearchPlaceholder => Strings.ToolPalette_SearchPlaceholder;

    /// <summary>DSG-TP-024: a form document shows its Design view.</summary>
    public bool HasForm => _designer is { IsDesignActive: true };

    /// <summary>DSG-TP-024, 055: the items can be armed and placed (the form is not read-only).</summary>
    public bool AreItemsEnabled => HasForm && !_designer!.Surface!.IsReadOnly;

    /// <summary>The palette as a whole is enabled.</summary>
    public bool IsEnabled => AreItemsEnabled;

    /// <summary>
    /// DSG-TP-061: the <c>Frames…</c> item is shown — the bound form resolves instances with a frame catalog (the IDE's
    /// project catalog always).
    /// </summary>
    public bool ShowsFrames => _designer?.Frames is not null;

    /// <summary>DSG-TP-061: the <c>Frames…</c> item is enabled: the items are (DSG-TP-024) and a project is loaded.</summary>
    public bool AreFramesEnabled => AreItemsEnabled && ShowsFrames && (HasProject?.Invoke() ?? true);

    /// <summary>A project is loaded (DSG-TP-061); null: always (tests without a project service).</summary>
    public Func<bool>? HasProject { get; set; }

    /// <summary>UI-DSG-137: the tooltip of the <c>Frames…</c> item.</summary>
    public string FramesTip => Strings.Palette_FramesItemTip;

    /// <summary>DSG-TP-063: the frame picker is open.</summary>
    public bool IsFramePickerOpen => _pickerRows is not null;

    /// <summary>DSG-TP-063: the frames the picker lists (empty: the placeholder <see cref="NoFramesText"/>).</summary>
    public IReadOnlyList<FramePickerRow> FramePickerRows => _pickerRows ?? [];

    /// <summary>UI-DSG-138: the placeholder of an empty picker (<c>Msg.Palette.NoFrames</c>).</summary>
    public string NoFramesText => Strings.Msg_Palette_NoFrames;

    /// <summary>The text of the disabled palette (<c>Msg.Palette.NoForm</c>).</summary>
    public string NoFormText => Strings.Msg_Palette_NoForm;

    /// <summary>The armed class (DSG-TP-040), or null.</summary>
    public string? ArmedClass => _designer?.Host.ArmedPaletteClass;

    /// <summary>The armed item stays armed after each placement (DSG-TP-041).</summary>
    public bool IsSticky => _designer?.Host.ArmedSticky == true;

    /// <summary>DSG-TP-023: <c>toolPalette.view</c> is <c>"icons"</c>.</summary>
    public bool IsIconView => _settings is { } s ? s.Get(SettingsCatalog.ToolPaletteView) == "icons" : _iconView;

    /// <summary><c>toolPalette.focusSearch</c>: the view focuses the search box and selects its text (DSG-TP-030).</summary>
    public event EventHandler? SearchFocusRequested;

    /// <summary>The items of the palette's context menu (§6); null until the commands are registered.</summary>
    public Func<IReadOnlyList<MenuItemModel>>? ContextMenu { get; private set; }

    private string[] Words => _searchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// UI-DSG-124: the ranges of <paramref name="className"/> the search words match (case-insensitive, merged, in
    /// order); empty without a search text.
    /// </summary>
    public IReadOnlyList<(int Start, int Length)> Highlights(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        var marked = new bool[className.Length];
        foreach (var word in Words)
        {
            var at = className.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            for (var i = 0; at >= 0 && i < word.Length; i++)
            {
                marked[at + i] = true;
            }
        }

        var ranges = new List<(int, int)>();
        for (var i = 0; i < marked.Length; i++)
        {
            if (!marked[i])
            {
                continue;
            }

            var start = i;
            while (i < marked.Length && marked[i])
            {
                i++;
            }

            ranges.Add((start, i - start));
        }

        return ranges;
    }

    /// <summary>UI-DSG-126: line 2 of an item's tooltip (<c>unit UnitName</c>).</summary>
    public static string UnitText(PaletteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return string.Format(CultureInfo.CurrentCulture, Strings.ToolPalette_TooltipUnit, item.Unit);
    }

    /// <summary>
    /// DSG-TP-009: the SVG bytes of the item's icon, read when first shown; null without an icon or when it cannot be read
    /// (one log line per file, no message: the view shows the generic glyph).
    /// </summary>
    public byte[]? ReadIcon(PaletteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Icon is not { } path || _fileSystem is null || _iconFailures.Contains(path))
        {
            return null;
        }

        try
        {
            if (!path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || !_fileSystem.FileExists(path))
            {
                ReportIconFailure(path);
                return null;
            }

            return _fileSystem.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ReportIconFailure(path);
            return null;
        }
    }

    /// <summary>DSG-TP-009: an icon could not be read or drawn (logged once; the generic glyph is shown).</summary>
    public void ReportIconFailure(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_iconFailures.Add(path))
        {
            _log?.Info(LogCategory, "Palette icon not usable: " + path);
        }
    }

    /// <summary>The project was opened or closed: the enablement of the <c>Frames…</c> item follows.</summary>
    public void Refresh() => Changed();

    /// <summary>
    /// DSG-TP-063: opens the frame picker — the catalog's frames except the class of the active document and every frame
    /// that contains it (transitively), sorted by class name. Nothing while the item is disabled.
    /// </summary>
    public void OpenFramePicker()
    {
        if (!AreFramesEnabled || _designer is not { Frames: { } frames, Model: { } model })
        {
            return;
        }

        // D-123: opening the picker disarms whatever is armed (the arrow state, the idle status), like choosing any other
        // item; only a frame chosen in the picker arms again (DSG-TP-064), Escape or a click outside leaves nothing armed.
        _designer.Host.Disarm();
        var root = model.Root.TypeName;
        _pickerRows = [.. frames.All
            .Where(f => !Names.Equals(f.ClassName, root) && AncestorCatalog.RepeatedClass(frames, f.ClassName, root) is null)
            .OrderBy(f => f.ClassName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FramePickerRow(f.ClassName, f.ClassName is ['T', _, ..] ? f.ClassName[1..] : f.ClassName, f.UnitName))];
        OnPropertyChanged(nameof(IsFramePickerOpen));
        OnPropertyChanged(nameof(FramePickerRows));
    }

    /// <summary>DSG-TP-065: Escape, another document or view, or choosing nothing closes the picker without arming.</summary>
    public void CloseFramePicker()
    {
        if (_pickerRows is null)
        {
            return;
        }

        _pickerRows = null;
        OnPropertyChanged(nameof(IsFramePickerOpen));
        OnPropertyChanged(nameof(FramePickerRows));
    }

    /// <summary>DSG-TP-064: a frame chosen in the picker is armed for single-shot placement (status <c>Msg.Designer.PlacementArmed</c>).</summary>
    public void ChooseFrame(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        if (FramePickerRows.FirstOrDefault(r => Names.Equals(r.ClassName, className)) is not { } row)
        {
            return;
        }

        CloseFramePicker();
        _designer!.Host.Arm(row.ClassName, false);
    }

    /// <summary>DSG-TP-008: the metadata was reloaded.</summary>
    public void SetCatalog(ComponentCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Build();
        Changed();
    }

    /// <summary>
    /// The active document (null: none, or not a form document); a change disarms the previous one. The palette lists
    /// the components of the catalog the bound form was read with.
    /// </summary>
    public void Bind(FormDesignerViewModel? designer)
    {
        if (ReferenceEquals(designer, _designer))
        {
            return;
        }

        CloseFramePicker(); // DSG-TP-065
        if (_designer is { } old)
        {
            old.Host.Disarm();
            old.ActiveViewChanged -= OnDesignerChanged;
            old.ModelReplaced -= OnDesignerChanged;
            old.Host.ArmedChanged -= OnDesignerChanged;
        }

        _designer = designer;
        if (designer is not null)
        {
            designer.ActiveViewChanged += OnDesignerChanged;
            designer.ModelReplaced += OnDesignerChanged;
            designer.Host.ArmedChanged += OnDesignerChanged;
            FollowCatalog();
        }

        Changed();
    }

    /// <summary>DSG-TP-022: expands or collapses a page (persisted).</summary>
    public void TogglePage(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_collapsed.Remove(name))
        {
            _collapsed.Add(name);
        }

        SaveCollapsed();
    }

    /// <summary><c>toolPalette.expandAll</c> / <c>toolPalette.collapseAll</c>.</summary>
    public void ExpandAll(bool expand)
    {
        _collapsed.Clear();
        if (!expand)
        {
            _collapsed.UnionWith(_all.Select(p => p.Name));
        }

        SaveCollapsed();
    }

    /// <summary>DSG-TP-023: <c>toolPalette.toggleView</c> switches between the list and the icon view (persisted).</summary>
    public void ToggleView() => SetIconView(!IsIconView);

    public void SetIconView(bool icons)
    {
        if (_settings is { } s)
        {
            s.Set(SettingsCatalog.ToolPaletteView, icons ? "icons" : "list");
        }
        else
        {
            _iconView = icons;
        }

        OnPropertyChanged(nameof(IsIconView));
    }

    /// <summary>The keyboard-current row becomes <paramref name="key"/> (a <see cref="PaletteRow.Key"/>).</summary>
    public void SetCurrent(string? key)
    {
        if (_currentKey == key)
        {
            return;
        }

        _currentKey = key;
        OnPropertyChanged(nameof(Current));
        _commands?.InvalidateCanExecute();
    }

    /// <summary>§6 Up / Down: the current row moves across items and page headers (the first row when there is none).</summary>
    public void MoveCurrent(int delta)
    {
        var rows = Rows;
        if (rows.Count == 0)
        {
            return;
        }

        var index = Current is { } current ? rows.ToList().FindIndex(r => r.Key == current.Key) : -1;
        SetCurrent(rows[index < 0 ? 0 : Math.Clamp(index + delta, 0, rows.Count - 1)].Key);
    }

    /// <summary>§6 Enter (<c>toolPalette.placeSelected</c>): a header toggles; an item is placed at the default position.</summary>
    public void ActivateCurrent()
    {
        switch (Current)
        {
            case { IsPage: true } page:
                TogglePage(page.Page.Name);
                break;
            case { Item: { } item }:
                Place(item.ClassName);
                break;
        }
    }

    /// <summary>§6 Space: arms or disarms the current item (single-shot); Shift+Space arms it sticky.</summary>
    public void ArmCurrent(bool sticky)
    {
        if (Current?.Item is { } item)
        {
            Click(item.ClassName, sticky);
        }
    }

    /// <summary>
    /// DSG-TP-033 Enter in the search box: the first match is placed at the default position and the search is cleared;
    /// false without a match.
    /// </summary>
    public bool PlaceFirstMatch()
    {
        if (CurrentItem is not { } item || !AreItemsEnabled)
        {
            return false;
        }

        if (item.IsFrames)
        {
            OpenFramePicker(); // the search stays: the picker opens under the item
            return true;
        }

        Place(item.ClassName);
        SearchText = "";
        return true;
    }

    /// <summary>
    /// §6 Escape: disarms when armed, else clears the search text, else moves focus to the document (UI-A11Y-004).
    /// </summary>
    public void EscapeKey()
    {
        if (IsFramePickerOpen)
        {
            CloseFramePicker();
            return;
        }

        if (Escape())
        {
            return;
        }

        if (SearchText.Length > 0)
        {
            SearchText = "";
            return;
        }

        ReturnFocusToDocument();
    }

    /// <summary>
    /// DSG-TP-040, 041: a click arms the item (single-shot); a click on the armed item disarms it; Shift+click arms it
    /// sticky. Nothing happens while the items are disabled.
    /// </summary>
    public void Click(string className, bool shift = false)
    {
        ArgumentNullException.ThrowIfNull(className);
        if (className == FramesKey)
        {
            OpenFramePicker(); // DSG-TP-063
            return;
        }

        if (!AreItemsEnabled)
        {
            return;
        }

        var host = _designer!.Host;
        if (!shift && Names.Equals(host.ArmedPaletteClass, className))
        {
            host.Disarm();
            return;
        }

        host.Arm(className, shift); // shows Msg.Designer.PlacementArmed while armed (I-294)
    }

    /// <summary>Escape in the palette: disarms; false when nothing was armed.</summary>
    public bool Escape()
    {
        if (ArmedClass is null)
        {
            return false;
        }

        _designer!.Host.Disarm();
        return true;
    }

    /// <summary>
    /// DSG-TP-042, 054: Enter or double-click places the item at the default position without arming it; keyboard focus
    /// moves to the design surface.
    /// </summary>
    public void Place(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        if (className == FramesKey)
        {
            OpenFramePicker(); // DSG-TP-063: Enter and double-click open the picker
            return;
        }

        if (!AreItemsEnabled)
        {
            return;
        }

        _designer!.Host.Disarm();
        _designer.Surface!.PlaceAtDefaultPosition(className);
        ReturnFocusToDocument();
    }

    /// <summary>DSG-TP-060: an item dragged over the surface point (<paramref name="x"/>, <paramref name="y"/>): the ghost follows.</summary>
    public void DragOver(string className, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(className);
        if (AreItemsEnabled && className != FramesKey)
        {
            _designer!.Surface!.PaletteDragOver(className, x, y);
        }
    }

    /// <summary>The drag left the surface (or ended outside it): the ghost goes.</summary>
    public void DragLeave() => _designer?.Surface?.PaletteDragLeave();

    /// <summary>DSG-TP-060: released over the surface point = armed and clicked there; focus moves to the surface.</summary>
    public void Drop(string className, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(className);
        if (className == FramesKey)
        {
            OpenFramePicker(); // DSG-TP-063: dragging the item only opens the picker
            return;
        }

        if (!AreItemsEnabled)
        {
            return;
        }

        _designer!.Host.Disarm(); // armed and clicked: single-shot (DSG-TP-040)
        _designer.Surface!.PaletteDrop(className, x, y);
        ReturnFocusToDocument();
    }

    /// <summary>Registers the <c>toolPalette.*</c> commands (tool-palette.md §9) and builds the context menu (§6).</summary>
    public void RegisterCommands(ICommandService commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands;
        commands.RegisterHandler("toolPalette.focusSearch", new Handler(() => HasForm, () => SearchFocusRequested?.Invoke(this, EventArgs.Empty)));
        commands.RegisterHandler("toolPalette.placeSelected", new Handler(
            () => Current is { IsPage: true } || (Current is { Item: not null } && AreItemsEnabled), ActivateCurrent));
        commands.RegisterHandler("toolPalette.toggleView", new Handler(() => true, ToggleView));
        commands.RegisterHandler("toolPalette.expandAll", new Handler(() => _all.Count > 0, () => ExpandAll(true)));
        commands.RegisterHandler("toolPalette.collapseAll", new Handler(() => _all.Count > 0, () => ExpandAll(false)));
        ContextMenu = () =>
        [
            MenuItemModel.ForCommand(Strings.Menu_toolPalette_placeSelected, commands.Get("toolPalette.placeSelected"), null, MenuToggleKind.None),
            MenuItemModel.Separator(),
            MenuItemModel.ForAction("toolPalette.viewAsList", Strings.Menu_toolPalette_viewAsList, () => SetIconView(false), !IsIconView),
            MenuItemModel.ForAction("toolPalette.viewAsIcons", Strings.Menu_toolPalette_viewAsIcons, () => SetIconView(true), IsIconView),
            MenuItemModel.Separator(),
            MenuItemModel.ForCommand(Strings.Menu_toolPalette_expandAll, commands.Get("toolPalette.expandAll"), null, MenuToggleKind.None),
            MenuItemModel.ForCommand(Strings.Menu_toolPalette_collapseAll, commands.Get("toolPalette.collapseAll"), null, MenuToggleKind.None),
        ];
    }

    private void SaveCollapsed()
    {
        _settings?.Set(SettingsCatalog.ToolPaletteCollapsedPages, (IReadOnlyList<string>)[.. _collapsed.Order(StringComparer.Ordinal)]);
        Changed();
    }

    private static bool StartsWith(PaletteItem item, string text) =>
        item.ClassName.StartsWith(text, StringComparison.OrdinalIgnoreCase) || item.DisplayName.StartsWith(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>The pages with the <c>Frames…</c> item first on page <see cref="FramesPage"/> when it is shown (DSG-TP-061).</summary>
    private IEnumerable<PalettePage> AllPages()
    {
        if (!ShowsFrames)
        {
            return _all;
        }

        var frames = new PaletteItem(FramesKey, Strings.Palette_FramesItem, "", null) { IsFrames = true };
        return _all.Exists(p => Names.Equals(p.Name, FramesPage))
            ? _all.Select(p => Names.Equals(p.Name, FramesPage) ? p with { Items = [frames, .. p.Items] } : p)
            : [new PalettePage(FramesPage, true, [frames]), .. _all];
    }

    /// <summary>
    /// DSG-TP-020, 021: the components with a palette entry that are not abstract and not forms; the pages named by the
    /// index first, in its order, then the others by name; items by <c>order</c>, ties by class name.
    /// </summary>
    private void Build()
    {
        var items = _catalog.All.Where(c => c.Palette is not null && !c.IsAbstract && c.Kind != ComponentKind.Form).ToList();
        var pageNames = _catalog.PalettePages.Where(p => items.Exists(i => Names.Equals(i.Palette!.Page, p)))
            .Concat(items.Select(i => i.Palette!.Page).Where(p => !_catalog.PalettePages.Contains(p, Names)).Distinct(Names).Order(StringComparer.Ordinal))
            .ToList();
        _all = [.. pageNames.Select(page => new PalettePage(page, true, [.. items
            .Where(i => Names.Equals(i.Palette!.Page, page))
            .OrderBy(i => i.Palette!.Order)
            .ThenBy(i => i.ClassName, StringComparer.Ordinal)
            .Select(i => new PaletteItem(i.ClassName, i.ClassName is ['T', _, ..] ? i.ClassName[1..] : i.ClassName, i.Unit, i.Palette!.Icon))]))];
    }

    /// <summary>The bound form was read with another catalog (a reload, or a test's own metadata): the palette follows it.</summary>
    private void FollowCatalog()
    {
        if (_designer is { } designer && !ReferenceEquals(designer.Catalog, _catalog))
        {
            _catalog = designer.Catalog;
            Build();
        }
    }

    private void OnDesignerChanged(object? sender, EventArgs e)
    {
        if (sender is FormDesignerViewModel)
        {
            CloseFramePicker(); // DSG-TP-065: another view, or the model replaced
        }

        FollowCatalog();
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Pages));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(IsFilterEmpty));
        OnPropertyChanged(nameof(FilterEmptyText));
        OnPropertyChanged(nameof(HasForm));
        OnPropertyChanged(nameof(AreItemsEnabled));
        OnPropertyChanged(nameof(AreFramesEnabled));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(ArmedClass));
        OnPropertyChanged(nameof(IsSticky));
        _commands?.InvalidateCanExecute();
    }

    private sealed class Handler(Func<bool> canExecute, Action execute) : ICommandHandler
    {
        public bool CanExecute(string? argument) => canExecute();

        public Task ExecuteAsync(string? argument)
        {
            execute();
            return Task.CompletedTask;
        }

        public bool IsChecked(string? argument) => false;
    }
}
