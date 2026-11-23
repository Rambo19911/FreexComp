using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>Keyboard nudging and resizing (design-surface.md §9, DSG-SUR-100 … 109) and the typing hook (DSG-SUR-105).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>DSG-SUR-107: consecutive nudges of the same kind and selection within this interval merge into one unit.</summary>
    public static readonly TimeSpan NudgeMergeWindow = TimeSpan.FromSeconds(1);

    // The last keyboard nudge (DSG-SUR-107): its unit, kind (undo title), selection and time.
    private (DesignerUndoUnit Unit, string Kind, SelectionSnapshot Selection, DateTimeOffset At)? _lastNudge;

    /// <summary>
    /// DSG-SUR-105: a letter or digit typed on the surface (no modifiers except Shift) starts editing the primary's
    /// <c>Caption</c> (else <c>Text</c>) in the Object Inspector with the character replacing the value; true = handled.
    /// </summary>
    public bool OnTextInput(string text, InputModifiers modifiers = InputModifiers.None)
    {
        if (State != InteractionState.Idle || text.Length != 1 || !char.IsLetterOrDigit(text[0])
            || (modifiers & (InputModifiers.Ctrl | InputModifiers.Alt)) != 0)
        {
            return false;
        }

        var primary = Selection.Primary;
        if (primary.IsPreserved || primary.Info is not { } info)
        {
            return false;
        }

        foreach (var name in (string[])["Caption", "Text"])
        {
            if (info.FindProperty(name) is { ReadOnly: false, Hidden: false } p
                && (p.Type.Equals("string", StringComparison.OrdinalIgnoreCase) || p.Type.Equals("TCaption", StringComparison.OrdinalIgnoreCase)))
            {
                _host.BeginPropertyEdit(p.Name, text);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// An arrow key (DSG-SUR-100 … 103, 106, 108): plain = move by a grid step, Ctrl = by 1 px; Shift = resize the
    /// right/bottom edge by a grid step, Shift+Alt = by 1 px. Ctrl+Shift is left to the z-order commands (§11.3).
    /// </summary>
    private bool Arrow(DesignKey key, InputModifiers modifiers)
    {
        if (State != InteractionState.Idle
            || (modifiers.HasFlag(InputModifiers.Ctrl) && modifiers.HasFlag(InputModifiers.Shift)))
        {
            return false;
        }

        var (dx, dy) = key switch
        {
            DesignKey.Left => (-1, 0),
            DesignKey.Right => (1, 0),
            DesignKey.Up => (0, -1),
            _ => (0, 1),
        };
        var resize = modifiers.HasFlag(InputModifiers.Shift);
        if (Selection.IsFormOnly && !resize)
        {
            return true; // DSG-SUR-108: plain and Ctrl arrows do nothing on the form
        }

        if (_settings.LockControls)
        {
            _host.SetStatus(StatusIds.Locked);
            return true;
        }

        var grid = _settings.SnapToGrid && !modifiers.HasFlag(resize ? InputModifiers.Alt : InputModifiers.Ctrl);
        if (resize)
        {
            KeyResize(dx, dy, grid);
        }
        else
        {
            KeyMove(dx, dy, grid);
        }

        return true;
    }

    /// <summary>
    /// The coordinate one step from <paramref name="value"/> in direction <paramref name="dir"/>: the next grid multiple
    /// in that direction (± grid size when already on the grid), or ± 1 without the grid.
    /// </summary>
    private static int Step(int value, int dir, int gridSize, bool grid)
    {
        if (!grid)
        {
            return value + dir;
        }

        var below = (int)Math.Floor((double)value / gridSize) * gridSize;
        return dir > 0 ? below + gridSize : (below == value ? value - gridSize : below);
    }

    /// <summary>
    /// DSG-SUR-100, 101, 106, 109: moves the effective set without the form, preserved and aligned controls; the primary
    /// (else the last member) is the anchor whose position steps, the others move by the same delta. Tray items stay at
    /// ≥ 0. No re-parenting.
    /// </summary>
    private void KeyMove(int dx, int dy, bool grid)
    {
        var g = Geometry;
        var members = Selection.GetEffectiveSet()
            .Where(o => !o.IsPreserved && (g.IsTrayItem(o) || (g.IsControl(o) && !IsAligned(o))))
            .ToList();
        if (members.Count == 0)
        {
            return;
        }

        var anchor = members.Contains(Selection.Primary) ? Selection.Primary : members[^1];
        var (ax, ay) = PositionOf(anchor);
        var delta = dx != 0 ? Step(ax, dx, _settings.GridSizeX, grid) - ax : Step(ay, dy, _settings.GridSizeY, grid) - ay;
        var changes = new List<FormChange>();
        var moved = false;
        foreach (var obj in members)
        {
            var tray = g.IsTrayItem(obj);
            var (x, y) = PositionOf(obj);
            var (nx, ny) = (x + (dx != 0 ? delta : 0), y + (dy != 0 ? delta : 0));
            if (tray)
            {
                (nx, ny) = (Math.Max(0, nx), Math.Max(0, ny));
            }

            if ((nx, ny) == (x, y))
            {
                continue;
            }

            moved = true;
            var (xPath, yPath) = tray ? ("DesignLeft", "DesignTop") : ("Left", "Top");
            if (nx != x || !PropertyAccess.IsStored(obj, xPath))
            {
                changes.Add(new SetProperty(obj.Name, xPath, new IntegerValue(nx, false)));
            }

            if (ny != y || !PropertyAccess.IsStored(obj, yPath))
            {
                changes.Add(new SetProperty(obj.Name, yPath, new IntegerValue(ny, false)));
            }
        }

        if (!moved)
        {
            return;
        }

        var selection = Selection.ToSnapshot();
        var undo = Editor.Apply(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        PushNudge(UndoTitles.Move, [.. changes, .. commitRedo], [.. commitUndo, .. undo], selection);
    }

    /// <summary>A control's stored-as-computed position, or a tray item's <c>DesignLeft</c>/<c>DesignTop</c>.</summary>
    private (int X, int Y) PositionOf(FormObject obj)
    {
        if (Geometry.IsTrayItem(obj))
        {
            return SurfaceGeometry.TrayPosition(obj);
        }

        var b = Layout.GetBounds(obj);
        return (b.Left, b.Top);
    }

    /// <summary>
    /// DSG-SUR-102, 103, 106, 108: moves the right (Left/Right keys) or bottom (Up/Down) edge of every selected resizable
    /// control by the anchor's step (DSG-SUR-082 limits, DSG-SUR-084 fixed edges); the form alone resizes its client area
    /// (at least 16 × 16). Containers re-flow their children (DSG-SUR-086).
    /// </summary>
    private void KeyResize(int dx, int dy, bool grid)
    {
        var edge = dx != 0 ? SnapEdges.Right : SnapEdges.Bottom;
        var (dir, gridSize) = dx != 0 ? (dx, _settings.GridSizeX) : (dy, _settings.GridSizeY);
        Rect? formClient = null;
        List<(FormObject Object, Rect Bounds)> bounds;
        if (Selection.IsFormOnly)
        {
            var b = Layout.GetBounds(Document.Root);
            var size = Math.Max(MinFormClientSize, Step(dx != 0 ? b.Width : b.Height, dir, gridSize, grid));
            var r = dx != 0 ? b with { Width = size } : b with { Height = size };
            bounds = [(Document.Root, r)];
            formClient = r;
        }
        else
        {
            var g = Geometry;
            var members = Selection.Items
                .Where(o => g.IsControl(o) && !o.IsPreserved && o.Info?.FindProperty("Left") is not { Stored: false }
                    && (FreeEdges(o) & edge) != 0)
                .ToList();
            if (members.Count == 0)
            {
                return;
            }

            var anchor = members.Contains(Selection.Primary) ? Selection.Primary : members[^1];
            var a = Layout.GetBounds(anchor);
            var far = dx != 0 ? a.Right : a.Bottom;
            var delta = Step(far, dir, gridSize, grid) - far;
            bounds = members.Select(o => (o, Clamp(o, Resized(Layout.GetBounds(o), edge, 0, delta, 0, delta), edge))).ToList();
        }

        if (bounds.TrueForAll(m => m.Bounds == Layout.GetBounds(m.Object)))
        {
            return;
        }

        var selection = Selection.ToSnapshot();
        var preview = _layout.ComputeWithResizes(Document, bounds.Select(m => (m.Object.Name, m.Bounds)).ToList());
        var (redo, undo) = ApplyResize(formClient, preview);
        PushNudge(UndoTitles.Resize, redo, undo, selection);
    }

    /// <summary>
    /// DSG-SUR-107: merges into the previous nudge unit when it is still the newest unit, has the same kind and selection,
    /// and was made at most <see cref="NudgeMergeWindow"/> ago; otherwise pushes a new unit.
    /// </summary>
    private void PushNudge(string kind, List<FormChange> redo, List<FormChange> undo, SelectionSnapshot selection)
    {
        var now = _time.GetUtcNow();
        DesignerUndoUnit unit;
        if (_lastNudge is { } last && last.Kind == kind && ReferenceEquals(Undo.Peek, last.Unit) && !Undo.CanRedo
            && last.Selection.Names.SequenceEqual(selection.Names, StringComparer.OrdinalIgnoreCase)
            && last.Selection.Primary == selection.Primary && now - last.At <= NudgeMergeWindow)
        {
            unit = new DesignerUndoUnit(kind, [.. last.Unit.Redo, .. redo], [.. undo, .. last.Unit.Undo], [],
                last.Unit.SelectionBefore, selection);
            Undo.ReplaceLast(unit);
        }
        else
        {
            unit = new DesignerUndoUnit(kind, redo, undo, [], selection, selection);
            Undo.Push(unit);
        }

        _lastNudge = (unit, kind, selection, now);
    }
}
