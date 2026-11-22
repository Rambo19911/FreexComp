namespace FreexComp.Designer.Rendering;

/// <summary>
/// The design-renderer palette (design-tokens.md §3.3, design-renderers.md §3.1): the <c>form.*</c> colours are
/// theme-independent; <see cref="Error"/> (<c>status.error</c>), <see cref="TileBackground"/> (<c>bg.raised</c>) and
/// <see cref="TileBorder"/> (<c>border.strong</c>) are IDE tokens of the current theme.
/// </summary>
public sealed record DesignPalette(Rgba Error, Rgba TileBackground, Rgba TileBorder)
{
    public static DesignPalette Light { get; } = new(Rgba.Parse("#B3261E"), Rgba.Parse("#FFFFFF"), Rgba.Parse("#9AA29A"));

    public static DesignPalette Dark { get; } = new(Rgba.Parse("#F2867E"), Rgba.Parse("#2A302D"), Rgba.Parse("#5C665F"));

    public Rgba Face { get; } = Rgba.Parse("#F0F0F0");

    public Rgba Window { get; } = Rgba.Parse("#FFFFFF");

    public Rgba Text { get; } = Rgba.Parse("#000000");

    public Rgba GrayText { get; } = Rgba.Parse("#6D6D6D");

    public Rgba Border { get; } = Rgba.Parse("#ADADAD");

    public Rgba BorderDark { get; } = Rgba.Parse("#7A7A7A");

    public Rgba Highlight { get; } = Rgba.Parse("#0A64C8");

    public Rgba HighlightText { get; } = Rgba.Parse("#FFFFFF");

    public Rgba CaptionBackground { get; } = Rgba.Parse("#FFFFFF");

    public Rgba CaptionText { get; } = Rgba.Parse("#000000");

    public Rgba ButtonFace { get; } = Rgba.Parse("#E1E1E1");
}
