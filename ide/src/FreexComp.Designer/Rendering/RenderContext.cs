using System.Collections.Immutable;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;

namespace FreexComp.Designer.Rendering;

/// <summary>
/// Everything a renderer may read about the control it draws (design-renderers.md §2). Property values are effective
/// values (stored, else the metadata default) read by name; a property absent from the metadata, or a stored value of
/// the wrong shape, yields the caller's fallback (DSG-RND-005, DSG-RND-006).
/// </summary>
public sealed class RenderContext
{
    private readonly Func<TextStyle, TextMetrics> _metrics;

    /// <param name="obj">The object to draw.</param>
    /// <param name="width">Control width; for the form its client width (the frame is decoration, §4.1).</param>
    /// <param name="height">Control height; for the form its client height.</param>
    /// <param name="clientRect">From the layout result (DSG-LAY-034; use <see cref="FromLayout"/>); null = computed by
    /// <see cref="ContainerRects"/> from stored properties, for drawing one control without a layout (renderer tests).</param>
    /// <param name="alignRect">From the layout result; null = computed by <see cref="ContainerRects"/>.</param>
    /// <param name="palette">Theme part of the palette; default <see cref="DesignPalette.Light"/>.</param>
    /// <param name="info">Metadata override (tests strip it); default the object's own, or <c>TComponent</c> when unknown.</param>
    /// <param name="metrics">Text metrics of the drawing canvas; default the deterministic rule of DSG-RND-012.</param>
    /// <param name="padding">The run-time <c>Padding</c> as (left, top, right, bottom) from the layout result; null = stored.</param>
    /// <param name="tabStrip">The tab-strip geometry from the layout result (<see cref="TabStrip"/>); null = none.</param>
    public RenderContext(FormObject obj, int width, int height, Rect? clientRect = null, Rect? alignRect = null,
        DesignPalette? palette = null, ComponentInfo? info = null, Func<TextStyle, TextMetrics>? metrics = null, Rect? padding = null,
        Layout.TabStripGeometry? tabStrip = null)
    {
        TabStrip = tabStrip;
        Object = obj;
        Info = info ?? obj.Info ?? ComponentCatalog.BuiltInRoot;
        Width = width;
        Height = height;
        Palette = palette ?? DesignPalette.Light;
        _metrics = metrics ?? RecordingCanvas.Metrics;
        (Font, FontColor) = ResolveFont(obj, Info, Palette);
        Enabled = ResolveEnabled(obj, Info);
        Padding = padding ?? new Rect(Get("Padding.Left", 0), Get("Padding.Top", 0), Get("Padding.Right", 0), Get("Padding.Bottom", 0));
        ClientRect = clientRect ?? ContainerRects.Client(this);
        AlignRect = alignRect ?? ContainerRects.Align(this);
    }

    /// <summary>
    /// D-102: the context of <paramref name="obj"/> as the design surface draws it — size, client and align rect from
    /// <paramref name="layout"/> (the run-time state after loading, steps and scaling; DSG-RND-004, DSG-LAY-034), never
    /// from stored properties. The align rect is moved from client into control coordinates. The form keeps its design
    /// caption: its client rect starts at (1, 31) (DSG-RND-005 exception).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="obj"/> has no layout bounds (non-visual or unplaceable).</exception>
    public static RenderContext FromLayout(FormObject obj, Layout.LayoutResult layout, DesignPalette? palette = null,
        Func<TextStyle, TextMetrics>? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(layout);
        var bounds = layout.GetBounds(obj);
        var client = layout.GetClientRect(obj);
        var align = layout.GetAlignRect(obj);
        if (obj.Parent is null && (obj.Info ?? ComponentCatalog.BuiltInRoot).RendererId == "form")
        {
            client = client with { Left = client.Left + FormClientLeft, Top = client.Top + FormClientTop };
        }

        align = align with { Left = align.Left + client.Left, Top = align.Top + client.Top };
        return new RenderContext(obj, bounds.Width, bounds.Height, client, align, palette, metrics: metrics, padding: layout.GetPadding(obj),
            tabStrip: layout.GetTabStrip(obj));
    }

    /// <summary>The form's client origin below the design caption (DSG-RND-005 exception; <see cref="ContainerRects.Client"/>).</summary>
    internal const int FormClientLeft = 1;

    /// <inheritdoc cref="FormClientLeft"/>
    internal const int FormClientTop = 31;

    public FormObject Object { get; }

    /// <summary>
    /// DSG-RND-020: the tab strip of a <c>TCustomTabControl</c> descendant as the layout computed it (<see cref="FromLayout"/>);
    /// null without a layout result (the tab renderers then compute it from the stored properties).
    /// </summary>
    public Layout.TabStripGeometry? TabStrip { get; }

