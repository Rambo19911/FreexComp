using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Layout;

/// <summary>
/// UIF-300 step 1 for the C# engine: builds the <see cref="LayoutNode"/> tree from the stored properties of a form
/// (layout-engine.md §3), the effective value of each input being the stored value or the metadata default. Bounds
/// pass through the constraints, not yet through the intrinsic rules (they apply once in <c>Loaded</c>, UIF-250).
/// </summary>
internal sealed class LayoutReader
{
    private readonly ComponentCatalog _catalog;
    private readonly Dictionary<ComponentInfo, LayoutNode> _templates = new(ReferenceEqualityComparer.Instance);

    public LayoutReader(ComponentCatalog catalog) => _catalog = catalog;

    /// <summary>The tree of <paramref name="form"/>; <paramref name="overrides"/> replace stored values (previews).</summary>
    public (LayoutNode Root, Dictionary<FormObject, LayoutNode> Nodes) Read(FormDocument form,
        IReadOnlyDictionary<string, LayoutOverride>? overrides = null)
    {
        var nodes = new Dictionary<FormObject, LayoutNode>(ReferenceEqualityComparer.Instance);
        var root = new LayoutNode(form.Root) { Kind = NodeKind.Form, IsWinControl = true, OwnFont = true };
        if (form.Root.Info is { } rootInfo && !form.Root.IsPreserved)
        {
            root.FontHeight = Template(rootInfo).FontHeight;
            ApplyFont(root, form.Root);
        }

        var client = PropertyAccess.GetBounds(form.Root);
        (root.Width, root.Height) = (Math.Max(0, client.Width), Math.Max(0, client.Height));
        root.Loading = true;
        nodes[form.Root] = root;
        foreach (var child in form.Root.Children)
        {
            ReadTree(child, root, nodes, overrides);
        }

        if (overrides is not null)
        {
            Reparent(form, nodes, overrides);
        }

        return (root, nodes);
    }

    private void ReadTree(FormObject obj, LayoutNode parent, Dictionary<FormObject, LayoutNode> nodes,
        IReadOnlyDictionary<string, LayoutOverride>? overrides)
    {
        var node = ReadNode(obj, overrides is not null && overrides.TryGetValue(obj.Name, out var o) ? o : null);
        if (node is null)
        {
            return; // non-visual or unplaceable: neither it nor its subtree takes part (DSG-LAY-030)
        }

        node.Parent = parent;
        parent.Children.Add(node);
        nodes[obj] = node;
        foreach (var child in obj.Children)
        {
            ReadTree(child, node, nodes, overrides);
        }
    }

    private static void Reparent(FormDocument form, Dictionary<FormObject, LayoutNode> nodes,
        IReadOnlyDictionary<string, LayoutOverride> overrides)
    {
        foreach (var (name, change) in overrides)
        {
            if (change.ParentName is not { } parentName || form.FindByName(name) is not { } obj || !nodes.TryGetValue(obj, out var node))
            {
                continue;
            }

            if (form.FindByName(parentName) is not { } parentObj || !nodes.TryGetValue(parentObj, out var newParent))
            {
                throw new ArgumentException($"Override of {name}: no placeable parent {parentName}", nameof(overrides));
            }

            for (var p = newParent; p is not null; p = p.Parent)
            {
                if (ReferenceEquals(p, node))
                {
                    throw new ArgumentException($"Override of {name}: {parentName} is inside it", nameof(overrides));
                }
            }

            node.Parent!.Children.Remove(node);
            node.Parent = newParent;
            newParent.Children.Add(node); // the dropped control becomes the last child (creation order)
        }
    }

    private LayoutNode? ReadNode(FormObject obj, LayoutOverride? change)
    {
        LayoutNode node;
        if (obj.IsPreserved || obj.Info is null)
        {
            // DSG-LAY-030: preserved objects with readable bounds take part as plain alNone controls.
            if (!TryStoredInt(obj, "Left", out var l) || !TryStoredInt(obj, "Top", out var t)
                || !TryStoredInt(obj, "Width", out var w) || !TryStoredInt(obj, "Height", out var h))
            {
                return change?.Bounds is null ? null : new LayoutNode(obj) { IsWinControl = true, Loading = true };
            }

            node = new LayoutNode(obj) { IsWinControl = true, Left = l, Top = t, Width = w, Height = h };
        }
        else
        {
            if (obj.Info.Kind is ComponentKind.NonVisual or ComponentKind.Form)
            {
                return null;
            }

            node = Copy(Template(obj.Info), obj);
            foreach (var property in obj.Properties)
            {
                Apply(node, property);
            }

            ApplyFont(node, obj);
        }

        if (change?.Bounds is { } bounds)
        {
            (node.Left, node.Top, node.Width, node.Height) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }

        if (change?.Align is { } align)
        {
            node.Align = ParseAlign(align);
        }

        // Read-time SetBounds: width/height ≥ 0, then constraints (no intrinsic rule while loading).
        node.Width = Math.Max(0, node.Width);
        node.Height = Math.Max(0, node.Height);
        if (node.MaxWidth > 0 && node.Width > node.MaxWidth) node.Width = node.MaxWidth;
        if (node.MinWidth > 0 && node.Width < node.MinWidth) node.Width = node.MinWidth;
        if (node.MaxHeight > 0 && node.Height > node.MaxHeight) node.Height = node.MaxHeight;
        if (node.MinHeight > 0 && node.Height < node.MinHeight) node.Height = node.MinHeight;
        node.Loading = true;
        return node;
    }

