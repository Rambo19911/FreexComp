using System.Collections.Immutable;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Inspector;

/// <summary><c>objectInspector.arrangement</c> (DSG-OI-010, 011).</summary>
public enum PropertyArrangement { Category, Name }

/// <summary>A category header or a property row.</summary>
public enum PropertyRowKind { Category, Property }

/// <summary>Where a property row comes from.</summary>
public enum PropertyRowSource
{
    /// <summary>A metadata property, a sub-property of an object property, or a set member (<see cref="PropertyRow.SetMember"/>).</summary>
    Metadata,

    /// <summary>The pseudo-property <c>Name</c> (DSG-OI-004).</summary>
    Name,

    /// <summary>The read-only pseudo-property <c>ClassName</c> of a preserved component (DSG-OI-005).</summary>
    ClassName,

    /// <summary>A stored property the metadata does not know, value = raw text (DSG-OI-005, 006).</summary>
    Unknown,
}

/// <summary>
/// One visible row of the grid. <see cref="Path"/> is the property path (<c>Font.Size</c>, <c>Anchors.akLeft</c>) or, for a
/// category row, the category name; <see cref="Name"/> its last segment. <see cref="Display"/> is blank when the selected
/// components disagree (<see cref="IsMixed"/>, DSG-OI-091). <see cref="Key"/> identifies the row (UI-TW-304).
/// </summary>
public sealed record PropertyRow(
    PropertyRowKind Kind,
    PropertyRowSource Source,
    string Path,
    string Name,
    string Category,
    int Depth,
    PropertyInfo? Info,
    string? SetMember,
    string Display,
    bool IsMixed,
    bool IsBold,
    bool IsReadOnly,
    bool IsExpandable,
    bool IsExpanded)
{
    /// <summary>The key prefix of a category row (UI-TW-304).</summary>
    public const string CategoryKeyPrefix = "$cat.";

    /// <summary>UI-TW-304: the property path; <c>$cat.&lt;Category&gt;</c> for a category row (never equal to a property path).</summary>
    public string Key => Kind == PropertyRowKind.Category ? CategoryKeyPrefix + Category : Path;
}

/// <summary>
/// The rows of the Object Inspector's Properties page (object-inspector.md §1–§3, §6, §9): built from the component
/// metadata of the selection, arranged by category or by name, with expandable object/set rows, the path filter, the
/// multi-selection intersection and the bold-when-stored rule. Pure model: <see cref="Rows"/> is recomputed from the
/// form on every read, so a commit is reflected at once (DSG-OI-094).
/// </summary>
public sealed class PropertyGridModel(ComponentCatalog catalog)
{
    /// <summary>The fixed category order (DSG-OI-010); <c>Unknown</c> comes last (DSG-OI-006).</summary>
    public static IReadOnlyList<string> CategoryOrder { get; } = ["Action", "Appearance", "Behavior", "Data", "Layout", "Input", "Misc", UnknownCategory];

    public const string UnknownCategory = "Unknown";

    private readonly ValueConverter _converter = new(catalog);
    private IReadOnlyList<FormObject> _selection = [];

    /// <summary>Selection order; the last element is the primary (as <c>Selection.Items</c>).</summary>
    public IReadOnlyList<FormObject> Selection => _selection;

    public PropertyArrangement Arrangement { get; set; } = PropertyArrangement.Category;

    /// <summary>Case-insensitive substring of the property path (DSG-OI-020); blank = no filter.</summary>
    public string Filter { get; set; } = "";

