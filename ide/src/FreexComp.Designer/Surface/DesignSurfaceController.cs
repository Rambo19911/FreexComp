using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>
/// The interaction logic of the design surface (design-surface.md; no UI dependency, DSG-ARCH-003): selection and the
/// click rules (D-035), the selection commands (D-036), the lasso (D-037) and move drags with undo (D-038). Snapping,
/// re-parenting, resizing, placement, the other keys and commands follow in later tasks.
/// </summary>
public sealed class DesignSurfaceController
{
    /// <summary>DSG-SUR-040: a drag begins after this many DIP of movement on either axis.</summary>
    public const int DragThreshold = 4;

    private readonly LayoutEngine _layout;
    private readonly IDesignerSettings _settings;
    private readonly IDesignerHost _host;
    private (int Version, LayoutResult Result, SurfaceGeometry Geometry, HitTester HitTester)? _cache;

    // The pending press (DSG-SUR-040, DSG-SUR-041).
    private (int X, int Y) _downAt;
    private FormObject? _pressed;
    private bool _pressedOnHandle;
    private bool _selectOnRelease;
    private bool _dragging;
    private InputModifiers _downModifiers;
    private FormObject? _lastAltPick;
    private HitResult _pressHit;
    private FormObject? _lassoContainer;

    // The move drag (§5): the effective move set with the stored origins, tray or controls, the current delta, the preview.
    private List<(FormObject Object, int Left, int Top)> _moveSet = [];
    private bool _moveTray;
    private (int X, int Y) _moveDelta;
    private (int Version, LayoutResult Result, SurfaceGeometry Geometry, HitTester HitTester)? _preview;

