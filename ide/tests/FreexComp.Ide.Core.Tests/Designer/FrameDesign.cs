using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Ide.Core.Tests.Designer;

/// <summary>
/// Frames for the view-model tests (frames-designer.md §10): the test metadata (with <c>TFrame</c>), an address frame,
/// and a customer form with one instance of it.
/// </summary>
internal static class FrameDesign
{
    private static readonly Lazy<ComponentCatalog> Loaded = new(() =>
    {
        var data = Path.Combine(AppContext.BaseDirectory, "TestData");
        var result = CatalogLoader.Load([Path.Combine(data, "metadata", "index.json")]);
        Assert.Empty(result.Problems);
        return result.Catalog;
    });

    /// <summary>The test metadata with <c>TFrame</c>.</summary>
    public static ComponentCatalog Catalog => Loaded.Value;

    /// <summary>A label, an edit with a handler of the frame and a panel holding a second label.</summary>
    public static readonly string AddressFrame = string.Join("\n",
        "object AddressFrame: TAddressFrame",
        "  Left = 0",
        "  Top = 0",
        "  Width = 320",
        "  Height = 120",
        "  Color = clWindow",
        "  ParentColor = False",
        "  object StreetLabel: TLabel",
        "    Left = 8",
        "    Top = 8",
        "    Width = 36", // the measured width of the auto-sized caption (no override from a layout commit)
        "    Height = 15",
        "    Caption = 'Street'",
        "  end",
        "  object StreetEdit: TEdit",
        "    Left = 64",
        "    Top = 4",
        "    Width = 240",
        "    Height = 23",
        "    TabOrder = 0",
        "    OnChange = StreetEditChange",
        "  end",
        "  object CityPanel: TPanel",
        "    Left = 8",
        "    Top = 40",
        "    Width = 296",
        "    Height = 64",
        "    TabOrder = 1",
        "    object CityLabel: TLabel",
        "      Left = 8",
        "      Top = 8",
        "      Width = 24",
        "      Height = 15",
        "      Caption = 'City'",
        "    end",
        "  end",
        "end",
        "");

    /// <summary>A catalog with <c>TAddressFrame</c> (unit <c>AddressFrameUnit</c>).</summary>
    public static AncestorCatalog Frames()
    {
        var parsed = FormParser.Parse(AddressFrame, @"C:\p\AddressFrameUnit.form", Catalog, "TFrame");
        Assert.Empty(parsed.Diagnostics);
        return new AncestorCatalog(new AncestorDefinition("TAddressFrame", "AddressFrameUnit", @"C:\p\AddressFrameUnit.form", parsed.Document!));
    }

    /// <summary>The customer form: an own panel and one instance whose label caption is overridden.</summary>
    public static readonly string Form = string.Join("\n",
        "object MainForm: TMainForm",
        "  Left = 200",
        "  Top = 120",
        "  Caption = 'Customer'",
        "  ClientWidth = 640",
        "  ClientHeight = 400",
        "  PixelsPerInch = 96",
        "  inline BillingAddress: TAddressFrame",
        "    Left = 8",
        "    Top = 8",
        "    Width = 320",
        "    Height = 120",
        "    TabOrder = 0",
        "    inherited StreetLabel: TLabel",
        "      Caption = 'Billing street'",
        "    end",
        "  end",
        "  object OwnPanel: TPanel",
        "    Left = 340",
        "    Top = 8",
        "    Width = 280",
        "    Height = 120",
        "    TabOrder = 1",
        "  end",
        "end",
        "");

    /// <summary>The unit of <see cref="Form"/>: a field for the instance and the panel, none for the frame's components.</summary>
    public static readonly string Unit = string.Join("\r\n",
        "unit MainForm;",
        "",
        "interface",
        "",
        "uses",
        "  Classes, Controls, Forms, StdCtrls, ExtCtrls, AddressFrameUnit;",
        "",
        "type",
        "  TMainForm = class(TForm)",
        "    BillingAddress: TAddressFrame;",
        "    OwnPanel: TPanel;",
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

    /// <summary>The form read with the frames, as the designer reads it.</summary>
    public static FormDocument ParseForm(string text, IAncestorCatalog frames)
    {
        var parsed = FormParser.Parse(text.Replace("\r\n", "\n", StringComparison.Ordinal), "MainForm.form", Catalog, null, frames);
        Assert.False(parsed.HasSyntaxErrors);
        return parsed.Document!;
    }
}