    /// <summary>Collapsed categories (DSG-OI-013); the view-model may share one set between grids.</summary>
    public ISet<string> CollapsedCategories { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths of expanded object/set rows (DSG-OI-013); the view-model swaps the set per class name.</summary>
    public ISet<string> ExpandedPaths { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// DSG-OI-113: the grid of a collection item — no <c>Name</c> row, no categories, rows in declaration order (the
    /// arrangement is ignored), nested collection rows read-only, no geometry exception of the bold rule.
    /// </summary>
    public bool ItemMode { get; set; }

    /// <summary>
    /// DSG-OI-095: the preview bounds of a control during a drag on the surface (parent-relative); null when there is no
    /// preview. The geometry rows <c>Left</c>, <c>Top</c>, <c>Width</c> and <c>Height</c> show them instead of the model.
    /// </summary>
    public Func<FormObject, Rect?>? PreviewBounds { get; set; }

    /// <summary>A filter is set and no property row matches it (<c>Msg.Inspector.FilterEmpty</c>).</summary>
    public bool IsFilterEmpty => FilterText.Length > 0 && Rows.Count == 0;

    public void SetSelection(IReadOnlyList<FormObject> selection) => _selection = [.. selection];

    /// <summary>The visible rows in display order.</summary>
    public IReadOnlyList<PropertyRow> Rows => Build();

    private string FilterText => Filter.Trim();

    private bool IsMulti => _selection.Count > 1;

    private List<PropertyRow> Build()
    {
        var rows = new List<PropertyRow>();
        if (_selection.Count == 0)
        {
            return rows;
        }

        var nodes = Common();
        var filter = FilterText;
        if (ItemMode)
        {
            foreach (var node in nodes)
            {
                Emit(node, 0, filter, rows);
            }

            return rows;
        }

        if (Arrangement == PropertyArrangement.Name)
        {
            foreach (var node in nodes.Where(n => n.Source != PropertyRowSource.Unknown).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .Concat(nodes.Where(n => n.Source == PropertyRowSource.Unknown)))
            {
                Emit(node, 0, filter, rows);
            }

            return rows;
        }

        foreach (var category in CategoryOrder)
        {
            var members = nodes.Where(n => string.Equals(n.Category, category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var visible = members.Where(n => Visible(n, filter)).ToList();
            if (visible.Count == 0)
            {
                continue;
            }

            var expanded = filter.Length > 0 || !CollapsedCategories.Contains(category);
            rows.Add(new PropertyRow(PropertyRowKind.Category, PropertyRowSource.Metadata, category, category, category, 0, null, null, "",
                false, false, true, true, expanded));
            if (expanded)
            {
                foreach (var node in visible)
                {
                    Emit(node, 0, filter, rows);
                }
            }
        }

        return rows;
    }

    private void Emit(Node node, int depth, string filter, List<PropertyRow> rows)
    {
        if (!Visible(node, filter))
        {
            return;
        }

        var expandable = node.Children.Count > 0;
        var expanded = expandable && (filter.Length > 0 ? node.Children.Any(c => Visible(c, filter)) : ExpandedPaths.Contains(node.Path));
        var displays = _selection.Select(o => Display(o, node)).ToList();
        var mixed = displays.Distinct(StringComparer.Ordinal).Count() > 1;
        rows.Add(new PropertyRow(PropertyRowKind.Property, node.Source, node.Path, node.Name, node.Category, depth, node.Info, node.SetMember,
            mixed || (IsMulti && node.Source == PropertyRowSource.Name) ? "" : displays[0], mixed,
            _selection.All(o => IsBold(o, node)), IsReadOnly(node), expandable, expanded));
        if (expanded)
        {
            foreach (var child in node.Children)
            {
                Emit(child, depth + 1, filter, rows);
            }
        }
    }

    /// <summary>DSG-OI-020: shown when the path matches or a sub-row matches.</summary>
    private static bool Visible(Node node, string filter) =>
        filter.Length == 0 || node.Path.Contains(filter, StringComparison.OrdinalIgnoreCase) || node.Children.Any(c => Visible(c, filter));

    /// <summary>DSG-OI-090: the primary's rows that every other selected component has with the same name, type and kind.</summary>
    private List<Node> Common()
    {
        var primary = Nodes(_selection[^1]);
        if (!IsMulti)
        {
            return primary;
        }

        var others = _selection.Take(_selection.Count - 1).Select(Nodes).ToList();
        return [.. primary.Where(n => n.Source != PropertyRowSource.Unknown && others.All(list => list.Any(o => SameShape(n, o))))];

        static bool SameShape(Node a, Node b) =>
            a.Source == b.Source && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            && (a.Info is null || (b.Info is not null && a.Info.Kind == b.Info.Kind
                && string.Equals(a.Info.Type, b.Info.Type, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The top-level rows of one component (DSG-OI-004 … 006).</summary>
    private List<Node> Nodes(FormObject obj)
    {
        var nodes = ItemMode ? [] : new List<Node> { new("Name", "Name", "Misc", null, null, PropertyRowSource.Name, []) };
        if (obj.IsPreserved || obj.Info is not { } info)
        {
            nodes.Add(new("ClassName", "ClassName", "Misc", null, null, PropertyRowSource.ClassName, []));
            nodes.AddRange(obj.Properties.Select(p => new Node(p.Path, p.Path, "Misc", null, null, PropertyRowSource.Unknown, [])));
            return nodes;
        }

        var isRoot = obj.Parent is null && !ItemMode && !PropertyAccess.IsFrameRoot(obj); // a frame root shows Width/Height (DSG-FD-117)
        foreach (var property in info.Properties)
        {
            if (property.Hidden || (isRoot && property.Name is ("Width" or "Height")))
            {
                continue;
            }

            nodes.Add(MetadataNode(property, property.Name));
        }

        nodes.AddRange(obj.Properties.Where(p => p.Info is null && !p.IsEvent)
            .Select(p => new Node(p.Path, p.Path, UnknownCategory, null, null, PropertyRowSource.Unknown, [])));
        return nodes;
    }

    private Node MetadataNode(PropertyInfo property, string path)
    {
        var name = path[(path.LastIndexOf('.') + 1)..];
        List<Node> children = property.Kind switch
        {
            PropertyKind.Object => [.. catalog.SubProperties(property).Where(p => !p.Hidden).Select(p => MetadataNode(p, path + "." + p.Name))],
            PropertyKind.Set => [.. SetMembers(property).Select(m => new Node(path + "." + m, m, property.Category, property, m, PropertyRowSource.Metadata, []))],
            _ => [],
        };
        return new Node(path, name, property.Category, property, null, PropertyRowSource.Metadata, children);
    }

    private ImmutableArray<string> SetMembers(PropertyInfo set) =>
        catalog.FindType(set.Type) is { Kind: TypeKind.Set, ElementType: { } element } && catalog.FindType(element) is { } members
            ? members.Values
            : [];

    /// <summary>DSG-OI-036, 072: metadata <c>readOnly</c>, pseudo <c>ClassName</c>, raw values, <c>Name</c> in a multi-selection.</summary>
    private bool IsReadOnly(Node node) => node.Source switch
    {
        PropertyRowSource.Metadata => node.Info!.ReadOnly || (ItemMode && node.Info.Kind == PropertyKind.Collection),
        PropertyRowSource.Name => IsMulti,
        _ => true,
    };

    private string Display(FormObject obj, Node node)
    {
        switch (node.Source)
        {
            case PropertyRowSource.Name:
                return obj.Name;
            case PropertyRowSource.ClassName:
                return obj.TypeName;
            case PropertyRowSource.Unknown:
                return obj.FindProperty(node.Path) is { } raw ? raw.RawText ?? raw.Value.ToString() ?? "" : "";
        }

        var info = node.Info!;
        if (node.SetMember is { } member)
        {
            return Contains(Effective(obj, ParentPath(node.Path), info), member) ? "True" : "False";
        }

        return info.Kind == PropertyKind.Object ? _converter.Format(info, null) : _converter.Format(info, Effective(obj, node.Path, info));
    }

    /// <summary>DSG-OI-060, 050, 052.</summary>
    private bool IsBold(FormObject obj, Node node)
    {
        switch (node.Source)
        {
            case PropertyRowSource.Name or PropertyRowSource.ClassName:
                return false;
            case PropertyRowSource.Unknown:
                return obj.FindProperty(node.Path) is not null;
        }

        var info = node.Info!;
        if (node.SetMember is { } member)
        {
            var parent = ParentPath(node.Path);
            return Contains(Effective(obj, parent, info), member) != Contains(PropertyAccess.DefaultValue(catalog, info), member);
        }

        if (info.Kind == PropertyKind.Object)
        {
            var prefix = node.Path + ".";
            return obj.Properties.Any(p => p.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        if (!ItemMode && AlwaysWrittenReference(obj, node.Path) is { } reference)
        {
            return Effective(obj, node.Path, info) is not IntegerValue { Value: var v } || v != reference;
        }

        return PropertyAccess.IsStored(obj, node.Path);
    }

    /// <summary>
    /// DSG-OI-060: the always-written geometry (DSG-FD-044) is bold only when it differs from Left/Top 0 and the class's
    /// <c>defaultSize</c> (for the root: <c>ClientWidth</c>/<c>ClientHeight</c>). Null for every other path.
    /// </summary>
    private static long? AlwaysWrittenReference(FormObject obj, string path)
    {
        var size = obj.Info?.DefaultSize ?? default;
        var isRoot = obj.Parent is null && !PropertyAccess.IsFrameRoot(obj); // DSG-FD-117: a frame root has bounds
        return path.ToLowerInvariant() switch
        {
            "left" or "top" => 0,
            "width" when !isRoot => size.Width,
            "height" when !isRoot => size.Height,
            "clientwidth" when isRoot => size.Width,
            "clientheight" when isRoot => size.Height,
            _ => null,
        };
    }

    private FormValue Effective(FormObject obj, string path, PropertyInfo info)
    {
        if (obj.Parent is not null && PreviewBounds?.Invoke(obj) is { } b)
        {
            int? preview = path.ToUpperInvariant() switch
            {
                "LEFT" => b.Left,
                "TOP" => b.Top,
                "WIDTH" => b.Width,
                "HEIGHT" => b.Height,
                _ => null,
            };
            if (preview is { } value)
            {
                return new IntegerValue(value, false);
            }
        }

        return obj.FindProperty(path)?.Value ?? PropertyAccess.DefaultValue(catalog, info);
    }

    private static bool Contains(FormValue set, string member) =>
        set is SetValue s && s.Elements.Contains(member, StringComparer.OrdinalIgnoreCase);

    private static string ParentPath(string path) => path[..path.LastIndexOf('.')];

    private sealed record Node(string Path, string Name, string Category, PropertyInfo? Info, string? SetMember, PropertyRowSource Source,
        IReadOnlyList<Node> Children);
}
