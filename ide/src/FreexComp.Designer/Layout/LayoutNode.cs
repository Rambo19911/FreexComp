using FreexComp.Designer.Model;

namespace FreexComp.Designer.Layout;

/// <summary><c>TAlign</c> in phase order (layout.md UIF-222).</summary>
internal enum AlignKind { None, Top, Bottom, Left, Right, Client }

/// <summary>The class-specific layout behaviour of a node, keyed on the metadata ancestor chain (layout-engine.md §4).</summary>
/// <remarks><see cref="PageControl"/> and <see cref="TabControl"/> are the <c>TCustomTabControl</c> descendants (DSG-LAY-070).</remarks>
internal enum NodeKind { Generic, Form, Panel, GroupBox, ScrollBox, Grid, PageControl, TabControl, TabSheet, ToolBar, ToolButton }

/// <summary>Intrinsic size rules I1 … I3 (layout.md UIF-250); I4 is <see cref="NodeKind.TabSheet"/>.</summary>
internal enum IntrinsicRule { None, ComboBox, Edit }

/// <summary>
/// One placeable control of the C# mirror of the run-time object model: the fields that the run-time
/// <c>TControl</c>/<c>TWinControl</c> keep for layout (layout.md §2 … §11), as plain mutable state.
/// </summary>
internal sealed class LayoutNode
{
    public LayoutNode(FormObject obj) => Object = obj;

    public FormObject Object { get; }

    public LayoutNode? Parent { get; set; }

    public List<LayoutNode> Children { get; } = [];

    public NodeKind Kind;
    public IntrinsicRule Intrinsic;

    /// <summary>A <c>TWinControl</c>: runs its own layout pass when its size changes (UIF-210 step 7).</summary>
    public bool IsWinControl;

    // Bounds (UIF-200); for the root: (0, 0, ClientWidth, ClientHeight).
    public int Left, Top, Width, Height;

    public AlignKind Align;
    public bool AkLeft = true, AkTop = true, AkRight, AkBottom;
    public bool Visible = true;
    public bool AlignWithMargins;
    public int MarginLeft = 3, MarginTop = 3, MarginRight = 3, MarginBottom = 3;
    public int MinWidth, MinHeight, MaxWidth, MaxHeight;
    public int PaddingLeft, PaddingTop, PaddingRight, PaddingBottom;

    /// <summary>Own font (<c>ParentFont = False</c>, a stored font, or the root); otherwise the parent's (UIF-251).</summary>
    public bool OwnFont;
    public int FontHeight = -12;

    // Edits, combo boxes, panels, scroll boxes, grids.
    public bool AutoSize = true;
    public bool BorderSingle;
    public bool ComboSimple;
    public int BorderWidth, BevelWidth = 1;
    public bool BevelOuter = true, BevelInner;

    // Tab controls, page controls and tab sheets (DSG-LAY-069).
    public int TabHeight, TabWidth;
    public bool MultiLine, ScrollOpposite;
    public TabPosition TabPosition;
    public TabStyle TabStyle;

    /// <summary><c>Images.Width</c> of the linked image list; null without <c>Images</c>.</summary>
    public int? ImagesWidth;

    public string? ActivePageName;

    /// <summary>The page activated by <c>Loaded</c> (UIF-771); the selected tab for <c>K</c> (DSG-LAY-072).</summary>
    public LayoutNode? ActivePage;

    public bool TabVisible = true;
    public string Caption = "";
    public int ImageIndex = -1;

    /// <summary><c>TTabControl.Tabs</c> and the effective <c>TabIndex</c>.</summary>
    public IReadOnlyList<string> Tabs = [];
    public int TabIndex = -1;

    /// <summary>The display rectangle of the last layout pass (UIF-1502: recomputed in every pass).</summary>
    public Rect DisplayRect;

    /// <summary>A tab-strip node: a <c>TCustomTabControl</c> descendant.</summary>
    public bool IsTabStrip => Kind is NodeKind.PageControl or NodeKind.TabControl;

    // Tool bars and tool buttons.
    public int ButtonWidth = 23, ButtonHeight = 22, Indent;
    public bool Spacer;

    // Scroll boxes (§11).
    public bool AutoScroll = true;
    public bool HorzBarVisible = true, VertBarVisible = true;
    public int HorzRange, VertRange;
    public bool HorzShown, VertShown;

    // Run-time state.
    public bool Loading = true;
    public bool Aligning;

    // Anchor baseline (UIF-230).
    public int BaseLeft, BaseTop, BaseWidth, BaseHeight, BaseParentWidth, BaseParentHeight;

    public int EffectiveFontHeight => OwnFont || Parent is null ? FontHeight : Parent.EffectiveFontHeight;

    public override string ToString() => $"{Object.Name} ({Left}, {Top}, {Width}, {Height})";
}