    public ComponentInfo Info { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Origin and size of the children's area, relative to the control's top-left (DSG-RND-004).</summary>
    public Rect ClientRect { get; }

    /// <summary>The part of <see cref="ClientRect"/> distributed among aligned children, in the same coordinates.</summary>
    public Rect AlignRect { get; }

    /// <summary>
    /// The <c>Padding</c> between the class's adjusted client rect and <see cref="AlignRect"/>, as (left, top, right,
    /// bottom) in its <see cref="Rect"/> fields <c>Left</c>, <c>Top</c>, <c>Width</c>, <c>Height</c>; run-time (scaled)
    /// values when from a layout (D-104).
    /// </summary>
    public Rect Padding { get; }

    /// <summary>Effective font (DSG-RND-011).</summary>
    public TextStyle Font { get; }

    public Rgba FontColor { get; }

    /// <summary>Own <c>Enabled</c> and that of every ancestor.</summary>
    public bool Enabled { get; }

    public DesignPalette Palette { get; }

    /// <summary>Metrics of <see cref="Font"/> (DSG-RND-012), used for baselines and text positions.</summary>
    public TextMetrics Metrics => _metrics(Font);

    /// <summary>Text ink: <see cref="FontColor"/>, or <c>form.grayText</c> when disabled (DSG-RND-013).</summary>
    public Rgba Ink => Enabled ? FontColor : Palette.GrayText;

    /// <summary>True when the metadata declares the property path.</summary>
    public bool Has(string propertyPath) => FindInfo(Object, Info, propertyPath) is not null;

    /// <summary>True when the form stores the property path (for an inherited object or an instance: here or in the frame).</summary>
    public bool IsStored(string propertyPath) => Stored(Object, propertyPath) is not null;

    /// <summary>The stored line of a path: the object's own, else that of the frame object it shows (DSG-FD-106).</summary>
    private static FormProperty? Stored(FormObject obj, string path)
    {
        for (var o = obj; o is not null; o = o.InheritedFrom)
        {
            if (o.FindProperty(path) is { } stored)
            {
                return stored;
            }
        }

        return null;
    }

    /// <summary>The effective value converted to <typeparamref name="T"/>, or <c>default</c> (see the class remarks).</summary>
    public T Get<T>(string propertyPath) => Get(propertyPath, default(T)!);

    /// <summary>
    /// The effective value converted to <typeparamref name="T"/> (<c>bool</c>, <c>int</c>, <c>long</c>, <c>double</c>,
    /// <c>string</c> — enum members as their name —, <c>IReadOnlyList&lt;string&gt;</c> for string lists and sets,
    /// <see cref="FormValue"/>), or <paramref name="fallback"/>.
    /// </summary>
    public T Get<T>(string propertyPath, T fallback) => Convert(Effective(Object, Info, propertyPath), fallback);

    /// <summary>TColor → colour (object-inspector.md §5.6); <paramref name="fallback"/> for <c>clNone</c>, <c>clDefault</c> and invalid values.</summary>
    /// <summary>The effective value of a property of the parent (stored, else its metadata default); the fallback without a parent.</summary>
    public T ParentValue<T>(string propertyPath, T fallback) =>
        Object.Parent is { } parent ? GetOf(parent, InfoOf(parent), propertyPath, fallback) : fallback;

    public Rgba ResolveColor(FormValue? colorValue, Rgba fallback) => ResolveColor(colorValue, fallback, Palette);

    /// <summary>The control's own fill (DSG-RND-015): <c>Color</c>, or the parent's fill when <c>ParentColor</c> is True.</summary>
    public Rgba Fill(Rgba fallback) => FillOf(Object, Info, fallback, Palette);

    /// <summary>The effective fill of the parent (<c>form.face</c> at the root).</summary>
    public Rgba ParentFill => Object.Parent is { } parent ? FillOf(parent, InfoOf(parent), Palette.Face, Palette) : Palette.Face;

    internal static Rgba ResolveColor(FormValue? value, Rgba fallback, DesignPalette palette)
    {
        long? number = value switch
        {
            IntegerValue i => i.Value,
            IdentValue ident when NamedValues.TryGetColor(ident.Name, out var v) => v,
            _ => null,
        };
        if (number is not { } n)
        {
            return fallback;
        }

        var bits = unchecked((uint)n);
        if ((bits & 0xFF000000) == 0xFF000000)
        {
            return (bits & 0xFFFFFF) switch
            {
                5 => palette.Window,
                8 or 18 => palette.Text,
                13 => palette.Highlight,
                14 => palette.HighlightText,
                15 => palette.Face,
                16 => palette.BorderDark,
                17 => palette.GrayText,
                _ => palette.Face,
            };
        }

        return (bits & 0xFF000000) == 0 ? new Rgba((byte)bits, (byte)(bits >> 8), (byte)(bits >> 16)) : fallback;
    }

    private static ComponentInfo InfoOf(FormObject obj) => obj.Info ?? ComponentCatalog.BuiltInRoot;

    private static PropertyInfo? FindInfo(FormObject obj, ComponentInfo info, string path) =>
        obj.Catalog.FindPropertyPath(info, path);

    private static FormValue? Effective(FormObject obj, ComponentInfo info, string path)
    {
        if (FindInfo(obj, info, path) is not { Kind: not PropertyKind.Object } property)
        {
            return null;
        }

        if (Stored(obj, path) is { Value: not RawValue } stored)
        {
            return stored.Value;
        }

        try
        {
            return PropertyAccess.DefaultValue(obj.Catalog, property);
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            return null; // a malformed metadata default is a metadata error, not a reason to fail drawing
        }
    }

    private static T Convert<T>(FormValue? value, T fallback)
    {
        object? result = value switch
        {
            null => null,
            _ when typeof(T) == typeof(FormValue) => value,
            IdentValue ident when typeof(T) == typeof(bool) =>
                ident.Name.Equals("True", StringComparison.OrdinalIgnoreCase) ? true
                : ident.Name.Equals("False", StringComparison.OrdinalIgnoreCase) ? false : null,
            IntegerValue i when typeof(T) == typeof(int) => (int)Math.Clamp(i.Value, int.MinValue, int.MaxValue),
            IntegerValue i when typeof(T) == typeof(long) => i.Value,
            IntegerValue i when typeof(T) == typeof(double) => (double)i.Value,
            FloatValue f when typeof(T) == typeof(double) => f.Value,
            StringValue s when typeof(T) == typeof(string) => s.Value,
            IdentValue ident when typeof(T) == typeof(string) => ident.Name,
            StringListValue list when typeof(T) == typeof(IReadOnlyList<string>) => (IReadOnlyList<string>)list.Lines,
            SetValue set when typeof(T) == typeof(IReadOnlyList<string>) => (IReadOnlyList<string>)set.Elements,
            _ => null,
        };
        return result is T typed ? typed : fallback;
    }

    private static T GetOf<T>(FormObject obj, ComponentInfo info, string path, T fallback) => Convert(Effective(obj, info, path), fallback);

    private static (TextStyle Font, Rgba Color) ResolveFont(FormObject obj, ComponentInfo info, DesignPalette palette)
    {
        // DSG-RND-011: without ParentFont in the metadata a control behaves as ParentFont = True; the root uses its own Font.
        if (obj.Parent is { } parent && GetOf(obj, info, "ParentFont", true))
        {
            return ResolveFont(parent, InfoOf(parent), palette);
        }

        var family = GetOf(obj, info, "Font.Name", "");
        var px = 12.0;
        if (Stored(obj, "Font.Size") is { Value: IntegerValue { Value: > 0 and < 10_000 } size })
        {
            px = Math.Round(size.Value * 96.0 / 72.0, MidpointRounding.AwayFromZero);
        }
        else if (GetOf(obj, info, "Font.Height", -12L) is var height and not 0 and > -10_000 and < 10_000)
        {
            px = Math.Abs(height);
        }

        var style = GetOf<IReadOnlyList<string>>(obj, info, "Font.Style", ImmutableArray<string>.Empty);
        bool HasStyle(string member) => style.Any(s => s.Equals(member, StringComparison.OrdinalIgnoreCase));
        var font = new TextStyle(family.Length > 0 ? family : "Segoe UI", px, HasStyle("fsBold"), HasStyle("fsItalic"),
            HasStyle("fsUnderline"), HasStyle("fsStrikeOut"));
        return (font, ResolveColor(GetOf<FormValue?>(obj, info, "Font.Color", null), palette.Text, palette));
    }

    private static bool ResolveEnabled(FormObject obj, ComponentInfo info)
    {
        if (!GetOf(obj, info, "Enabled", true))
        {
            return false;
        }

        for (var o = obj.Parent; o is not null; o = o.Parent)
        {
            if (!GetOf(o, InfoOf(o), "Enabled", true))
            {
                return false;
            }
        }

        return true;
    }

    private static Rgba FillOf(FormObject obj, ComponentInfo info, Rgba fallback, DesignPalette palette)
    {
        if (FindInfo(obj, info, "Color") is null)
        {
            return obj.Parent is { } p ? FillOf(p, InfoOf(p), palette.Face, palette) : fallback;
        }

        if (obj.Parent is { } parent && GetOf(obj, info, "ParentColor", false))
        {
            return FillOf(parent, InfoOf(parent), palette.Face, palette);
        }

        return ResolveColor(GetOf<FormValue?>(obj, info, "Color", null), fallback, palette);
    }
}