    /// <summary>The defaults of a class: metadata defaults, else the fallbacks of layout-engine.md §3.</summary>
    private LayoutNode Template(ComponentInfo info)
    {
        if (_templates.TryGetValue(info, out var cached))
        {
            return cached;
        }

        var t = new LayoutNode(null!)
        {
            IsWinControl = info.Kind is ComponentKind.WinControl or ComponentKind.Container or ComponentKind.Form,
            Width = info.DefaultSize.Width,
            Height = info.DefaultSize.Height,
            Kind = Is(info, "TCustomPanel") ? NodeKind.Panel
                : Is(info, "TCustomGroupBox") ? NodeKind.GroupBox
                : Is(info, "TScrollBox") ? NodeKind.ScrollBox
                : Is(info, "TCustomGrid") ? NodeKind.Grid
                : Is(info, "TPageControl") ? NodeKind.PageControl
                : Is(info, "TTabSheet") ? NodeKind.TabSheet
                : Is(info, "TToolBar") ? NodeKind.ToolBar
                : Is(info, "TToolButton") ? NodeKind.ToolButton
                : NodeKind.Generic,
            Intrinsic = Is(info, "TCustomComboBox") ? IntrinsicRule.ComboBox
                : Is(info, "TCustomEdit") && !Is(info, "TCustomMemo") ? IntrinsicRule.Edit
                : IntrinsicRule.None,
        };
        t.Left = Int(info, "Left", 0);
        t.Top = Int(info, "Top", 0);
        t.Align = ParseAlign(Ident(info, "Align", "alNone"));
        SetAnchors(t, Default(info, "Anchors") is SetValue anchors ? anchors.Elements : ["akLeft", "akTop"]);
        t.Visible = Bool(info, "Visible", true);
        t.AlignWithMargins = Bool(info, "AlignWithMargins", false);
        t.MarginLeft = Int(info, "Margins.Left", 3);
        t.MarginTop = Int(info, "Margins.Top", 3);
        t.MarginRight = Int(info, "Margins.Right", 3);
        t.MarginBottom = Int(info, "Margins.Bottom", 3);
        t.MinWidth = Int(info, "Constraints.MinWidth", 0);
        t.MinHeight = Int(info, "Constraints.MinHeight", 0);
        t.MaxWidth = Int(info, "Constraints.MaxWidth", 0);
        t.MaxHeight = Int(info, "Constraints.MaxHeight", 0);
        t.PaddingLeft = Int(info, "Padding.Left", 0);
        t.PaddingTop = Int(info, "Padding.Top", 0);
        t.PaddingRight = Int(info, "Padding.Right", 0);
        t.PaddingBottom = Int(info, "Padding.Bottom", 0);
        t.OwnFont = !Bool(info, "ParentFont", true);
        t.FontHeight = Int(info, "Font.Height", -12);
        t.AutoSize = Bool(info, "AutoSize", true);
        t.BorderSingle = IsName(Ident(info, "BorderStyle", t.Kind == NodeKind.Panel ? "bsNone" : "bsSingle"), "bsSingle");
        t.ComboSimple = IsName(Ident(info, "Style", "csDropDown"), "csSimple");
        t.BorderWidth = Int(info, "BorderWidth", 0);
        t.BevelWidth = Int(info, "BevelWidth", 1);
        t.BevelOuter = !IsName(Ident(info, "BevelOuter", t.Kind == NodeKind.Panel ? "bvRaised" : "bvNone"), "bvNone");
        t.BevelInner = !IsName(Ident(info, "BevelInner", "bvNone"), "bvNone");
        t.TabHeight = Int(info, "TabHeight", 0);
        t.TabVisible = Bool(info, "TabVisible", true);
        t.ButtonWidth = Int(info, "ButtonWidth", 23);
        t.ButtonHeight = Int(info, "ButtonHeight", 22);
        t.Indent = Int(info, "Indent", 0);
        t.Spacer = IsSpacer(Ident(info, "Style", "tbsButton"));
        t.AutoScroll = Bool(info, "AutoScroll", true);
        t.HorzBarVisible = Bool(info, "HorzScrollBar.Visible", true);
        t.VertBarVisible = Bool(info, "VertScrollBar.Visible", true);
        t.HorzRange = Int(info, "HorzScrollBar.Range", 0);
        t.VertRange = Int(info, "VertScrollBar.Range", 0);
        _templates[info] = t;
        return t;
    }

