using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Layout;

/// <summary>
/// The C# implementation of the layout rules of ui-framework layout.md (ADR 004; layout-engine.md). Pure: it never
/// modifies the document (DSG-LAY-004). Integer arithmetic in logical pixels at 96 DPI (DSG-LAY-003).
/// </summary>
public sealed class LayoutEngine
{
    private readonly ComponentCatalog _catalog;

    /// <param name="catalog">Class ancestry and property defaults (the metadata the form was parsed with).</param>
    /// <param name="renderers">The renderers of the design surface (they take their rectangles from the result, DSG-LAY-034).</param>
    public LayoutEngine(ComponentCatalog catalog, RendererRegistry renderers)
    {
        _catalog = catalog;
        Renderers = renderers;
    }

    public RendererRegistry Renderers { get; }

    /// <summary>
    /// DSG-LAY-071: the text extent of a tab caption for automatic tab lengths — the design surface passes its canvas
    /// measurement with the tab control's effective font (control, raw caption including <c>&amp;</c> → width); null = the
    /// headless estimate <see cref="TabStripGeometry.EstimateTextExtent"/>.
    /// </summary>
    public Func<FormObject, string, int>? TabTextWidth { get; set; }

    /// <summary>Full layout from stored properties (load sequence of layout.md §12, steps 1, 3, 5).</summary>
    public LayoutResult Compute(FormDocument form, LayoutMode mode = LayoutMode.Design) =>
        LayoutResult.Capture(Load(form, mode, null).Machine);

    /// <summary>
    /// Load, then apply steps as explicit changes (layout.md UIF-330 grammar: <c>client</c>, <c>set</c>, <c>dump</c>,
    /// <c>scale</c>), one result per <c>dump</c> step (a single result when the script has no <c>dump</c>).
    /// </summary>
    /// <exception cref="FormatException">A malformed step, an unknown component or an unsupported property (UIF-334).</exception>
    public IReadOnlyList<LayoutResult> ComputeWithSteps(FormDocument form, string stepsText, LayoutMode mode = LayoutMode.RunTime)
    {
        var (machine, nodes) = Load(form, mode, null);
        var results = new List<LayoutResult>();
        foreach (var raw in stepsText.Replace("\r\n", "\n").Split('\n'))
        {
            var words = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                continue;
            }

            switch (words[0].ToLowerInvariant())
            {
                case "dump" when words.Length == 1:
                    results.Add(LayoutResult.Capture(machine));
                    break;
                case "client" when words.Length == 3:
                    machine.SetClientWidth(machine.Root, IntArg(words[1], raw));
                    machine.SetClientHeight(machine.Root, IntArg(words[2], raw));
                    break;
                case "scale" when words.Length == 3:
                    machine.ScaleBy(IntArg(words[1], raw), IntArg(words[2], raw));
                    break;
                case "set" when words.Length == 4:
                    Set(machine, FindNode(form, nodes, words[1], raw), words[2], words[3], raw);
                    break;
                default:
                    throw new FormatException($"bad step '{raw.Trim()}'");
            }
        }

        if (results.Count == 0)
        {
            results.Add(LayoutResult.Capture(machine));
        }

