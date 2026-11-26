using FreexComp.Designer.Model;
using FreexComp.Designer.Text;
using static FreexComp.Designer.Tests.Support.FrameFixtures;

namespace FreexComp.Designer.Tests.Support;

/// <summary>
/// Descendant (inherited) form documents for the model tests (form-inheritance-designer.md §13): a three-level chain of
/// dialogs and in-memory ancestor catalogs holding forms and frames.
/// </summary>
internal static class InheritanceFixtures
{
    /// <summary>Level 1: a title label and a button panel with two buttons (the OK button has a handler).</summary>
    public const string BaseDialog = """
        object BaseDialog: TBaseDialog
          Left = 200
          Top = 120
          Caption = 'Base'
          ClientWidth = 400
          ClientHeight = 200
          PixelsPerInch = 96
          object TitleLabel: TLabel
            Left = 8
            Top = 8
            Width = 60
            Height = 15
            Caption = 'Title'
          end
          object ButtonPanel: TPanel
            Left = 0
            Top = 160
            Width = 400
            Height = 40
            TabOrder = 0
            object OkButton: TButton
              Left = 230
              Top = 8
              Width = 75
              Height = 25
              Caption = 'OK'
              TabOrder = 0
              OnClick = OkButtonClick
            end
            object CancelButton: TButton
              Left = 315
              Top = 8
              Width = 75
              Height = 25
              Caption = 'Cancel'
              TabOrder = 1
            end
          end
        end

        """;

    /// <summary>Level 2: overrides the root caption and the OK caption, adds a button into the panel and an edit at the root.</summary>
    public const string MidDialog = """
        inherited MidDialog: TMidDialog
          Caption = 'Mid'
          inherited ButtonPanel: TPanel
            inherited OkButton: TButton
              Caption = 'Apply'
            end
            object HelpButton: TButton
              Left = 8
              Top = 8
              Width = 75
              Height = 25
              Caption = 'Help'
              TabOrder = 2
            end
          end
          object NameEdit: TEdit
            Left = 8
            Top = 32
            Width = 200
            Height = 23
            TabOrder = 1
          end
        end

        """;

    /// <summary>
    /// Level 3: overrides a level-2 override (the OK caption, in a block outside its panel's block: the nesting is not
    /// significant), the OK button's <c>Left</c>, a level-2 new component and the root width; adds a label.
    /// </summary>
    public const string LeafDialog = """
        inherited LeafDialog: TLeafDialog
          ClientWidth = 480
          inherited OkButton: TButton
            Left = 220
            Caption = 'Save'
          end
          inherited ButtonPanel: TPanel
            inherited HelpButton: TButton
              Caption = '?'
            end
          end
          object NoteLabel: TLabel
            Left = 8
            Top = 64
            Width = 80
            Height = 15
            Caption = 'Note'
          end
        end

        """;

    /// <summary>A document parsed with a frame catalog and an ancestor catalog; no syntax errors.</summary>
    public static FormDocument ParseWith(string text, string? ancestorClass, IFrameCatalog? frames, IFrameCatalog? ancestors, string path = "form.form")
    {
        var result = FormParser.Parse(text, path, Components, ancestorClass, frames, ancestors);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        return result.Document!;
    }

    /// <summary>Parses <paramref name="text"/> against <paramref name="ancestors"/> and adds it there under its root class.</summary>
    public static FrameDefinition AddLevel(FrameCatalog ancestors, string text, string unitName, string ancestorClass, IFrameCatalog? frames = null)
    {
        var document = ParseWith(text, ancestorClass, frames, ancestors, unitName + ".form");
        var definition = new FrameDefinition(document.Root.TypeName, unitName, unitName + ".form", document);
        ancestors.Set(definition);
        return definition;
    }

    /// <summary>An ancestor catalog holding the three dialog levels (each merged against the ones before it).</summary>
    public static FrameCatalog DialogChain()
    {
        var ancestors = new FrameCatalog();
        AddLevel(ancestors, BaseDialog, "BaseDialogUnit", "TForm");
        AddLevel(ancestors, MidDialog, "MidDialogUnit", "TBaseDialog");
        AddLevel(ancestors, LeafDialog, "LeafDialogUnit", "TMidDialog");
        return ancestors;
    }
}
