using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;
using static FreexComp.Designer.Tests.Support.FrameFixtures;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>
/// Delete clears exactly the component references that resolve to a removed component by the scope rules of the line's
/// owner (DSG-SUR-131, DSG-FD-108, RTL-1093, RTL-1104; D-142), with the lookup renames use (D-141).
/// </summary>
public sealed class ReferenceClearingTests
{
    /// <summary>A frame holding an instance of <c>TAddressFrame</c> and labels that name its edit through it.</summary>
    private const string OuterFrame = """
        object OuterFrame: TOuterFrame
          Left = 0
          Top = 0
          Width = 340
          Height = 240
          inline Home: TAddressFrame
            Left = 8
            Top = 8
            Width = 320
            Height = 180
            TabOrder = 0
          end
          object HomeLabel: TLabel
            Left = 8
            Top = 192
            Width = 40
            Height = 15
            FocusControl = Home.StreetEdit
          end
          object RootLabel: TLabel
            Left = 8
            Top = 212
            Width = 40
            Height = 15
            FocusControl = OuterFrame.Home.StreetEdit
          end
        end

        """;

    private static FrameCatalog Frames()
    {
        var frames = AddressFrames();
        AddFrame(frames, OuterFrame, "OuterFrameUnit");
        return frames;
    }

    private static DesignerHarness Open(string text) =>
        DesignerHarness.FromText(text, "customer.form", catalog: Components, frames: Frames());

    private static FormValue? Reference(DesignerHarness h, string owner, string path = "FocusControl") =>
        h.Object(owner).FindProperty(path)?.Value;

    private static QualifiedIdentValue Q(params string[] parts) => new([.. parts]);

