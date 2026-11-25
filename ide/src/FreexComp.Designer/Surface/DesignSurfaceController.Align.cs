using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>The align, size, spacing and grid commands (design-surface.md §11.1, DSG-SUR-140 … 142).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// "Others" and the movable controls of §11.1: controls (not the form, not tray items) that are not preserved, have
    /// <c>Align = alNone</c> and stored geometry.
    /// </summary>
    private bool IsArrangeable(FormObject obj) =>
        Geometry.IsControl(obj) && !obj.IsPreserved && !IsAligned(obj) && obj.Info?.FindProperty("Left") is not { Stored: false };

    /// <summary>
    /// §11.1 enabled-when: align/size need a control as reference, ≥ 2 selected controls and ≥ 1 other; centering and the
    /// grid commands ≥ 1 movable control; spacing ≥ 3. All disabled while locked (DSG-SUR-141), during a drag and in
    /// tab-order mode.
    /// </summary>
    private bool CanArrange(string commandId)
    {
        if (IsPointerActive || State == InteractionState.TabOrder || _settings.LockControls)
        {
            return false;
        }

        var g = Geometry;
        var primary = Selection.Primary;
        var movable = Selection.Items.Count(IsArrangeable);
        return commandId switch
        {
            Commands.SpaceHEqually or Commands.SpaceVEqually => movable >= 3,
            Commands.CenterHInParent or Commands.CenterVInParent or Commands.AlignToGrid or Commands.SizeToGrid => movable >= 1,
            _ => g.IsControl(primary) && Selection.Items.Count(g.IsControl) >= 2
                && Selection.Items.Any(o => !ReferenceEquals(o, primary) && IsArrangeable(o)),
        };
    }

    /// <summary>
    /// Runs a §11.1 command: the new bounds are computed in surface coordinates (so controls in different parents line up
    /// visually) and stored back in each control's parent coordinates; resized containers re-flow their children
    /// (DSG-SUR-142). One "Align" or "Size" unit; nothing when nothing changes (DSG-SUR-140).
    /// </summary>
    private void Arrange(string commandId)
    {
        List<(FormObject Object, Rect Bounds)> targets = commandId switch
        {
            Commands.CenterHInParent or Commands.CenterVInParent => Center(commandId == Commands.CenterHInParent),
            Commands.SpaceHEqually or Commands.SpaceVEqually => Space(commandId == Commands.SpaceHEqually),
            Commands.AlignToGrid => Selection.Items.Where(IsArrangeable).Select(o => (o, Layout.GetBounds(o) with
            {
                Left = SnapEngine.ToGrid(Layout.GetBounds(o).Left, _settings.GridSizeX),
                Top = SnapEngine.ToGrid(Layout.GetBounds(o).Top, _settings.GridSizeY),
            })).ToList(),
            Commands.SizeToGrid => Selection.Items.Where(IsArrangeable).Select(o => (o, Layout.GetBounds(o) with
            {
                Width = Math.Max(_settings.GridSizeX, SnapEngine.ToGrid(Layout.GetBounds(o).Width, _settings.GridSizeX)),
                Height = Math.Max(_settings.GridSizeY, SnapEngine.ToGrid(Layout.GetBounds(o).Height, _settings.GridSizeY)),
            })).ToList(),
            _ => AlignToReference(commandId),
        };

        var changed = targets.Where(t => t.Bounds != Layout.GetBounds(t.Object)).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var preview = _layout.ComputeWithResizes(Document, changed.Select(t => (t.Object.QualifiedName, t.Bounds)).ToList());
        var selection = Selection.ToSnapshot();
        var (redo, undo) = ApplyResize(null, preview);
        var title = commandId is Commands.SameWidth or Commands.SameHeight or Commands.SameSize or Commands.SizeToGrid
            ? UndoTitles.Size
            : UndoTitles.Align;
        PushUnit(new DesignerUndoUnit(title, redo, undo, [], selection, selection));
    }

    /// <summary>The align and same-size commands: every other movable selected control against the primary (reference).</summary>
    private List<(FormObject Object, Rect Bounds)> AlignToReference(string commandId)
    {
        var g = Geometry;
        var primary = Selection.Primary;
        var r = g.GetBounds(primary);
        var result = new List<(FormObject, Rect)>();
        foreach (var c in Selection.Items.Where(o => !ReferenceEquals(o, primary) && IsArrangeable(o)))
        {
            var s = g.GetBounds(c);
            var stored = Layout.GetBounds(c);
            var (left, top) = commandId switch
            {
                Commands.AlignLeft => (r.Left, s.Top),
                Commands.AlignRight => (r.Right - s.Width, s.Top),
                Commands.AlignTop => (s.Left, r.Top),
                Commands.AlignBottom => (s.Left, r.Bottom - s.Height),
                Commands.AlignHCenter => (r.Left + (r.Width - s.Width) / 2, s.Top),
                Commands.AlignVCenter => (s.Left, r.Top + (r.Height - s.Height) / 2),
                _ => (s.Left, s.Top),
            };
            var bounds = stored with { Left = stored.Left + left - s.Left, Top = stored.Top + top - s.Top };
            if (commandId is Commands.SameWidth or Commands.SameSize)
            {
                bounds = Clamp(c, bounds with { Width = r.Width }, SnapEdges.Right);
            }

            if (commandId is Commands.SameHeight or Commands.SameSize)
            {
                bounds = Clamp(c, bounds with { Height = r.Height }, SnapEdges.Bottom);
            }

            result.Add((c, bounds));
        }

        return result;
    }

    /// <summary>
    /// Center in parent: the movable selected controls that share the parent of the primary (of the last movable one when
    /// the primary is not a control) are shifted together so their bounding box is centred in that parent's client area.
    /// </summary>
    private List<(FormObject Object, Rect Bounds)> Center(bool horizontal)
    {
        var movable = Selection.Items.Where(IsArrangeable).ToList();
        var parent = Geometry.IsControl(Selection.Primary) ? Selection.Primary.Parent : movable[^1].Parent;
        var group = movable.Where(o => ReferenceEquals(o.Parent, parent)).ToList();
        if (group.Count == 0)
        {
            return [];
        }

        var bounds = group.Select(Layout.GetBounds).ToList();
        var client = Geometry.GetClientRect(parent!);
        var left = bounds.Min(b => b.Left);
        var top = bounds.Min(b => b.Top);
        var dx = horizontal ? (client.Width - (bounds.Max(b => b.Right) - left)) / 2 - left : 0;
        var dy = horizontal ? 0 : (client.Height - (bounds.Max(b => b.Bottom) - top)) / 2 - top;
        return group.Zip(bounds, (o, b) => (o, b with { Left = b.Left + dx, Top = b.Top + dy })).ToList();
    }

    /// <summary>
    /// Space equally: sorted by surface left (top), ties in creation order; the first and last stay; inner control i of n
    /// starts at first.right + round(G × i / (n + 1)) + the sizes of the inner ones before it, G = the total free space,
    /// halves rounded up.
    /// </summary>
    private List<(FormObject Object, Rect Bounds)> Space(bool horizontal)
    {
        var g = Geometry;
        var order = new Dictionary<FormObject, int>(ReferenceEqualityComparer.Instance);
        foreach (var obj in Document.AllObjects())
        {
            order[obj] = order.Count;
        }

        int Start(Rect r) => horizontal ? r.Left : r.Top;
        int Size(Rect r) => horizontal ? r.Width : r.Height;
        var sorted = Selection.Items.Where(IsArrangeable)
            .OrderBy(o => Start(g.GetBounds(o)))
            .ThenBy(o => order[o])
            .ToList();
        var first = g.GetBounds(sorted[0]);
        var last = g.GetBounds(sorted[^1]);
        var inner = sorted.Skip(1).SkipLast(1).ToList();
        var n = inner.Count;
        long gap = Start(last) - (Start(first) + Size(first)) - inner.Sum(o => Size(g.GetBounds(o)));
        var result = new List<(FormObject, Rect)>();
        var before = 0;
        for (var i = 1; i <= n; i++)
        {
            var c = inner[i - 1];
            var s = g.GetBounds(c);
            var share = (int)Math.Floor((2.0 * gap * i + (n + 1)) / (2.0 * (n + 1))); // round(G × i / (n + 1)), halves up
            var start = Start(first) + Size(first) + share + before;
            before += Size(s);
            var stored = Layout.GetBounds(c);
            result.Add((c, horizontal ? stored with { Left = stored.Left + start - s.Left } : stored with { Top = stored.Top + start - s.Top }));
        }

        return result;
    }
}
