using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using static FreexComp.Designer.Tests.Support.FrameFixtures;
using static FreexComp.Designer.Tests.Support.InheritanceFixtures;

namespace FreexComp.Designer.Tests.Text;

/// <summary>Descendant form documents in the parser and the model (form-inheritance-designer.md §2; D-129).</summary>
public sealed class InheritedFormParseTests
{
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-122")]
    [Trait("Req", "DSG-FD-125")]
    [Trait("Req", "DSG-FD-126")]
    [Trait("Req", "DSG-FD-135")]
    public void ThreeLevels_MergedChildren_EffectiveValues()
    {
        var ancestors = DialogChain();
        var baseDef = ancestors.Find("tbasedialog")!;
        var midDef = ancestors.Find("TMidDialog")!;
        Assert.Equal(["TBaseDialog", "TLeafDialog", "TMidDialog"], ancestors.All.Select(d => d.ClassName));
        Assert.All(ancestors.All, d => Assert.Empty(d.Document.Diagnostics));

        var leaf = ParseWith(LeafDialog, "TMidDialog", null, ancestors, "leaf.form");
        var mid = midDef.Document;
        Assert.Empty(leaf.Diagnostics);

        // DSG-FD-125: the root inherits from the ancestor's root and takes its class metadata.
        var root = leaf.Root;
        Assert.Equal((FormObjectKind.Inherited, "LeafDialog", "TLeafDialog", true, false, false),
            (root.Kind, root.Name, root.TypeName, root.IsInheritedRoot, root.IsPreserved, root.IsInherited));
        Assert.Same(midDef, leaf.Ancestor);
        Assert.Same(ancestors, leaf.Ancestors);
        Assert.Same(mid.Root, root.InheritedFrom);
        Assert.Same(baseDef.Document.Root, mid.Root.InheritedFrom);
        Assert.Same(Components.Find("TForm"), root.Info);
        Assert.Same(midDef, root.InheritedSource);
        Assert.False(baseDef.Document.Root.IsInheritedRoot);
        Assert.Null(baseDef.Document.Ancestor);

        // Inherited children first (definition nesting and creation order), new children after them.
        Assert.Equal(["TitleLabel", "ButtonPanel", "NameEdit", "NoteLabel"], root.Children.Select(c => c.Name));
        Assert.Equal(["OkButton", "CancelButton", "HelpButton"], root.Children[1].Children.Select(c => c.Name));
        Assert.All(root.Children.Take(3).Concat(root.Children[1].Children),
            c => Assert.Equal((FormObjectKind.Inherited, true, false), (c.Kind, c.IsInherited, c.IsPreserved)));
        var note = root.Children[3];
        Assert.Equal((FormObjectKind.Object, false, false), (note.Kind, note.IsInherited, note.IsPreserved));
        for (var i = 0; i < 3; i++)
        {
            Assert.Same(mid.Root.Children[i], root.Children[i].InheritedFrom);
        }

        // DSG-FD-126: inherited objects belong to the file scope.
        var ok = leaf.FindByName("okbutton")!;
        Assert.Same(root.Children[1].Children[0], ok);
        Assert.Equal("OkButton", ok.QualifiedName);
        Assert.Same(leaf.FindByName("HelpButton"), leaf.FindByQualifiedName("HelpButton"));
        Assert.Same(mid.FindByName("OkButton"), ok.InheritedFrom);
        Assert.Same(baseDef.Document.FindByName("OkButton"), ok.InheritedFrom!.InheritedFrom);
        Assert.Null(ok.InheritedFrom.InheritedFrom!.InheritedFrom);

        // Effective values: overrides at two levels, else the chain's value.
        Assert.Equal(("Save", "Apply", "OK"), (Effective(ok, "Caption"), Effective(mid.FindByName("OkButton")!, "Caption"),
            Effective(baseDef.Document.FindByName("OkButton")!, "Caption")));
        Assert.Equal(("220", "230", "75"), (Effective(ok, "Left"), Effective(mid.FindByName("OkButton")!, "Left"), Effective(ok, "Width")));
        Assert.True(ok.IsOverridden("Caption"));
        Assert.False(ok.IsOverridden("Width"));
        Assert.Equal(new StringValue("Apply"), ok.InheritedValue("Caption"));
        Assert.Equal("Cancel", Effective(leaf.FindByName("CancelButton")!, "Caption"));
        Assert.Equal(("?", "Help"), (Effective(leaf.FindByName("HelpButton")!, "Caption"), Effective(mid.FindByName("HelpButton")!, "Caption")));
        Assert.Equal(("Mid", "480", "400", "200"), (Effective(root, "Caption"), Effective(root, "ClientWidth"), Effective(mid.Root, "ClientWidth"),
            Effective(root, "Left")));
        Assert.True(root.IsOverridden("ClientWidth"));
        Assert.False(root.IsOverridden("Caption"));
        Assert.Equal("Note", Effective(note, "Caption"));

        // DSG-FD-135: where an inherited value comes from.
        Assert.Same(midDef, ok.InheritedSource);
        Assert.Same(baseDef, mid.FindByName("OkButton")!.InheritedSource);
        Assert.Same(midDef, leaf.FindByName("HelpButton")!.InheritedSource);
        Assert.Null(note.InheritedSource);
        Assert.Null(ok.InheritedFrame);
        var handler = ok.InheritedHandler("OnClick")!.Value;
        Assert.Equal("OkButtonClick", handler.Method);
        Assert.Same(baseDef, handler.Frame);
        Assert.Null(leaf.FindByName("CancelButton")!.InheritedHandler("OnClick"));

        // New children are accepted outside frame instances.
        Assert.True(root.AcceptsNewChildren);
        Assert.True(root.Children[1].AcceptsNewChildren);
        Assert.True(note.AcceptsNewChildren);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-127")]
    [Trait("Req", "UI-DSG-145")]
    public void Diagnostics_F0112_F0113_F0109_F0106_F0107()
    {
        var ancestors = new FrameCatalog();
        var baseDef = AddLevel(ancestors, BaseDialog, "BaseDialogUnit", "TForm");
        Assert.Empty(baseDef.Document.Diagnostics);

        const string problems = """
            inherited MidDialog: TMidDialog
              Caption = 'Problems'
              inherited Gone: TButton
                Caption = 'gone'
                object Inside: TButton
                  Left = 1
                  Top = 1
                  Width = 75
                  Height = 25
                end
              end
              inherited OkButton: TLabel
              end
              inherited OkButton: TButton
                Caption = 'A'
              end
              inherited OkButton: TButton
                Caption = 'B'
              end
              object NameEdit: TEdit
                Left = 8
                Top = 32
                Width = 200
                Height = 23
                inherited CancelButton: TButton
                end
              end
              object TitleLabel: TLabel
                Left = 1
                Top = 1
                Width = 10
                Height = 10
              end
            end

            """;
        var document = ParseWith(problems, "TBaseDialog", null, ancestors, "problem.form");

        Assert.Equal(
        [
            "problem.form(3,13): warning F0109: Component Gone: TButton is not part of TBaseDialog: shown read-only",
            "problem.form(12,13): warning F0109: Component OkButton: TLabel is not part of TBaseDialog: shown read-only",
            "problem.form(17,13): error F0106: A component named OkButton already exists",
            "problem.form(25,5): hint F0107: \"inherited\" objects are not supported: shown read-only",
            "problem.form(28,10): error F0106: A component named TitleLabel already exists",
        ], document.Diagnostics.Select(d => d.ToString()));

        // The descendant stays resolved; bad blocks are preserved where they were read, with the components inside them.
        var root = document.Root;
        Assert.True(root.IsInheritedRoot);
        Assert.False(root.IsPreserved);
        Assert.Equal("A", Effective(document.FindByName("OkButton")!, "Caption"));
        Assert.Equal(["Gone", "OkButton", "OkButton"], root.PreservedBlocks.Select(b => b.Name));
        Assert.All(root.PreservedBlocks, b => Assert.True(b.IsPreserved));
        Assert.Contains("object Inside: TButton", root.PreservedBlocks[0].PreservedText, StringComparison.Ordinal);
        Assert.Null(document.FindByName("Inside"));
        Assert.Equal(["TitleLabel", "ButtonPanel", "NameEdit", "TitleLabel"], root.Children.Select(c => c.Name));
        Assert.Same(root.Children[0], document.FindByName("TitleLabel")); // the inherited object owns the name
        Assert.True(root.Children[3].IsPreserved);
        var nameEdit = document.FindByName("NameEdit")!;
        Assert.False(nameEdit.IsPreserved);
        Assert.True(Assert.Single(nameEdit.Children).IsPreserved); // an `inherited` block inside a new component
        Assert.Same(root.Children[1].Children[1], document.FindByName("CancelButton"));

        // F0112: no ancestor with a form (unknown class, no class, an ancestor shown read-only, a chain that repeats).
        var broken = FormParser.Parse("inherited BrokenDialog: TBrokenDialog\nend\n", "broken.form", Components).Document!;
        ancestors.Set(new FrameDefinition("TBrokenDialog", "BrokenDialogUnit", "BrokenDialogUnit.form", broken));
        foreach (var (text, ancestorClass) in new (string, string?)[]
        {
            (MidDialog, "TForm"), (MidDialog, null), (MidDialog, "TBrokenDialog"),
            (MidDialog.Replace("MidDialog: TMidDialog", "BaseDialog: TBaseDialog", StringComparison.Ordinal), "TBaseDialog"),
        })
        {
            var noAncestor = ParseWith(text, ancestorClass, null, ancestors);
            var className = noAncestor.Root.TypeName;
            Assert.Equal($"form.form(1,1): warning F0112: Class {className} has no ancestor form: its form file cannot start with \"inherited\": shown read-only",
                Assert.Single(noAncestor.Diagnostics).ToString());
            Assert.Equal((true, false), (noAncestor.Root.IsPreserved, noAncestor.Root.IsInheritedRoot));
            Assert.Null(noAncestor.Ancestor);
            Assert.Equal(text, noAncestor.Root.PreservedText);
        }

        // F0113: an `object` root whose ancestor has a form.
        const string objectRoot = """
            object MidDialog: TMidDialog
              Left = 200
              Top = 120
              ClientWidth = 400
              ClientHeight = 200
              PixelsPerInch = 96
            end

            """;
        var mustInherit = ParseWith(objectRoot, "TBaseDialog", null, ancestors);
        Assert.Equal("form.form(1,1): warning F0113: Class TMidDialog inherits the form of TBaseDialog: its form file must start with \"inherited\": shown read-only",
            Assert.Single(mustInherit.Diagnostics).ToString());
        Assert.True(mustInherit.Root.IsPreserved);
        Assert.Equal(objectRoot, mustInherit.Root.PreservedText);
        Assert.Empty(ParseWith(objectRoot, "TForm", null, ancestors).Diagnostics); // an ancestor without a form: an ordinary form

        // DSG-FD-122: without an ancestor catalog an `inherited` root is F0107 and the whole form read-only, as before.
        var without = ParseWith(MidDialog, "TBaseDialog", null, null);
        Assert.Equal("form.form(1,1): hint F0107: \"inherited\" objects are not supported: shown read-only", Assert.Single(without.Diagnostics).ToString());
        Assert.Equal((true, false), (without.Root.IsPreserved, without.Root.IsInheritedRoot));
        Assert.Equal(MidDialog, without.Root.PreservedText);
        Assert.Null(without.Ancestors);
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-125")]
    [Trait("Req", "DSG-FD-127")]
    [Trait("Req", "DSG-FD-135")]
    public void InheritedFrameInstance_OverridesOfOverrides_ClosedScope()
    {
        var frames = AddressFrames();
        var address = frames.Find("TAddressFrame")!;
        var ancestors = new FrameCatalog();
        var dialog = AddLevel(ancestors, """
            object AddrDialog: TAddrDialog
              Left = 200
              Top = 120
              ClientWidth = 400
              ClientHeight = 300
              PixelsPerInch = 96
              inline Addr: TAddressFrame
                Left = 8
                Top = 8
                Width = 320
                Height = 180
                TabOrder = 0
                inherited StreetLabel: TLabel
                  Caption = 'Base street'
                end
                inherited StreetEdit: TEdit
                  OnChange = AddrDialogStreetChange
                end
                inherited CityPanel: TPanel
                  inherited CityLabel: TLabel
                    Left = 16
                  end
                end
              end
            end

            """, "AddrDialogUnit", "TForm", frames);
        Assert.Empty(dialog.Document.Diagnostics);

        var document = ParseWith("""
            inherited AddrChild: TAddrChild
              inherited Addr: TAddressFrame
                Top = 16
                inherited StreetLabel: TLabel
                  Caption = 'Child street'
                end
                inherited CityPicker: TCityFrame
                  inherited CityEdit: TEdit
                    Width = 120
                  end
                end
                object Extra: TButton
                  Left = 1
                  Top = 1
                  Width = 75
                  Height = 25
                end
              end
              inherited ZipEdit: TEdit
              end
            end

            """, "TAddrDialog", frames, ancestors, "child.form");

        // The inherited instance keeps the closed scope (F0110); its components are not in the file scope (F0109).
        Assert.Equal(
        [
            "child.form(12,5): warning F0110: Components cannot be added to frame instance Addr: shown read-only",
            "child.form(19,13): warning F0109: Component ZipEdit: TEdit is not part of TAddrDialog: shown read-only",
        ], document.Diagnostics.Select(d => d.ToString()));
        var addr = document.FindByName("Addr")!;
        Assert.Equal((FormObjectKind.Inherited, true, true, false), (addr.Kind, addr.IsInherited, addr.IsFrameInstance, addr.IsPreserved));
        Assert.Same(address, addr.Frame);
        Assert.Same(dialog.Document.FindByName("Addr"), addr.InheritedFrom);
        Assert.Equal(["Extra"], addr.PreservedBlocks.Select(b => b.Name));
        Assert.Equal(["ZipEdit"], document.Root.PreservedBlocks.Select(b => b.Name));
        Assert.Null(document.FindByName("StreetLabel"));
        Assert.Null(document.FindByName("Extra"));

        // §9: frame → ancestor form → descendant.
        var label = document.FindByQualifiedName("Addr.StreetLabel")!;
        Assert.Equal("Addr.StreetLabel", label.QualifiedName);
        Assert.Equal(("Child street", "Base street", "Street"), (Effective(label, "Caption"),
            Effective(dialog.Document.FindByQualifiedName("Addr.StreetLabel")!, "Caption"), Effective(address.Document.FindByName("StreetLabel")!, "Caption")));
        Assert.Equal("16", Effective(document.FindByQualifiedName("Addr.CityLabel")!, "Left"));
        var city = document.FindByQualifiedName("Addr.CityPicker.CityEdit")!;
        Assert.Equal(("Springfield", "120", "8"), (Effective(city, "Text"), Effective(city, "Width"), Effective(city, "Left")));
        Assert.Equal(("16", "8"), (Effective(addr, "Top"), Effective(addr, "Left")));
        Assert.Equal("clWindow", Effective(addr, "Color"));

        // DSG-FD-135: the instance comes from the ancestor form, its components from the frame; a handler from the file
        // that stores it.
        Assert.Same(dialog, addr.InheritedSource);
        Assert.Same(address, label.InheritedSource);
        Assert.Same(address, label.InheritedFrame);
        var childHandler = document.FindByQualifiedName("Addr.StreetEdit")!.InheritedHandler("OnChange")!.Value;
        Assert.Equal("AddrDialogStreetChange", childHandler.Method);
        Assert.Same(dialog, childHandler.Frame);
        var dialogHandler = dialog.Document.FindByQualifiedName("Addr.StreetEdit")!.InheritedHandler("OnChange")!.Value;
        Assert.Equal("StreetEditChange", dialogHandler.Method);
        Assert.Same(address, dialogHandler.Frame);

        // Nothing can be added to an instance or inside one; the root accepts new children.
        Assert.Equal((true, false, false, false), (document.Root.AcceptsNewChildren, addr.AcceptsNewChildren,
            document.FindByQualifiedName("Addr.CityPanel")!.AcceptsNewChildren, city.AcceptsNewChildren));
    }
}
