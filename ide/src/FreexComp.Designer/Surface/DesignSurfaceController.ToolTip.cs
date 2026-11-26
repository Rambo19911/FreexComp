namespace FreexComp.Designer.Surface;

/// <summary>
/// The tooltip of a tray item: its class name, whether a double-click opens its component editor (UI-DSG-083) and, for an
/// inherited component of a descendant, the ancestor class (UI-DSG-142).
/// </summary>
public sealed record TrayToolTip(string ClassName, bool OpensEditor, string? InheritedFrom = null);

/// <summary>The tooltip of an inherited control of a descendant (UI-DSG-142): <c>Name: TypeName</c> and the ancestor class.</summary>
public sealed record InheritedControlToolTip(string Name, string ClassName, string InheritedFrom);

/// <summary>Pointer-at-rest feedback: the tray tooltip (ui/designer.md UI-DSG-083) and leaving the surface (UI-DSG-084).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// UI-DSG-083: over a tray item (tile or name), its class name; <see cref="TrayToolTip.OpensEditor"/> when the class has
    /// a <c>componentEditor</c> (the view adds the line "Double-click to edit"). UI-DSG-142: an inherited item also names
    /// its ancestor class. Null elsewhere.
    /// </summary>
    public TrayToolTip? GetTrayToolTip(double x, double y) =>
        Current.HitTester.HitTest(x, y, [], handleSize: _settings.HandleSize) is { Kind: HitKind.TrayItem, Object: { } item }
            ? new TrayToolTip(item.TypeName, item.Info?.ComponentEditor is { Length: > 0 }, InheritedFromOf(item))
            : null;

    /// <summary>
    /// UI-DSG-142: over an inherited control of a descendant (one that shows the inherited marker), its name, class and
    /// ancestor class. Null over every other control and elsewhere.
    /// </summary>
    public InheritedControlToolTip? GetInheritedControlToolTip(double x, double y) =>
        Current.HitTester.HitTest(x, y, [], handleSize: _settings.HandleSize) is { Kind: HitKind.Control, Object: { } obj }
        && InheritedFromOf(obj) is { } ancestor
            ? new InheritedControlToolTip(obj.Name, obj.TypeName, ancestor)
            : null;

    private static string? InheritedFromOf(Model.FormObject obj) =>
        ShowsInheritedMarker(obj) ? obj.InheritedSource?.ClassName : null;

    /// <summary>UI-DSG-084: the pointer left the surface — no tab is hovered any more.</summary>
    public void PointerLeft()
    {
        if (_hoveredTab is not null)
        {
            _hoveredTab = null;
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }
}
