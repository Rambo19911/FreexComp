namespace FreexComp.Designer.Surface;

/// <summary>The tooltip of a tray item: its class name and whether a double-click opens its component editor (UI-DSG-083).</summary>
public sealed record TrayToolTip(string ClassName, bool OpensEditor);

/// <summary>Pointer-at-rest feedback: the tray tooltip (ui/designer.md UI-DSG-083) and leaving the surface (UI-DSG-084).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// UI-DSG-083: over a tray item (tile or name), its class name; <see cref="TrayToolTip.OpensEditor"/> when the class has
    /// a <c>componentEditor</c> (the view adds the line "Double-click to edit"). Null elsewhere.
    /// </summary>
    public TrayToolTip? GetTrayToolTip(double x, double y) =>
        Current.HitTester.HitTest(x, y, [], handleSize: _settings.HandleSize) is { Kind: HitKind.TrayItem, Object: { } item }
            ? new TrayToolTip(item.TypeName, item.Info?.ComponentEditor is { Length: > 0 })
            : null;

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
