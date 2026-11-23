using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Surface;

/// <summary>
/// The interaction logic of the design surface (design-surface.md; no UI dependency, DSG-ARCH-003): selection and the
/// click rules (D-035), the selection commands (D-036), the lasso (D-037), move drags with snapping and re-parenting
/// (D-038 … D-041), resize drags incl. the form and child re-flow (D-042, D-043), keyboard nudging (D-044), tab order
/// (D-045, D-046), delete (D-047), clipboard (D-048, D-049), Ctrl-drag copy (D-050), the align commands (D-051), z-order
/// (D-052), palette placement (D-053), the default event handler (D-054) and read-only forms / preserved items (D-055).
/// </summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>DSG-SUR-040: a drag begins after this many DIP of movement on either axis.</summary>
    public const int DragThreshold = 4;

    private readonly LayoutEngine _layout;
    private readonly IDesignerSettings _settings;
    private readonly IDesignerHost _host;
    private readonly TimeProvider _time;
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
    private List<MoveMember> _moveSet = [];

    // The resize drag (§7): the members with their original bounds, the primary and its handle, the current bounds.
    private List<ResizeMember> _resizeSet = [];
    private List<(FormObject Object, Rect Bounds)> _resizeBounds = [];
    private FormObject? _resizePrimary;

    private (FormObject? Container, int Left, int Top) _moveTarget;
    private FormObject? _moveAnchor;
    private bool _moveTray;
    private (int X, int Y) _moveDelta;
    private (int Version, LayoutResult Result, SurfaceGeometry Geometry, HitTester HitTester)? _preview;

    public DesignSurfaceController(FormDocument document, FormEditor editor, DesignerUndoStack undo, ComponentCatalog catalog,
        LayoutEngine layout, IDesignerSettings settings, IDesignerHost host, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
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
        if (State == InteractionState.TabOrder)
        {
            if (input is { Kind: PointerKind.Down, Button: PointerButton.Left })
            {
                TabOrderPress(input); // DSG-SUR-092, 093: no selection, no drag, no context menu in the mode
            }

            return;
        }

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
                    if (_placeClass is not null)
                    {
                        StartPlacementDrag(input);
                    }
                    else
                    {
                        StartDrag(input);
                    }
                }

                break;
            case PointerKind.Move when State == InteractionState.Placing:
                UpdatePlacement(input);
                break;
            case PointerKind.Up when input.Button == PointerButton.Left && State == InteractionState.Placing:
                UpdatePlacement(input);
                FinishPlacement();
                break;
            case PointerKind.Up when input.Button == PointerButton.Left && State == InteractionState.PressPending && _placeClass is not null:
                FinishPlacement();
                break;
            case PointerKind.Move when State == InteractionState.Resizing:
                UpdateResize(input);
                break;
            case PointerKind.Up when input.Button == PointerButton.Left && State == InteractionState.Resizing:
                UpdateResize(input);
                DropResize();
                break;
            case PointerKind.Move when State == InteractionState.Moving:
                UpdateMove(input);
                break;
            case PointerKind.Up when input.Button == PointerButton.Left && State == InteractionState.Moving:
                UpdateMove(input);
                if (input.Modifiers.HasFlag(InputModifiers.Ctrl))
                {
                    DropCopy();
                }
                else
                {
                    Drop();
                }

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
        if (State == InteractionState.TabOrder)
        {
            return TabOrderKey(input);
        }

        switch (input.Key)
        {
            case DesignKey.Escape:
                return Escape();
            case DesignKey.Tab:
                Execute(input.Modifiers.HasFlag(InputModifiers.Shift) ? Commands.SelectPrevious : Commands.SelectNext);
                return true;
            case DesignKey.Up or DesignKey.Down when input.Modifiers == (InputModifiers.Ctrl | InputModifiers.Shift):
                var zOrder = input.Key == DesignKey.Up ? Commands.BringToFront : Commands.SendToBack; // §9
                if (!CanExecute(zOrder))
                {
                    return false;
                }

                Execute(zOrder);
                return true;
            case DesignKey.Left or DesignKey.Right or DesignKey.Up or DesignKey.Down:
                return Arrow(input.Key, input.Modifiers);
            case DesignKey.Enter when input.Modifiers == InputModifiers.None:
                return RunDefaultAction(); // §9, §11.4
            case DesignKey.Delete when input.Modifiers == InputModifiers.None:
                if (!CanExecute(Commands.Delete))
                {
                    return false;
                }

                Execute(Commands.Delete);
                return true;
            default:
                return false;
        }
    }

    /// <summary>DSG-SUR-003: on a read-only form only the selection commands and Copy can run.</summary>
    public bool CanExecute(string commandId) => (!IsReadOnly || IsReadOnlySafe(commandId)) && CanExecuteCore(commandId);

    private bool CanExecuteCore(string commandId) => commandId switch
    {
        Commands.SelectAll or Commands.SelectNext or Commands.SelectPrevious => true,
        Commands.SelectParent => Selection.Primary.Parent is not null,
        Commands.TabOrder => !IsPointerActive,
        Commands.Delete => CanDelete,
        Commands.Copy => CanCopy,
        Commands.Cut => CanCut,
        Commands.Paste => CanPaste,
        Commands.Undo => Undo.CanUndo && !IsPointerActive, // DSG-SUR-171
        Commands.Redo => Undo.CanRedo && !IsPointerActive,
        Commands.CreateDefaultHandler => CanCreateDefaultHandler,
        Commands.BringToFront => CanZOrder(front: true),
        Commands.SendToBack => CanZOrder(front: false),
        _ when Commands.Arrange.Contains(commandId) => CanArrange(commandId),
        _ => false,
    };

    private bool IsPointerActive => State is InteractionState.PressPending or InteractionState.Moving or InteractionState.Resizing
        or InteractionState.Lasso or InteractionState.Placing;

    public void Execute(string commandId, string? argument = null)
    {
        if (!CanExecute(commandId))
        {
            return;
        }

        if (commandId is Commands.Undo or Commands.Redo)
        {
            ExitTabOrderMode(); // DSG-SUR-170
            if (_host.ArmedPaletteClass is not null)
            {
                _host.Disarm();
            }
        }

        switch (commandId)
        {
            case Commands.TabOrder:
                ToggleTabOrderMode();
                break;
            case Commands.Delete:
                Delete();
                break;
            case Commands.Copy:
                Copy();
                break;
            case Commands.Cut:
                Cut();
                break;
            case Commands.Paste:
                Paste();
                break;
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
            case Commands.CreateDefaultHandler:
                CreateDefaultHandler();
                break;
            case Commands.BringToFront or Commands.SendToBack:
                ZOrder(commandId == Commands.BringToFront);
                break;
            case var _ when Commands.Arrange.Contains(commandId):
                Arrange(commandId);
                break;
            case Commands.Undo when Undo.Undo() is { } unit: // DSG-SUR-170
                Editor.Apply(unit.Undo);
                if (unit.CodeEdits.Count > 0 && !_host.CodeSync.RevertEdits(unit.CodeEdits))
                {
                    _host.SetStatus(StatusIds.UndoCodeSkipped); // DSG-CS-101
                }

                Selection.Set(unit.SelectionBefore.Names);
                break;
            case Commands.Redo when Undo.Redo() is { } unit:
                Editor.Apply(unit.Redo);
                if (unit.CodeOps.Count > 0)
                {
                    unit.CodeEdits = RedoCode(unit.CodeOps); // DSG-CS-102
                }

                Selection.Set(unit.SelectionAfter.Names);
                break;
        }
    }

    /// <summary>DSG-SUR-104: Escape cancels the first applicable mode, else selects the parent (unhandled on the form).</summary>
    private bool Escape()
    {
        if (State is InteractionState.Lasso or InteractionState.Moving or InteractionState.Resizing or InteractionState.Placing
            || (State == InteractionState.PressPending && _placeClass is not null))
        {
            ResetPress(); // DSG-SUR-037, DSG-SUR-058, DSG-SUR-087, DSG-TP-045 (the item stays armed): nothing is recorded
            return true;
        }

        if (_host.ArmedPaletteClass is not null)
        {
            _host.Disarm(); // DSG-TP-040
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

    /// <summary>
    /// During a move that would re-parent: the target container, unless it is the form root (UI-DSG-041 drop-target
    /// highlight, DSG-SUR-055); null otherwise.
    /// </summary>
    public FormObject? DropTarget { get; private set; }

    /// <summary>The guidelines of the snaps currently applied during a drag (DSG-SUR-074, UI-DSG-050); empty otherwise.</summary>
    public IReadOnlyList<Guideline> Guidelines { get; private set; } = [];

    /// <summary>The lasso rectangle in surface coordinates while a lasso is active (DSG-SUR-035), else null.</summary>
    public Rect? LassoRect { get; private set; }

    /// <summary>
    /// The drag threshold was crossed (DSG-SUR-040): a lasso on the form client area, on a container hit with Ctrl or on
    /// the tray background (DSG-SUR-035); the backdrop starts nothing; a move on a selected component; a resize on a
    /// handle. With <c>designer.lockControls</c> no move or resize starts (DSG-SUR-051, DSG-SUR-087). A drag never applies
    /// the click rule.
    /// </summary>
    private void StartDrag(PointerInput input)
    {
        _dragging = true;
        var lassoInControl = _pressHit.Kind == HitKind.Control && _downModifiers.HasFlag(InputModifiers.Ctrl)
            && _pressed is { Info.AcceptsChildren: true, IsPreserved: false };
        if (!_pressedOnHandle && (_pressHit.Kind is HitKind.FormClient or HitKind.Tray || lassoInControl))
        {
            _lassoContainer = _pressHit.Kind == HitKind.Tray ? null : _pressed;
            State = InteractionState.Lasso;
            UpdateLasso(input);
            return;
        }

        var moves = !_pressedOnHandle && _pressHit.Kind is HitKind.Control or HitKind.TrayItem && _pressed is { } p && Selection.Contains(p);
        if ((!_pressedOnHandle && !moves) || IsReadOnly)
        {
            return;
        }

        if (_settings.LockControls)
        {
            _host.SetStatus(StatusIds.Locked);
            return;
        }

        if (moves)
        {
            StartMove(_pressed!, input);
        }
        else if (_pressHit is { Kind: HitKind.Handle, Object: { } primary, Handle: { } handle })
        {
            StartResize(primary, handle, input);
        }
        else if (_pressHit is { Kind: HitKind.FormHandle, Handle: { } formHandle })
        {
            StartFormResize(formHandle, input);
        }
    }

    /// <summary>
    /// DSG-SUR-050: the effective move set: the selection after DSG-SUR-024 without the form, preserved and aligned
    /// controls; only tray items when the drag starts on a tray item, only controls otherwise. A drag that starts on an
    /// aligned control moves nothing (status <c>Msg.Designer.AlignedCannotMove</c>), nor one that starts on a preserved
    /// component (DSG-SUR-004, <c>Msg.Designer.ReadOnlyItem</c>).
    /// </summary>
    private void StartMove(FormObject anchor, PointerInput input)
    {
        var g = Geometry;
        _moveTray = g.IsTrayItem(anchor);
        if (anchor.IsPreserved)
        {
            _host.SetStatus(StatusIds.ReadOnlyItem, anchor.Name);
            return;
        }

        if (!_moveTray && IsAligned(anchor))
        {
            _host.SetStatus(StatusIds.AlignedCannotMove, anchor.Name);
            return;
        }

        _moveSet = Selection.GetEffectiveSet()
            .Where(o => _moveTray ? g.IsTrayItem(o) && !o.IsPreserved : g.IsControl(o) && !o.IsPreserved && !IsAligned(o))
            .Select(o => _moveTray
                ? new MoveMember(o, SurfaceGeometry.TrayPosition(o).X, SurfaceGeometry.TrayPosition(o).Y, 0, 0)
                : new MoveMember(o, Layout.GetBounds(o).Left, Layout.GetBounds(o).Top, g.GetBounds(o).Left, g.GetBounds(o).Top))
            .ToList();
        if (_moveSet.Count == 0)
        {
            return;
        }

        _moveAnchor = _moveSet.Exists(m => ReferenceEquals(m.Object, anchor)) ? anchor : _moveSet[0].Object;
        State = InteractionState.Moving;
        UpdateMove(input);
    }

    /// <summary>
    /// DSG-SUR-052: raw delta = pointer - down pointer; Shift forces the smaller component to 0 (each event). DSG-SUR-055:
    /// the target container under the pointer. DSG-SUR-053, DSG-SUR-056: snapping (§6) of the anchor control in the
    /// target's client coordinates, among the target's children, adjusts the delta of the whole move set. An axis forced to
    /// 0 by Shift is not snapped (the constrained move stays on its axis).
    /// </summary>
    private void UpdateMove(PointerInput input)
    {
        var (x, y) = SurfaceGeometry.Round(input.X, input.Y);
        _pointerAt = (x, y);
        CopyBadge = input.Modifiers.HasFlag(InputModifiers.Ctrl);
        var (dx, dy) = (x - _downAt.X, y - _downAt.Y);
        var (snapX, snapY) = (true, true);
        if (input.Modifiers.HasFlag(InputModifiers.Shift))
        {
            (dx, dy, snapX, snapY) = Math.Abs(dx) >= Math.Abs(dy) ? (dx, 0, true, false) : (0, dy, false, true);
        }

        _preview = null; // the target, the snapping and the preview start from the model's layout
        var g = Geometry;
        var options = SnapOptions.From(_settings, input.Modifiers.HasFlag(InputModifiers.Alt));
        var anchor = _moveAnchor!;
        IReadOnlyList<Guideline> guidelines = [];
        if (_moveTray)
        {
            var (left, top) = SurfaceGeometry.TrayPosition(anchor);
            var snapped = SnapEngine.SnapTray((left + dx, top + dy), options);
            (dx, dy) = (snapX ? snapped.X - left : dx, snapY ? snapped.Y - top : dy);
        }
        else
        {
            var target = TargetContainer((x, y), anchor);
            var origin = g.GetClientRect(target);
            var s = g.GetBounds(anchor);
            var candidate = s with { Left = s.Left + dx - origin.Left, Top = s.Top + dy - origin.Top };
            var snap = SnapEngine.SnapMove(candidate, BaselineOffset(anchor), SnapTargetOf(target, _moveSet.Select(m => m.Object)), options,
                snapX, snapY);
            (dx, dy, guidelines) = (dx + snap.Dx, dy + snap.Dy, snap.Guidelines);
            _moveTarget = (target, origin.Left, origin.Top);
            DropTarget = ReferenceEquals(target, anchor.Parent) || target.Parent is null ? null : target;
        }

        Guidelines = guidelines;
        _moveDelta = (dx, dy);
        if (!_moveTray && (_moveDelta != (0, 0) || Reparents))
        {
            var overrides = new Dictionary<string, LayoutOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in _moveSet)
            {
                var b = Layout.GetBounds(m.Object);
                overrides[m.Object.Name] = MovesToTarget(m)
                    ? new LayoutOverride(b with { Left = m.SurfaceLeft + dx - _moveTarget.Left, Top = m.SurfaceTop + dy - _moveTarget.Top },
                        ParentName: _moveTarget.Container!.Name)
                    : new LayoutOverride(b with { Left = m.Left + dx, Top = m.Top + dy });
            }

            var result = _layout.ComputeWithOverrides(Document, overrides);
            var geometry = new SurfaceGeometry(Document, result, _layout.Renderers, _settings.ShowComponentCaptions);
            _preview = (Document.Version, result, geometry, new HitTester(geometry));
        }

        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The drop re-parents: the target differs from the anchor control's parent (DSG-SUR-055).</summary>
    private bool Reparents => _moveTarget.Container is { } t && _moveAnchor is { } a && !ReferenceEquals(t, a.Parent);

    /// <summary>A member of the move set that the drop moves into the target (members already there stay in place).</summary>
    private bool MovesToTarget(MoveMember m) => Reparents && !ReferenceEquals(m.Object.Parent, _moveTarget.Container);

    /// <summary>
    /// DSG-SUR-055: the deepest container (accepts children, not preserved, not in the move set nor inside a member of it)
    /// whose client rect contains the surface point; the form when no container does; outside the form client area the
    /// anchor's current parent. Containers are searched front to back, each inside its parent's client rect.
    /// </summary>
    private FormObject TargetContainer((int X, int Y) p, FormObject anchor)
    {
        var g = Geometry;
        if (!Contains(g.FormClientRect, p))
        {
            return anchor.Parent!;
        }

        var target = Document.Root;
        while (true)
        {
            FormObject? next = null;
            for (var i = target.Children.Count - 1; i >= 0 && next is null; i--)
            {
                var c = target.Children[i];
                if (c.Info is { AcceptsChildren: true } && !c.IsPreserved && g.IsControl(c) && !_moveSet.Exists(m => ReferenceEquals(m.Object, c))
                    && !IsHiddenPage(c, g) && Contains(g.GetBounds(c), p) && Contains(g.GetClientRect(c), p))
                {
                    next = c;
                }
            }

            if (next is null)
            {
                return target;
            }

            target = next;
        }
    }

    /// <summary>An inactive tab sheet: not drawn, not hit (lead decisions 2026-11-23), so not a drop target either.</summary>
    private static bool IsHiddenPage(FormObject obj, SurfaceGeometry g) =>
        obj.Info?.FindProperty("Left") is { Stored: false } && !g.Layout.GetVisible(obj);

    private static bool Contains(Rect r, (int X, int Y) p) => p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    /// <summary>
    /// DSG-SUR-072: the guideline references in <paramref name="parent"/>: its visual children that are not dragged and not
    /// preserved, with their baselines, and its client size. From the model's layout (call with no preview active).
    /// </summary>
    private SnapTarget SnapTargetOf(FormObject parent, IEnumerable<FormObject> dragged)
    {
        var g = Geometry;
        var excluded = new HashSet<FormObject>(dragged, ReferenceEqualityComparer.Instance);
        var siblings = parent.Children
            .Where(c => g.IsControl(c) && !c.IsPreserved && !excluded.Contains(c))
            .Select(c =>
            {
                var b = Layout.GetBounds(c);
                return new SnapReference(c, b, BaselineOffset(c) is { } offset ? b.Top + offset : null);
            })
            .ToList();
        var client = g.GetClientRect(parent);
        return new SnapTarget(parent, client.Width, client.Height, siblings);
    }

    /// <summary>The text baseline below the control's top (<c>IDesignRenderer.GetBaseline</c>); null for controls without text.</summary>
    private int? BaselineOffset(FormObject obj)
    {
        var ctx = RenderContext.FromLayout(obj, Layout);
        return _layout.Renderers.Resolve(ctx.Info.RendererId).GetBaseline(ctx);
    }

    /// <summary>
    /// The drop: one "Move" undo unit with the re-parenting (appended to the target in creation order, Left/Top converted
    /// so the surface position is the dropped one), the new positions and the layout commit (DSG-LAY-040); nothing when no
    /// position or parent changes (DSG-SUR-059). Tray positions do not go below 0.
    /// </summary>
    private void Drop()
    {
        var (dx, dy) = _moveDelta;
        var (xPath, yPath) = _moveTray ? ("DesignLeft", "DesignTop") : ("Left", "Top");
        var reparents = !_moveTray && Reparents;
        var target = _moveTarget;
        var changes = new List<FormChange>();
        var appended = 0;
        foreach (var m in _moveSet)
        {
            var (obj, left, top) = (m.Object, m.Left, m.Top);
            int x, y;
            if (!_moveTray && MovesToTarget(m))
            {
                changes.Add(new MoveObject(obj.Name, target.Container!.Name, target.Container.Children.Count + appended++));
                (x, y, left, top) = (m.SurfaceLeft + dx - target.Left, m.SurfaceTop + dy - target.Top, int.MinValue, int.MinValue);
            }
            else
            {
                (x, y) = _moveTray ? (Math.Max(0, left + dx), Math.Max(0, top + dy)) : (left + dx, top + dy);
            }

            if (x != left || !PropertyAccess.IsStored(obj, xPath))
            {
                changes.Add(new SetProperty(obj.Name, xPath, new IntegerValue(x, false)));
            }

            if (y != top || !PropertyAccess.IsStored(obj, yPath))
            {
                changes.Add(new SetProperty(obj.Name, yPath, new IntegerValue(y, false)));
            }
        }

        if (reparents)
        {
            // DSG-SUR-096: the old parents close their gaps, the re-parented controls take the last positions in the target.
            var entering = _moveSet.Where(MovesToTarget).Select(m => m.Object).ToList();
            foreach (var oldParent in entering.Where(TabOrderRules.IsFocusable).Select(o => o.Parent!).Distinct())
            {
                changes.AddRange(TabOrderRules.Renumber(oldParent, entering));
            }

            if (entering.Exists(TabOrderRules.IsFocusable))
            {
                changes.AddRange(TabOrderRules.Renumber(target.Container!, appended: entering));
            }
        }

        var moved = reparents || _moveSet.Exists(m => _moveTray
            ? (Math.Max(0, m.Left + dx), Math.Max(0, m.Top + dy)) != (m.Left, m.Top)
            : (dx, dy) != (0, 0));
        ResetPress();
        if (!moved)
        {
            return;
        }

        var selection = Selection.ToSnapshot();
        var undo = ApplyEdit(changes);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        PushUnit(new DesignerUndoUnit(UndoTitles.Move, [.. changes, .. commitRedo], [.. commitUndo, .. undo], [], selection, selection));
    }

    /// <summary>
    /// DSG-SUR-057: Ctrl held at the drop copies instead of moving — the originals stay, copies are created where the move
    /// would have put the members (in the drop target when the drop re-parents) with the paste naming and event rules, as
    /// one "Copy" unit; the copies become the selection. Nothing when the move would change nothing (DSG-SUR-059). The
    /// clipboard is not touched.
    /// </summary>
    private void DropCopy()
    {
        var (dx, dy) = _moveDelta;
        var reparents = !_moveTray && Reparents;
        var target = _moveTarget;
        var placements = _moveSet.Select(m => !_moveTray && MovesToTarget(m)
                ? new Placement(m.Object, target.Container!, (m.SurfaceLeft + dx - target.Left, m.SurfaceTop + dy - target.Top))
                : new Placement(m.Object, m.Object.Parent!,
                    _moveTray ? (Math.Max(0, m.Left + dx), Math.Max(0, m.Top + dy)) : (m.Left + dx, m.Top + dy)))
            .ToList();
        var changed = reparents || _moveSet.Exists(m => _moveTray
            ? (Math.Max(0, m.Left + dx), Math.Max(0, m.Top + dy)) != (m.Left, m.Top)
            : (dx, dy) != (0, 0));
        ResetPress();
        if (changed)
        {
            AddCopies(placements, UndoTitles.Copy);
        }
    }

    /// <summary>
    /// DSG-SUR-080: a drag on a handle of the primary resizes all selected resizable controls (not the form, tray items,
    /// preserved components or controls with computed geometry) by the primary's edge deltas. DSG-SUR-084: of an aligned
    /// control only the edges not fixed by <c>Align</c> move; a handle that moves none of them is inert.
    /// </summary>
    private void StartResize(FormObject primary, HandleKind handle, PointerInput input)
    {
        var g = Geometry;
        var edges = HandleEdges(handle);
        _resizeSet = Selection.Items
            .Where(o => g.IsControl(o) && !o.IsPreserved && o.Info?.FindProperty("Left") is not { Stored: false })
            .Select(o => new ResizeMember(o, Layout.GetBounds(o), edges & FreeEdges(o)))
            .ToList();
        if (_resizeSet.Find(m => ReferenceEquals(m.Object, primary)) is not { Edges: not SnapEdges.None })
        {
            if (primary.IsPreserved)
            {
                _host.SetStatus(StatusIds.ReadOnlyItem, primary.Name); // DSG-SUR-004
            }

            _resizeSet = [];
            return;
        }

        _resizePrimary = primary;
        State = InteractionState.Resizing;
        UpdateResize(input);
    }

    /// <summary>DSG-SUR-085: a drag on the form's E, S or SE handle changes its client size.</summary>
    private void StartFormResize(HandleKind handle, PointerInput input)
    {
        var root = Document.Root;
        _resizeSet = [new ResizeMember(root, Layout.GetBounds(root), HandleEdges(handle) & (SnapEdges.Right | SnapEdges.Bottom))];
        _resizePrimary = root;
        State = InteractionState.Resizing;
        UpdateResize(input);
    }

    private static SnapEdges HandleEdges(HandleKind h) =>
        (h is HandleKind.NW or HandleKind.W or HandleKind.SW ? SnapEdges.Left : 0)
        | (h is HandleKind.NE or HandleKind.E or HandleKind.SE ? SnapEdges.Right : 0)
        | (h is HandleKind.NW or HandleKind.N or HandleKind.NE ? SnapEdges.Top : 0)
        | (h is HandleKind.SW or HandleKind.S or HandleKind.SE ? SnapEdges.Bottom : 0);

    /// <summary>DSG-SUR-084: the edges <c>Align</c> leaves free — <c>alTop</c>: bottom; <c>alBottom</c>: top; <c>alLeft</c>: right; <c>alRight</c>: left; <c>alClient</c>: none.</summary>
    private static SnapEdges FreeEdges(FormObject obj)
    {
        const SnapEdges all = SnapEdges.Left | SnapEdges.Right | SnapEdges.Top | SnapEdges.Bottom;
        if (!IsAligned(obj) || PropertyAccess.GetEffective(obj, "Align") is not IdentValue { Name: var align })
        {
            return all;
        }

        return align.ToLowerInvariant() switch
        {
            "altop" => SnapEdges.Bottom,
            "albottom" => SnapEdges.Top,
            "alleft" => SnapEdges.Right,
            "alright" => SnapEdges.Left,
            "alclient" => SnapEdges.None,
            _ => all, // alCustom and unknown values: the layout engine decides
        };
    }

    /// <summary>
    /// DSG-SUR-081 … 083: the dragged edges of the primary follow the pointer, then snapping (§6, dragged edges only), then
    /// the size limits (the opposite edge never moves, no flipping); with Shift on a corner the axis with the larger
    /// relative change drives and the other keeps the original aspect ratio (snapping on the driving axis only). Every
    /// other member gets the primary's edge deltas, clamped by its own limits. DSG-SUR-086: the preview applies the new
    /// bounds as explicit changes, so containers re-flow their children (anchors against the baselines, Align).
    /// </summary>
    private void UpdateResize(PointerInput input)
    {
        var (x, y) = SurfaceGeometry.Round(input.X, input.Y);
        _pointerAt = (x, y);
        var (dx, dy) = (x - _downAt.X, y - _downAt.Y);
        _preview = null; // the snapping and the preview start from the model's layout

        var primary = _resizePrimary!;
        var p = _resizeSet.Find(m => ReferenceEquals(m.Object, primary));
        var (b, edges) = (p.Original, p.Edges);
        Rect r;
        if (primary.Parent is null)
        {
            r = FormResized(b, edges, dx, dy, input.Modifiers.HasFlag(InputModifiers.Alt));
            _resizeBounds = [(primary, r)];
        }
        else
        {
            var raw = Resized(b, edges, dx, dx, dy, dy);

            // Shift on a corner: the axis with the larger relative change (|Δw| / w vs |Δh| / h) drives.
            var corner = (edges & (SnapEdges.Left | SnapEdges.Right)) != 0 && (edges & (SnapEdges.Top | SnapEdges.Bottom)) != 0;
            var keepRatio = input.Modifiers.HasFlag(InputModifiers.Shift) && corner && b.Width > 0 && b.Height > 0;
            var widthDrives = (long)Math.Abs(raw.Width - b.Width) * b.Height >= (long)Math.Abs(raw.Height - b.Height) * b.Width;
            var snapEdges = !keepRatio ? edges
                : edges & (widthDrives ? SnapEdges.Left | SnapEdges.Right : SnapEdges.Top | SnapEdges.Bottom);
            var options = SnapOptions.From(_settings, input.Modifiers.HasFlag(InputModifiers.Alt));
            var snap = SnapEngine.SnapResize(raw, snapEdges, SnapTargetOf(primary.Parent!, _resizeSet.Select(m => m.Object)), options);
            Guidelines = snap.Guidelines;
            var snapped = Resized(b, edges, dx + snap.Dx, dx + snap.Dx, dy + snap.Dy, dy + snap.Dy);
            r = Clamp(primary, snapped, edges);
            if (keepRatio)
            {
                r = widthDrives
                    ? Clamp(primary, WithHeight(r, edges, RoundRatio(r.Width, b.Height, b.Width)), edges & (SnapEdges.Top | SnapEdges.Bottom))
                    : Clamp(primary, WithWidth(r, edges, RoundRatio(r.Height, b.Width, b.Height)), edges & (SnapEdges.Left | SnapEdges.Right));
            }

            var (dl, dr, dt, db) = (r.Left - b.Left, r.Right - b.Right, r.Top - b.Top, r.Bottom - b.Bottom);
            _resizeBounds = _resizeSet
                .Select(m => (m.Object, ReferenceEquals(m.Object, primary) ? r
                    : Clamp(m.Object, Resized(m.Original, m.Edges, dl, dr, dt, db), m.Edges)))
                .ToList();
        }

        var result = _layout.ComputeWithResizes(Document, _resizeBounds.Select(m => (m.Object.Name, m.Bounds)).ToList());
        var geometry = new SurfaceGeometry(Document, result, _layout.Renderers, _settings.ShowComponentCaptions);
        _preview = (Document.Version, result, geometry, new HitTester(geometry));
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>DSG-SUR-085: the new client size — dragged dimensions follow the pointer, grid snapping only (not with Alt), at least 16 × 16.</summary>
    private Rect FormResized(Rect b, SnapEdges edges, int dx, int dy, bool alt)
    {
        var grid = _settings.SnapToGrid && !alt;
        int Size(int value, int g) => Math.Max(MinFormClientSize, grid ? SnapEngine.ToGrid(value, g) : value);
        var width = edges.HasFlag(SnapEdges.Right) ? Size(b.Width + dx, _settings.GridSizeX) : b.Width;
        var height = edges.HasFlag(SnapEdges.Bottom) ? Size(b.Height + dy, _settings.GridSizeY) : b.Height;
        return b with { Width = width, Height = height };
    }

    /// <summary>DSG-SUR-085: the minimum client width and height of a form resized by handle.</summary>
    private const int MinFormClientSize = 16;

    /// <summary>The bounds with the dragged edges moved: left by <paramref name="dl"/>, right by <paramref name="dr"/>, … (may be inverted).</summary>
    private static Rect Resized(Rect b, SnapEdges edges, int dl, int dr, int dt, int db)
    {
        var left = b.Left + (edges.HasFlag(SnapEdges.Left) ? dl : 0);
        var right = b.Right + (edges.HasFlag(SnapEdges.Right) ? dr : 0);
        var top = b.Top + (edges.HasFlag(SnapEdges.Top) ? dt : 0);
        var bottom = b.Bottom + (edges.HasFlag(SnapEdges.Bottom) ? db : 0);
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>DSG-SUR-082: width/height ≥ 1 and within <c>Constraints</c> (when &gt; 0); only the dragged edge of an axis moves.</summary>
    private static Rect Clamp(FormObject obj, Rect r, SnapEdges edges)
    {
        static int Limit(FormObject o, string path) => o.FindProperty(path) is { Value: IntegerValue { Value: > 0 and <= int.MaxValue } v } ? (int)v.Value : 0;
        static int Size(int value, int min, int max) => Math.Max(Math.Max(1, min), max > 0 ? Math.Min(value, max) : value);

        if ((edges & (SnapEdges.Left | SnapEdges.Right)) != 0)
        {
            r = WithWidth(r, edges, Size(r.Width, Limit(obj, "Constraints.MinWidth"), Limit(obj, "Constraints.MaxWidth")));
        }

        if ((edges & (SnapEdges.Top | SnapEdges.Bottom)) != 0)
        {
            r = WithHeight(r, edges, Size(r.Height, Limit(obj, "Constraints.MinHeight"), Limit(obj, "Constraints.MaxHeight")));
        }

        return r;
    }

    /// <summary>A new width, keeping the edge that is not dragged (the right edge when the left one is dragged).</summary>
    private static Rect WithWidth(Rect r, SnapEdges edges, int width) =>
        edges.HasFlag(SnapEdges.Left) ? new Rect(r.Right - width, r.Top, width, r.Height) : r with { Width = width };

    private static Rect WithHeight(Rect r, SnapEdges edges, int height) =>
        edges.HasFlag(SnapEdges.Top) ? new Rect(r.Left, r.Bottom - height, r.Width, height) : r with { Height = height };

    /// <summary>DSG-SUR-083: <c>round(driven × num / den)</c>, halves away from zero.</summary>
    private static int RoundRatio(int driven, int num, int den) =>
        (int)Math.Round((double)driven * num / den, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The drop of a resize: one "Resize" undo unit with the form's new client size (form resize) and every bounds of
    /// the preview layout that differs from the stored one — the resized controls and the re-flowed children
    /// (DSG-SUR-085, DSG-SUR-086) — then the layout commit of the result (DSG-LAY-040); nothing when nothing changed.
    /// </summary>
    private void DropResize()
    {
        var changed = _resizeBounds.Exists(m => m.Bounds != _resizeSet.Find(s => ReferenceEquals(s.Object, m.Object)).Original);
        var preview = _preview?.Result;
        var form = _resizeBounds is [{ Object.Parent: null } f] ? f.Bounds : (Rect?)null;
        ResetPress();
        if (!changed || preview is null)
        {
            return;
        }

        var selection = Selection.ToSnapshot();
        var (redo, undo) = ApplyResize(form, preview);
        PushUnit(new DesignerUndoUnit(UndoTitles.Resize, redo, undo, [], selection, selection));
    }

    /// <summary>
    /// Stores a resize: the form's new client size (when <paramref name="formClient"/> is given), every bounds of
    /// <paramref name="preview"/> that differs from the stored one, then the layout commit; returns the unit's changes.
    /// </summary>
    private (List<FormChange> Redo, List<FormChange> Undo) ApplyResize(Rect? formClient, LayoutResult preview)
    {
        var changes = new List<FormChange>();
        if (formClient is { } client)
        {
            var root = Document.Root;
            if (client.Width != PropertyAccess.GetInt(root, "ClientWidth"))
            {
                changes.Add(new SetProperty(root.Name, "ClientWidth", new IntegerValue(client.Width, false)));
            }

            if (client.Height != PropertyAccess.GetInt(root, "ClientHeight"))
            {
                changes.Add(new SetProperty(root.Name, "ClientHeight", new IntegerValue(client.Height, false)));
            }
        }

        var undo = ApplyEdit(changes);
        var (flowRedo, flowUndo) = LayoutCommit.StoreDifferences(Editor, preview);
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        return ([.. changes, .. flowRedo, .. commitRedo], [.. commitUndo, .. flowUndo, .. undo]);
    }

    /// <summary>A member of a resize drag: bounds at drag start (the form: its client size) and the edges it moves.</summary>
    private readonly record struct ResizeMember(FormObject Object, Rect Original, SnapEdges Edges);

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
        if (_host.ArmedPaletteClass is { } armed)
        {
            PlacementPress(input, armed); // tool-palette.md §5.2
            return;
        }

        var alt = input.Modifiers.HasFlag(InputModifiers.Alt);
        var previous = alt && _lastAltPick is { } pick && ReferenceEquals(pick, Selection.Primary) ? pick : null;
        var hit = Current.HitTester.HitTest(input.X, input.Y, Selection.Items, alt, previous, _settings.HandleSize);
        _lastAltPick = alt && hit.Kind is HitKind.Control or HitKind.FormClient ? hit.Object : null;
        if (input.ClickCount == 2 && hit.Kind is HitKind.Control or HitKind.TrayItem or HitKind.FormClient or HitKind.FormFrame)
        {
            // DSG-SUR-042: a double-click on a component runs its default action (a container is not "entered").
            ResetPress();
            Selection.Select(Target(hit));
            RunDefaultAction();
            return;
        }

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
        if (_host.ArmedPaletteClass is not null)
        {
            _host.Disarm(); // DSG-TP-041: a right-click on the surface disarms
            return;
        }

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
        _moveAnchor = null;
        _resizeSet = [];
        _resizeBounds = [];
        _resizePrimary = null;
        _moveTarget = default;
        DropTarget = null;
        _moveDelta = (0, 0);
        CopyBadge = false;
        Guidelines = [];
        _placeClass = null;
        _placeParent = null;
        if (PlacementRect is not null)
        {
            PlacementRect = null;
            Invalidated?.Invoke(this, EventArgs.Empty);
        }

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

    /// <summary>A member of the move set: stored-as-computed Left/Top (tray: DesignLeft/DesignTop) and the surface origin at drag start.</summary>
    private readonly record struct MoveMember(FormObject Object, int Left, int Top, int SurfaceLeft, int SurfaceTop);
}
