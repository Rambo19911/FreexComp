namespace FreexComp.Designer.Surface;

/// <summary>One pointer event in surface (backdrop) coordinates, DIP (design-surface.md §1).</summary>
public readonly record struct PointerInput(PointerKind Kind, double X, double Y, PointerButton Button, InputModifiers Modifiers, int ClickCount = 1);

public enum PointerKind { Down, Move, Up, CaptureLost }

public enum PointerButton { None, Left, Right, Middle }

[Flags]
public enum InputModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

/// <summary>The controller's interaction mode (design-surface.md §1).</summary>
public enum InteractionState { Idle, PressPending, Moving, Resizing, Lasso, Placing, TabOrder }

/// <summary>
/// The designer settings the surface reads live (design-surface.md §1, §6; settings keys <c>designer.*</c>). Implemented
/// by the IDE over its settings service; an in-memory fake in tests (testing.md DSG-TEST-005).
/// </summary>
public interface IDesignerSettings
{
    /// <summary><c>designer.showComponentCaptions</c>: tray items show their name (UI-DSG-062).</summary>
    bool ShowComponentCaptions { get; }

    /// <summary><c>designer.handleSize</c> (5 … 11, odd; default 7, UI-DSG-032).</summary>
    int HandleSize { get; }
}

/// <summary>What the surface needs from the IDE (design-surface.md §1; implemented in the IDE core).</summary>
public interface IDesignerHost
{
    /// <summary>Shows a status message (<c>states-and-messages.md</c> id and its arguments).</summary>
    void SetStatus(string messageId, params object[] args);

    /// <summary>Opens the design-surface context menu at a surface point.</summary>
    void ShowContextMenu(double x, double y);
}