    private static void Delete(DesignerHarness h, string name)
    {
        h.Select(name);
        h.Execute(Commands.Delete);
        Assert.Null(h.Document.FindByQualifiedName(name));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-131")]
    [Trait("Req", "DSG-FD-108")]
    public void DeleteLeavesInstanceScopeNames()
    {
        // The form has an own StreetEdit beside the instances' edits of that name.
        const string form = """
            object CustomerForm: TCustomerForm
              Left = 200
              Top = 120
              Caption = 'Customer'
              ClientWidth = 640
              ClientHeight = 420
              PixelsPerInch = 96
              ActiveControl = BillingAddress.StreetEdit
              inline BillingAddress: TAddressFrame
                Left = 8
                Top = 8
                Width = 320
                Height = 180
                TabOrder = 0
                inherited StreetLabel: TLabel
                  FocusControl = StreetEdit
                end
                inherited CityPanel: TPanel
                  inherited CityLabel: TLabel
                    FocusControl = CustomerForm.StreetEdit
                  end
                end
              end
              inline ShippingAddress: TAddressFrame
                Left = 8
                Top = 196
                Width = 320
                Height = 180
                TabOrder = 1
                inherited StreetLabel: TLabel
                  FocusControl = AddressFrame.StreetEdit
                end
              end
              object StreetEdit: TEdit
                Left = 340
                Top = 8
                Width = 120
                Height = 23
                TabOrder = 2
              end
              object OwnLabel: TLabel
                Left = 340
                Top = 40
                Width = 40
                Height = 15
                FocusControl = StreetEdit
              end
            end

            """;
        using var h = Open(form);
        var original = h.Text;

        Delete(h, "StreetEdit");

        // File scope: the plain name and the root-qualified name in an instance's line reach the form's edit.
        Assert.Null(Reference(h, "OwnLabel"));
        Assert.Null(Reference(h, "BillingAddress.CityLabel"));

        // The instance's scope resolves its plain name first; an instance-qualified reference names the instance's edit;
        // the frame's root name is no name of this file (RTL-1104).
        Assert.Equal(new IdentValue("StreetEdit"), Reference(h, "BillingAddress.StreetLabel"));
        Assert.Equal(Q("BillingAddress", "StreetEdit"), Reference(h, "CustomerForm", "ActiveControl"));
        Assert.Equal(Q("AddressFrame", "StreetEdit"), Reference(h, "ShippingAddress.StreetLabel"));

        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-131")]
    [Trait("Req", "DSG-FD-108")]
    public void DeleteInstanceClearsReferencesThroughIt()
    {
        const string form = """
            object CustomerForm: TCustomerForm
              Left = 200
              Top = 120
              Caption = 'Customer'
              ClientWidth = 820
              ClientHeight = 420
              PixelsPerInch = 96
              ActiveControl = ShippingAddress.StreetEdit
              inline BillingAddress: TAddressFrame
                Left = 8
                Top = 8
                Width = 320
                Height = 180
                TabOrder = 0
              end
              inline ShippingAddress: TAddressFrame
                Left = 8
                Top = 196
                Width = 320
                Height = 180
                TabOrder = 1
                inherited StreetLabel: TLabel
                  FocusControl = StreetEdit
                end
                inherited CityPanel: TPanel
                  inherited CityLabel: TLabel
                    FocusControl = BillingAddress.StreetEdit
                  end
                end
              end
              inline Outer1: TOuterFrame
                Left = 340
                Top = 8
                Width = 340
                Height = 240
                TabOrder = 2
              end
              object Label1: TLabel
                Left = 700
                Top = 8
                Width = 40
                Height = 15
                FocusControl = BillingAddress.StreetEdit
              end
              object Label2: TLabel
                Left = 700
                Top = 28
                Width = 40
                Height = 15
                FocusControl = CustomerForm.BillingAddress.StreetEdit
              end
              object Label3: TLabel
                Left = 700
                Top = 48
                Width = 40
                Height = 15
                FocusControl = Outer1.Home.StreetEdit
              end
              object Label4: TLabel
                Left = 700
                Top = 68
                Width = 40
                Height = 15
                FocusControl = CustomerForm.Outer1.Home.StreetEdit
              end
            end

            """;
        using var h = Open(form);
        var original = h.Text;

        // The instance: references qualified by it (first part), through it (middle part) and from another instance's line.
        Delete(h, "BillingAddress");
        Assert.Null(Reference(h, "Label1"));
        Assert.Null(Reference(h, "Label2"));
        Assert.Null(Reference(h, "ShippingAddress.CityLabel"));
        Assert.Equal(new IdentValue("StreetEdit"), Reference(h, "ShippingAddress.StreetLabel")); // another scope's edit
        Assert.Equal(Q("ShippingAddress", "StreetEdit"), Reference(h, "CustomerForm", "ActiveControl"));
        Assert.Equal(Q("Outer1", "Home", "StreetEdit"), Reference(h, "Label3"));
        Assert.Equal(Q("CustomerForm", "Outer1", "Home", "StreetEdit"), Reference(h, "Label4"));
        Assert.Equal(1, h.Undo.UndoCount);
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // An outer instance: the references through its nested instance.
        Delete(h, "Outer1");
        Assert.Null(Reference(h, "Label3"));
        Assert.Null(Reference(h, "Label4"));
        Assert.Equal(Q("BillingAddress", "StreetEdit"), Reference(h, "Label1"));
        h.ExecuteUndo();
        Assert.Equal(original, h.Text);

        // In the frame's own file: the nested instance, named first and in the middle after the frame's root name.
        using var frame = DesignerHarness.FromText(OuterFrame, "OuterFrameUnit.form", catalog: Components, frames: Frames(),
            rootAncestorClass: "TFrame");
        var frameText = frame.Text;
        Delete(frame, "Home");
        Assert.Null(Reference(frame, "HomeLabel"));
        Assert.Null(Reference(frame, "RootLabel"));
        frame.ExecuteUndo();
        Assert.Equal(frameText, frame.Text);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-123")]
    [Trait("Req", "DSG-FD-108")]
    public void PasteKeepsOnlyQualifiedReferencesThatResolve()
    {
        const string form = """
            object CustomerForm: TCustomerForm
              Left = 200
              Top = 120
              Caption = 'Customer'
              ClientWidth = 640
              ClientHeight = 420
              PixelsPerInch = 96
              inline BillingAddress: TAddressFrame
                Left = 8
                Top = 8
                Width = 320
                Height = 180
                TabOrder = 0
              end
              object StreetEdit: TEdit
                Left = 340
                Top = 8
                Width = 120
                Height = 23
                TabOrder = 1
              end
              object Label1: TLabel
                Left = 340
                Top = 40
                Width = 40
                Height = 15
                FocusControl = BillingAddress.StreetEdit
              end
              object Label2: TLabel
                Left = 340
                Top = 60
                Width = 40
                Height = 15
                FocusControl = CustomerForm.StreetEdit
              end
            end

            """;
        using var h = Open(form);
        h.Select("Label1", "Label2");
        h.Execute(Commands.Copy);

        // In the same form both resolve and are kept.
        h.Select("CustomerForm");
        h.Execute(Commands.Paste);
        Assert.Equal(["Label3", "Label4"], h.Selected);
        Assert.Equal(Q("BillingAddress", "StreetEdit"), Reference(h, "Label3"));
        Assert.Equal(Q("CustomerForm", "StreetEdit"), Reference(h, "Label4"));

        // In a form with an instance of another name and another root name neither resolves: both are removed.
        using var other = Open(form.Replace("CustomerForm", "OrderForm", StringComparison.Ordinal)
            .Replace("BillingAddress", "ShippingAddress", StringComparison.Ordinal));
        other.Host.Clipboard.SetData(new Dictionary<string, string>
        {
            [ClipboardFormats.Components] = h.Host.Clipboard.GetData(ClipboardFormats.Components)!,
        });
        other.Select("OrderForm");
        other.Execute(Commands.Paste);
        Assert.Equal(["Label3", "Label4"], other.Selected);
        Assert.Null(Reference(other, "Label3"));
        Assert.Null(Reference(other, "Label4"));
    }
}
