using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using DesignRect = FreexComp.Designer.Rect;

namespace FreexComp.Ide.Designer;

/// <summary>
/// Automation of the design surface (accessibility.md §3 Designer, UI-A11Y-035; D-078): the control is
/// <c>Designer.&lt;formName&gt;</c>; its virtual children are the form frame (<c>.Form</c>, holding the controls as nested
/// <c>.Control.&lt;Name&gt;</c> elements), the tray (<c>.Tray</c> with <c>.Tray.&lt;Name&gt;</c>) and the handles of the primary
/// selection (<c>.Handle.&lt;Position&gt;</c>). Components expose their surface bounds and their selection state.
/// During a drag the guidelines of the applied snaps are children too (<c>.Guideline.&lt;Kind&gt;.&lt;Axis&gt;</c>, designer.md
/// §10; a centre line is an <c>Edge</c> guideline); a margin guideline's help text is its distance in design units as
/// an invariant integer (UI-DSG-131).
/// </summary>
public sealed class DesignSurfaceAutomationPeer(DesignSurfaceControl owner) : ControlAutomationPeer(owner)
{
    private readonly Dictionary<string, VirtualPeer> _peers = new(StringComparer.Ordinal);

    internal DesignSurfaceControl Surface => owner;

    /// <summary>Raises the children-changed event after a model or selection change.</summary>
    internal void Refresh() => InvalidateChildren();

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore()
    {
        if (owner.Surface is not { } surface || owner.Designer?.Model is not { } model)
        {
            return [];
        }

        var form = owner.FormName;
        var g = surface.Geometry;
        var children = new List<AutomationPeer>
        {
            Peer(AutomationIds.DesignerForm(form), PeerKind.Form, model.Root, this),
            Peer(AutomationIds.DesignerTray(form), PeerKind.Tray, null, this),
        };
        if (surface.Selection.Items.Count > 0 && surface.State != InteractionState.TabOrder)
        {
            var primary = surface.Selection.Primary;
            var hit = new HitTester(g);
            var sole = surface.Selection.Items.Count == 1;
            var handles = (primary.Parent is null && !sole) || !g.Layout.HasBounds(primary) && primary.Parent is not null
                ? []
                : hit.GetHandleSquares(primary, sole);
            foreach (var (kind, square) in handles.OrderBy(h => h.Kind))
            {
                var peer = Peer(AutomationIds.DesignerHandle(form, kind.ToString()), PeerKind.Handle, primary, this);
                peer.Bounds = square;
                children.Add(peer);
            }
        }

        // UI-DSG-054: at most one guideline per axis and kind is drawn — the first one.
        foreach (var line in surface.Guidelines)
        {
            var kind = line.Kind == GuidelineKind.Center ? GuidelineKind.Edge : line.Kind;
            var id = AutomationIds.DesignerGuideline(form, kind.ToString(), line.Axis.ToString());
            if (surface.GuidelineRect(line) is { } rect && !children.Exists(c => c.GetAutomationId() == id))
            {
                var peer = Peer(id, PeerKind.Guideline, null, this);
                peer.Bounds = rect;
                peer.HelpText = line.Kind == GuidelineKind.Margin ? line.Spacing.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                children.Add(peer);
            }
        }

        return children;
    }

    /// <summary>The children of a virtual element: the controls of the form or a container, or the tray items.</summary>
    internal IReadOnlyList<AutomationPeer> ChildrenOf(VirtualPeer parent)
    {
        if (owner.Surface is not { } surface)
        {
            return [];
        }

        var g = surface.Geometry;
        var form = owner.FormName;
        return parent.Kind switch
        {
            PeerKind.Tray => [.. g.TrayItems.Select(o => Peer(AutomationIds.DesignerTrayItem(form, o.Name), PeerKind.Component, o, parent))],
            PeerKind.Form or PeerKind.Component when parent.Object is { } obj && !g.IsTrayItem(obj) =>
                [.. obj.Children.Where(g.IsControl).Select(o => Peer(AutomationIds.DesignerControl(form, o.Name), PeerKind.Component, o, parent))],
            _ => [],
        };
    }

