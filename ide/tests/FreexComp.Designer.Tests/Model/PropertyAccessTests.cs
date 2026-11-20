using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Model;

public sealed class PropertyAccessTests
{
    private static readonly ComponentCatalog Catalog = TestCatalog.Instance;

    private const string Text = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'Hello'
          ClientWidth = 400
          ClientHeight = 240
          PixelsPerInch = 96
          object Edit1: TEdit
            Left = 16
            Top = 16
            Width = 200
            Height = 23
            Font.Height = -16
            TabOrder = 0
          end
          object Timer1: TTimer
            Interval = 250
            DesignLeft = 8
          end
          object S: TStatusBar
            Left = 0
            Top = 221
            Width = 400
            Height = 19
          end
        end
        """;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-004")]
    [Trait("Req", "DSG-FD-010")]
    [Trait("Req", "DSG-FD-012")]
    public void EffectiveValueFallsBackToMetadataDefault()
    {
        var document = FormParser.Parse(Text, "p.form", Catalog).Document!;
        var edit = document.FindByName("Edit1")!;
        var timer = document.FindByName("Timer1")!;
        var status = document.FindByName("S")!;

        // Stored values win.
        Assert.True(PropertyAccess.IsStored(edit, "font.height"));
        Assert.Equal(new IntegerValue(-16, false), PropertyAccess.GetEffective(edit, "Font.Height"));
        Assert.Equal(new IntegerValue(250, false), PropertyAccess.GetEffective(timer, "Interval"));

        // Metadata defaults, by type.
        Assert.False(PropertyAccess.IsStored(edit, "Text"));
        Assert.Equal(new StringValue(""), PropertyAccess.GetEffective(edit, "Text"));
        Assert.Equal(new IdentValue("True"), PropertyAccess.GetEffective(edit, "AutoSelect"));
        Assert.Equal(new IdentValue("alNone"), PropertyAccess.GetEffective(edit, "Align"));
        Assert.Equal(new IdentValue("alBottom"), PropertyAccess.GetEffective(status, "Align")); // redeclared default
        Assert.Equal(new SetValue(["akLeft", "akTop"]), PropertyAccess.GetEffective(edit, "Anchors"));
        Assert.Equal(new IdentValue("clWindow"), PropertyAccess.GetEffective(edit, "Color"));
        Assert.Equal(new IdentValue("crDefault"), PropertyAccess.GetEffective(edit, "Cursor"));
        Assert.Equal(new StringValue("Segoe UI"), PropertyAccess.GetEffective(edit, "Font.Name"));
        Assert.Equal(new SetValue([]), PropertyAccess.GetEffective(edit, "Font.Style"));
        Assert.Equal(new IntegerValue(0, false), PropertyAccess.GetEffective(edit, "TabOrder")); // stored
        Assert.Equal(new IntegerValue(-1, false), PropertyAccess.DefaultValue(Catalog, Catalog.Find("TEdit")!.FindProperty("TabOrder")!));
        Assert.Equal(new StringValue("\0"), PropertyAccess.GetEffective(edit, "PasswordChar")); // "" = #0
        Assert.Equal(new IdentValue("nil"), PropertyAccess.GetEffective(edit, "PopupMenu"));
        Assert.Equal(new CollectionValue([]), PropertyAccess.GetEffective(status, "Panels"));
        Assert.Equal(new IntegerValue(0, false), PropertyAccess.GetEffective(edit, "Tag"));

        // No declared default: the type zero value (DSG-FD-010).
        Assert.Equal(new IntegerValue(0, false), PropertyAccess.GetEffective(timer, "DesignTop"));
        Assert.Equal(new IntegerValue(8, false), PropertyAccess.GetEffective(timer, "DesignLeft"));
        Assert.Equal(new IntegerValue(3, false), PropertyAccess.GetEffective(edit, "Margins.Left"));
        Assert.Equal(3, PropertyAccess.GetInt(edit, "Margins.Left"));
        Assert.Equal(0, PropertyAccess.GetInt(edit, "Constraints.MaxWidth"));

        // Geometry.
        Assert.Equal(new Rect(16, 16, 200, 23), PropertyAccess.GetBounds(edit));
        Assert.Equal(new Rect(0, 0, 400, 240), PropertyAccess.GetBounds(document.Root));
        var legacy = FormParser.Parse("object F: TForm\n  Width = 416\n  Height = 339\nend\n", "l.form", Catalog).Document!;
        Assert.Equal(new Rect(0, 0, 400, 300), PropertyAccess.GetBounds(legacy.Root)); // DSG-FD-012: Width − 16, Height − 39

        // Unknown paths and object properties have no value.
        Assert.Throws<ArgumentException>(() => PropertyAccess.GetEffective(edit, "Nope"));
        Assert.Throws<ArgumentException>(() => PropertyAccess.GetEffective(edit, "Font"));
        Assert.Throws<ArgumentException>(() => PropertyAccess.GetEffective(edit, "DesignLeft")); // controls have bounds, not a tray position
    }

    public static TheoryData<string, string, string, FormValue, bool> Comparisons() => new()
    {
        // object, path, metadata type (documentation only), value, equal to default
        { "Edit1", "MaxLength", "Integer", new IntegerValue(0, false), true },
        { "Edit1", "MaxLength", "Integer", new IntegerValue(0, true), true }, // hex flag ignored
        { "Edit1", "MaxLength", "Integer", new IntegerValue(1, false), false },
        { "Edit1", "TabOrder", "Integer", new IntegerValue(-1, false), true },
        { "Edit1", "Tag", "Int64", new IntegerValue(0, false), true },
        { "Edit1", "Tag", "Int64", new StringValue("0"), false },
        { "Edit1", "Color", "TColor name", new IdentValue("CLWINDOW"), true },
        { "Edit1", "Color", "TColor name", new IntegerValue(unchecked((int)0xFF000005), true), true }, // clWindow's value
        { "Edit1", "Color", "TColor name", new IdentValue("clBtnFace"), false },
        { "Edit1", "Color", "TColor name", new IntegerValue(5, false), false },
        { "Edit1", "Cursor", "TCursor name", new IntegerValue(0, false), true },
        { "Edit1", "Cursor", "TCursor name", new IdentValue("crHandPoint"), false },
        { "Edit1", "Font.Color", "TColor name", new IdentValue("clWindowText"), true },
        { "Edit1", "AutoSelect", "Boolean", new IdentValue("true"), true },
        { "Edit1", "AutoSelect", "Boolean", new IdentValue("False"), false },
        { "Edit1", "Text", "string", new StringValue(""), true },
        { "Edit1", "Text", "string", new StringValue(" "), false },
        { "Edit1", "Font.Name", "string", new StringValue("Segoe UI"), true },
        { "Edit1", "Font.Name", "string", new StringValue("segoe ui"), false }, // ordinal
        { "Edit1", "PasswordChar", "Char", new StringValue("\0"), true },
        { "Edit1", "PasswordChar", "Char", new StringValue("*"), false },
        { "Edit1", "CharCase", "enum", new IdentValue("ECNORMAL"), true },
        { "Edit1", "CharCase", "enum", new IdentValue("ecUpperCase"), false },
        { "Edit1", "Anchors", "set", new SetValue(["AKTOP", "akLeft"]), true }, // order and case ignored
        { "Edit1", "Anchors", "set", new SetValue(["akLeft"]), false },
        { "Edit1", "Font.Style", "set", new SetValue([]), true },
        { "Memo1", "Lines", "TStrings", new StringListValue([]), true },
        { "Memo1", "Lines", "TStrings", new StringListValue([""]), false },
        { "Edit1", "PopupMenu", "component", new IdentValue("NIL"), true },
        { "Edit1", "PopupMenu", "component", new IdentValue("PopupMenu1"), false },
        { "S", "Panels", "collection", new CollectionValue([]), true },
        { "S", "Panels", "collection", new CollectionValue([new CollectionItem([])]), false },
        { "Timer1", "DesignLeft", "no default: zero", new IntegerValue(0, false), true },
        { "Edit1", "Left", "no default: zero", new IntegerValue(0, false), true },
        { "Edit1", "Align", "enum", new RawValue("alNone"), false }, // a preserved raw value never equals the default
    };

    [Theory]
    [MemberData(nameof(Comparisons))]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-010")]
    public void DefaultComparisonPerType(string objectName, string path, string type, FormValue value, bool expected)
    {
        _ = type;
        var document = FormParser.Parse(Text + "\n", "p.form", Catalog).Document!;
        var obj = objectName == "Memo1"
            ? FormParser.Parse("object F: TForm\n  object Memo1: TMemo\n  end\nend\n", "m.form", Catalog).Document!.FindByName("Memo1")!
            : document.FindByName(objectName)!;
        Assert.Equal(expected, PropertyAccess.IsDefault(obj, path, value));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-010")]
    public void DoubleDefaultsAcceptFloatsAndIntegers()
    {
        var info = new PropertyInfo("Ratio", "Double", PropertyKind.Simple, 1.0, true, "Misc", null, false, false, null, null, null,
            false, null, null, "TTest", true, false, null);
        Assert.True(PropertyAccess.IsDefault(Catalog, info, new FloatValue(1.0)));
        Assert.True(PropertyAccess.IsDefault(Catalog, info, new IntegerValue(1, false)));
        Assert.False(PropertyAccess.IsDefault(Catalog, info, new FloatValue(1.5)));
        Assert.Equal(new FloatValue(0.0), PropertyAccess.DefaultValue(Catalog, info with { Default = null, HasDefault = false }));
    }
}
