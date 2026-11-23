namespace FreexComp.Designer.Rendering;

/// <summary>
/// The IDE colours the design surface draws around and over the form (design-tokens.md §3.3 <c>designer.*</c> and the
/// general tokens named by ui/designer.md §3 … §7), per theme. The form itself uses <see cref="Renderers"/>, whose
/// <c>form.*</c> part is theme-independent.
/// </summary>
public sealed record SurfacePalette
{
    /// <summary>The Light theme values.</summary>
    public static SurfacePalette Light { get; } = new()
    {
        Backdrop = Rgba.Parse("#E9EBE7"),
        RubberBandBorder = Rgba.Parse("#0F7B6C"),
        RubberBandFill = Rgba.Parse("#0F7B6C22"),
        DropTarget = Rgba.Parse("#0F7B6C"),
        Accent = Rgba.Parse("#0F7B6C"),
        OnAccent = Rgba.Parse("#FFFFFF"),
        Panel = Rgba.Parse("#FAFAF8"),
        Raised = Rgba.Parse("#FFFFFF"),
        BorderDefault = Rgba.Parse("#C9CEC7"),
        TextPrimary = Rgba.Parse("#1C211E"),
        TextTertiary = Rgba.Parse("#737D76"),
        Warning = Rgba.Parse("#8A5A00"),
        Renderers = DesignPalette.Light,
    };

    /// <summary>The Dark theme values.</summary>
    public static SurfacePalette Dark { get; } = new()
    {
        Backdrop = Rgba.Parse("#141716"),
        RubberBandBorder = Rgba.Parse("#3FC1AE"),
        RubberBandFill = Rgba.Parse("#3FC1AE22"),
        DropTarget = Rgba.Parse("#3FC1AE"),
        Accent = Rgba.Parse("#3FC1AE"),
        OnAccent = Rgba.Parse("#06201C"),
        Panel = Rgba.Parse("#222725"),
        Raised = Rgba.Parse("#2A302D"),
        BorderDefault = Rgba.Parse("#3A423E"),
        TextPrimary = Rgba.Parse("#E4E8E5"),
        TextTertiary = Rgba.Parse("#838E87"),
        Warning = Rgba.Parse("#E5B454"),
        Renderers = DesignPalette.Dark,
    };

    /// <summary><c>designer.backdrop</c>.</summary>
    public required Rgba Backdrop { get; init; }

    /// <summary><c>designer.grid.dot</c> (same in both themes).</summary>
    public Rgba GridDot { get; init; } = Rgba.Parse("#9AA29A");

    /// <summary><c>designer.handle.fill</c>.</summary>
    public Rgba HandleFill { get; init; } = Rgba.Parse("#FFFFFF");

    /// <summary><c>designer.handle.border</c>: primary handles and outline.</summary>
    public Rgba HandleBorder { get; init; } = Rgba.Parse("#0F7B6C");

    /// <summary><c>designer.handle.secondary</c>: secondary and read-only selections.</summary>
    public Rgba HandleSecondary { get; init; } = Rgba.Parse("#737D76");

    /// <summary><c>designer.handle.locked</c>.</summary>
    public Rgba HandleLocked { get; init; } = Rgba.Parse("#B3261E");

    /// <summary><c>designer.guideline.edge</c>.</summary>
    public Rgba GuidelineEdge { get; init; } = Rgba.Parse("#D0306F");

    /// <summary><c>designer.guideline.baseline</c>.</summary>
    public Rgba GuidelineBaseline { get; init; } = Rgba.Parse("#1E5FA8");

    /// <summary><c>designer.guideline.margin</c>.</summary>
    public Rgba GuidelineMargin { get; init; } = Rgba.Parse("#C28A00");

    /// <summary><c>designer.rubberband.border</c>.</summary>
    public required Rgba RubberBandBorder { get; init; }

    /// <summary><c>designer.rubberband.fill</c>.</summary>
    public required Rgba RubberBandFill { get; init; }

    /// <summary><c>designer.taborder.badge.bg</c>.</summary>
    public Rgba TabBadgeBackground { get; init; } = Rgba.Parse("#1E5FA8");

    /// <summary><c>designer.taborder.badge.fg</c>.</summary>
    public Rgba TabBadgeText { get; init; } = Rgba.Parse("#FFFFFF");

    /// <summary><c>designer.droptarget</c>.</summary>
    public required Rgba DropTarget { get; init; }

    /// <summary><c>accent.default</c>: badges of controls already numbered in the current pass (UI-DSG-071).</summary>
    public required Rgba Accent { get; init; }

    /// <summary><c>text.onAccent</c>.</summary>
    public required Rgba OnAccent { get; init; }

    /// <summary><c>bg.panel</c>: the tray background (UI-DSG-060).</summary>
    public required Rgba Panel { get; init; }

    /// <summary><c>bg.raised</c>: the drag tip (UI-DSG-037).</summary>
    public required Rgba Raised { get; init; }

    /// <summary><c>border.default</c>: tray and drag-tip borders.</summary>
    public required Rgba BorderDefault { get; init; }

    /// <summary><c>text.primary</c>: tray item names, drag-tip text.</summary>
    public required Rgba TextPrimary { get; init; }

    /// <summary><c>text.tertiary</c>: the empty-tray text (UI-DSG-061).</summary>
    public required Rgba TextTertiary { get; init; }

    /// <summary><c>status.warning</c>: the triangle of a component with an unknown property or bad value (ui/designer.md §10).</summary>
    public required Rgba Warning { get; init; }

    /// <summary>The palette handed to the design renderers (theme part: error, tile colours).</summary>
    public required DesignPalette Renderers { get; init; }
}
