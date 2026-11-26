using System.Text;
using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using static FreexComp.Designer.Tests.Support.FrameFixtures;
using static FreexComp.Designer.Tests.Support.InheritanceFixtures;

namespace FreexComp.Designer.Tests.Text;

/// <summary>Writing descendant form documents as deltas (form-inheritance-designer.md §3; D-130).</summary>
public sealed class InheritedFormRoundTripTests
{
    /// <summary>The three-level chain of the canonical corpus: form file and the unit that declares its class.</summary>
    private static readonly (string Form, string Unit)[] Chain =
        [("inherit-base.form", "InheritBase.pas"), ("inherit-mid.form", "InheritMid.pas"), ("inherit-leaf.form", "InheritLeaf.pas")];

    /// <summary>
    /// D-133: the level <paramref name="form"/> of <see cref="Chain"/> parsed as the designer reads it — every level before it
    /// parsed with the ancestor its unit declares and added to the ancestor catalog, the corpus frames known.
    /// </summary>
    internal static FormDocument ParseChainLevel(string form)
    {
        var frames = FrameFormRoundTripTests.CorpusFrames();
        var ancestors = new AncestorCatalog();
        foreach (var (level, unit) in Chain)
        {
            var text = File.ReadAllText(TestPaths.Combine("forms/canonical/" + level));
            var (className, ancestorClass) = FormClassOf(File.ReadAllText(TestPaths.Combine("forms/canonical/" + unit)),
                Path.GetFileNameWithoutExtension(unit));
            var document = ParseWith(text, ancestorClass, frames, ancestors, level);
            if (level == form)
            {
                return document;
            }

            ancestors.Set(new AncestorDefinition(className, Path.GetFileNameWithoutExtension(unit), level, document));
        }

        throw new ArgumentException(form + " is not a level of the inheritance chain", nameof(form));
    }

