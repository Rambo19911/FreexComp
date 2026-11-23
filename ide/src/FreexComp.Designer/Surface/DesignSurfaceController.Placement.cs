using System.Globalization;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>Placing components from the Tool Palette (tool-palette.md §5.2, §5.3, DSG-TP-043 … 055).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>DSG-TP-045: below this width or height (after snapping) a placement drag is treated as a click.</summary>
    private const int MinPlacementSize = 4;

    // The armed class of the pending placement press (null: the press is not a placement) and its target parent.
    private string? _placeClass;
    private FormObject? _placeParent;

    /// <summary>The placement rectangle (surface coordinates) while a placement drag is active (DSG-TP-045), else null.</summary>
    public Rect? PlacementRect { get; private set; }

    // The placement drag's bounds in the target's client coordinates.
    private Rect _placeBounds;

    /// <summary>
    /// DSG-TP-042, 046: places <paramref name="className"/> at the default position without arming (palette double-click or
    /// Enter). Nothing for an unknown or abstract class, a read-only form (DSG-TP-055) or during a drag.
    /// </summary>
    public void PlaceAtDefaultPosition(string className)
    {
        if (IsReadOnly || IsPointerActive || State == InteractionState.TabOrder || PlaceableInfo(className) is not { } info)
        {
            return;
        }

        PlaceDefault(info);
    }

    private ComponentInfo? PlaceableInfo(string className) => Catalog.Find(className) is { IsAbstract: false } info ? info : null;

    /// <summary>A left press while a palette item is armed: a placement press (refused silently on a read-only form, DSG-TP-055).</summary>
    private void PlacementPress(PointerInput input, string className)
    {
        if (IsReadOnly)
        {
            return;
        }

        ResetPress();
        State = InteractionState.PressPending;
        _placeClass = className;
        _downAt = SurfaceGeometry.Round(input.X, input.Y);
        _downModifiers = input.Modifiers;
        _placeParent = PlaceableInfo(className) is { Kind: not ComponentKind.NonVisual } info && Contains(Geometry.FormClientRect, _downAt)
            ? PlacementParent(_downAt, info, report: false)
            : null;
    }

    /// <summary>The drag threshold was crossed during a placement press: a visual component starts the placement drag (DSG-TP-045).</summary>
    private void StartPlacementDrag(PointerInput input)
    {
        _dragging = true;
        if (_placeParent is null)
        {
            return; // non-visual, outside the form client area or nowhere to place: the release counts as a click
        }

        State = InteractionState.Placing;
        UpdatePlacement(input);
    }

    /// <summary>
    /// DSG-TP-045: the rectangle from the press point to the pointer, normalized, in the target's client coordinates; both
    /// corners snapped (grid and guidelines, resize rules: the top-left corner as dragged left/top edges, the other as
    /// dragged right/bottom edges); Alt suspends snapping. The target is highlighted.
    /// </summary>
    private void UpdatePlacement(PointerInput input)
    {
        var parent = _placeParent!;
        var g = Geometry;
        var (x, y) = SurfaceGeometry.Round(input.X, input.Y);
        var a = g.SurfaceToParentClient(parent, _downAt);
        var b = g.SurfaceToParentClient(parent, (x, y));
        var raw = new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        var options = SnapOptions.From(_settings, input.Modifiers.HasFlag(InputModifiers.Alt));
        var target = SnapTargetOf(parent, []);
        var low = SnapEngine.SnapResize(raw, SnapEdges.Left | SnapEdges.Top, target, options);
        var high = SnapEngine.SnapResize(raw, SnapEdges.Right | SnapEdges.Bottom, target, options);
        var (left, top) = (raw.Left + low.Dx, raw.Top + low.Dy);
        var (right, bottom) = (raw.Right + high.Dx, raw.Bottom + high.Dy);
        _placeBounds = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        var origin = g.GetClientRect(parent);
        PlacementRect = _placeBounds with { Left = _placeBounds.Left + origin.Left, Top = _placeBounds.Top + origin.Top };
        Guidelines = [.. low.Guidelines, .. high.Guidelines];
        DropTarget = parent.Parent is null ? null : parent;
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The release of a placement press or drag: the drag's bounds when large enough, else a click at the press point.</summary>
    private void FinishPlacement()
    {
        var (className, parent, bounds, dragged) = (_placeClass!, _placeParent, _placeBounds, State == InteractionState.Placing);
        var (at, modifiers) = (_downAt, _downModifiers);
        ResetPress();
        if (PlaceableInfo(className) is not { } info)
        {
            return;
        }

        var placed = dragged && bounds.Width >= MinPlacementSize && bounds.Height >= MinPlacementSize
            ? Create(info, parent!, (bounds.Left, bounds.Top), (bounds.Width, bounds.Height))
            : PlaceClick(info, at, modifiers.HasFlag(InputModifiers.Alt));
        if (placed && !_host.ArmedSticky)
        {
            _host.Disarm(); // DSG-TP-040, 054: single-shot
        }
    }

    /// <summary>
    /// DSG-TP-043, 044: a click places a non-visual component in the tray (inside the tray: at the click − (16, 16),
    /// grid-snapped, ≥ 0; else the next free slot) and a visual one at the grid-snapped click point in the target with its
    /// default size; outside the form client area at the default position (DSG-TP-046).
    /// </summary>
    private bool PlaceClick(ComponentInfo info, (int X, int Y) at, bool alt)
    {
        var g = Geometry;
        var options = SnapOptions.From(_settings, alt);
        if (info.Kind == ComponentKind.NonVisual)
        {
            if (!Contains(g.TrayRect, at))
            {
                return PlaceDefault(info);
            }

            var (tx, ty) = g.SurfaceToTray(at);
            var (sx, sy) = SnapEngine.SnapTray((tx - 16, ty - 16), options);
            return Create(info, Document.Root, (Math.Max(0, sx), Math.Max(0, sy)), null);
        }

        if (!Contains(g.FormClientRect, at))
        {
            return PlaceDefault(info);
        }

        if (PlacementParent(at, info, report: true) is not { } parent)
        {
            return false;
        }

        var (x, y) = g.SurfaceToParentClient(parent, at);
        if (options.SnapToGrid)
        {
            (x, y) = (SnapEngine.ToGrid(x, options.GridSizeX), SnapEngine.ToGrid(y, options.GridSizeY));
        }

        return Create(info, parent, (x, y), null);
    }

    /// <summary>
    /// DSG-TP-044: the deepest container under the surface point (accepts children, not preserved, DSG-SUR-055 search),
    /// else its nearest ancestor that can hold <paramref name="info"/> (<c>childClasses</c>, <c>parentClasses</c>);
    /// null when not even the form can (status <c>Msg.Designer.CannotDropHere</c>). With <paramref name="report"/>, a point
    /// over a control that is not a container also shows that status for the control.
    /// </summary>
    private FormObject? PlacementParent((int X, int Y) at, ComponentInfo info, bool report)
    {
        if (report && Current.HitTester.HitTest(at.X, at.Y, [], handleSize: _settings.HandleSize) is
            { Kind: HitKind.Control, Object: { Info.AcceptsChildren: false } over })
        {
            _host.SetStatus(StatusIds.CannotDropHere, over.Name);
        }

        return HoldingAncestor(TargetContainer(at, Document.Root), info, report);
    }

    /// <summary><paramref name="parent"/> or its nearest ancestor that can hold <paramref name="info"/>; null (and the status) when none can.</summary>
    private FormObject? HoldingAncestor(FormObject parent, ComponentInfo info, bool report)
    {
        FormObject? p = parent;
        while (p is not null && !CanHold(p, info))
        {
            p = p.Parent;
        }

        if (p is null && report)
        {
            _host.SetStatus(StatusIds.CannotDropHere, Document.Root.Name);
        }

        return p;
    }

    /// <summary>
    /// DSG-TP-046: non-visual → the next free tray slot. Visual → target = the primary if it is a container that accepts
    /// children, else its parent (the form for a tray item or the form); top-left (gridX, gridY), moved by two grid steps
    /// while another direct child of the target is exactly there, stopping at the last position whose default bounds stay
    /// inside the client area.
    /// </summary>
    private bool PlaceDefault(ComponentInfo info)
    {
        var g = Geometry;
        if (info.Kind == ComponentKind.NonVisual)
        {
            return Create(info, Document.Root, TraySlots(1)[0], null);
        }

        var primary = Selection.Primary;
        var start = primary.Parent is null || g.IsTrayItem(primary) ? Document.Root
            : primary is { Info.AcceptsChildren: true, IsPreserved: false } ? primary
            : primary.Parent;
        if (HoldingAncestor(start, info, report: true) is not { } parent)
        {
            return false;
        }

        var (gx, gy) = (_settings.GridSizeX, _settings.GridSizeY);
        var client = g.GetClientRect(parent);
        var (w, h) = (info.DefaultSize.Width, info.DefaultSize.Height);
        var (x, y) = (gx, gy);
        while (parent.Children.Any(c => g.IsControl(c) && Layout.GetBounds(c) is var b && b.Left == x && b.Top == y))
        {
            var (nx, ny) = (x + 2 * gx, y + 2 * gy);
            if (nx + w > client.Width || ny + h > client.Height)
            {
                break;
            }

            (x, y) = (nx, ny);
        }

        return Create(info, parent, (x, y), null);
    }

    /// <summary>
    /// DSG-TP-050 … 052: creates a component of <paramref name="info"/>'s class as the last child of
    /// <paramref name="parent"/> — named <c>&lt;namePrefix&gt;&lt;n&gt;</c> (smallest free n ≥ 1, DSG-CS-031), with Left/Top
    /// and Width/Height (<paramref name="size"/>, else the default size) or DesignLeft/DesignTop, then the metadata
    /// <c>initial</c> values (<c>$(Name)</c> replaced; defaults are not stored), then the last TabOrder when focusable —
    /// as one "Add {ClassName}" unit with the layout commit. The new component becomes the only selection.
    /// </summary>
    private bool Create(ComponentInfo info, FormObject parent, (int X, int Y) position, (int Width, int Height)? size)
    {
        var name = NewComponentName(info.NamePrefix);
        FormPropertyData Int(string path, int value) => new(path, new IntegerValue(value, false));
        var (width, height) = size ?? (info.DefaultSize.Width, info.DefaultSize.Height);
        FormPropertyData[] geometry = info.Kind == ComponentKind.NonVisual
            ? [Int("DesignLeft", position.X), Int("DesignTop", position.Y)]
            : [Int("Left", position.X), Int("Top", position.Y), Int("Width", width), Int("Height", height)];
        var changes = new List<FormChange>
        {
            new AddObject(parent.Name, parent.Children.Count, new FormObjectData(FormObjectKind.Object, name, info.ClassName, [.. geometry], [])),
        };
        foreach (var property in info.Properties)
        {
            if (property.Initial is { } initial && InitialValue(property, initial.Replace("$(Name)", name, StringComparison.Ordinal)) is { } value)
            {
                changes.Add(new SetProperty(name, property.Name, value));
            }
        }

        var before = Selection.ToSnapshot();
        var undo = new List<FormChange>(ApplyEdit(changes));
        var created = Document.FindByName(name)!;
        var renumber = TabOrderRules.IsFocusable(created) ? TabOrderRules.Renumber(parent, appended: [created]) : [];
        undo.InsertRange(0, ApplyEdit(renumber));
        var (commitRedo, commitUndo) = LayoutCommit.StoreDifferences(Editor, _layout.Compute(Document));
        Selection.Select(created);
        PushUnit(new DesignerUndoUnit(UndoTitles.Add, [.. changes, .. renumber, .. commitRedo], [.. commitUndo, .. undo], [],
            before, Selection.ToSnapshot()) { TitleArgument = info.ClassName });
        return true;
    }

    /// <summary>DSG-TP-050: <c>&lt;prefix&gt;&lt;n&gt;</c> with the smallest n ≥ 1 that no component uses and the form class does not declare.</summary>
    private string NewComponentName(string prefix)
    {
        var n = 1;
        while (Document.FindByName(prefix + n) is not null || _host.CodeSync.LookupMember(prefix + n) != MemberLookup.None)
        {
            n++;
        }

        return prefix + n;
    }

    /// <summary>
    /// DSG-TP-051: an <c>initial</c> text converted by the property type — strings as is, numbers and booleans parsed, enum
    /// and other identifier values as identifiers; null (skipped) when it cannot be converted.
    /// </summary>
    private static FormValue? InitialValue(PropertyInfo property, string text)
    {
        if (property.Kind == PropertyKind.Enum)
        {
            return new IdentValue(text);
        }

        if (property.Kind != PropertyKind.Simple)
        {
            return null;
        }

        var integer = long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n);
        return property.Type.ToLowerInvariant() switch
        {
            "integer" or "int64" or "tshortcut" => integer ? new IntegerValue(n, false) : null,
            "boolean" => bool.TryParse(text, out var flag) ? new IdentValue(flag ? "True" : "False") : null,
            "double" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? new FloatValue(d) : null,
            "tcolor" or "tcursor" => integer ? new IntegerValue(n, false) : new IdentValue(text),
            "tstrings" => new StringListValue([.. text.Split('\n')]),
            _ => new StringValue(text),
        };
    }
}
