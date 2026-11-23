namespace FreexComp.Designer.Surface;

/// <summary>One pointer event in surface (backdrop) coordinates, DIP (design-surface.md §1).</summary>
public readonly record struct PointerInput(PointerKind Kind, double X, double Y, PointerButton Button, InputModifiers Modifiers, int ClickCount = 1);

public enum PointerKind { Down, Move, Up, CaptureLost }

public enum PointerButton { None, Left, Right, Middle }

[Flags]
public enum InputModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

/// <summary>One key press in the Designer scope (design-surface.md §1, §9).</summary>
public readonly record struct KeyInput(DesignKey Key, InputModifiers Modifiers = InputModifiers.None);

/// <summary>The keys the design surface handles itself (design-surface.md §9).</summary>
public enum DesignKey { Left, Right, Up, Down, Tab, Enter, Escape, Delete, Space }

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

    /// <summary><c>designer.snapToGrid</c> (default true, design-surface.md §6).</summary>
    bool SnapToGrid { get; }

    /// <summary><c>designer.gridSizeX</c> (2 … 64, default 8).</summary>
    int GridSizeX { get; }

    /// <summary><c>designer.gridSizeY</c> (2 … 64, default 8).</summary>
    int GridSizeY { get; }

    /// <summary><c>designer.useGuidelines</c> (default true).</summary>
    bool UseGuidelines { get; }

    /// <summary><c>designer.guidelineSnapDistance</c> (1 … 16, default 4).</summary>
    int GuidelineSnapDistance { get; }

    /// <summary><c>designer.guidelineMargin</c> (0 … 32, default 8, design-surface.md §15).</summary>
    int GuidelineMargin { get; }

    /// <summary><c>designer.lockControls</c> (default false): no drag moves or resizes anything (DSG-SUR-051, DSG-SUR-087).</summary>
    bool LockControls { get; }
}

/// <summary>What the surface needs from the IDE (design-surface.md §1; implemented in the IDE core).</summary>
public interface IDesignerHost
{
    /// <summary>Shows a status message (<c>states-and-messages.md</c> id and its arguments).</summary>
    void SetStatus(string messageId, params object[] args);

    /// <summary>Opens the design-surface context menu at a surface point.</summary>
    void ShowContextMenu(double x, double y);

    /// <summary>
    /// DSG-SUR-105: moves focus to the Object Inspector, selects the row of <paramref name="propertyName"/> and starts
    /// editing it with <paramref name="text"/> replacing the value.
    /// </summary>
    void BeginPropertyEdit(string propertyName, string text);
}
