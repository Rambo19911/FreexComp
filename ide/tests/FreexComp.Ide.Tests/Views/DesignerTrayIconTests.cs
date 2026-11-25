using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FreexComp.Ide.Designer;
using DesignRect = FreexComp.Designer.Rect;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// D-121, UI-DSG-062: the tray tile of a non-visual component holds the component's palette icon — the SVG file its
/// metadata names (<c>palette.icon</c>, DSG-TP-009) — not the generic glyph; the generic glyph stays for a component
/// without a usable icon. The design canvas is recorded into a drawing group, so what it draws can be measured.
/// </summary>
[Trait("Phase", "8")]
public sealed class DesignerTrayIconTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FreexComp.Tests", "tray-icon-" + Guid.NewGuid().ToString("N"));

    public DesignerTrayIconTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>What <see cref="AvaloniaDesignCanvas.DrawIcon"/> draws for <paramref name="iconKey"/> into the tile's icon box (4, 4, 24 × 24).</summary>
    private static Rect Drawn(string iconKey)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            var canvas = new AvaloniaDesignCanvas(context, _ => Brushes.Black);
            canvas.DrawIcon(iconKey, new DesignRect(4, 4, 24, 24));
        }

        return group.GetBounds();
    }

    private static void AssertNear(Rect expected, Rect actual) =>
        Assert.True(Math.Abs(expected.X - actual.X) <= 0.5 && Math.Abs(expected.Y - actual.Y) <= 0.5
            && Math.Abs(expected.Width - actual.Width) <= 0.5 && Math.Abs(expected.Height - actual.Height) <= 0.5,
            $"drawn {actual}, expected {expected}");

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DSG-062")]
    [Trait("Req", "DSG-TP-009")]
    public void TrayTile_DrawsTheMetadataIconOfTheComponent()
    {
        // The metadata icon: a filled bar across the top of its 24 × 24 view box (2, 2) … (22, 6).
        var icon = Path.Combine(_dir, "TBar.svg");
        File.WriteAllText(icon, """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" stroke="none"><path d="M2 2H22V6H2Z"/></svg>
            """);

        // Drawn scaled into the icon box: (4 + 2, 4 + 2), 20 × 4 — not the generic glyph's centred square.
        AssertNear(new Rect(6, 6, 20, 4), Drawn(icon));

        // No usable icon (missing file, not SVG): the generic glyph, as before.
        var generic = Drawn("builtin:component");
        Assert.True(generic.Width > 0 && generic.Height > 0, "the generic glyph is drawn");
        AssertNear(generic, Drawn(Path.Combine(_dir, "Missing.svg")));
        var notSvg = Path.Combine(_dir, "TText.svg");
        File.WriteAllText(notSvg, "not an svg");
        AssertNear(generic, Drawn(notSvg));
    }
}
