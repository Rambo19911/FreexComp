using FreexComp.Designer.Model;

namespace FreexComp.Designer.Layout;

/// <summary>Run-time mode follows layout.md literally; design mode treats every control as visible (DSG-LAY-005).</summary>
public enum LayoutMode { RunTime, Design }

/// <summary>Overridden stored values of one object for a preview (layout-engine.md §2).</summary>
/// <param name="Bounds">Replaces the stored <c>Left</c>/<c>Top</c>/<c>Width</c>/<c>Height</c>.</param>
/// <param name="Align">Replaces the stored <c>Align</c> (an identifier such as <c>alTop</c>).</param>
/// <param name="ParentName">Moves the object to this parent, as its last child.</param>
public sealed record LayoutOverride(Rect? Bounds = null, string? Align = null, string? ParentName = null);

/// <summary>A snapshot of computed geometry (layout-engine.md §2); immutable, independent of later changes.</summary>
public sealed class LayoutResult
{
    private readonly Dictionary<FormObject, Entry> _entries;
    private readonly FormObject _root;
    private readonly LayoutDumpForm _dump;

    private LayoutResult(FormObject root, Dictionary<FormObject, Entry> entries, LayoutDumpForm dump)
    {
        _root = root;
        _entries = entries;
        _dump = dump;
    }

    /// <summary>Relative to the parent's client origin; the root: (0, 0, ClientWidth, ClientHeight).</summary>
    public Rect GetBounds(FormObject obj) => Find(obj).Bounds;

    /// <summary>Client origin and size relative to the object's own top-left (layout.md UIF-215).</summary>
    public Rect GetClientRect(FormObject obj) => Find(obj).Client;

    /// <summary>The align rect in client coordinates (layout.md UIF-220); extents may be negative (no normalisation).</summary>
    public Rect GetAlignRect(FormObject obj) => Find(obj).Align;

    /// <summary>D-104: the run-time <c>Padding</c> (after scaling) that insets the align rect, as (left, top, right, bottom).</summary>
    public Rect GetPadding(FormObject obj) => Find(obj).Padding;

    /// <summary>Relative to the form's client origin.</summary>
    public Rect GetSurfaceBounds(FormObject obj) => Find(obj).Surface;

    /// <summary>The <c>visible</c> value of the dump: own <c>Visible</c>; tab sheets: is the active page.</summary>
    public bool GetVisible(FormObject obj) => Find(obj).Visible;

    /// <summary>False for non-visual components, unplaceable preserved objects and objects outside the tree.</summary>
    public bool HasBounds(FormObject obj) => _entries.ContainsKey(obj);

    /// <summary>One element of the dump's <c>forms</c> array (layout-engine.md §7.1).</summary>
    public LayoutDumpForm ToDump() => _dump;

    /// <summary>
    /// The objects whose computed bounds differ from their stored bounds (the stored = computed invariant, DSG-LAY-040);
    /// objects whose geometry metadata says <c>"stored": false</c> (tab sheets, tool buttons) are exempt.
    /// </summary>
    public IReadOnlyList<(FormObject Object, Rect Stored, Rect Computed)> Differences()
    {
        var list = new List<(FormObject, Rect, Rect)>();
        foreach (var (obj, entry) in _entries)
        {
            if (obj.Info?.FindProperty("Left") is { Stored: false })
            {
                continue;
            }

            var stored = StoredBounds(obj);
            if (stored != entry.Bounds)
            {
                list.Add((obj, stored, entry.Bounds));
            }
        }

        return list;
    }

    private Rect StoredBounds(FormObject obj)
    {
        if (ReferenceEquals(obj, _root))
        {
            return PropertyAccess.GetBounds(obj);
        }

        static int? Stored(FormObject o, string path) => o.FindProperty(path) is { Value: IntegerValue i } ? (int)i.Value : null;
        var size = obj.Info?.DefaultSize ?? default;
        return new Rect(Stored(obj, "Left") ?? 0, Stored(obj, "Top") ?? 0, Stored(obj, "Width") ?? size.Width, Stored(obj, "Height") ?? size.Height);
    }

    private Entry Find(FormObject obj) =>
        _entries.TryGetValue(obj, out var entry) ? entry
        : throw new ArgumentException($"{obj.Name} has no layout bounds (non-visual or unplaceable)", nameof(obj));

    /// <summary>Snapshots the current state of a machine.</summary>
    internal static LayoutResult Capture(LayoutNode root)
    {
        var entries = new Dictionary<FormObject, Entry>(ReferenceEqualityComparer.Instance);
        var controls = Children(root, 0, 0, entries);
        var (cw, ch) = LayoutMachine.ClientSize(root);
        entries[root.Object] = MakeEntry(root, 0, 0);
        var dump = new LayoutDumpForm(root.Object.TypeName, root.Object.Name, cw, ch, controls);
        return new LayoutResult(root.Object, entries, dump);
    }

    private static List<LayoutDumpControl> Children(LayoutNode parent, int clientX, int clientY, Dictionary<FormObject, Entry> entries)
    {
        var list = new List<LayoutDumpControl>(parent.Children.Count);
        foreach (var c in parent.Children)
        {
            var sx = clientX + c.Left;
            var sy = clientY + c.Top;
            entries[c.Object] = MakeEntry(c, sx, sy);
            var (ox, oy) = LayoutMachine.ClientOrigin(c);
            list.Add(new LayoutDumpControl(c.Object.TypeName, c.Object.Name, c.Left, c.Top, c.Width, c.Height, c.Visible,
                Children(c, sx + ox, sy + oy, entries)));
        }

        return list;
    }

    private static Entry MakeEntry(LayoutNode n, int surfaceX, int surfaceY)
    {
        var (ox, oy) = LayoutMachine.ClientOrigin(n);
        var (cw, ch) = LayoutMachine.ClientSize(n);
        var (l, t, r, b) = LayoutMachine.AlignRect(n);
        return new Entry(new Rect(n.Left, n.Top, n.Width, n.Height), new Rect(ox, oy, cw, ch), new Rect(l, t, r - l, b - t),
            new Rect(surfaceX, surfaceY, n.Width, n.Height), n.Visible,
            new Rect(n.PaddingLeft, n.PaddingTop, n.PaddingRight, n.PaddingBottom));
    }

    private readonly record struct Entry(Rect Bounds, Rect Client, Rect Align, Rect Surface, bool Visible, Rect Padding);
}
