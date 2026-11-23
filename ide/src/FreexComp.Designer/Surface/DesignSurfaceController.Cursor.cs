namespace FreexComp.Designer.Surface;

/// <summary>The pointer cursors of the surface (ui/designer.md UI-DSG-036); the view maps them to platform cursors.</summary>
public enum CursorKind { Arrow, SizeAll, SizeNS, SizeWE, SizeNWSE, SizeNESW, Cross }

/// <summary>The cursor the surface asks for at a point (ui/designer.md UI-DSG-036, UI-DSG-034; design-surface.md DSG-SUR-084).</summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// UI-DSG-036: an armed palette item or a placement drag: crosshair; a move drag: SizeAll; a resize drag: the dragged
    /// handle's cursor. Otherwise from the hit: a handle that resizes gives its size cursor, the body of a selected control or
    /// tray item that can move gives SizeAll, everything else the arrow. With Lock Controls (UI-DSG-034), on a read-only form,
    /// over preserved components and inert handles of aligned controls (DSG-SUR-084) the arrow stays.
    /// </summary>
    public CursorKind GetCursor(double x, double y)
    {
        switch (State)
        {
            case InteractionState.Placing:
                return CursorKind.Cross;
            case InteractionState.Moving:
                return CursorKind.SizeAll;
            case InteractionState.Resizing:
                return SizeCursor(_resizeHandle);
        }

        if (_host.ArmedPaletteClass is not null && State != InteractionState.TabOrder)
        {
            return CursorKind.Cross;
        }

        if (IsReadOnly || _settings.LockControls || State == InteractionState.TabOrder)
        {
            return CursorKind.Arrow;
        }

        var g = Geometry;
        var hit = Current.HitTester.HitTest(x, y, Selection.Items, handleSize: _settings.HandleSize);
        return hit switch
        {
            { Kind: HitKind.FormHandle, Handle: { } h } => SizeCursor(h),
            { Kind: HitKind.Handle, Object: { } o, Handle: { } h }
                when !o.IsPreserved && o.Info?.FindProperty("Left") is not { Stored: false } && (HandleEdges(h) & FreeEdges(o)) != SnapEdges.None
                => SizeCursor(h),
            { Kind: HitKind.Control, Object: { } o } when Selection.Contains(o) && !o.IsPreserved && !IsAligned(o) => CursorKind.SizeAll,
            { Kind: HitKind.TrayItem, Object: { } o } when Selection.Contains(o) && !o.IsPreserved && g.IsTrayItem(o) => CursorKind.SizeAll,
            _ => CursorKind.Arrow,
        };
    }

    private static CursorKind SizeCursor(HandleKind h) => h switch
    {
        HandleKind.NW or HandleKind.SE => CursorKind.SizeNWSE,
        HandleKind.NE or HandleKind.SW => CursorKind.SizeNESW,
        HandleKind.N or HandleKind.S => CursorKind.SizeNS,
        _ => CursorKind.SizeWE,
    };
}
