using System.Text;
using FreexComp.Ide.Core.Designer;
using FreexComp.Ide.Core.Tests.Designer.CodeSync;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>Palette icons (tool-palette.md DSG-TP-009; D-087).</summary>
[Trait("Phase", "5")]
public sealed class ToolPaletteRuleTests
{
    [Fact]
    [Trait("Req", "DSG-TP-009")]
    public void IconsAreReadLazilyAndAMissingOneFallsBackWithOneLogLine()
    {
        var disk = new InMemoryFileSystem();
        disk.CreateDirectory(@"C:\meta\icons");
        var svg = Encoding.UTF8.GetBytes("<svg viewBox=\"0 0 24 24\"><rect x=\"2\" y=\"2\" width=\"20\" height=\"20\"/></svg>");
        disk.WriteAllBytes(@"C:\meta\icons\button.svg", svg);
        disk.WriteAllBytes(@"C:\meta\icons\edit.png", [1, 2, 3]);
        var log = new RecordingLog();
        var palette = new ToolPaletteViewModel(CodeSyncTests.Catalog, fileSystem: disk, log: log);

        // A readable SVG: its bytes, read when asked for (not when the palette is built).
        Assert.Equal(svg, palette.ReadIcon(new PaletteItem("TButton", "Button", "StdCtrls", @"C:\meta\icons\button.svg")));
        Assert.Empty(log.Lines);

        // Missing, or not SVG: null (the view draws the generic glyph), no message, one log line per file.
        var missing = new PaletteItem("TLabel", "Label", "StdCtrls", @"C:\meta\icons\label.svg");
        Assert.Null(palette.ReadIcon(missing));
        Assert.Null(palette.ReadIcon(missing));
        Assert.Null(palette.ReadIcon(new PaletteItem("TEdit", "Edit", "StdCtrls", @"C:\meta\icons\edit.png")));
        Assert.Equal(2, log.Lines.Count);
        Assert.Contains(log.Lines, l => l.Contains(@"label.svg", StringComparison.Ordinal));

        // No icon at all: null, nothing logged.
        Assert.Null(palette.ReadIcon(new PaletteItem("TPanel", "Panel", "ExtCtrls", null)));
        Assert.Equal(2, log.Lines.Count);

        // An icon the view could not draw is reported once as well.
        palette.ReportIconFailure(@"C:\meta\icons\button.svg");
        palette.ReportIconFailure(@"C:\meta\icons\button.svg");
        Assert.Equal(3, log.Lines.Count);
        Assert.Null(palette.ReadIcon(new PaletteItem("TButton", "Button", "StdCtrls", @"C:\meta\icons\button.svg")));
    }
}
