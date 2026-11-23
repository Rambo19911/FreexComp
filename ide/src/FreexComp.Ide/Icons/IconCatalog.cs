using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace FreexComp.Ide.Icons;

/// <summary>One drawn part of an icon: outline (1.25 px stroke) or fill, in the colour of a design token.</summary>
public sealed record IconLayer(Geometry Geometry, bool IsFilled, string ColorToken);

/// <summary>An icon of toolbars.md §4; <see cref="ColorToken"/> is its meaning colour (UI-TOK-041).</summary>
public sealed record Icon(string Name, string ColorToken, IReadOnlyList<IconLayer> Layers);

/// <summary>
/// Icon lookup (UI-TOK-040…042): reads the original 16 × 16 path geometry in <c>Assets/Icons/&lt;name&gt;.svg</c>.
/// Each <c>path</c> is a layer; <c>fill="none"</c> means outline. <c>data-token</c> on the root sets the icon's colour
/// token (default <c>text.primary</c>), on a path that layer's token. Geometry is created per call (thread-affine
/// objects are never cached). The symbol-kind icons (UI-ED-114, UI-TW-203) are a second set in <c>Assets/SymbolIcons</c>,
/// named <c>symbol-*</c>: <see cref="Get"/> finds both, <see cref="Names"/> lists the toolbar set only.
/// </summary>
public static class IconCatalog
{
    public const string DefaultToken = "text.primary";
    public const double StrokeThickness = 1.25;
    private const string Folder = "avares://FreexComp.Ide/Assets/Icons/";
    private const string SymbolFolder = "avares://FreexComp.Ide/Assets/SymbolIcons/";

    private static readonly Lazy<IReadOnlyDictionary<string, Definition>> Toolbar = new(() => Load(Folder));
    private static readonly Lazy<IReadOnlyDictionary<string, Definition>> Symbols = new(() => Load(SymbolFolder));

    /// <summary>The icons of toolbars.md §4.</summary>
    public static IReadOnlyCollection<string> Names => [.. Toolbar.Value.Keys];

    /// <summary>The symbol-kind icons (UI-ED-114, UI-TW-203).</summary>
    public static IReadOnlyCollection<string> SymbolNames => [.. Symbols.Value.Keys];

    /// <exception cref="KeyNotFoundException">No icon of that name.</exception>
    public static Icon Get(string name) =>
        TryGet(name, out var icon) ? icon : throw new KeyNotFoundException($"No icon '{name}'.");

    public static bool TryGet(string name, out Icon icon)
    {
        if (!Toolbar.Value.TryGetValue(name, out var definition) && !Symbols.Value.TryGetValue(name, out definition))
        {
            icon = null!;
            return false;
        }

        icon = new Icon(
            name,
            definition.Token,
            [.. definition.Paths.Select(p => new IconLayer(Geometry.Parse(p.Data), p.IsFilled, p.Token ?? definition.Token))]);
        return true;
    }

    /// <summary>Resource key of a token's brush (UI-TOK-002): <c>Fxc</c> + PascalCase segments + <c>Brush</c>.</summary>
    public static string BrushKey(string token) =>
        "Fxc" + string.Concat(token.Split('.').Select(s => char.ToUpperInvariant(s[0]) + s[1..])) + "Brush";

    /// <summary>A 16 × 16 vector image of the icon; <paramref name="brushForToken"/> supplies each layer's brush.</summary>
    public static DrawingImage CreateImage(string name, Func<string, IBrush> brushForToken)
    {
        var group = new DrawingGroup();
        // Invisible frame: keeps the image at the full 16 × 16 grid whatever the drawn bounds.
        group.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = Brushes.Transparent, Pen = null });
        foreach (var layer in Get(name).Layers)
        {
            var brush = brushForToken(layer.ColorToken);
            group.Children.Add(layer.IsFilled
                ? new GeometryDrawing { Geometry = layer.Geometry, Brush = brush, Pen = null }
                : new GeometryDrawing
                {
                    Geometry = layer.Geometry,
                    Brush = null,
                    Pen = new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                });
        }

        return new DrawingImage(group);
    }

    private static Dictionary<string, Definition> Load(string folder)
    {
        var result = new Dictionary<string, Definition>(StringComparer.Ordinal);
        foreach (var uri in AssetLoader.GetAssets(new Uri(folder), null).Where(u => u.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = AssetLoader.Open(uri);
            var root = XDocument.Load(stream).Root!;
            XNamespace svg = "http://www.w3.org/2000/svg";
            var paths = root.Elements(svg + "path")
                .Select(p => new PathDefinition(
                    (string)p.Attribute("d")!,
                    (string?)p.Attribute("fill") != "none",
                    (string?)p.Attribute("data-token")))
                .ToList();
            result[Path.GetFileNameWithoutExtension(uri.AbsolutePath)] = new Definition((string?)root.Attribute("data-token") ?? DefaultToken, paths);
        }

        return result;
    }

    private sealed record PathDefinition(string Data, bool IsFilled, string? Token);

    private sealed record Definition(string Token, IReadOnlyList<PathDefinition> Paths);
}