    /// <summary>The bounds of a virtual element: its surface rectangle offset by the control's own bounds.</summary>
    internal Rect BoundsOf(VirtualPeer peer)
    {
        var origin = GetBoundingRectangle().TopLeft;
        if (owner.Surface is not { } surface)
        {
            return default;
        }

        var g = surface.Geometry;
        DesignRect r = peer.Kind switch
        {
            PeerKind.Form => g.FrameRect,
            PeerKind.Tray => g.TrayRect,
            PeerKind.Handle or PeerKind.Guideline => peer.Bounds,
            _ => peer.Object is { } obj ? g.GetBounds(obj) : default,
        };
        return new Rect(origin.X + r.Left, origin.Y + r.Top, r.Width, r.Height);
    }

    private VirtualPeer Peer(string id, PeerKind kind, FormObject? obj, AutomationPeer parent)
    {
        if (!_peers.TryGetValue(id, out var peer) || !ReferenceEquals(peer.Object, obj))
        {
            _peers[id] = peer = new VirtualPeer(this, id, kind, obj);
        }

        peer.Parent = parent;
        return peer;
    }

    protected override string? GetAutomationIdCore() => AutomationIds.Designer(owner.FormName);

    protected override string? GetNameCore() => owner.FormName;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
}

internal enum PeerKind { Form, Tray, Component, Handle, Guideline }

/// <summary>A virtual element of the design surface: the form frame, the tray, a component, a handle or a guideline.</summary>
internal sealed class VirtualPeer(DesignSurfaceAutomationPeer root, string id, PeerKind kind, FormObject? obj) : AutomationPeer, ISelectionItemProvider
{
    public PeerKind Kind => kind;

    public FormObject? Object => obj;

    public AutomationPeer? Parent { get; set; }

    /// <summary>The hit square of a handle or the rectangle of a guideline (surface coordinates).</summary>
    public DesignRect Bounds { get; set; }

    /// <summary>The distance of a margin guideline (UI-DSG-131); null for every other element.</summary>
    public string? HelpText { get; set; }

    public bool IsSelected => obj is not null && kind != PeerKind.Handle && root.Surface.Surface?.Selection.Contains(obj) == true;

    public ISelectionProvider? SelectionContainer => null;

    public void AddToSelection()
    {
        if (Selectable() is { } surface && !surface.Selection.Contains(obj!))
        {
            surface.Selection.Set([.. surface.Selection.Items.Select(o => o.Name), obj!.Name]);
        }
    }

    public void RemoveFromSelection()
    {
        if (Selectable() is { } surface && surface.Selection.Contains(obj!))
        {
            surface.Selection.Set([.. surface.Selection.Items.Where(o => !ReferenceEquals(o, obj)).Select(o => o.Name)]);
        }
    }

    public void Select()
    {
        if (Selectable() is { } surface)
        {
            surface.Selection.Set([obj!.Name]);
        }
    }

    private DesignSurfaceController? Selectable() => obj is not null && kind != PeerKind.Handle ? root.Surface.Surface : null;

    protected override string? GetAutomationIdCore() => id;

    protected override string GetNameCore() => kind switch
    {
        PeerKind.Handle => id[(id.LastIndexOf('.') + 1)..],
        PeerKind.Guideline => id[(id.IndexOf(".Guideline.", StringComparison.Ordinal) + 1)..],
        PeerKind.Tray => "Tray",
        _ => obj?.Name ?? "",
    };

    protected override string GetClassNameCore() => kind == PeerKind.Component ? obj?.TypeName ?? "" : kind.ToString();

    protected override AutomationControlType GetAutomationControlTypeCore() => kind switch
    {
        PeerKind.Form => AutomationControlType.Window,
        PeerKind.Tray => AutomationControlType.List,
        PeerKind.Handle => AutomationControlType.Thumb,
        PeerKind.Guideline => AutomationControlType.Separator,
        _ => AutomationControlType.Custom,
    };

    protected override Rect GetBoundingRectangleCore() => root.BoundsOf(this);

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => root.ChildrenOf(this);

    protected override AutomationPeer? GetParentCore() => Parent;

    protected override string? GetHelpTextCore() => HelpText;

    protected override AutomationPeer? GetLabeledByCore() => null;

    protected override string? GetAcceleratorKeyCore() => null;

    protected override string? GetAccessKeyCore() => null;

    protected override bool HasKeyboardFocusCore() => false;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsEnabledCore() => true;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override void SetFocusCore()
    {
    }

    protected override bool ShowContextMenuCore() => false;

    protected override void BringIntoViewCore()
    {
    }

    protected override bool TrySetParent(AutomationPeer? parent)
    {
        Parent = parent;
        return true;
    }
}