    public DesignSurfaceController(FormDocument document, FormEditor editor, DesignerUndoStack undo, ComponentCatalog catalog,
        LayoutEngine layout, IDesignerSettings settings, IDesignerHost host)
    {
        Document = document;
        Editor = editor;
        Undo = undo;
        Catalog = catalog;
        _layout = layout;
        _settings = settings;
        _host = host;
        Selection = new Selection(document);
        Selection.Changed += (_, _) => Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The surface must repaint.</summary>
    public event EventHandler? Invalidated;

    public FormDocument Document { get; }

    public FormEditor Editor { get; }

    public DesignerUndoStack Undo { get; }

    public ComponentCatalog Catalog { get; }

    public Selection Selection { get; }

    public InteractionState State { get; private set; }

    /// <summary>The layout of the current model version (DSG-SUR-181: cached per <see cref="FormDocument.Version"/>).</summary>
    public LayoutResult Layout => Current.Result;

    /// <summary>The surface geometry of the current model version.</summary>
    public SurfaceGeometry Geometry => Current.Geometry;

    private (int Version, LayoutResult Result, SurfaceGeometry Geometry, HitTester HitTester) Current
    {
        get
        {
            if (_preview is { } preview)
            {
                return preview; // during a drag: the preview state (DSG-SUR-181)
            }

            if (_cache is not { } cache || cache.Version != Document.Version)
            {
                var result = _layout.Compute(Document);
                var geometry = new SurfaceGeometry(Document, result, _layout.Renderers, _settings.ShowComponentCaptions);
                cache = (Document.Version, result, geometry, new HitTester(geometry));
                _cache = cache;
            }

            return cache;
        }
    }

    public void OnPointer(PointerInput input)
    {
        switch (input.Kind)
        {
            case PointerKind.Down when input.Button == PointerButton.Left:
                PressLeft(input);
                break;
            case PointerKind.Down when input.Button == PointerButton.Right:
                PressRight(input);
                break;
            case PointerKind.Move when State == InteractionState.PressPending:
                var (x, y) = SurfaceGeometry.Round(input.X, input.Y);
                if (!_dragging && (Math.Abs(x - _downAt.X) >= DragThreshold || Math.Abs(y - _downAt.Y) >= DragThreshold))
                {
                    StartDrag(input);
                }

                break;
            case PointerKind.Move when State == InteractionState.Moving:
                UpdateMove(input);
                break;
            case PointerKind.Up when input.Button == PointerButton.Left && State == InteractionState.Moving:
                UpdateMove(input);
                Drop();
                break;
            case PointerKind.Move when State == InteractionState.Lasso:
                UpdateLasso(input);
                break;
            case PointerKind.Up when input.Button == PointerButton.Left && State == InteractionState.PressPending:
                Release();
                break;
            case PointerKind.Up when input.Button == PointerButton.Left && State == InteractionState.Lasso:
                UpdateLasso(input);
                FinishLasso(input.Modifiers.HasFlag(InputModifiers.Shift));
                break;
            case PointerKind.CaptureLost:
                ResetPress();
                break;
        }
    }

    /// <summary>A key in the Designer scope (design-surface.md §9); true = handled.</summary>
    public bool OnKey(KeyInput input)
    {
        switch (input.Key)
        {
            case DesignKey.Escape:
                return Escape();
            case DesignKey.Tab:
                Execute(input.Modifiers.HasFlag(InputModifiers.Shift) ? Commands.SelectPrevious : Commands.SelectNext);
                return true;
            default:
                return false;
        }
    }

    public bool CanExecute(string commandId) => commandId switch
    {
        Commands.SelectAll or Commands.SelectNext or Commands.SelectPrevious => true,
        Commands.SelectParent => Selection.Primary.Parent is not null,
        Commands.Undo => Undo.CanUndo && !IsPointerActive, // DSG-SUR-171
        Commands.Redo => Undo.CanRedo && !IsPointerActive,
        _ => false,
    };

    private bool IsPointerActive => State is InteractionState.PressPending or InteractionState.Moving or InteractionState.Resizing
        or InteractionState.Lasso;

    public void Execute(string commandId, string? argument = null)
    {
        if (!CanExecute(commandId))
        {
            return;
        }

        switch (commandId)
        {
            case Commands.SelectAll:
                Selection.Select(SelectAllSet());
                break;
            case Commands.SelectParent:
                Selection.Select(Geometry.IsTrayItem(Selection.Primary) ? Document.Root : Selection.Primary.Parent!);
                break;
            case Commands.SelectNext:
            case Commands.SelectPrevious:
                SelectInCycle(commandId == Commands.SelectNext ? 1 : -1);
                break;
            case Commands.Undo when Undo.Undo() is { } unit: // DSG-SUR-170 (code-sync edits come with code sync)
                Editor.Apply(unit.Undo);
                Selection.Set(unit.SelectionBefore.Names);
                break;
            case Commands.Redo when Undo.Redo() is { } unit:
                Editor.Apply(unit.Redo);
                Selection.Set(unit.SelectionAfter.Names);
                break;
        }
    }

    /// <summary>DSG-SUR-104: Escape cancels the first applicable mode, else selects the parent (unhandled on the form).</summary>
    private bool Escape()
    {
        if (State is InteractionState.Lasso or InteractionState.Moving)
        {
            ResetPress(); // DSG-SUR-037, DSG-SUR-058: nothing is recorded
            return true;
        }

        if (!CanExecute(Commands.SelectParent))
        {
            return false;
        }

        Execute(Commands.SelectParent);
        return true;
    }

    /// <summary>
    /// DSG-SUR-026: the direct children of the primary's parent; for the form, its direct visual children; for a tray item,
    /// all tray items. Creation order.
    /// </summary>
    private List<FormObject> SelectAllSet()
    {
        var g = Geometry;
        var primary = Selection.Primary;
        if (g.IsTrayItem(primary))
        {
            return [.. g.TrayItems];
        }

        var parent = primary.Parent ?? primary;
        return parent.Children.Where(c => !g.IsTrayItem(c)).ToList();
    }

    /// <summary>
    /// DSG-SUR-028: all non-root components in pre-order, the tray items after the rest; from the form the first (next) or
    /// the last (previous); wraps around. Single selection.
    /// </summary>
    private void SelectInCycle(int step)
    {
        var g = Geometry;
        var all = Document.AllObjects().Where(o => o.Parent is not null).ToList();
        var cycle = all.Where(o => !g.IsTrayItem(o)).Concat(all.Where(g.IsTrayItem)).ToList();
        if (cycle.Count == 0)
        {
            return;
        }

        var index = cycle.FindIndex(o => ReferenceEquals(o, Selection.Primary));
        var next = index < 0 ? (step > 0 ? 0 : cycle.Count - 1) : (index + step + cycle.Count) % cycle.Count;
        Selection.Select(cycle[next]);
    }

    /// <summary>The lasso rectangle in surface coordinates while a lasso is active (DSG-SUR-035), else null.</summary>
    public Rect? LassoRect { get; private set; }

    /// <summary>
    /// The drag threshold was crossed (DSG-SUR-040): a lasso on the form client area, on a container hit with Ctrl or on
    /// the tray background (DSG-SUR-035); the backdrop starts nothing. A drag never applies the click rule.
    /// </summary>
    private void StartDrag(PointerInput input)
    {
        _dragging = true;
        if (_pressedOnHandle)
        {
            return;
        }

        var lassoInControl = _pressHit.Kind == HitKind.Control && _downModifiers.HasFlag(InputModifiers.Ctrl)
            && _pressed is { Info.AcceptsChildren: true, IsPreserved: false };
        if (_pressHit.Kind is HitKind.FormClient or HitKind.Tray || lassoInControl)
        {
            _lassoContainer = _pressHit.Kind == HitKind.Tray ? null : _pressed;
            State = InteractionState.Lasso;
            UpdateLasso(input);
        }
        else if (_pressHit.Kind is HitKind.Control or HitKind.TrayItem && _pressed is { } anchor && Selection.Contains(anchor))
        {
            StartMove(anchor, input);
        }
    }

    /// <summary>
    /// DSG-SUR-050: the effective move set: the selection after DSG-SUR-024 without the form, preserved and aligned
    /// controls; only tray items when the drag starts on a tray item, only controls otherwise. A drag that starts on an
    /// aligned or preserved control moves nothing.
    /// </summary>
    private void StartMove(FormObject anchor, PointerInput input)
    {
        var g = Geometry;
        _moveTray = g.IsTrayItem(anchor);
        if (!_moveTray && (anchor.IsPreserved || IsAligned(anchor)))
        {
            return;
        }

        _moveSet = Selection.GetEffectiveSet()
            .Where(o => _moveTray ? g.IsTrayItem(o) : g.IsControl(o) && !o.IsPreserved && !IsAligned(o))
            .Select(o => _moveTray
                ? (o, SurfaceGeometry.TrayPosition(o).X, SurfaceGeometry.TrayPosition(o).Y)
                : (o, Layout.GetBounds(o).Left, Layout.GetBounds(o).Top))
            .ToList();
        if (_moveSet.Count == 0)
        {
            return;
        }

        State = InteractionState.Moving;
        UpdateMove(input);
    }

    /// <summary>DSG-SUR-052: raw delta = pointer - down pointer; Shift forces the smaller component to 0 (each event).</summary>
    private void UpdateMove(PointerInput input)
    {
        var (x, y) = SurfaceGeometry.Round(input.X, input.Y);
        var (dx, dy) = (x - _downAt.X, y - _downAt.Y);
        if (input.Modifiers.HasFlag(InputModifiers.Shift))
        {
            (dx, dy) = Math.Abs(dx) >= Math.Abs(dy) ? (dx, 0) : (0, dy);
        }

        if (_moveDelta == (dx, dy))
        {
            return;
        }

        _moveDelta = (dx, dy);
        _preview = null;
        if (!_moveTray && _moveDelta != (0, 0))
        {
            var overrides = new Dictionary<string, LayoutOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (var (obj, left, top) in _moveSet)
            {
                var b = Layout.GetBounds(obj);
                overrides[obj.Name] = new LayoutOverride(b with { Left = left + dx, Top = top + dy });
            }

            var result = _layout.ComputeWithOverrides(Document, overrides);
            var geometry = new SurfaceGeometry(Document, result, _layout.Renderers, _settings.ShowComponentCaptions);
            _preview = (Document.Version, result, geometry, new HitTester(geometry));
        }

        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The drop: one "Move" undo unit with the new positions and the layout commit (DSG-LAY-040); nothing when no position
    /// changes (DSG-SUR-059). Tray positions do not go below 0.
    /// </summary>
    private void Drop()
    {
        var (dx, dy) = _moveDelta;
        var (xPath, yPath) = _moveTray ? ("DesignLeft", "DesignTop") : ("Left", "Top");
        var changes = new List<FormChange>();
        foreach (var (obj, left, top) in _moveSet)
        {
            var (x, y) = _moveTray ? (Math.Max(0, left + dx), Math.Max(0, top + dy)) : (left + dx, top + dy);
            if (x != left || !PropertyAccess.IsStored(obj, xPath))
            {
                changes.Add(new SetProperty(obj.Name, xPath, new IntegerValue(x, false)));
            }

            if (y != top || !PropertyAccess.IsStored(obj, yPath))
            {
                changes.Add(new SetProperty(obj.Name, yPath, new IntegerValue(y, false)));
            }
        }

        var moved = _moveSet.Exists(m => _moveTray
            ? (Math.Max(0, m.Left + dx), Math.Max(0, m.Top + dy)) != (m.Left, m.Top)
            : (dx, dy) != (0, 0));
        ResetPress();
        if (!moved)
        {
            return;
        }

        var selection = Selection.ToSnapshot();
        var undo = Editor.Apply(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        Undo.Push(new DesignerUndoUnit(UndoTitles.Move, [.. changes, .. commitRedo], [.. commitUndo, .. undo], [], selection, selection));
    }

    /// <summary>The effective <c>Align</c> is not <c>alNone</c> (aligned controls cannot be moved by drag, DSG-SUR-050).</summary>
    private static bool IsAligned(FormObject obj) =>
        (obj.FindProperty("Align") is not null || obj.Info?.FindProperty("Align") is not null)
        && PropertyAccess.GetEffective(obj, "Align") is IdentValue { Name: var align }
        && !string.Equals(align, "alNone", StringComparison.OrdinalIgnoreCase);

    private void UpdateLasso(PointerInput input)
    {
        var (x, y) = SurfaceGeometry.Round(input.X, input.Y);
        LassoRect = new Rect(Math.Min(x, _downAt.X), Math.Min(y, _downAt.Y), Math.Abs(x - _downAt.X), Math.Abs(y - _downAt.Y));
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// DSG-SUR-036: the direct children of the lasso container (the tray items for a tray lasso) whose bounds intersect
    /// the lasso with an area &gt; 0, in creation order; added with Shift; no hit → { the container } ({ form } for the tray).
    /// </summary>
    private void FinishLasso(bool add)
    {
        var lasso = LassoRect!.Value;
        var g = Geometry;
        var candidates = _lassoContainer is { } container
            ? container.Children.Where(g.IsControl)
            : g.TrayItems;
        var hits = candidates.Where(c => Intersects(g.GetBounds(c), lasso)).ToList();
        if (hits.Count == 0)
        {
            Selection.Select(_lassoContainer ?? Document.Root);
        }
        else if (add && !Selection.IsFormOnly)
        {
            Selection.Select([.. Selection.Items, .. hits.Where(o => !Selection.Contains(o))]);
        }
        else
        {
            Selection.Select(hits);
        }

        ResetPress();
    }

    private static bool Intersects(Rect a, Rect b) =>
        Math.Min(a.Right, b.Right) > Math.Max(a.Left, b.Left) && Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Top, b.Top);

    private void PressLeft(PointerInput input)
    {
        var alt = input.Modifiers.HasFlag(InputModifiers.Alt);
        var previous = alt && _lastAltPick is { } pick && ReferenceEquals(pick, Selection.Primary) ? pick : null;
        var hit = Current.HitTester.HitTest(input.X, input.Y, Selection.Items, alt, previous, _settings.HandleSize);
        _lastAltPick = alt && hit.Kind is HitKind.Control or HitKind.FormClient ? hit.Object : null;

        State = InteractionState.PressPending;
        _downAt = SurfaceGeometry.Round(input.X, input.Y);
        _downModifiers = input.Modifiers;
        _dragging = false;
        _pressHit = hit;
        _pressedOnHandle = hit.Kind is HitKind.Handle or HitKind.FormHandle;
        _pressed = _pressedOnHandle ? hit.Object : Target(hit);
        _selectOnRelease = false;
        if (_pressedOnHandle)
        {
            return;
        }

        if (input.Modifiers.HasFlag(InputModifiers.Shift) || input.Modifiers.HasFlag(InputModifiers.Ctrl))
        {
            _selectOnRelease = true; // DSG-SUR-021 is a click rule: applied on release
        }
        else if (_pressed!.Parent is null)
        {
            _selectOnRelease = true; // the form cannot be dragged: a click selects it, a drag (lasso) leaves the selection
        }
        else if (!Selection.Contains(_pressed!))
        {
            Selection.Select(_pressed!); // DSG-SUR-041: so that a following drag moves it
        }
        else
        {
            _selectOnRelease = true;
        }
    }

    private void Release()
    {
        if (!_dragging && !_pressedOnHandle && _selectOnRelease && _pressed is { } obj)
        {
            if (_downModifiers.HasFlag(InputModifiers.Shift) || _downModifiers.HasFlag(InputModifiers.Ctrl))
            {
                Selection.Toggle(obj);
            }
            else
            {
                Selection.Select(obj);
            }
        }

        ResetPress();
    }

    /// <summary>UI-DSG-080 / DSG-SUR-043: an unselected component becomes the only selection, then the context menu opens.</summary>
    private void PressRight(PointerInput input)
    {
        ResetPress();
        var hit = Current.HitTester.HitTest(input.X, input.Y, Selection.Items, handleSize: _settings.HandleSize);
        var target = hit.Kind is HitKind.Handle or HitKind.FormHandle ? hit.Object! : Target(hit);
        if (!Selection.Contains(target))
        {
            Selection.Select(target);
        }

        _host.ShowContextMenu(input.X, input.Y);
    }

    /// <summary>The component a click selects: the hit component; the form for its client area, frame, tray background and backdrop (DSG-SUR-025).</summary>
    private FormObject Target(HitResult hit) => hit.Object ?? Document.Root;

    private void ResetPress()
    {
        State = InteractionState.Idle;
        _pressed = null;
        _pressedOnHandle = false;
        _selectOnRelease = false;
        _dragging = false;
        _lassoContainer = null;
        _moveSet = [];
        _moveDelta = (0, 0);
        if (_preview is not null)
        {
            _preview = null;
            Invalidated?.Invoke(this, EventArgs.Empty);
        }

        if (LassoRect is not null)
        {
            LassoRect = null;
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }
}
