using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Tests.Rendering;

/// <summary>The design renderers (design-renderers.md; D-022 … D-030).</summary>
public sealed partial class RendererTests
{
    private static readonly DesignPalette P = DesignPalette.Light;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-005")]
    [Trait("Req", "DSG-RND-006")]
    public void NeverThrowsOnMissingProperties()
    {
        // Stored values of every kind, some of the wrong shape for their property (they are preserved as raw text).
        const string odd = """
            Caption = 'A&&b&c'
            Text = 'line1'#10'line2'
            Alignment = taBogus
            Color = 'red'
            Font.Height = 'x'
            Font.Style = [fsBold, fsNothing]
            Items = 5
            ItemIndex = 99
            Lines = (
              'a'
              'b'
            )
            State = cbGrayed
            Style = csSimple
            BevelWidth = -4
            BorderWidth = 100000
            ScrollBars = ssBoth
            PasswordChar = '*'
            """;
        var withValues = RenderHarness.Control("TButton", 60, 30, odd);
        var stripped = withValues.Info! with { Properties = [] };

        foreach (var id in RendererRegistryTests.RendererIds)
        {
            var renderer = RenderHarness.Registry.Resolve(id);
            foreach (var (w, h) in new[] { (60, 30), (0, 0), (1, 1), (-5, 7), (3, 200) })
            {
                // Metadata stripped to nothing: every read falls back.
                var ctx = new RenderContext(withValues, w, h, info: stripped with { RendererId = id });
                renderer.Render(ctx, new RecordingCanvas());
                _ = renderer.GetClientRect(ctx);
                _ = renderer.GetBaseline(ctx);
            }
        }

        // Full metadata with malformed stored values, for every class of the test catalog.
        foreach (var info in TestCatalog.Instance.All.Where(i => !i.IsAbstract && i.Kind != FreexComp.Designer.Catalog.ComponentKind.Form))
        {
            var obj = RenderHarness.Control(info.ClassName, 50, 40, odd);
            var ctx = RenderHarness.Context(obj);
            var renderer = RenderHarness.Registry.Resolve(info.RendererId);
            renderer.Render(ctx, new RecordingCanvas());
            _ = renderer.GetBaseline(ctx);
        }
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-010")]
    [Trait("Req", "DSG-RND-011")]
    public void FontResolutionFollowsParentFont()
    {
        var document = RenderHarness.Parse("""
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              Font.Color = clRed
              Font.Height = -16
              Font.Name = 'Arial'
              Font.Style = [fsBold]
              object Inherits: TPanel
                object Deep: TLabel
                end
                object Picture: TImage
                end
              end
              object Own: TPanel
                Font.Size = 12
                ParentFont = False
                object FromOwn: TLabel
                end
              end
              object HeightOnly: TEdit
                Font.Height = -20
                Font.Style = [fsItalic, fsUnderline, fsStrikeOut]
                ParentFont = False
              end
              object ParentFontOff: TLabel
                ParentFont = False
              end
            end

            """.ReplaceLineEndings("\n"));
        TextStyle FontOf(string name) => RenderHarness.Context(document.FindByName(name)!).Font;

        var formFont = new TextStyle("Arial", 16, true, false, false, false);
        Assert.Equal(formFont, FontOf("F"));
        Assert.Equal(formFont, FontOf("Inherits"));
        Assert.Equal(formFont, FontOf("Deep"));
        Assert.Equal(formFont, FontOf("Picture")); // no ParentFont in the metadata: behaves as True
        Assert.Equal(new Rgba(255, 0, 0), RenderHarness.Context(document.FindByName("Deep")!).FontColor);

        // Own font: Font.Size in points (12 pt = 16 px) over the defaults; children inherit it.
        var own = new TextStyle("Segoe UI", 16, false, false, false, false);
        Assert.Equal(own, FontOf("Own"));
        Assert.Equal(own, FontOf("FromOwn"));
        Assert.Equal(P.Text, RenderHarness.Context(document.FindByName("FromOwn")!).FontColor);

        // Font.Height (negative px) when Font.Size is not stored; all four style members.
        Assert.Equal(new TextStyle("Segoe UI", 20, false, true, true, true), FontOf("HeightOnly"));

        // ParentFont = False without own font properties: the default design font (DSG-RND-010).
        Assert.Equal(new TextStyle("Segoe UI", 12, false, false, false, false), FontOf("ParentFontOff"));
        Assert.Equal(new TextStyle("Segoe UI", 12, false, false, false, false),
            RenderHarness.Context(RenderHarness.Parse("object G: TForm\nend\n").Root).Font);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-015")]
    public void ColorsAndEnabledChainResolve()
    {
        var document = RenderHarness.Parse("""
            object F: TForm
              Color = clWindow
              object Outer: TPanel
                Color = $00336699
                Enabled = False
                object Inner: TLabel
                end
                object InnerPanel: TPanel
                  ParentColor = True
                end
              end
              object Plain: TPanel
                Color = clNone
              end
            end

            """.ReplaceLineEndings("\n"));
        RenderContext Ctx(string name) => RenderHarness.Context(document.FindByName(name)!);

        Assert.Equal(P.Window, Ctx("F").Fill(P.Face));                 // system colour → design palette
        Assert.Equal(new Rgba(0x99, 0x66, 0x33), Ctx("Outer").Fill(P.Face)); // $00BBGGRR
        Assert.Equal(new Rgba(0x99, 0x66, 0x33), Ctx("Inner").Fill(P.Face)); // TLabel: ParentColor True by default
        Assert.Equal(new Rgba(0x99, 0x66, 0x33), Ctx("InnerPanel").Fill(P.Face));
        Assert.Equal(P.Window, Ctx("Outer").ParentFill);
        Assert.Equal(P.Highlight, Ctx("Plain").Fill(P.Highlight));       // clNone → the caller's fallback

        Assert.False(Ctx("Outer").Enabled);
        Assert.False(Ctx("Inner").Enabled); // inherited
        Assert.True(Ctx("Plain").Enabled);
        Assert.Equal(P.GrayText, Ctx("Inner").Ink);
    }
}