    private static readonly string[] AlwaysWrittenRootLines = ["Left", "Top", "ClientWidth", "ClientHeight", "PixelsPerInch"];

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-128")]
    [Trait("Req", "DSG-FD-129")]
    [Trait("Req", "DSG-FD-130")]
    [Trait("Req", "DSG-FD-131")]
    public void ThreeLevelChain_RoundTripsByteIdentical_DescendantHoldsOnlyDeltas()
    {
        // Each level is parsed with the ancestor its unit declares (class(…)) and joins the ancestor catalog; the leaf
        // contains an instance of the corpus frame.
        var frames = FrameFormRoundTripTests.CorpusFrames();
        var ancestors = new AncestorCatalog();
        var documents = new List<(FormDocument Document, byte[] Bytes)>();
        foreach (var (form, unit) in Chain)
        {
            var bytes = File.ReadAllBytes(TestPaths.Combine("forms/canonical/" + form));
            var text = new UTF8Encoding(false, true).GetString(bytes);
            var unitText = File.ReadAllText(TestPaths.Combine("forms/canonical/" + unit));
            var (className, ancestorClass) = FormClassOf(unitText, Path.GetFileNameWithoutExtension(unit));
            var document = ParseWith(text, ancestorClass, frames, ancestors, form);
            Assert.Empty(document.Diagnostics);
            Assert.Equal(className, document.Root.TypeName);
            ancestors.Set(new AncestorDefinition(className, Path.GetFileNameWithoutExtension(unit), form, document));
            documents.Add((document, bytes));
        }

        // DSG-FD-131: every level writes back its bytes.
        foreach (var (document, bytes) in documents)
        {
            Assert.Equal(bytes, Encoding.UTF8.GetBytes(FormWriter.Write(document)));
        }

        var (baseForm, mid, leaf) = (documents[0].Document, documents[1].Document, documents[2].Document);
        Assert.False(baseForm.Root.IsInheritedRoot);
        Assert.Same(ancestors.Find("TBaseForm"), mid.Ancestor);
        Assert.Same(ancestors.Find("TMidForm"), leaf.Ancestor);

        // DSG-FD-128…130: the descendants hold deltas only — no line equal to the inherited value, no always-written root
        // line, no block for an inherited object that changed nothing.
        foreach (var descendant in new[] { mid, leaf })
        {
            Assert.DoesNotContain(descendant.Root.Properties, p => AlwaysWrittenRootLines.Contains(p.Path, StringComparer.OrdinalIgnoreCase));
            foreach (var obj in descendant.AllObjects().Where(o => o.IsInheritedRoot || (o.IsInherited && o.Scope is null)))
            {
                foreach (var property in obj.Properties)
                {
                    Assert.False(obj.InheritedValue(property.Path) is { } inherited
                        && PropertyAccess.SameValue(Components, property.Info!, property.Value, inherited),
                        $"{obj.Name}.{property.Path} repeats the inherited value");
                }
            }

            var text = FormWriter.Write(descendant);
            Assert.DoesNotContain("CancelButton", text, StringComparison.Ordinal);
            Assert.StartsWith("inherited ", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("TitleLabel", FormWriter.Write(leaf), StringComparison.Ordinal);

        // The merged leaf: inherited children first, new ones after them; values from three levels and the frame.
        Assert.Equal(["TitleLabel", "ButtonPanel", "NameEdit", "Address"], leaf.Root.Children.Select(c => c.Name));
        Assert.Equal(["OkButton", "CancelButton", "HelpButton"], leaf.FindByName("ButtonPanel")!.Children.Select(c => c.Name));
        var ok = leaf.FindByName("OkButton")!;
        Assert.Equal(("Save", "220", "8", "OkButtonClick"), (Effective(ok, "Caption"), Effective(ok, "Left"), Effective(ok, "Top"),
            ok.InheritedHandler("OnClick")!.Value.Method));
        Assert.Equal(("Leaf", "400", "?", "240", "Mid title"), (Effective(leaf.Root, "Caption"), Effective(leaf.Root, "ClientWidth"),
            Effective(leaf.FindByName("HelpButton")!, "Caption"), Effective(leaf.FindByName("NameEdit")!, "Width"),
            Effective(leaf.FindByName("TitleLabel")!, "Caption")));
        Assert.Equal("bvNone", Effective(leaf.FindByName("ButtonPanel")!, "BevelOuter"));
        Assert.Equal(("Road", "City"), (Effective(leaf.FindByQualifiedName("Address.StreetLabel")!, "Caption"),
            Effective(leaf.FindByQualifiedName("Address.CityLabel")!, "Caption")));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-FD-128")]
    [Trait("Req", "DSG-FD-129")]
    [Trait("Req", "DSG-FD-130")]
    [Trait("Req", "DSG-FD-131")]
    public void BlocksInDefinitionOrder_NewChildrenLast_PreservedBlocksAtTheirSlot()
    {
        var ancestors = new AncestorCatalog();
        AddLevel(ancestors, BaseDialog, "BaseDialogUnit", "TForm");

        // DSG-FD-128: a descendant that changed nothing writes no root line.
        var plain = ParseWith("inherited PlainDialog: TPlainDialog\nend\n", "TBaseDialog", null, ancestors);
        Assert.Empty(plain.Diagnostics);
        Assert.Equal("inherited PlainDialog: TPlainDialog\nend\n", FormWriter.Write(plain));

        // Blocks out of order and outside their container's block, a new component before them, empty blocks, a block
        // that matches nothing (F0109) and one for an object that already has one (F0106).
        const string input = """
            inherited MixedDialog: TMixedDialog
              Caption = 'Mixed'
              ClientWidth = 400
              object FirstEdit: TEdit
                Left = 8
                Top = 32
                Width = 100
                Height = 23
                TabOrder = 1
              end
              inherited Gone: TButton
                Caption = 'gone'
              end
              inherited CancelButton: TButton
                Caption = 'Close'
              end
              inherited TitleLabel: TLabel
              end
              inherited ButtonPanel: TPanel
                object ExtraButton: TButton
                  Left = 8
                  Top = 8
                  Width = 75
                  Height = 25
                  Caption = 'Extra'
                  TabOrder = 2
                end
                inherited OkButton: TButton
                  Caption = 'Go'
                end
              end
              inherited CancelButton: TButton
                Caption = 'Twice'
              end
            end

            """;
        const string expected = """
            inherited MixedDialog: TMixedDialog
              Caption = 'Mixed'
              ClientWidth = 400
              inherited Gone: TButton
                Caption = 'gone'
              end
              inherited ButtonPanel: TPanel
                inherited OkButton: TButton
                  Caption = 'Go'
                end
                inherited CancelButton: TButton
                  Caption = 'Close'
                end
                object ExtraButton: TButton
                  Left = 8
                  Top = 8
                  Width = 75
                  Height = 25
                  Caption = 'Extra'
                  TabOrder = 2
                end
              end
              inherited CancelButton: TButton
                Caption = 'Twice'
              end
              object FirstEdit: TEdit
                Left = 8
                Top = 32
                Width = 100
                Height = 23
                TabOrder = 1
              end
            end

            """;

        var document = ParseWith(input, "TBaseDialog", null, ancestors, "mixed.form");
        Assert.Equal(["F0109", "F0106"], document.Diagnostics.Select(d => d.Code));
        Assert.Equal(["TitleLabel", "ButtonPanel", "FirstEdit"], document.Root.Children.Select(c => c.Name));
        Assert.Equal(["OkButton", "CancelButton", "ExtraButton"], document.FindByName("ButtonPanel")!.Children.Select(c => c.Name));

        // Written in the definition's order and nesting, the inherited blocks first (empty ones dropped), the preserved
        // blocks verbatim after the inherited child whose block preceded them (the first ones first), the new components
        // last; the canonical text reads back to itself.
        Assert.Equal(expected, FormWriter.Write(document));
        var again = ParseWith(expected, "TBaseDialog", null, ancestors, "mixed.form");
        Assert.Equal(["F0109", "F0106"], again.Diagnostics.Select(d => d.Code));
        Assert.Equal(expected, FormWriter.Write(again));

        // A root line equal to the inherited value is kept as read (DSG-FD-115); set again by the designer it is removed.
        var editor = new FormEditor(again, Components);
        editor.Apply(new SetProperty("MixedDialog", "ClientWidth", new IntegerValue(400, false)));
        Assert.StartsWith("inherited MixedDialog: TMixedDialog\n  Caption = 'Mixed'\n  inherited Gone: TButton\n", FormWriter.Write(again), StringComparison.Ordinal);

        // An inherited object that had no block gets one for its first override, at its place in the definition order; a
        // new component added to the root is written after the earlier new components.
        editor.Apply(new SetProperty("TitleLabel", "Caption", new StringValue("Head")));
        editor.Apply(new AddObject("MixedDialog", 3, new FormObjectData(FormObjectKind.Object, "LastEdit", "TEdit",
        [
            new FormPropertyData("Left", new IntegerValue(8, false)), new FormPropertyData("Top", new IntegerValue(64, false)),
            new FormPropertyData("Width", new IntegerValue(100, false)), new FormPropertyData("Height", new IntegerValue(23, false)),
            new FormPropertyData("TabOrder", new IntegerValue(2, false)),
        ], [])));
        var written = FormWriter.Write(again);
        Assert.StartsWith("""
            inherited MixedDialog: TMixedDialog
              Caption = 'Mixed'
              inherited Gone: TButton
                Caption = 'gone'
              end
              inherited TitleLabel: TLabel
                Caption = 'Head'
              end
              inherited ButtonPanel: TPanel

            """, written, StringComparison.Ordinal);
        Assert.EndsWith("""
                TabOrder = 1
              end
              object LastEdit: TEdit
                Left = 8
                Top = 64
                Width = 100
                Height = 23
                TabOrder = 2
              end
            end

            """, written, StringComparison.Ordinal);
        Assert.Equal(written, FormWriter.Write(ParseWith(written, "TBaseDialog", null, ancestors, "mixed.form")));
    }

    /// <summary>The form class a unit declares (named as the unit's form) and its ancestor, read from the syntax tree.</summary>
    private static (string ClassName, string AncestorClass) FormClassOf(string unitText, string unitName)
    {
        var tree = SyntaxTree.Parse(SourceText.From(@"C:\" + unitName + ".pas", unitText));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var unit = Assert.IsType<UnitSyntax>(tree.Root);
        var declaration = unit.InterfaceSection.Declarations.OfType<TypeSectionSyntax>().SelectMany(s => s.Declarations)
            .OfType<TypeDeclarationSyntax>().Single(d => d.Type is ClassTypeSyntax);
        var ancestor = ((ClassTypeSyntax)declaration.Type).Heritage!.Types.Single().ToString();
        return (declaration.Identifier.Text, ancestor);
    }
}
