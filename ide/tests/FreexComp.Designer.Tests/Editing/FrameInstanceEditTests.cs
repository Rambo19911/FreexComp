using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using static FreexComp.Designer.Tests.Support.FrameFixtures;

namespace FreexComp.Designer.Tests.Editing;

/// <summary>Editing frame instances and their inherited objects (frames-designer.md DSG-FD-109…111; D-118).</summary>
public sealed class FrameInstanceEditTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-109")]
    [Trait("Req", "DSG-FD-110")]
    [Trait("Req", "DSG-FD-112")]
    public void OverrideSetEqualToInheritedIsRemoved_StructureChangesRejected()
    {
        var document = ParseForm(CustomerForm, AddressFrames());
        var editor = new FormEditor(document, Components);
        var label = document.FindByQualifiedName("ShippingAddress.StreetLabel")!;

        // An override is stored; setting the inherited value again removes it (not the metadata default).
        var inverse = editor.Apply(new SetProperty("ShippingAddress.StreetLabel", "Caption", new StringValue("Delivery street")));
        Assert.True(label.IsOverridden("Caption"));
        Assert.Contains("    inherited StreetLabel: TLabel\n      Caption = 'Delivery street'\n    end\n", FormWriter.Write(document), StringComparison.Ordinal);
        Assert.Equal("ShippingAddress.StreetLabel", Assert.IsType<RemoveProperty>(inverse).ObjectName);
        editor.Apply(new SetProperty("ShippingAddress.StreetLabel", "Caption", new StringValue("Street")));
        Assert.False(label.IsOverridden("Caption"));
        Assert.Equal(CustomerForm, FormWriter.Write(document));

        // A value equal to the metadata default is an override when the frame stores another value.
        editor.Apply(new SetProperty("ShippingAddress.StreetEdit", "Text", new StringValue("")));
        Assert.Contains("    inherited StreetEdit: TEdit\n      Text = ''\n    end\n", FormWriter.Write(document), StringComparison.Ordinal);
        editor.Apply(new RemoveProperty("ShippingAddress.StreetEdit", "Text")); // = revert to inherited
        Assert.Equal(CustomerForm, FormWriter.Write(document));

        // Geometry of inherited objects is not always-written: equal to the inherited value → nothing stored.
        editor.Apply(new SetProperty("ShippingAddress.StreetLabel", "Left", new IntegerValue(8, false)));
        Assert.False(label.IsOverridden("Left"));
        var moved = editor.Apply(new SetProperty("ShippingAddress.StreetLabel", "Left", new IntegerValue(20, false)));
        Assert.Equal(("20", true), (Effective(label, "Left"), label.IsOverridden("Left")));
        editor.Apply(moved); // the inverse removes the override
        Assert.Equal(CustomerForm, FormWriter.Write(document));

        // Paired properties: Font.* on an inherited label stores ParentFont = False as an override as well.
        editor.Apply(new SetProperty("BillingAddress.CityLabel", "Font.Height", new IntegerValue(-16, false)));
        Assert.Contains("      inherited CityLabel: TLabel\n        Left = 16\n        Caption = 'Town'\n        Font.Height = -16\n        ParentFont = False\n      end\n",
            FormWriter.Write(document), StringComparison.Ordinal);
        editor.Apply(new SetProperty("BillingAddress.CityLabel", "ParentFont", new IdentValue("True")));
        Assert.False(document.FindByQualifiedName("BillingAddress.CityLabel")!.IsOverridden("Font.Height"));

        // DSG-FD-112: override lines are validated against the inherited object's class.
        editor.Apply(new SetProperty("BillingAddress.StreetEdit", "MaxLength", new IntegerValue(-1, false)));
        Assert.Contains(document.Diagnostics, d => d.Code == "F0103" && d.ObjectName == "BillingAddress.StreetEdit");
        editor.Apply(new RemoveProperty("BillingAddress.StreetEdit", "MaxLength"));
        Assert.Empty(document.Diagnostics);

        // Structure changes inside an instance are rejected and change nothing.
        var text = FormWriter.Write(document);
        var version = document.Version;
        var button = new FormObjectData(FormObjectKind.Object, "Button9", "TButton", [], []);
        FormChange[] rejected =
        [
            new RemoveObject("BillingAddress.StreetLabel"),
            new RenameObject("BillingAddress.StreetLabel", "RoadLabel"),
            new MoveObject("BillingAddress.StreetLabel", "BillingAddress.CityPanel", 0),
            new MoveObject("BillingAddress.StreetLabel", "BillingAddress", 2),
            new MoveObject("BillingAddress.CityLabel", "CustomerForm", 0),
            new AddObject("BillingAddress.CityPanel", 0, button),
            new AddObject("BillingAddress", 0, button),
            new MoveObject("StreetLabel", "BillingAddress", 0),
            new MoveObject("StreetLabel", "BillingAddress.CityPanel", 0),
        ];
        foreach (var change in rejected)
        {
            Assert.Throws<InvalidFormChangeException>(() => editor.Apply(change));
        }

        Assert.Equal((text, version), (FormWriter.Write(document), document.Version));

        // DSG-FD-110: the instance's own lines are compared with the definition root's values.
        var shipping = document.FindByName("ShippingAddress")!;
        editor.Apply(new SetProperty("ShippingAddress", "Color", new IdentValue("clWindow"))); // = the frame root's colour
        Assert.False(shipping.IsOverridden("Color"));
        editor.Apply(new SetProperty("ShippingAddress", "Color", new IdentValue("clBtnFace"))); // = the metadata default
        Assert.True(shipping.IsOverridden("Color"));
        editor.Apply(new RemoveProperty("ShippingAddress", "Color"));
        editor.Apply(new SetProperty("ShippingAddress", "Width", new IntegerValue(320, false))); // geometry: always written
        Assert.True(shipping.IsOverridden("Width"));
        editor.Apply(new SetProperty("ShippingAddress", "TabOrder", new IntegerValue(-1, false))); // TabOrder: form-format §2.3
        Assert.False(shipping.IsOverridden("TabOrder"));
        editor.Apply(new SetProperty("ShippingAddress", "TabOrder", new IntegerValue(1, false)));

        // An instance is an ordinary object: rename, delete and undo.
        editor.Apply(new RenameObject("ShippingAddress", "DeliveryAddress"));
        Assert.Equal("DeliveryAddress.StreetLabel", document.FindByQualifiedName("DeliveryAddress.StreetLabel")!.QualifiedName);
        var restore = editor.Apply(new RemoveObject("DeliveryAddress"));
        Assert.Null(document.FindByQualifiedName("DeliveryAddress.StreetLabel"));
        editor.Apply(restore);
        editor.Apply(new RenameObject("DeliveryAddress", "ShippingAddress"));
        Assert.Equal(CustomerForm, FormWriter.Write(document));
        Assert.Equal(4, document.FindByName("ShippingAddress")!.Children.Count);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-111")]
    [Trait("Req", "DSG-FD-120")]
    public void ChangingAFrameProperty_UpdatesBothInstances_OverrideKept()
    {
        var frames = AddressFrames();
        var frame = frames.Find("TAddressFrame")!.Document;
        var document = ParseForm(CustomerForm, frames);
        var editor = new FormEditor(document, Components);
        var undo = editor.Apply(new SetProperty("ShippingAddress.StreetEdit", "Text", new StringValue("delivery")));
        var text = FormWriter.Write(document);
        var events = new List<FormChangedEventArgs>();
        document.Changed += (_, e) => events.Add(e);
        var version = document.Version;

        // The frame is edited in its own designer: both instances follow, the override stays.
        var frameEditor = new FormEditor(frame, Components);
        frameEditor.Apply([
            new SetProperty("StreetLabel", "Caption", new StringValue("Road")),
            new SetProperty("StreetEdit", "Width", new IntegerValue(200, false)),
        ]);

        var e = Assert.Single(events);
        Assert.Equal((FormChangeFlags.Structure, true), (e.Flags, e.IsRemerge));
        Assert.Equal(version + 1, document.Version);
        Assert.Equal(text, FormWriter.Write(document)); // the file model's overrides are untouched
        Assert.Equal(("Road", "Billing street"), (Effective(document.FindByQualifiedName("ShippingAddress.StreetLabel")!, "Caption"),
            Effective(document.FindByQualifiedName("BillingAddress.StreetLabel")!, "Caption")));
        Assert.Equal(("200", "200"), (Effective(document.FindByQualifiedName("ShippingAddress.StreetEdit")!, "Width"),
            Effective(document.FindByQualifiedName("BillingAddress.StreetEdit")!, "Width")));
        Assert.Equal("delivery", Effective(document.FindByQualifiedName("ShippingAddress.StreetEdit")!, "Text"));
        Assert.Same(frame.FindByName("StreetLabel"), document.FindByQualifiedName("BillingAddress.StreetLabel")!.InheritedFrom);
        Assert.Empty(document.Diagnostics);

        // The undo stack is kept: an inverse made before the re-merge still applies.
        editor.Apply(undo);
        Assert.Equal("street", Effective(document.FindByQualifiedName("ShippingAddress.StreetEdit")!, "Text"));

        // A nested frame changes: the instances of frames that contain it follow as well.
        var city = frames.Find("TCityFrame")!.Document;
        new FormEditor(city, Components).Apply(new SetProperty("ZipEdit", "Width", new IntegerValue(70, false)));
        Assert.Equal("70", Effective(document.FindByQualifiedName("BillingAddress.CityPicker.ZipEdit")!, "Width"));

        // A child renamed in the frame: the override that no longer matches becomes a preserved F0109 block.
        events.Clear();
        frameEditor.Apply(new RenameObject("CityLabel", "TownLabel"));
        Assert.Single(events);
        Assert.Equal("Component CityLabel: TLabel is not part of TAddressFrame: shown read-only",
            Assert.Single(document.Diagnostics, d => d.Code == "F0109").Message);
        Assert.Equal(["CityLabel"], document.FindByQualifiedName("BillingAddress.CityPanel")!.PreservedBlocks.Select(b => b.Name));
        Assert.Equal("City", Effective(document.FindByQualifiedName("BillingAddress.TownLabel")!, "Caption"));
        Assert.Equal(CustomerForm, FormWriter.Write(document)); // nothing is lost

        // A frame that leaves the catalog leaves its instances preserved (F0101); text unchanged.
        var definition = frames.Find("TAddressFrame")!;
        frames.Remove("TAddressFrame");
        Assert.True(document.FindByName("BillingAddress")!.IsPreserved);
        Assert.Equal(2, document.Diagnostics.Count(d => d.Code == "F0101"));
        Assert.Equal(CustomerForm, FormWriter.Write(document));
        Assert.Empty(document.FindByName("BillingAddress")!.PreservedBlocks);

        // DSG-FD-120: when it joins the catalog again, the preserved instances resolve again with their overrides.
        frames.Set(definition);
        Assert.True(document.FindByName("BillingAddress")!.IsFrameInstance);
        Assert.False(document.FindByName("ShippingAddress")!.IsPreserved);
        Assert.Equal("F0109", Assert.Single(document.Diagnostics).Code);
        Assert.Equal("Billing street", Effective(document.FindByQualifiedName("BillingAddress.StreetLabel")!, "Caption"));
        Assert.Equal(CustomerForm, FormWriter.Write(document));
    }
}