    private static LayoutNode Copy(LayoutNode t, FormObject obj) => new(obj)
    {
        Kind = t.Kind, Intrinsic = t.Intrinsic, IsWinControl = t.IsWinControl,
        Left = t.Left, Top = t.Top, Width = t.Width, Height = t.Height,
        Align = t.Align, AkLeft = t.AkLeft, AkTop = t.AkTop, AkRight = t.AkRight, AkBottom = t.AkBottom,
        Visible = t.Visible, AlignWithMargins = t.AlignWithMargins,
        MarginLeft = t.MarginLeft, MarginTop = t.MarginTop, MarginRight = t.MarginRight, MarginBottom = t.MarginBottom,
        MinWidth = t.MinWidth, MinHeight = t.MinHeight, MaxWidth = t.MaxWidth, MaxHeight = t.MaxHeight,
        PaddingLeft = t.PaddingLeft, PaddingTop = t.PaddingTop, PaddingRight = t.PaddingRight, PaddingBottom = t.PaddingBottom,
        OwnFont = t.OwnFont, FontHeight = t.FontHeight, AutoSize = t.AutoSize, BorderSingle = t.BorderSingle,
        ComboSimple = t.ComboSimple, BorderWidth = t.BorderWidth, BevelWidth = t.BevelWidth, BevelOuter = t.BevelOuter,
        BevelInner = t.BevelInner, TabHeight = t.TabHeight, TabVisible = t.TabVisible, ButtonWidth = t.ButtonWidth,
        ButtonHeight = t.ButtonHeight, Indent = t.Indent, Spacer = t.Spacer, AutoScroll = t.AutoScroll,
        HorzBarVisible = t.HorzBarVisible, VertBarVisible = t.VertBarVisible, HorzRange = t.HorzRange, VertRange = t.VertRange,
    };

    /// <summary>One stored property line; values of the wrong shape keep the default (like <see cref="PropertyAccess"/>).</summary>
    private static void Apply(LayoutNode n, FormProperty p)
    {
        var v = p.Value;
        switch (p.Path.ToLowerInvariant())
        {
            case "left": SetInt(v, ref n.Left); break;
            case "top": SetInt(v, ref n.Top); break;
            case "width": SetInt(v, ref n.Width); break;
            case "height": SetInt(v, ref n.Height); break;
            case "align" when v is IdentValue a: n.Align = ParseAlign(a.Name); break;
            case "anchors" when v is SetValue s: SetAnchors(n, s.Elements); break;
            case "visible": SetBool(v, ref n.Visible); break;
            case "alignwithmargins": SetBool(v, ref n.AlignWithMargins); break;
            case "margins.left": SetInt(v, ref n.MarginLeft); break;
            case "margins.top": SetInt(v, ref n.MarginTop); break;
            case "margins.right": SetInt(v, ref n.MarginRight); break;
            case "margins.bottom": SetInt(v, ref n.MarginBottom); break;
            case "constraints.minwidth": SetInt(v, ref n.MinWidth); break;
            case "constraints.minheight": SetInt(v, ref n.MinHeight); break;
            case "constraints.maxwidth": SetInt(v, ref n.MaxWidth); break;
            case "constraints.maxheight": SetInt(v, ref n.MaxHeight); break;
            case "padding.left": SetInt(v, ref n.PaddingLeft); break;
            case "padding.top": SetInt(v, ref n.PaddingTop); break;
            case "padding.right": SetInt(v, ref n.PaddingRight); break;
            case "padding.bottom": SetInt(v, ref n.PaddingBottom); break;
            case "autosize": SetBool(v, ref n.AutoSize); break;
            case "borderstyle" when v is IdentValue b: n.BorderSingle = IsName(b.Name, "bsSingle"); break;
            case "style" when v is IdentValue s:
                n.ComboSimple = IsName(s.Name, "csSimple");
                n.Spacer = IsSpacer(s.Name);
                break;
            case "borderwidth": SetInt(v, ref n.BorderWidth); break;
            case "bevelwidth": SetInt(v, ref n.BevelWidth); break;
            case "bevelouter" when v is IdentValue b: n.BevelOuter = !IsName(b.Name, "bvNone"); break;
            case "bevelinner" when v is IdentValue b: n.BevelInner = !IsName(b.Name, "bvNone"); break;
            case "tabheight": SetInt(v, ref n.TabHeight); break;
            case "activepage" when v is IdentValue a: n.ActivePageName = a.Name; break;
            case "tabvisible": SetBool(v, ref n.TabVisible); break;
            case "buttonwidth": SetInt(v, ref n.ButtonWidth); break;
            case "buttonheight": SetInt(v, ref n.ButtonHeight); break;
            case "indent": SetInt(v, ref n.Indent); break;
            case "autoscroll": SetBool(v, ref n.AutoScroll); break;
            case "horzscrollbar.visible": SetBool(v, ref n.HorzBarVisible); break;
            case "vertscrollbar.visible": SetBool(v, ref n.VertBarVisible); break;
            case "horzscrollbar.range": SetInt(v, ref n.HorzRange); break;
            case "vertscrollbar.range": SetInt(v, ref n.VertRange); break;
        }
    }

