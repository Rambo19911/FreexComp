using FreexComp.Designer.Layout;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using static FreexComp.Designer.Tests.Support.FrameFixtures;

namespace FreexComp.Designer.Tests.Layout;

public sealed partial class LayoutEngineTests
{
    /// <summary>
    /// DSG-LAY-065, 066: a resolved instance is a container of its merged inherited objects with their effective values
    /// (nested instances too), listed in the dump under the instance by their own names; a preserved instance is a box
    /// with its stored bounds and no children.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-LAY-065")]
    [Trait("Req", "DSG-LAY-066")]
    public void FrameInstances_DumpListsMergedChildren()
    {
        var text = CustomerForm.Replace("  inline ShippingAddress", """
              inline Lost: TUnknownFrame
                Left = 340
                Top = 40
                Width = 100
                Height = 50
                inherited StreetLabel: TLabel
                  Left = 1
                  Top = 1
                  Width = 10
                  Height = 10
                end
              end
              inline ShippingAddress
            """.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        var document = ParseForm(text, AddressFrames());
        var engine = new LayoutEngine(Components, RendererRegistry.CreateDefault());

        var result = engine.Compute(document, LayoutMode.RunTime);

        Assert.Equal(
        [
            "TLabel StreetLabel 340,8,40,15",
            "TAddressFrame BillingAddress 8,8,320,180",
            "  TLabel StreetLabel 8,8,40,15",
            "  TEdit StreetEdit 64,4,240,23",
            "  TPanel CityPanel 8,40,296,64",
            "    TLabel CityLabel 16,8,24,15",
            "  TCityFrame CityPicker 8,112,200,60",
            "    TEdit CityEdit 8,8,150,23",
            "    TEdit ZipEdit 8,32,60,23",
            "TUnknownFrame Lost 340,40,100,50",
            "TAddressFrame ShippingAddress 8,196,320,180",
            "  TLabel StreetLabel 8,8,40,15",
            "  TEdit StreetEdit 64,4,240,23",
            "  TPanel CityPanel 8,40,296,64",
            "    TLabel CityLabel 8,8,24,15",
            "  TCityFrame CityPicker 8,112,200,60",
            "    TEdit CityEdit 8,8,100,23",
            "    TEdit ZipEdit 8,32,60,23",
        ], Flatten(result.ToDump().Controls, ""));

        // Inherited objects have layout bounds; their computed bounds equal their effective stored ones.
        var city = document.FindByQualifiedName("BillingAddress.CityPicker.CityEdit")!;
        Assert.Equal(new Rect(8 + 8 + 8, 8 + 112 + 8, 150, 23), result.GetSurfaceBounds(city));
        Assert.Empty(result.Differences());
    }

    private static List<string> Flatten(IReadOnlyList<LayoutDumpControl> controls, string indent)
    {
        var lines = new List<string>();
        foreach (var c in controls)
        {
            lines.Add($"{indent}{c.Class} {c.Name} {c.Left},{c.Top},{c.Width},{c.Height}");
            lines.AddRange(Flatten(c.Controls, indent + "  "));
        }

        return lines;
    }
}
