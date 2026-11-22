using FreexComp.Designer.Editing;
using FreexComp.Designer.Layout;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Layout;

/// <summary><see cref="LayoutCommit"/>: the designer keeps stored bounds equal to computed bounds (DSG-LAY-040; D-038).</summary>
public sealed class LayoutCommitTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-040")]
    public void StoresComputedBoundsOfDifferences()
    {
        // An alClient panel whose stored bounds are not the docked result.
        const string text = """
            object F: TForm
              ClientWidth = 480
              ClientHeight = 320
              object Outer: TPanel
                Left = 8
                Top = 8
                Width = 464
                Height = 304
                Align = alClient
              end
            end

            """;
        var document = FormParser.Parse(text, "commit.form", TestCatalog.Instance).Document!;
        var editor = new FormEditor(document, TestCatalog.Instance);
        var engine = new LayoutEngine(TestCatalog.Instance, RendererRegistry.CreateDefault());
        var before = FormWriter.Write(document);
        Assert.Single(engine.Compute(document).Differences());

        var (redo, undo) = LayoutCommit.StoreDifferences(editor, engine.Compute(document));
        Assert.Empty(engine.Compute(document).Differences());
        Assert.Contains("    Left = 0\n    Top = 0\n    Width = 480\n    Height = 320\n", FormWriter.Write(document), StringComparison.Ordinal);
        Assert.Equal(4, redo.Count);

        editor.Apply(undo);
        Assert.Equal(before, FormWriter.Write(document));
        editor.Apply(redo);
        Assert.Empty(engine.Compute(document).Differences());

        // Nothing to store: no changes.
        var (none, _) = LayoutCommit.StoreDifferences(editor, engine.Compute(document));
        Assert.Empty(none);
    }
}
