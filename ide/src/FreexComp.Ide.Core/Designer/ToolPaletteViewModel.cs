using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FreexComp.Designer.Catalog;
using FreexComp.Ide.Core.Resources;

namespace FreexComp.Ide.Core.Designer;

/// <summary>One component of the Tool Palette: its class and the name shown (the class without the leading <c>T</c>).</summary>
public sealed record PaletteItem(string ClassName, string DisplayName, string Unit, string? Icon);

/// <summary>A page of the Tool Palette as shown (DSG-TP-020 … 022): expanded or collapsed, with its visible items.</summary>
public sealed record PalettePage(string Name, bool IsExpanded, IReadOnlyList<PaletteItem> Items);

/// <summary>
/// The Tool Palette (tool-palette.md §3–§5): the catalog's palette pages and items in their order, the search box, arming
/// an item for placement on the active form document's Design view (single-shot or sticky) and the enablement rules.
/// No Avalonia types.
/// </summary>
public sealed class ToolPaletteViewModel : ObservableObject
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly HashSet<string> _collapsed = new(Names);
    private ComponentCatalog _catalog;
    private List<PalettePage> _all = [];
    private FormDesignerViewModel? _designer;
    private string _searchText = "";

    public ToolPaletteViewModel(ComponentCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Build();
    }

    /// <summary>The pages shown: all pages, or with a search text the pages with matches (expanded), DSG-TP-031.</summary>
    public IReadOnlyList<PalettePage> Pages
    {
        get
        {
            var words = Words;
            if (words.Length == 0)
            {
                return [.. _all.Select(p => p with { IsExpanded = !_collapsed.Contains(p.Name) })];
            }

            var pages = new List<PalettePage>();
            foreach (var page in _all)
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

    /// <summary>The collapsed pages (DSG-TP-022; persisted by the view in the layout).</summary>
    public IReadOnlySet<string> CollapsedPages => _collapsed;

    /// <summary>The search box (DSG-TP-030): words separated by spaces, each a substring of the class name.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? ""))
            {
                Changed();
            }
        }
    }

    /// <summary>The item Enter places: with a search text the first match (DSG-TP-032); null otherwise.</summary>
    public PaletteItem? CurrentItem => Words.Length == 0 ? null : Pages.SelectMany(p => p.Items).FirstOrDefault();

    /// <summary>A search text matches nothing: <see cref="FilterEmptyText"/> is shown.</summary>
    public bool IsFilterEmpty => Words.Length > 0 && Pages.Count == 0;

    public string FilterEmptyText => string.Format(CultureInfo.CurrentCulture, Strings.Msg_Palette_FilterEmpty, SearchText.Trim());

    /// <summary>DSG-TP-024: a form document shows its Design view.</summary>
    public bool HasForm => _designer is { IsDesignActive: true };

    /// <summary>DSG-TP-024, 055: the items can be armed and placed (the form is not read-only).</summary>
    public bool AreItemsEnabled => HasForm && !_designer!.Surface!.IsReadOnly;

    /// <summary>The palette as a whole is enabled.</summary>
    public bool IsEnabled => AreItemsEnabled;

    /// <summary>The text of the disabled palette (<c>Msg.Palette.NoForm</c>).</summary>
    public string NoFormText => Strings.Msg_Palette_NoForm;

    /// <summary>The armed class (DSG-TP-040), or null.</summary>
    public string? ArmedClass => _designer?.Host.ArmedPaletteClass;

    /// <summary>The armed item stays armed after each placement (DSG-TP-041).</summary>
    public bool IsSticky => _designer?.Host.ArmedSticky == true;

    private string[] Words => _searchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>DSG-TP-008: the metadata was reloaded.</summary>
    public void SetCatalog(ComponentCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Build();
        Changed();
    }

    /// <summary>The active document (null: none, or not a form document); a change disarms the previous one.</summary>
    public void Bind(FormDesignerViewModel? designer)
    {
        if (ReferenceEquals(designer, _designer))
        {
            return;
        }

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
        }

        Changed();
    }

    /// <summary>DSG-TP-022: expands or collapses a page.</summary>
    public void TogglePage(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_collapsed.Remove(name))
        {
            _collapsed.Add(name);
        }

        Changed();
    }

    /// <summary>
    /// DSG-TP-040, 041: a click arms the item (single-shot); a click on the armed item disarms it; Shift+click arms it
    /// sticky. Nothing happens while the items are disabled.
    /// </summary>
    public void Click(string className, bool shift = false)
    {
        ArgumentNullException.ThrowIfNull(className);
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

        host.Arm(className, shift);
        host.SetStatus("Msg.Designer.PlacementArmed", className);
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

    /// <summary>DSG-TP-042: Enter or double-click places the item at the default position without arming it.</summary>
    public void Place(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        if (!AreItemsEnabled)
        {
            return;
        }

        _designer!.Host.Disarm();
        _designer.Surface!.PlaceAtDefaultPosition(className);
    }

    private static bool StartsWith(PaletteItem item, string text) =>
        item.ClassName.StartsWith(text, StringComparison.OrdinalIgnoreCase) || item.DisplayName.StartsWith(text, StringComparison.OrdinalIgnoreCase);

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

    private void OnDesignerChanged(object? sender, EventArgs e) => Changed();

    private void Changed()
    {
        OnPropertyChanged(nameof(Pages));
        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(IsFilterEmpty));
        OnPropertyChanged(nameof(FilterEmptyText));
        OnPropertyChanged(nameof(HasForm));
        OnPropertyChanged(nameof(AreItemsEnabled));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(ArmedClass));
        OnPropertyChanged(nameof(IsSticky));
    }
}
