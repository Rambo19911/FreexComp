using FreexComp.Designer.Model;
using FreexComp.Designer.Tests.Support;
using FreexComp.Designer.Text;
using static FreexComp.Designer.Tests.Support.FrameFixtures;

namespace FreexComp.Designer.Tests.Text;

/// <summary>Frame instances in the parser and the model (frames-designer.md §2, §3; D-118).</summary>
public sealed class FrameInstanceParseTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-102")]
    [Trait("Req", "DSG-FD-106")]
    [Trait("Req", "DSG-FD-108")]
    [Trait("Req", "DSG-FD-118")]
    public void TwoInstances_MergedChildren_EffectiveValues()
    {
        var frames = AddressFrames();
        var address = frames.Find("taddressframe")!; // ASCII case-insensitive
        Assert.Equal(["TAddressFrame", "TCityFrame"], frames.All.Select(f => f.ClassName));
        Assert.Empty(address.Document.Diagnostics);

        var document = ParseForm(CustomerForm, frames);

        Assert.Empty(document.Diagnostics);
        var billing = document.FindByName("BillingAddress")!;
        var shipping = document.FindByName("ShippingAddress")!;
        foreach (var instance in new[] { billing, shipping })
        {
            Assert.Equal((FormObjectKind.Inline, true, false, false), (instance.Kind, instance.IsFrameInstance, instance.IsPreserved, instance.IsInherited));
            Assert.Same(address, instance.Frame);
            Assert.Same(Components.Find("TFrame"), instance.Info);
            Assert.Equal("TAddressFrame", instance.TypeName);
            Assert.Same(address.Document.Root, instance.InheritedFrom);

            // One inherited object per object of the definition, same nesting and creation order.
            Assert.Equal(["StreetLabel", "StreetEdit", "CityPanel", "CityPicker"], instance.Children.Select(c => c.Name));
            Assert.All(instance.Children, c => Assert.Equal((FormObjectKind.Inherited, true, false), (c.Kind, c.IsInherited, c.IsPreserved)));
            for (var i = 0; i < instance.Children.Count; i++)
            {
                Assert.Same(address.Document.Root.Children[i], instance.Children[i].InheritedFrom);
                Assert.Same(address.Document.Root.Children[i].Info, instance.Children[i].Info);
            }

            Assert.Equal(["CityLabel"], instance.Children[2].Children.Select(c => c.Name));
            var picker = instance.Children[3];
            Assert.True(picker.IsFrameInstance); // a nested instance is the scope of its own children
            Assert.Equal("TCityFrame", picker.Frame!.ClassName);
            Assert.Equal(["CityEdit", "ZipEdit"], picker.Children.Select(c => c.Name));
            Assert.Same(address.Document.FindByQualifiedName("CityPicker.CityEdit"), picker.Children[0].InheritedFrom);
        }

        // Effective value = the override when present, else the definition's effective value.
        var billingLabel = document.FindByQualifiedName("BillingAddress.StreetLabel")!;
        var shippingLabel = document.FindByQualifiedName("ShippingAddress.StreetLabel")!;
        Assert.Equal(("Billing street", "Street"), (Effective(billingLabel, "Caption"), Effective(shippingLabel, "Caption")));
        Assert.Equal(("8", "8"), (Effective(billingLabel, "Left"), Effective(shippingLabel, "Left")));
        Assert.True(billingLabel.IsOverridden("Caption"));
        Assert.False(billingLabel.IsOverridden("Left"));
        Assert.False(shippingLabel.IsOverridden("Caption"));
        Assert.Equal(new StringValue("Street"), billingLabel.InheritedValue("Caption"));
        Assert.Equal(new IdentValue("taLeftJustify"), billingLabel.InheritedValue("Alignment")); // the metadata default
        Assert.Equal(("Town", "City"), (Effective(document.FindByQualifiedName("BillingAddress.CityLabel")!, "Caption"),
            Effective(document.FindByQualifiedName("ShippingAddress.CityLabel")!, "Caption")));
        Assert.Equal(("16", "8"), (Effective(document.FindByQualifiedName("BillingAddress.CityLabel")!, "Left"),
            Effective(document.FindByQualifiedName("ShippingAddress.CityLabel")!, "Left")));

        // Events: the override binds the form's method; without one the frame's handler is inherited.
        Assert.Equal(("BillingAddressStreetEditChange", "StreetEditChange"),
            (Effective(document.FindByQualifiedName("BillingAddress.StreetEdit")!, "OnChange"),
             Effective(document.FindByQualifiedName("ShippingAddress.StreetEdit")!, "OnChange")));
        Assert.Equal(new IdentValue("StreetEditChange"), document.FindByQualifiedName("BillingAddress.StreetEdit")!.InheritedValue("OnChange"));
        Assert.Null(document.FindByQualifiedName("BillingAddress.StreetLabel")!.InheritedValue("OnClick"));

        // Nested frames: the frame's own override and the form's override both apply.
        var billingCity = document.FindByQualifiedName("BillingAddress.CityPicker.CityEdit")!;
        var shippingCity = document.FindByQualifiedName("ShippingAddress.CityPicker.CityEdit")!;
        Assert.Equal(("Springfield", "150"), (Effective(billingCity, "Text"), Effective(billingCity, "Width")));
        Assert.Equal(("Springfield", "100"), (Effective(shippingCity, "Text"), Effective(shippingCity, "Width")));
        Assert.Equal("ShippingAddress.CityPicker.CityEdit", shippingCity.QualifiedName);

        // The instance's own values: stored lines, else the definition root's effective values.
        Assert.Equal(("196", "clWindow", "False"), (Effective(shipping, "Top"), Effective(shipping, "Color"), Effective(shipping, "ParentColor")));

        // DSG-FD-108: names per scope — the form's own StreetLabel does not clash with the frame's.
        Assert.Equal("Form street", Effective(document.FindByName("StreetLabel")!, "Caption"));
        Assert.Null(document.FindByName("CityLabel"));
        Assert.Null(document.FindByQualifiedName("BillingAddress.Nope"));
        Assert.Null(document.FindByQualifiedName("StreetLabel.Caption"));
        Assert.Equal("BillingAddress", billing.QualifiedName);

        // DSG-FD-102: without a frame catalog `inline` behaves as before (F0107); an empty catalog knows no frame (F0101).
        var without = ParseForm(CustomerForm, null);
        Assert.Equal(["F0107", "F0107"], without.Diagnostics.Select(d => d.Code));
        Assert.True(without.FindByName("BillingAddress")!.IsPreserved);
        Assert.False(without.FindByName("BillingAddress")!.IsFrameInstance);
        var empty = ParseForm(CustomerForm, AncestorCatalog.Empty);
        Assert.Equal(["form.form(15,26): warning F0101: Class TAddressFrame not found", "form.form(39,27): warning F0101: Class TAddressFrame not found"],
            empty.Diagnostics.Select(d => d.ToString()));
        Assert.Equal(CustomerForm, FormWriter.Write(empty));

        // Fragments (the clipboard) resolve instances as well.
        var fragment = FormParser.ParseFragment(CustomerForm[CustomerForm.IndexOf("  inline ShippingAddress", StringComparison.Ordinal)..^4],
            "clip", Components, frames);
        Assert.Empty(fragment.Diagnostics);
        Assert.True(Assert.Single(fragment.Objects).IsFrameInstance);
        Assert.Equal(4, fragment.Objects[0].Children.Count);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-107")]
    [Trait("Req", "DSG-FD-119")]
    public void Diagnostics_F0109_F0110_F0111_F0101_F0107()
    {
        var frames = AddressFrames();
        // A frame that contains itself: its own document reports nothing yet (the catalog does not know it while it is
        // parsed), but every instance of it is F0111.
        AddFrame(frames, """
            object LoopFrame: TLoopFrame
              Left = 0
              Top = 0
              Width = 100
              Height = 50
              inline Inner: TLoopFrame
                Left = 0
                Top = 0
                Width = 100
                Height = 50
              end
            end

            """, "LoopFrameUnit");
        const string text = """
            object ProblemForm: TProblemForm
              Left = 200
              Top = 120
              ClientWidth = 640
              ClientHeight = 400
              PixelsPerInch = 96
              inline A1: TAddressFrame
                Left = 8
                Top = 8
                Width = 320
                Height = 180
                inherited Gone: TLabel
                  Caption = 'removed child'
                end
                inherited StreetLabel: TLabel
                  Caption = 'kept'
                end
                inherited StreetEdit: TLabel
                  Caption = 'wrong class'
                end
                object Extra: TButton
                  Left = 1
                  Top = 1
                  Width = 75
                  Height = 25
                end
                inherited CityPanel: TPanel
                  object Inner: TButton
                    Left = 2
                    Top = 2
                    Width = 75
                    Height = 25
                  end
                end
                inline Added: TCityFrame
                  Left = 3
                  Top = 3
                  Width = 200
                  Height = 60
                end
              end
              inline L1: TLoopFrame
                Left = 8
                Top = 200
                Width = 100
                Height = 50
              end
              inline U1: TUnknownFrame
                Left = 120
                Top = 200
                Width = 100
                Height = 50
                inherited X: TLabel
                  Caption = 'x'
                end
              end
              inherited Base1: TButton
                Left = 240
                Top = 200
                Width = 75
                Height = 25
              end
            end

            """;

        var document = ParseForm(text, frames, "problem.form");

        Assert.Equal(
        [
            "problem.form(12,15): warning F0109: Component Gone: TLabel is not part of TAddressFrame: shown read-only",
            "problem.form(18,15): warning F0109: Component StreetEdit: TLabel is not part of TAddressFrame: shown read-only",
            "problem.form(21,5): warning F0110: Components cannot be added to frame instance A1: shown read-only",
            "problem.form(28,7): warning F0110: Components cannot be added to frame instance A1: shown read-only",
            "problem.form(35,5): warning F0110: Components cannot be added to frame instance A1: shown read-only",
            "problem.form(42,3): warning F0111: Frame TLoopFrame contains itself: shown read-only",
            "problem.form(48,14): warning F0101: Class TUnknownFrame not found",
            "problem.form(57,3): hint F0107: \"inherited\" objects are not supported: shown read-only",
        ], document.Diagnostics.Select(d => d.ToString()));

        // The instance with bad blocks stays resolved; the blocks are preserved where they were read.
        var a1 = document.FindByName("A1")!;
        Assert.True(a1.IsFrameInstance);
        Assert.False(a1.IsPreserved);
        Assert.Equal("kept", Effective(document.FindByQualifiedName("A1.StreetLabel")!, "Caption"));
        Assert.Equal("street", Effective(document.FindByQualifiedName("A1.StreetEdit")!, "Text"));
        Assert.Equal(["Gone", "StreetEdit", "Extra", "Added"], a1.PreservedBlocks.Select(b => b.Name));
        Assert.All(a1.PreservedBlocks, b => Assert.True(b.IsPreserved));
        Assert.Equal(["Inner"], document.FindByQualifiedName("A1.CityPanel")!.PreservedBlocks.Select(b => b.Name));
        Assert.Null(document.FindByName("Extra")); // names inside preserved blocks belong to no scope of the file

        // F0111 / F0101: the instance is preserved read-only, its blocks are not merged children.
        Assert.Equal((true, false), (document.FindByName("L1")!.IsPreserved, document.FindByName("L1")!.IsFrameInstance));
        Assert.Equal((true, false), (document.FindByName("U1")!.IsPreserved, document.FindByName("U1")!.IsFrameInstance));
        Assert.True(document.FindByName("Base1")!.IsPreserved);

        // Each block is written back verbatim at its position.
        Assert.Equal(text, FormWriter.Write(document));

        // F0111 through the document's own root class: a frame whose instance contains this very frame.
        AddFrame(frames, """
            object WrapFrame: TWrapFrame
              Left = 0
              Top = 0
              Width = 100
              Height = 50
              inline Host: THostFrame
                Left = 0
                Top = 0
                Width = 100
                Height = 50
              end
            end

            """, "WrapFrameUnit");
        var host = ParseFrame("""
            object HostFrame: THostFrame
              Left = 0
              Top = 0
              Width = 300
              Height = 200
              inline Wrap: TWrapFrame
                Left = 0
                Top = 0
                Width = 100
                Height = 50
              end
            end

            """, frames, "host.form");
        Assert.Equal("host.form(6,3): warning F0111: Frame THostFrame contains itself: shown read-only", Assert.Single(host.Diagnostics).ToString());
        Assert.True(host.FindByName("Wrap")!.IsPreserved);

        // DSG-FD-119: a second override block for the same component of a scope is F0106, preserved where it was read.
        var twiceText = CustomerForm.Replace("      Caption = 'Billing street'\n    end\n",
            "      Caption = 'Billing street'\n    end\n    inherited StreetLabel: TLabel\n      Caption = 'twice'\n    end\n", StringComparison.Ordinal);
        var twice = ParseForm(twiceText, frames, "twice.form");
        Assert.Equal("twice.form(24,15): error F0106: A component named StreetLabel already exists", Assert.Single(twice.Diagnostics).ToString());
        Assert.Equal("Billing street", Effective(twice.FindByQualifiedName("BillingAddress.StreetLabel")!, "Caption"));
        Assert.Equal(["StreetLabel"], twice.FindByName("BillingAddress")!.PreservedBlocks.Select(b => b.Name));
        Assert.Equal(twiceText, FormWriter.Write(twice));
    }
}
