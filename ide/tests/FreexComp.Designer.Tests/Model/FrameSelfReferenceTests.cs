using FreexComp.Designer.Inspector;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Text;
using static FreexComp.Designer.Tests.Support.FrameFixtures;

namespace FreexComp.Designer.Tests.Model;

/// <summary>
/// The header name inside a frame's own file names the frame being read (rtl RTL-1104, D-124): a plain <c>FooterFrame</c>
/// and a qualified <c>FooterFrame.Child</c> reference resolve to the frame and its own child, in the frame document and in
/// every instance merged from it; they are kept as written. In another file the frame's header name is no name of that file.
/// </summary>
public sealed class FrameSelfReferenceTests
{
    private const string FooterFrame = """
        object FooterFrame: TFooterFrame
          Left = 0
          Top = 0
          Width = 320
          Height = 200
          object Pages: TPageControl
            Left = 8
            Top = 8
            Width = 300
            Height = 120
            ActivePage = FooterFrame.Sheet2
            TabOrder = 0
            object Sheet1: TTabSheet
              Caption = 'First'
            end
            object Sheet2: TTabSheet
              Caption = 'Second'
            end
          end
          object Note: TEdit
            Left = 8
            Top = 136
            Width = 120
            Height = 23
            TabOrder = 1
          end
          object NoteLabel: TLabel
            Left = 136
            Top = 140
            Width = 40
            Height = 15
            Caption = 'Note'
            FocusControl = FooterFrame.Note
          end
          object FrameLabel: TLabel
            Left = 136
            Top = 160
            Width = 40
            Height = 15
            Caption = 'Frame'
            FocusControl = FooterFrame
          end
        end

        """;

    private const string OrderForm = """
        object OrderForm: TOrderForm
          Left = 200
          Top = 120
          Caption = 'Order'
          ClientWidth = 680
          ClientHeight = 240
          PixelsPerInch = 96
          inline Footer1: TFooterFrame
            Left = 8
            Top = 8
            Width = 320
            Height = 200
            TabOrder = 0
          end
          inline Footer2: TFooterFrame
            Left = 340
            Top = 8
            Width = 320
            Height = 200
            TabOrder = 1
            inherited NoteLabel: TLabel
              Caption = 'Remark'
            end
          end
        end

        """;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-028")]
    [Trait("Req", "DSG-FD-070")]
    [Trait("Req", "DSG-RND-017")]
    public void FrameDocument_HeaderNameReferencesResolveToItselfAndAreKept()
    {
        var frames = new AncestorCatalog();
        var definition = AddFrame(frames, FooterFrame, "FooterFrameUnit");
        var document = definition.Document;

        // Not reported as unresolved (F0105) or of a wrong class (F0103), and written back unchanged.
        Assert.Empty(document.Diagnostics);
        Assert.Equal(FooterFrame.Replace("\r\n", "\n", StringComparison.Ordinal), FormWriter.Write(document));

        // The Object Inspector shows the references as written.
        var converter = new ValueConverter(Components);
        Assert.Equal("FooterFrame.Note", Shown(converter, document.FindByName("NoteLabel")!, "FocusControl"));
        Assert.Equal("FooterFrame", Shown(converter, document.FindByName("FrameLabel")!, "FocusControl"));
        Assert.Equal("FooterFrame.Sheet2", Shown(converter, document.FindByName("Pages")!, "ActivePage"));

        // ActivePage = FooterFrame.Sheet2 names the frame's own Sheet2: the design-active sheet, the visible page.
        var pages = document.FindByName("Pages")!;
        Assert.Same(document.FindByName("Sheet2"), PageControlRenderer.ActiveSheet(pages));
        var layout = new LayoutEngine(Components, RendererRegistry.CreateDefault()).Compute(document);
        Assert.Equal((false, true), (layout.GetVisible(document.FindByName("Sheet1")!), layout.GetVisible(document.FindByName("Sheet2")!)));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-106")]
    [Trait("Req", "DSG-FD-113")]
    [Trait("Req", "DSG-RND-017")]
    public void Instances_HeaderNameReferencesOfTheFrameResolveInEachInstanceAndAreKept()
    {
        var frames = new AncestorCatalog();
        var definition = AddFrame(frames, FooterFrame, "FooterFrameUnit");
        var document = ParseForm(OrderForm, frames);

        Assert.Empty(document.Diagnostics);
        Assert.Equal(OrderForm.Replace("\r\n", "\n", StringComparison.Ordinal), FormWriter.Write(document));
        Assert.Equal(FooterFrame.Replace("\r\n", "\n", StringComparison.Ordinal), FormWriter.Write(definition.Document));

        var converter = new ValueConverter(Components);
        var registry = RendererRegistry.CreateDefault();
        var layout = new LayoutEngine(Components, registry).Compute(document);
        foreach (var instance in new[] { "Footer1", "Footer2" })
        {
            // The inherited values are shown as the frame's file has them; nothing is stored in the form.
            var noteLabel = document.FindByQualifiedName(instance + ".NoteLabel")!;
            var frameLabel = document.FindByQualifiedName(instance + ".FrameLabel")!;
            var pages = document.FindByQualifiedName(instance + ".Pages")!;
            Assert.Equal("FooterFrame.Note", Shown(converter, noteLabel, "FocusControl"));
            Assert.Equal("FooterFrame", Shown(converter, frameLabel, "FocusControl"));
            Assert.Equal("FooterFrame.Sheet2", Shown(converter, pages, "ActivePage"));
            Assert.False(noteLabel.IsOverridden("FocusControl"));
            Assert.False(pages.IsOverridden("ActivePage"));

            // Each instance's own Sheet2 is its design-active sheet and its visible page.
            var sheet1 = document.FindByQualifiedName(instance + ".Sheet1")!;
            var sheet2 = document.FindByQualifiedName(instance + ".Sheet2")!;
            Assert.Same(sheet2, PageControlRenderer.ActiveSheet(pages));
            Assert.Equal((false, true), (layout.GetVisible(sheet1), layout.GetVisible(sheet2)));

            // The tab strip draws the inherited captions and fills the active (second) tab with the window colour.
            var canvas = new RecordingCanvas();
            var strip = RenderContext.FromLayout(pages, layout);
            registry.Resolve(strip.Info.RendererId).Render(strip, canvas);
            var drawn = canvas.ToText();
            Assert.Contains("\"First\"", drawn, StringComparison.Ordinal);
            Assert.Contains("\"Second\"", drawn, StringComparison.Ordinal);
        }
    }

    /// <summary>In the outer file the frame's header name is no name of that file: a plain reference to it is <c>F0105</c>.</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-028")]
    [Trait("Req", "DSG-FD-112")]
    public void OuterFile_FrameHeaderNameIsNotAName()
    {
        var frames = new AncestorCatalog();
        AddFrame(frames, FooterFrame, "FooterFrameUnit");
        var text = OrderForm.Replace("      Caption = 'Remark'", "      Caption = 'Remark'\n      FocusControl = FooterFrame", StringComparison.Ordinal);

        var document = ParseForm(text, frames);

        var diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("F0105", "Footer2.NoteLabel", "FocusControl"), (diagnostic.Code, diagnostic.ObjectName, diagnostic.PropertyPath));
        Assert.Equal(text.Replace("\r\n", "\n", StringComparison.Ordinal), FormWriter.Write(document));
    }

    private static string Shown(ValueConverter converter, FormObject obj, string path) =>
        converter.Format(Components.FindPropertyPath(obj.Info!, path)!, obj.FindProperty(path)?.Value ?? obj.InheritedValue(path));
}
