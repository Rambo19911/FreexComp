using FreexComp.Designer.Model;
using static FreexComp.Designer.Tests.Support.FrameFixtures;
using static FreexComp.Designer.Tests.Support.InheritanceFixtures;

namespace FreexComp.Designer.Tests.Text;

/// <summary>Frames derived from frames in the model (form-inheritance-designer.md §2; D-129).</summary>
public sealed class InheritedFrameParseTests
{
    private const string FancyAddressFrame = """
        inherited FancyAddressFrame: TFancyAddressFrame
          Color = clBtnFace
          inherited StreetLabel: TLabel
            Caption = 'Fancy street'
          end
          inherited CityPanel: TPanel
            object CountryLabel: TLabel
              Left = 8
              Top = 32
              Width = 50
              Height = 15
              Caption = 'Country'
            end
          end
        end

        """;

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-124")]
    [Trait("Req", "DSG-FD-125")]
    public void FrameDescendant_InstancesMergeTheWholeChain()
    {
        // The frame catalog doubles as the ancestor catalog: a frame derived from a project frame.
        var frames = AddressFrames();
        var address = frames.Find("TAddressFrame")!;
        var fancy = AddLevel(frames, FancyAddressFrame, "FancyAddressFrameUnit", "TAddressFrame", frames);
        var derived = fancy.Document;

        // DSG-FD-125 for a `container` root.
        Assert.Empty(derived.Diagnostics);
        Assert.True(derived.Root.IsInheritedRoot);
        Assert.Same(address, derived.Ancestor);
        Assert.Same(Components.Find("TFrame"), derived.Root.Info);
        Assert.Same(address.Document.Root, derived.Root.InheritedFrom);
        Assert.Equal(["StreetLabel", "StreetEdit", "CityPanel", "CityPicker"], derived.Root.Children.Select(c => c.Name));
        Assert.Equal(["CityLabel", "CountryLabel"], derived.FindByName("CityPanel")!.Children.Select(c => c.Name));
        var picker = derived.FindByName("CityPicker")!;
        Assert.Equal((true, true), (picker.IsInherited, picker.IsFrameInstance));
        Assert.Equal(["CityEdit", "ZipEdit"], picker.Children.Select(c => c.Name));
        Assert.Equal("Springfield", Effective(derived.FindByQualifiedName("CityPicker.CityEdit")!, "Text"));

        // DSG-FD-124: an instance of the derived frame merges the whole chain.
        var form = ParseForm("""
            object HouseForm: THouseForm
              Left = 200
              Top = 120
              ClientWidth = 640
              ClientHeight = 400
              PixelsPerInch = 96
              inline House: TFancyAddressFrame
                Left = 8
                Top = 8
                Width = 320
                Height = 180
                TabOrder = 0
                inherited StreetEdit: TEdit
                  Text = 'Main road'
                end
                inherited CountryLabel: TLabel
                  Caption = 'Land'
                end
              end
            end

            """, frames, "house.form");
        Assert.Empty(form.Diagnostics);
        var house = form.FindByName("House")!;
        Assert.Same(fancy, house.Frame);
        Assert.Same(derived.Root, house.InheritedFrom);
        Assert.Equal(["StreetLabel", "StreetEdit", "CityPanel", "CityPicker"], house.Children.Select(c => c.Name));
        Assert.Equal(["CityLabel", "CountryLabel"], form.FindByQualifiedName("House.CityPanel")!.Children.Select(c => c.Name));
        Assert.Equal(["CityEdit", "ZipEdit"], form.FindByQualifiedName("House.CityPicker")!.Children.Select(c => c.Name));
        Assert.Equal(("Fancy street", "Main road", "Land", "City"), (Effective(form.FindByQualifiedName("House.StreetLabel")!, "Caption"),
            Effective(form.FindByQualifiedName("House.StreetEdit")!, "Text"), Effective(form.FindByQualifiedName("House.CountryLabel")!, "Caption"),
            Effective(form.FindByQualifiedName("House.CityLabel")!, "Caption")));
        Assert.Equal(("Springfield", "clBtnFace", "False"), (Effective(form.FindByQualifiedName("House.CityPicker.CityEdit")!, "Text"),
            Effective(house, "Color"), Effective(house, "ParentColor")));
        var street = form.FindByQualifiedName("House.StreetLabel")!;
        Assert.Same(derived.FindByName("StreetLabel"), street.InheritedFrom);
        Assert.Same(address.Document.FindByName("StreetLabel"), street.InheritedFrom!.InheritedFrom);

        // The handler stored in the base frame's file, through the derived frame.
        var handler = form.FindByQualifiedName("House.StreetEdit")!.InheritedHandler("OnChange")!.Value;
        Assert.Equal("StreetEditChange", handler.Method);
        Assert.Same(address, handler.Frame);
    }

    /// <summary>
    /// F0111 through an ancestor (Q-P8A-18, D-129 follow-up, D-132): <c>TFancyAddressFrame</c> contains the city frame only
    /// through the inherited instance of its ancestor, so a city frame placing a fancy address frame contains itself; and a
    /// frame placing its own descendant contains itself through the ancestor level.
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-107")]
    [Trait("Req", "DSG-FD-124")]
    public void RepeatedClass_CountsInheritedInstancesAndTheAncestor_F0111()
    {
        var frames = AddressFrames();
        AddLevel(frames, FancyAddressFrame, "FancyAddressFrameUnit", "TAddressFrame", frames);
        Assert.Equal("TCityFrame", FrameCatalog.RepeatedClass(frames, "TFancyAddressFrame", "TCityFrame"));
        Assert.Equal("TAddressFrame", FrameCatalog.RepeatedClass(frames, "TFancyAddressFrame", "TAddressFrame"));
        Assert.Null(FrameCatalog.RepeatedClass(frames, "TFancyAddressFrame", "THouseForm"));

        var city = ParseFrame("""
            object CityFrame: TCityFrame
              Left = 0
              Top = 0
              Width = 400
              Height = 300
              inline Fancy: TFancyAddressFrame
                Left = 0
                Top = 0
                Width = 320
                Height = 180
              end
            end

            """, frames, "city.form");
        var diagnostic = Assert.Single(city.Diagnostics);
        Assert.Equal("F0111", diagnostic.Code);
        Assert.True(city.FindByName("Fancy")!.IsPreserved);
    }
}
