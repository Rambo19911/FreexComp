using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FreexComp.Ide.Core.Tests.Infrastructure;
using FreexComp.Ide.Icons;

namespace FreexComp.Ide.Tests.Themes;

[Trait("Phase", "1")]
[Trait("Kind", "Ui")]
public sealed partial class IconTests
{
    [AvaloniaFact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-TOK-040")]
    [Trait("Req", "UI-TOK-041")]
    public void EveryCatalogIconExists()
    {
        var spec = SpecIcons();
        Assert.True(spec.Count >= 60, $"icon catalog not read: {spec.Count}");

        foreach (var (name, token) in spec)
        {
            var icon = IconCatalog.Get(name);

            Assert.NotEmpty(icon.Layers);
            Assert.Equal(token, icon.ColorToken);
            foreach (var layer in icon.Layers)
            {
                var bounds = layer.Geometry.Bounds;
                Assert.True(bounds.Width > 0 || bounds.Height > 0, $"{name}: empty geometry");
                Assert.True(bounds.X >= -0.01 && bounds.Y >= -0.01 && bounds.Right <= 16.01 && bounds.Bottom <= 16.01, $"{name}: {bounds} outside the 16 x 16 grid");
                Assert.True(Application.Current!.TryFindResource(IconCatalog.BrushKey(layer.ColorToken), Application.Current!.ActualThemeVariant, out _), $"{name}: no brush for {layer.ColorToken}");
            }
        }

        Assert.Equal(spec.Keys.Order(StringComparer.Ordinal), IconCatalog.Names.Order(StringComparer.Ordinal)); // no unlisted files
        Assert.Contains(IconCatalog.Get("bring-front").Layers, l => l.IsFilled && l.ColorToken == "accent.default");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-TOK-042")]
    public void IconBuildsVectorDrawingWithTheGivenBrushes()
    {
        var image = IconCatalog.CreateImage("run", token => token == "status.success" ? Brushes.Green : Brushes.Black);

        var drawing = Assert.IsType<DrawingGroup>(image.Drawing);
        var children = drawing.Children.OfType<GeometryDrawing>().ToList();
        Assert.Null(children[0].Pen); // invisible 16 x 16 frame
        var stroke = Assert.Single(children.Skip(1));
        Assert.Same(Brushes.Green, stroke.Pen!.Brush);
        Assert.Equal(1.25, stroke.Pen.Thickness);
        Assert.Null(stroke.Brush); // outline only
        Assert.Equal(new Size(16, 16), image.Size);
        Assert.False(IconCatalog.TryGet("no-such-icon", out _));
        Assert.Throws<KeyNotFoundException>(() => IconCatalog.Get("no-such-icon"));
    }

    /// <summary>toolbars.md §4: icon name → colour token (the brief's "; token" suffix, else <c>text.primary</c>).</summary>
    private static Dictionary<string, string> SpecIcons()
    {
        var rows = SpecTables.Read(SpecTables.DocPath("spec/ui/toolbars.md"), "4. Icon catalog");
        var icons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var token = TokenSuffix().Match(row["Drawing brief"]) is { Success: true } m ? m.Groups[1].Value : "text.primary";
            foreach (var name in row["Name"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                icons.Add(name, token);
            }
        }

        // Lead notes that add an icon to the §4 catalogue until it gets its table row.
        foreach (Match note in NoteAddition().Matches(File.ReadAllText(SpecTables.DocPath("spec/ui/toolbars.md"))))
        {
            icons.TryAdd(note.Groups["name"].Value, note.Groups["token"].Value);
        }

        return icons;
    }

    [GeneratedRegex(@";\s*([a-z]+\.[a-zA-Z]+)\s*$")]
    private static partial Regex TokenSuffix();

    [GeneratedRegex(@"^\*\*Lead note[^*]*\*\*.*?icon `(?<name>[a-z0-9-]+)` \([^)]*token `(?<token>[a-z]+\.[a-zA-Z]+)`\) is added to the §4 catalogue", RegexOptions.Multiline)]
    private static partial Regex NoteAddition();
}
