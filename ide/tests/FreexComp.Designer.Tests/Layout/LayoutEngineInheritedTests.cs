using FreexComp.Designer.Layout;
using FreexComp.Designer.Rendering;
using static FreexComp.Designer.Tests.Support.FrameFixtures;
using static FreexComp.Designer.Tests.Support.InheritanceFixtures;

namespace FreexComp.Designer.Tests.Layout;

public sealed partial class LayoutEngineTests
{
    /// <summary>
    /// DSG-LAY-067, 068: a resolved descendant is laid out from its merged tree with effective values; the dump lists the
    /// children of every container inherited first, then new; a read-only descendant is laid out as a box form.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-LAY-067")]
    [Trait("Req", "DSG-LAY-068")]
    public void InheritedForm_DumpListsInheritedThenNewChildren()
    {
        var ancestors = DialogChain();
        var leaf = ParseWith(LeafDialog, "TMidDialog", null, ancestors, "leaf.form");
        var engine = new LayoutEngine(Components, RendererRegistry.CreateDefault());

        var result = engine.Compute(leaf, LayoutMode.RunTime);

        var dump = result.ToDump();
        Assert.Equal(("TLeafDialog", "LeafDialog", 480, 200), (dump.Class, dump.Name, dump.ClientWidth, dump.ClientHeight));
        Assert.Equal(
        [
            "TLabel TitleLabel 8,8,60,15",
            "TPanel ButtonPanel 0,160,400,40",
            "  TButton OkButton 220,8,75,25",
            "  TButton CancelButton 315,8,75,25",
            "  TButton HelpButton 8,8,75,25",
            "TEdit NameEdit 8,32,200,23",
            "TLabel NoteLabel 8,64,80,15",
        ], Flatten(dump.Controls, ""));
        Assert.Empty(result.Differences());

        // DSG-LAY-068: a read-only descendant (F0112) is outside the dump guarantee; it is laid out as a box form with
        // nothing merged into it.
        var readOnly = ParseWith(LeafDialog, "TForm", null, ancestors);
        Assert.True(readOnly.Root.IsPreserved);
        var box = engine.Compute(readOnly, LayoutMode.RunTime).ToDump();
        Assert.Equal(("TLeafDialog", "LeafDialog"), (box.Class, box.Name));
        Assert.DoesNotContain(Flatten(box.Controls, ""), l => l.Contains("CancelButton", StringComparison.Ordinal));
    }
}
