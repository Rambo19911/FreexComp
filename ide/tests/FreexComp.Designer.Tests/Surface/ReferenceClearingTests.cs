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

    /// <summary>Pastes the custom clipboard content of <paramref name="source"/> into the form of <paramref name="target"/>.</summary>
    private static void PasteInto(DesignerHarness source, DesignerHarness target, string root)
    {
        target.Host.Clipboard.SetData(new Dictionary<string, string>
        {
            [ClipboardFormats.Components] = source.Host.Clipboard.GetData(ClipboardFormats.Components)!,
        });
        target.Select(root);
        target.Execute(Commands.Paste);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-123")]
    [Trait("Req", "DSG-FD-108")]
    public void PasteRenamesQualifiedReferencesInsideTheSet()
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
                PopupMenu = PopupMenu1
                inherited StreetLabel: TLabel
                  FocusControl = CustomerForm.Edit1
                end
              end
              object Edit1: TEdit
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
                FocusControl = CustomerForm.Edit1
              end
              object Label2: TLabel
                Left = 340
                Top = 60
                Width = 40
                Height = 15
                FocusControl = BillingAddress.StreetEdit
              end
              object Label3: TLabel
                Left = 340
                Top = 80
                Width = 40
                Height = 15
                FocusControl = CustomerForm.BillingAddress.StreetEdit
              end
              object PopupMenu1: TPopupMenu
              end
            end

            """;
        using var h = Open(form);
        h.Select("BillingAddress", "Edit1", "Label1", "Label2", "Label3", "PopupMenu1");
        h.Execute(Commands.Copy);

        // In the same form every reference into the copied set names the pasted copy, in whichever part.
        h.Select("CustomerForm");
        h.Execute(Commands.Paste);
        Assert.Equal(["BillingAddress1", "Edit2", "Label4", "Label5", "Label6", "PopupMenu2"], h.Selected);
        Assert.Equal(Q("CustomerForm", "Edit2"), Reference(h, "Label4"));
        Assert.Equal(Q("BillingAddress1", "StreetEdit"), Reference(h, "Label5"));
        Assert.Equal(Q("CustomerForm", "BillingAddress1", "StreetEdit"), Reference(h, "Label6"));
        Assert.Equal(new IdentValue("PopupMenu2"), Reference(h, "BillingAddress1", "PopupMenu"));
        Assert.Equal(Q("CustomerForm", "Edit2"), Reference(h, "BillingAddress1.StreetLabel"));

        // The originals keep their references.
        Assert.Equal(Q("CustomerForm", "Edit1"), Reference(h, "Label1"));
        Assert.Equal(Q("BillingAddress", "StreetEdit"), Reference(h, "Label2"));
        Assert.Equal(new IdentValue("PopupMenu1"), Reference(h, "BillingAddress", "PopupMenu"));

        // In another form the names are free and kept; the source form's root name becomes this form's.
        using var other = Open("""
            object OrderForm: TOrderForm
              Left = 200
              Top = 120
              Caption = 'Order'
              ClientWidth = 640
              ClientHeight = 420
              PixelsPerInch = 96
            end

            """);
        PasteInto(h, other, "OrderForm");
        Assert.Equal(["BillingAddress", "Edit1", "Label1", "Label2", "Label3", "PopupMenu1"], other.Selected);
        Assert.Equal(Q("OrderForm", "Edit1"), Reference(other, "Label1"));
        Assert.Equal(Q("BillingAddress", "StreetEdit"), Reference(other, "Label2"));
        Assert.Equal(Q("OrderForm", "BillingAddress", "StreetEdit"), Reference(other, "Label3"));
        Assert.Equal(new IdentValue("PopupMenu1"), Reference(other, "BillingAddress", "PopupMenu"));
        Assert.Equal(Q("OrderForm", "Edit1"), Reference(other, "BillingAddress.StreetLabel"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-111")]
    [Trait("Req", "DSG-SUR-123")]
    public void PasteUsesTheSourceRootFromTheClipboard()
    {
        // D-145: Label2 names the target form's own Edit1, Label3 a component of a third form.
        using var h = Open("""
            object CustomerForm: TCustomerForm
              Left = 200
              Top = 120
              Caption = 'Customer'
              ClientWidth = 640
              ClientHeight = 420
              PixelsPerInch = 96
              object Edit1: TEdit
                Left = 8
                Top = 8
                Width = 120
                Height = 23
                TabOrder = 0
              end
              object Label1: TLabel
                Left = 8
                Top = 40
                Width = 40
                Height = 15
                FocusControl = CustomerForm.Edit1
              end
              object Label2: TLabel
                Left = 8
                Top = 60
                Width = 40
                Height = 15
                FocusControl = OrderForm.Edit1
              end
              object Label3: TLabel
                Left = 8
                Top = 80
                Width = 40
                Height = 15
                FocusControl = ThirdForm.Edit1
              end
            end

            """);
        h.Select("Edit1", "Label1", "Label2", "Label3");
        h.Execute(Commands.Copy);
        const string order = """
            object OrderForm: TOrderForm
              Left = 200
              Top = 120
              Caption = 'Order'
              ClientWidth = 640
              ClientHeight = 420
              PixelsPerInch = 96
              object Edit1: TEdit
                Left = 300
                Top = 300
                Width = 120
                Height = 23
                TabOrder = 0
              end
            end

            """;

        // The clipboard names the source root: only references qualified by it reach the pasted set; the others are
        // references outside the set, kept only when they resolve in the target.
        using var other = Open(order);
        PasteInto(h, other, "OrderForm");
        Assert.Equal(["Edit2", "Label1", "Label2", "Label3"], other.Selected);
        Assert.Equal(Q("OrderForm", "Edit2"), Reference(other, "Label1"));
        Assert.Equal(Q("OrderForm", "Edit1"), Reference(other, "Label2")); // the target's own Edit1, as in the source
        Assert.Null(Reference(other, "Label3"));

        // Plain text has no header: the D-143 rule takes a first part that names nothing of the target but its root
        // as the source root.
        using var plain = Open(order);
        plain.Host.Clipboard.SetText(h.Host.Clipboard.GetData(ClipboardFormats.Text)!);
        plain.Select("OrderForm");
        plain.Execute(Commands.Paste);
        Assert.Equal(["Edit2", "Label1", "Label2", "Label3"], plain.Selected);
        Assert.Equal(Q("OrderForm", "Edit2"), Reference(plain, "Label1"));
        Assert.Equal(Q("OrderForm", "Edit2"), Reference(plain, "Label2"));
        Assert.Equal(Q("OrderForm", "Edit2"), Reference(plain, "Label3"));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-123")]
    public void PasteFiltersCollectionItemReferences()
    {
        // D-145: component references inside collection items follow the paste rule of the other lines.
        var catalog = FreexComp.Designer.Tests.Model.FormValidatorTests.RestrictedCatalog();
        using var h = DesignerHarness.FromText("""
            object F: TForm
              Left = 0
              Top = 0
              ClientWidth = 640
              ClientHeight = 420
              object Header1: THeader
                Left = 8
                Top = 8
                Width = 200
                Height = 20
                Sections = <
                  item
                    Partner = Header1
                  end
                  item
                    Width = 10
                    Partner = Header2
                  end
                  item
                    Partner = F.Header1
                  end
                >
              end
              object Header2: THeader
                Left = 8
                Top = 40
                Width = 200
                Height = 20
              end
            end

            """, "f.form", catalog: catalog);
        h.Select("Header1");
        h.Execute(Commands.Copy);

        // Same form: the reference into the set names the copy; the one outside it resolves and is kept.
        h.Select("F");
        h.Execute(Commands.Paste);
        Assert.Equal(["Header3"], h.Selected);
        Assert.Equal([[new IdentValue("Header3")], [new IdentValue("Header2")], [Q("F", "Header3")]], Partners(h, "Header3"));
        Assert.Equal([[new IdentValue("Header1")], [new IdentValue("Header2")], [Q("F", "Header1")]], Partners(h, "Header1"));

        // Another form without Header2: that reference is removed (the item stays), the source root becomes the target's.
        using var other = DesignerHarness.FromText("""
            object G: TForm
              Left = 0
              Top = 0
              ClientWidth = 640
              ClientHeight = 420
            end

            """, "g.form", catalog: catalog);
        PasteInto(h, other, "G");
        Assert.Equal(["Header1"], other.Selected);
        Assert.Equal([[new IdentValue("Header1")], [], [Q("G", "Header1")]], Partners(other, "Header1"));
        var items = ((CollectionValue)other.Object("Header1").FindProperty("Sections")!.Value).Items;
        Assert.Equal(new IntegerValue(10, false), items[1].Properties.Single().Value);
    }

    /// <summary>The Partner values of each item of <paramref name="owner"/>'s Sections (empty: the item has none).</summary>
    private static List<List<FormValue>> Partners(DesignerHarness h, string owner) =>
        [.. ((CollectionValue)h.Object(owner).FindProperty("Sections")!.Value).Items
            .Select(item => item.Properties.Where(p => p.Path == "Partner").Select(p => p.Value).ToList())];

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-SUR-123")]
    [Trait("Req", "DSG-SUR-230")]
    [Trait("Req", "DSG-FD-108")]
    public void PastedInstanceLinesKeepOnlyResolvingReferences()
    {
        const string form = """
            object CustomerForm: TCustomerForm
              Left = 200
              Top = 120
              Caption = 'Customer'
              ClientWidth = 820
              ClientHeight = 460
              PixelsPerInch = 96
              inline Outer1: TOuterFrame
                Left = 8
                Top = 8
                Width = 340
                Height = 240
                TabOrder = 0
                PopupMenu = PopupMenu1
                inherited Home: TAddressFrame
                  inherited StreetLabel: TLabel
                    FocusControl = StreetEdit
                  end
                  inherited StreetEdit: TEdit
                    PopupMenu = EditMenu
                  end
                  inherited CityPanel: TPanel
                    PopupMenu = CustomerForm.PopupMenu1
                    inherited CityLabel: TLabel
                      FocusControl = ShippingAddress.StreetEdit
                    end
                  end
                end
                inherited HomeLabel: TLabel
                  FocusControl = Home.CityPicker.CityEdit
                end
                inherited RootLabel: TLabel
                  FocusControl = Outer1.Home.StreetEdit
                end
              end
              inline ShippingAddress: TAddressFrame
                Left = 360
                Top = 8
                Width = 320
                Height = 180
                TabOrder = 1
              end
              object PopupMenu1: TPopupMenu
              end
              object EditMenu: TPopupMenu
              end
            end

            """;
        using var h = Open(form);
        h.Select("Outer1");
        h.Execute(Commands.Copy);

        // The target has an instance named Outer1 (the copy becomes Outer2), ShippingAddress and PopupMenu1, no EditMenu.
        using var other = Open("""
            object OrderForm: TOrderForm
              Left = 200
              Top = 120
              Caption = 'Order'
              ClientWidth = 820
              ClientHeight = 520
              PixelsPerInch = 96
              inline Outer1: TOuterFrame
                Left = 8
                Top = 8
                Width = 340
                Height = 240
                TabOrder = 0
              end
              inline ShippingAddress: TAddressFrame
                Left = 360
                Top = 8
                Width = 320
                Height = 180
                TabOrder = 1
              end
              object PopupMenu1: TPopupMenu
              end
            end

            """);
        PasteInto(h, other, "OrderForm");
        Assert.Equal(["Outer2"], other.Selected);

        // Kept: a file-scope name of the target, names the instance's scopes resolve, a reference qualified by another
        // instance of the target; the instance's own name in its lines names the pasted copy.
        Assert.Equal(new IdentValue("PopupMenu1"), Reference(other, "Outer2", "PopupMenu"));
        Assert.Equal(new IdentValue("StreetEdit"), Reference(other, "Outer2.Home.StreetLabel"));
        Assert.Equal(Q("ShippingAddress", "StreetEdit"), Reference(other, "Outer2.Home.CityLabel"));
        Assert.Equal(Q("Home", "CityPicker", "CityEdit"), Reference(other, "Outer2.HomeLabel"));
        Assert.Equal(Q("Outer2", "Home", "StreetEdit"), Reference(other, "Outer2.RootLabel"));

        // Removed: a component the target does not have, and a name qualified by the source form's root name.
        Assert.Null(Reference(other, "Outer2.Home.StreetEdit", "PopupMenu"));
        Assert.Null(Reference(other, "Outer2.Home.CityPanel", "PopupMenu"));
        Assert.Equal("street", Effective(other.Object("Outer2.Home.StreetEdit"), "Text"));

        Assert.Equal(1, other.Undo.UndoCount);
        other.ExecuteUndo();
        Assert.Null(other.Document.FindByQualifiedName("Outer2"));
    }
}
