using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// A descendant form for the view-model tests (form-inheritance-designer.md §13): an ancestor dialog with handlers at the
/// root, on a button and on its panel, and the main form inheriting from it.
/// </summary>
internal static class InheritedDesign
{
    /// <summary>The ancestor: a title label and a button panel with two buttons; the OK button and the root have handlers.</summary>
    public static readonly string BaseDialog = string.Join("\n",
        "object BaseDialog: TBaseDialog",
        "  Left = 200",
        "  Top = 120",
        "  Caption = 'Base'",
        "  ClientWidth = 400",
        "  ClientHeight = 200",
        "  PixelsPerInch = 96",
        "  OnCreate = FormCreate",
        "  object TitleLabel: TLabel",
        "    Left = 8",
        "    Top = 8",
        "    Width = 60",
        "    Height = 15",
        "    Caption = 'Title'",
        "  end",
        "  object ButtonPanel: TPanel",
        "    Left = 0",
        "    Top = 160",
        "    Width = 400",
        "    Height = 40",
        "    TabOrder = 0",
        "    OnClick = CancelButtonClick",
        "    object OkButton: TButton",
        "      Left = 230",
        "      Top = 8",
        "      Width = 75",
        "      Height = 25",
        "      Caption = 'OK'",
        "      TabOrder = 0",
        "      OnClick = OkButtonClick",
        "    end",
        "    object CancelButton: TButton",
        "      Left = 315",
        "      Top = 8",
        "      Width = 75",
        "      Height = 25",
        "      Caption = 'Cancel'",
        "      TabOrder = 1",
        "    end",
        "  end",
        "end",
        "");

    public const string BaseFormPath = @"C:\p\BaseDialogUnit.form";

    /// <summary>The unit of <see cref="BaseDialog"/> (<c>BaseDialogUnit</c>): its fields and handlers (D-132).</summary>
    public static readonly string BaseUnit = string.Join("\r\n",
        "unit BaseDialogUnit;", "", "interface", "", "uses", "  Classes, Controls, Forms, StdCtrls, ExtCtrls;", "", "type",
        "  TBaseDialog = class(TForm)", "    TitleLabel: TLabel;", "    ButtonPanel: TPanel;", "    OkButton: TButton;",
        "    CancelButton: TButton;", "    procedure FormCreate(Sender: TObject);", "    procedure CancelButtonClick(Sender: TObject);",
        "    procedure OkButtonClick(Sender: TObject);", "  end;", "", "var", "  BaseDialog: TBaseDialog;", "", "implementation", "",
        "{$FORM BaseDialogUnit.form}", "", "procedure TBaseDialog.FormCreate(Sender: TObject);", "begin", "end;", "",
        "procedure TBaseDialog.CancelButtonClick(Sender: TObject);", "begin", "end;", "",
        "procedure TBaseDialog.OkButtonClick(Sender: TObject);", "begin", "end;", "", "end.", "");

    /// <summary>A catalog with <c>TBaseDialog</c> (unit <c>BaseDialogUnit</c>).</summary>
    public static AncestorCatalog Ancestors()
    {
        var parsed = FormParser.Parse(BaseDialog, BaseFormPath, FrameDesign.Catalog, "TForm");
        Assert.Empty(parsed.Diagnostics);
        var ancestors = new AncestorCatalog();
        ancestors.Set(new AncestorDefinition("TBaseDialog", "BaseDialogUnit", BaseFormPath, parsed.Document!));
        return ancestors;
    }

    /// <summary>The descendant: overrides the root caption and the OK caption, adds a button into the panel and an edit.</summary>
    public static readonly string Form = string.Join("\n",
        "inherited MainForm: TMainForm",
        "  Caption = 'Main'",
        "  inherited ButtonPanel: TPanel",
        "    inherited OkButton: TButton",
        "      Caption = 'Apply'",
        "    end",
        "    object HelpButton: TButton",
        "      Left = 8",
        "      Top = 8",
        "      Width = 75",
        "      Height = 25",
        "      Caption = 'Help'",
        "      TabOrder = 2",
        "    end",
        "  end",
        "  object NameEdit: TEdit",
        "    Left = 8",
        "    Top = 32",
        "    Width = 200",
        "    Height = 23",
        "    TabOrder = 1",
        "  end",
        "end",
        "");

    /// <summary>The unit of <see cref="Form"/>: fields for the new components only.</summary>
    public static readonly string Unit = string.Join("\r\n",
        "unit MainForm;",
        "",
        "interface",
        "",
        "uses",
        "  Classes, Controls, Forms, StdCtrls, ExtCtrls, BaseDialogUnit;",
        "",
        "type",
        "  TMainForm = class(TBaseDialog)",
        "    HelpButton: TButton;",
        "    NameEdit: TEdit;",
        "  end;",
        "",
        "var",
        "  MainForm: TMainForm;",
        "",
        "implementation",
        "",
        "{$FORM MainForm.form}",
        "",
        "end.",
        "");

    /// <summary>The descendant read against the ancestor, as the designer reads it.</summary>
    public static FormDocument ParseForm(string text, IAncestorCatalog ancestors)
    {
        var parsed = FormParser.Parse(text, "MainForm.form", FrameDesign.Catalog, "TBaseDialog", null, ancestors);
        Assert.False(parsed.HasSyntaxErrors);
        Assert.True(parsed.Document!.Root.IsInheritedRoot);
        return parsed.Document;
    }
}