        return results;
    }

    /// <summary>
    /// Layout as if <paramref name="containerName"/> had been given the new outer size by an explicit change (root form:
    /// new client size): its children's anchors apply against their baselines (stored bounds and the container's stored
    /// client size), then Align; recursion into children whose size changed (DSG-LAY-021, DSG-LAY-023). Design mode.
    /// </summary>
    public LayoutResult ComputeWithResize(FormDocument form, string containerName, int newWidth, int newHeight)
    {
        var (machine, nodes) = Load(form, LayoutMode.Design, null);
        var node = FindNode(form, nodes, containerName, containerName);
        machine.SetBoundsExplicit(node, node.Left, node.Top, newWidth, newHeight);
        return LayoutResult.Capture(machine);
    }

    /// <summary>
    /// Layout after explicit bounds changes of several controls, applied in list order (interactive resize preview,
    /// design-surface.md DSG-SUR-086): each re-flows its own children against their baselines and requests its parent's
    /// align pass, as <see cref="ComputeWithResize"/> does for one. For the root, the bounds' size is the client size.
    /// </summary>
    public LayoutResult ComputeWithResizes(FormDocument form, IReadOnlyList<(string Name, Rect Bounds)> changes)
    {
        var (machine, nodes) = Load(form, LayoutMode.Design, null);
        foreach (var (name, b) in changes)
        {
            var node = FindNode(form, nodes, name, name);
            if (node.Parent is null)
            {
                machine.SetBoundsExplicit(node, node.Left, node.Top, b.Width, b.Height);
            }
            else
            {
                machine.SetBoundsExplicit(node, b.Left, b.Top, b.Width, b.Height);
            }
        }

        return LayoutResult.Capture(machine);
    }

    /// <summary>Layout with overridden stored bounds/properties for some objects (drag previews). Design mode.</summary>
    public LayoutResult ComputeWithOverrides(FormDocument form, IReadOnlyDictionary<string, LayoutOverride> overrides) =>
        LayoutResult.Capture(Load(form, LayoutMode.Design, overrides).Machine);

    private (LayoutMachine Machine, Dictionary<FormObject, LayoutNode> Nodes) Load(FormDocument form, LayoutMode mode,
        IReadOnlyDictionary<string, LayoutOverride>? overrides)
    {
        var (root, nodes) = new LayoutReader(_catalog).Read(form, overrides);
        var machine = new LayoutMachine(root, mode, TabTextWidth);
        machine.Load();
        return (machine, nodes);
    }

    private static LayoutNode FindNode(FormDocument form, Dictionary<FormObject, LayoutNode> nodes, string name, string step) =>
        form.FindByQualifiedName(name) is { } obj && nodes.TryGetValue(obj, out var node) ? node
        : throw new FormatException($"no placeable component '{name}' in step '{step.Trim()}'");

    /// <summary>
    /// <c>set C P v</c>: an explicit assignment with the run-time setter's semantics (UIF-210, UIF-232, UIF-227). Supported:
    /// <c>Left</c>, <c>Top</c>, <c>Width</c>, <c>Height</c>, <c>ClientWidth</c>, <c>ClientHeight</c>, <c>Align</c>,
    /// <c>Visible</c>, <c>AlignWithMargins</c>; on a <c>TCustomTabControl</c> descendant also <c>TabHeight</c>,
    /// <c>TabWidth</c>, <c>MultiLine</c>, <c>TabPosition</c>, <c>Style</c>, <c>ScrollOpposite</c> (DSG-LAY-069).
    /// </summary>
    private static void Set(LayoutMachine machine, LayoutNode node, string property, string value, string step)
    {
        switch (property.ToLowerInvariant())
        {
            case "left":
                machine.SetBoundsExplicit(node, IntArg(value, step), node.Top, node.Width, node.Height);
                break;
            case "top":
                machine.SetBoundsExplicit(node, node.Left, IntArg(value, step), node.Width, node.Height);
                break;
            case "width":
                machine.SetBoundsExplicit(node, node.Left, node.Top, IntArg(value, step), node.Height);
                break;
            case "height":
                machine.SetBoundsExplicit(node, node.Left, node.Top, node.Width, IntArg(value, step));
                break;
            case "clientwidth":
                machine.SetClientWidth(node, IntArg(value, step));
                break;
            case "clientheight":
                machine.SetClientHeight(node, IntArg(value, step));
                break;
            case "align":
                var align = LayoutReader.ParseAlign(value);
                if (align == AlignKind.None && !value.Equals("alNone", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException($"not a TAlign value: '{value}' in step '{step.Trim()}'");
                }

                machine.SetAlign(node, align);
                break;
            case "visible":
                machine.SetVisible(node, BoolArg(value, step));
                break;
            case "alignwithmargins":
                machine.SetAlignWithMargins(node, BoolArg(value, step));
                break;
            case "tabheight" when node.IsTabStrip:
                var tabHeight = IntArg(value, step);
                machine.SetTabStripInput(node, n => n.TabHeight = tabHeight);
                break;
            case "tabwidth" when node.IsTabStrip:
                var tabWidth = IntArg(value, step);
                machine.SetTabStripInput(node, n => n.TabWidth = tabWidth);
                break;
            case "multiline" when node.IsTabStrip:
                var multiLine = BoolArg(value, step);
                machine.SetTabStripInput(node, n => n.MultiLine = multiLine);
                break;
            case "scrollopposite" when node.IsTabStrip:
                var scrollOpposite = BoolArg(value, step);
                machine.SetTabStripInput(node, n => n.ScrollOpposite = scrollOpposite);
                break;
            case "tabposition" when node.IsTabStrip:
                var position = LayoutReader.ParseTabPosition(value)
                    ?? throw new FormatException($"not a TTabPosition value: '{value}' in step '{step.Trim()}'");
                machine.SetTabStripInput(node, n => n.TabPosition = position);
                break;
            case "style" when node.IsTabStrip:
                var style = LayoutReader.ParseTabStyle(value)
                    ?? throw new FormatException($"not a TTabStyle value: '{value}' in step '{step.Trim()}'");
                machine.SetTabStripInput(node, n => n.TabStyle = style);
                break;
            default:
                throw new FormatException($"unsupported property '{property}' in step '{step.Trim()}'");
        }
    }

    private static int IntArg(string text, string step) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value
        : throw new FormatException($"not an integer: '{text}' in step '{step.Trim()}'");

    private static bool BoolArg(string text, string step) =>
        text.Equals("True", StringComparison.OrdinalIgnoreCase) ? true
        : text.Equals("False", StringComparison.OrdinalIgnoreCase) ? false
        : throw new FormatException($"not a Boolean: '{text}' in step '{step.Trim()}'");
}