    /// <summary>
    /// UIF-251: a stored font makes the font the control's own (the run-time <c>FontChanged</c> clears
    /// <c>ParentFont</c>); <c>ParentFont = False</c> alone keeps the default font. <c>Font.Size</c> without
    /// <c>Font.Height</c> converts at 96 DPI.
    /// </summary>
    private static void ApplyFont(LayoutNode n, FormObject obj)
    {
        int? height = null, size = null;
        foreach (var p in obj.Properties)
        {
            if (p.Path.StartsWith("Font.", StringComparison.OrdinalIgnoreCase))
            {
                n.OwnFont = true;
            }

            if (p.Value is IntegerValue i && string.Equals(p.Path, "Font.Height", StringComparison.OrdinalIgnoreCase))
            {
                height = (int)Math.Clamp(i.Value, int.MinValue, int.MaxValue);
            }
            else if (p.Value is IntegerValue s && string.Equals(p.Path, "Font.Size", StringComparison.OrdinalIgnoreCase))
            {
                size = (int)Math.Clamp(s.Value, -100_000, 100_000);
            }
            else if (p.Value is IdentValue b && string.Equals(p.Path, "ParentFont", StringComparison.OrdinalIgnoreCase)
                && IsName(b.Name, "False"))
            {
                n.OwnFont = true;
            }
        }

        if (height is { } h)
        {
            n.FontHeight = h;
        }
        else if (size is { } s)
        {
            n.FontHeight = -LayoutMachine.MulDiv(s, 96, 72);
        }
    }

    private bool Is(ComponentInfo info, string ancestor) => _catalog.IsAssignable(info.ClassName, ancestor);

    private FormValue? Default(ComponentInfo info, string path)
    {
        if (_catalog.FindPropertyPath(info, path) is not { Kind: not PropertyKind.Object } property)
        {
            return null;
        }

        try
        {
            return PropertyAccess.DefaultValue(_catalog, property);
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    private int Int(ComponentInfo info, string path, int fallback) =>
        Default(info, path) is IntegerValue i ? (int)Math.Clamp(i.Value, int.MinValue, int.MaxValue) : fallback;

    private bool Bool(ComponentInfo info, string path, bool fallback) =>
        Default(info, path) is IdentValue b ? (IsName(b.Name, "True") ? true : IsName(b.Name, "False") ? false : fallback) : fallback;

    private string Ident(ComponentInfo info, string path, string fallback) =>
        Default(info, path) is IdentValue { Name.Length: > 0 } i ? i.Name : fallback;

    private static bool TryStoredInt(FormObject obj, string path, out int value)
    {
        value = 0;
        if (obj.FindProperty(path) is not { Value: IntegerValue i })
        {
            return false;
        }

        value = (int)Math.Clamp(i.Value, int.MinValue, int.MaxValue);
        return true;
    }

    private static void SetInt(FormValue v, ref int field)
    {
        if (v is IntegerValue i)
        {
            field = (int)Math.Clamp(i.Value, int.MinValue, int.MaxValue);
        }
    }

    private static void SetBool(FormValue v, ref bool field)
    {
        if (v is IdentValue b && (IsName(b.Name, "True") || IsName(b.Name, "False")))
        {
            field = IsName(b.Name, "True");
        }
    }

    private static void SetAnchors(LayoutNode n, IEnumerable<string> elements)
    {
        var set = elements.ToHashSet(StringComparer.OrdinalIgnoreCase);
        (n.AkLeft, n.AkTop, n.AkRight, n.AkBottom) = (set.Contains("akLeft"), set.Contains("akTop"), set.Contains("akRight"), set.Contains("akBottom"));
    }

    public static AlignKind ParseAlign(string name) => name.ToLowerInvariant() switch
    {
        "altop" => AlignKind.Top,
        "albottom" => AlignKind.Bottom,
        "alleft" => AlignKind.Left,
        "alright" => AlignKind.Right,
        "alclient" => AlignKind.Client,
        _ => AlignKind.None,
    };

    private static bool IsSpacer(string style) => IsName(style, "tbsSeparator") || IsName(style, "tbsDivider");

    private static bool IsName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
