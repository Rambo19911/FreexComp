using System.Text;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Inspector;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using static FreexComp.Designer.Tests.Support.FrameFixtures;

namespace FreexComp.Designer.Tests.Text;

/// <summary>Writing frame instances and frame documents (frames-designer.md §4; D-118).</summary>
public sealed class FrameFormRoundTripTests
{
    /// <summary>
    /// The frames of the canonical corpus: every <c>*-frame.form</c> file, parsed as a frame document (root ancestor
    /// <c>TFrame</c>) and registered under its root class; the other corpus files are parsed with this catalog.
    /// </summary>
    internal static AncestorCatalog CorpusFrames()
    {
        var frames = new AncestorCatalog();
        foreach (var file in FormParserTests.CorpusFiles("canonical").Where(IsFrameFile))
        {
            AddFrame(frames, File.ReadAllText(TestPaths.Combine("forms/canonical/" + file)), Path.GetFileNameWithoutExtension(file));
        }

        return frames;
    }

    internal static bool IsFrameFile(string file) => file.EndsWith("-frame.form", StringComparison.Ordinal);

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-113")]
    [Trait("Req", "DSG-FD-114")]
    [Trait("Req", "DSG-FD-115")]
    [Trait("Req", "DSG-FD-116")]
    public void TwoInstancesOneOverride_RoundTripsByteIdentical()
    {
        var frames = CorpusFrames();
        var frameBytes = File.ReadAllBytes(TestPaths.Combine("forms/canonical/address-frame.form"));
        var formBytes = File.ReadAllBytes(TestPaths.Combine("forms/canonical/two-frames.form"));
        var formText = new UTF8Encoding(false, true).GetString(formBytes);

        var frame = frames.Find("TAddressFrame")!.Document;
        var form = ParseForm(formText, frames, "two-frames.form");

        Assert.Empty(frame.Diagnostics);
        Assert.Empty(form.Diagnostics);
        Assert.True(form.FindByName("BillingAddress")!.IsFrameInstance);
        Assert.True(form.FindByName("ShippingAddress")!.IsFrameInstance);
        Assert.Equal("BillingAddressStreetEditChange", Effective(form.FindByQualifiedName("BillingAddress.StreetEdit")!, "OnChange"));
        Assert.Equal(frameBytes, Encoding.UTF8.GetBytes(FormWriter.Write(frame)));
        Assert.Equal(formBytes, Encoding.UTF8.GetBytes(FormWriter.Write(form)));

        // An override line equal to the inherited value, read from a file and not edited, keeps its text (DSG-FD-115).
        var equal = formText.Replace("      Caption = 'Billing street'\n", "      Caption = 'Street'\n", StringComparison.Ordinal);
        Assert.Equal(equal, FormWriter.Write(ParseForm(equal, frames)));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-110")]
    [Trait("Req", "DSG-FD-114")]
    public void OverrideBlocksInDefinitionOrder_GeometryOnlyWhenOverridden()
    {
        var frames = CorpusFrames();
        const string input = """
            object CustomerForm: TCustomerForm
              Left = 200
              Top = 120
              Caption = 'Customer'
              ClientWidth = 344
              ClientHeight = 264
              PixelsPerInch = 96
              inline BillingAddress: TAddressFrame
                Top = 8
                Left = 8
                inherited CityLabel: TLabel
                  Caption = 'Town'
                end
                inherited StreetEdit: TEdit
                end
                inherited StreetLabel: TLabel
                  Caption = 'Billing street'
                end
              end
            end

            """;
        const string expected = """
            object CustomerForm: TCustomerForm
              Left = 200
              Top = 120
              Caption = 'Customer'
              ClientWidth = 344
              ClientHeight = 264
              PixelsPerInch = 96
              inline BillingAddress: TAddressFrame
                Left = 8
                Top = 8
                Width = 320
                Height = 120
                inherited StreetLabel: TLabel
                  Caption = 'Billing street'
                end
                inherited CityPanel: TPanel
                  inherited CityLabel: TLabel
                    Caption = 'Town'
                  end
                end
              end
            end

            """;

        // Blocks are matched by name in the instance's scope, then written in the definition's order and nesting; an
        // empty block writes nothing; the instance gets the definition root's size.
        var document = ParseForm(input, frames);
        Assert.Empty(document.Diagnostics);
        Assert.Equal(expected, FormWriter.Write(document));
        Assert.Equal(expected, FormWriter.Write(ParseForm(expected, frames)));

        // Geometry of an inherited object appears only as an override, at its metadata position.
        var editor = new FormEditor(document, Components);
        editor.Apply(new SetProperty("BillingAddress.CityLabel", "Top", new IntegerValue(12, false)));
        Assert.Contains("      inherited CityLabel: TLabel\n        Top = 12\n        Caption = 'Town'\n      end\n", FormWriter.Write(document), StringComparison.Ordinal);
        editor.Apply(new SetProperty("BillingAddress.CityLabel", "Top", new IntegerValue(8, false)));
        Assert.Equal(expected, FormWriter.Write(document));

        // The instance's own lines: a value equal to the definition root's is not stored (DSG-FD-110).
        editor.Apply(new SetProperty("BillingAddress", "Color", new IdentValue("clWindow")));
        Assert.Equal(expected, FormWriter.Write(document));
        editor.Apply(new SetProperty("BillingAddress", "Color", new IdentValue("clInfoBk")));
        Assert.Contains("    Height = 120\n    Color = clInfoBk\n    inherited StreetLabel: TLabel\n", FormWriter.Write(document), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-117")]
    [Trait("Req", "DSG-FD-121")]
    public void FrameRootWritesLeftTopWidthHeight()
    {
        var frame = ParseFrame("object PlainFrame: TPlainFrame\n  Width = 200\nend\n", AncestorCatalog.Empty);

        Assert.Same(Components.Find("TFrame"), frame.Root.Info);
        Assert.Equal("object PlainFrame: TPlainFrame\n  Left = 0\n  Top = 0\n  Width = 200\n  Height = 240\nend\n", FormWriter.Write(frame));
        Assert.Equal(new Rect(0, 0, 200, 240), PropertyAccess.GetBounds(frame.Root));

        var editor = new FormEditor(frame, Components);
        editor.Apply(new SetProperty("PlainFrame", "Height", new IntegerValue(240, false))); // always written, even when equal
        editor.Apply(new SetProperty("PlainFrame", "Left", new IntegerValue(0, false)));
        Assert.True(PropertyAccess.IsStored(frame.Root, "Height"));
        Assert.True(PropertyAccess.IsStored(frame.Root, "Left"));
        Assert.Equal("object PlainFrame: TPlainFrame\n  Left = 0\n  Top = 0\n  Width = 200\n  Height = 240\nend\n", FormWriter.Write(frame));

        // The Object Inspector shows the frame root's Width / Height, bold only when they differ from the default size.
        var grid = new PropertyGridModel(Components);
        grid.SetSelection([frame.Root]);
        var rows = grid.Rows.Where(r => r.Kind == PropertyRowKind.Property).ToDictionary(r => r.Path);
        Assert.Equal((true, false), (rows["Width"].IsBold, rows["Height"].IsBold));
        Assert.DoesNotContain("ClientWidth", rows.Keys);

        // A form root is unchanged: client size and PixelsPerInch.
        var form = ParseForm("object F: TF\nend\n", AncestorCatalog.Empty);
        Assert.Equal("object F: TF\n  Left = 200\n  Top = 120\n  ClientWidth = 0\n  ClientHeight = 0\n  PixelsPerInch = 96\nend\n", FormWriter.Write(form));
    }
}
