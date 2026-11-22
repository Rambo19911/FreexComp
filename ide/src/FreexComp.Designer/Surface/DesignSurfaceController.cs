using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Surface;

/// <summary>
/// The interaction logic of the design surface (design-surface.md; no UI dependency, DSG-ARCH-003). This skeleton (D-035)
/// covers the selection and the click rules: press, release, the drag threshold, Shift/Ctrl+click, Alt+click and the
/// right-button selection rule. Moving, resizing, lasso, placement, keys and commands follow in later tasks.
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
                if (Math.Abs(x - _downAt.X) >= DragThreshold || Math.Abs(y - _downAt.Y) >= DragThreshold)
                {
                    _dragging = true; // a drag never applies the click rule; what it moves comes with the move tasks
                }

                break;
            case PointerKind.Up when input.Button == PointerButton.Left && State == InteractionState.PressPending:
                Release();
                break;
            case PointerKind.CaptureLost:
                ResetPress();
                break;
        }
    }

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
    }
}
