using FreexComp.Designer.Catalog;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Support;

/// <summary>
/// Frames for the model tests (frames-designer.md §10): the test metadata plus a <c>TFrame</c> entry
/// (<c>TestData/frame-metadata</c>, until the framework metadata has one), an in-memory frame catalog and the fixture texts.
/// </summary>
internal static class FrameFixtures
{
    private static readonly Lazy<ComponentCatalog> Loaded = new(() =>
    {
        var result = CatalogLoader.Load([TestPaths.MetadataIndex, TestPaths.Combine("frame-metadata/index.json")]);
        Assert.Empty(result.Problems);
        return result.Catalog;
    });

    /// <summary>The test metadata with <c>TFrame</c> (kind <c>container</c>, renderer <c>frame</c>, 320 × 240).</summary>
    public static ComponentCatalog Components => Loaded.Value;

    public const string CityFrame = """
        object CityFrame: TCityFrame
          Left = 0
          Top = 0
          Width = 200
          Height = 60
          object CityEdit: TEdit
            Left = 8
            Top = 8
            Width = 100
            Height = 23
            TabOrder = 0
          end
          object ZipEdit: TEdit
            Left = 8
            Top = 32
            Width = 60
            Height = 23
            TabOrder = 1
          end
        end

        """;

    /// <summary>A label, an edit with a handler, a panel holding a second label, and a nested instance of <c>TCityFrame</c>.</summary>
    public const string AddressFrame = """
        object AddressFrame: TAddressFrame
          Left = 0
          Top = 0
          Width = 320
          Height = 180
          Color = clWindow
          ParentColor = False
          object StreetLabel: TLabel
            Left = 8
            Top = 8
            Width = 40
            Height = 15
            Caption = 'Street'
          end
          object StreetEdit: TEdit
            Left = 64
            Top = 4
            Width = 240
            Height = 23
            TabOrder = 0
            Text = 'street'
            OnChange = StreetEditChange
          end
          object CityPanel: TPanel
            Left = 8
            Top = 40
            Width = 296
            Height = 64
            TabOrder = 1
            object CityLabel: TLabel
              Left = 8
              Top = 8
              Width = 24
              Height = 15
              Caption = 'City'
            end
          end
          inline CityPicker: TCityFrame
            Left = 8
            Top = 112
            Width = 200
            Height = 60
            TabOrder = 2
            inherited CityEdit: TEdit
              Text = 'Springfield'
            end
          end
        end

        """;

    /// <summary>A form with an own <c>StreetLabel</c> and two instances; the first overrides five values.</summary>
    public const string CustomerForm = """
        object CustomerForm: TCustomerForm
          Left = 200
          Top = 120
          Caption = 'Customer'
          ClientWidth = 640
          ClientHeight = 400
          PixelsPerInch = 96
          object StreetLabel: TLabel
            Left = 340
            Top = 8
            Width = 40
            Height = 15
            Caption = 'Form street'
          end
          inline BillingAddress: TAddressFrame
            Left = 8
            Top = 8
            Width = 320
            Height = 180
            TabOrder = 0
            inherited StreetLabel: TLabel
              Caption = 'Billing street'
            end
            inherited StreetEdit: TEdit
              OnChange = BillingAddressStreetEditChange
            end
            inherited CityPanel: TPanel
              inherited CityLabel: TLabel
                Left = 16
                Caption = 'Town'
              end
            end
            inherited CityPicker: TCityFrame
              inherited CityEdit: TEdit
                Width = 150
              end
            end
          end
          inline ShippingAddress: TAddressFrame
            Left = 8
            Top = 196
            Width = 320
            Height = 180
            TabOrder = 1
          end
        end

        """;

    /// <summary>A frame document (root ancestor <c>TFrame</c>) parsed with <paramref name="frames"/>; no syntax errors.</summary>
    public static FormDocument ParseFrame(string text, IFrameCatalog frames, string path = "frame.form")
    {
        var result = FormParser.Parse(text, path, Components, "TFrame", frames);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        return result.Document!;
    }

    /// <summary>A form document parsed with <paramref name="frames"/>; no syntax errors.</summary>
    public static FormDocument ParseForm(string text, IFrameCatalog? frames, string path = "form.form")
    {
        var result = FormParser.Parse(text, path, Components, null, frames);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        return result.Document!;
    }

    /// <summary>Adds the frame document of <paramref name="text"/> to <paramref name="frames"/> under its root class.</summary>
    public static FrameDefinition AddFrame(FrameCatalog frames, string text, string unitName)
    {
        var document = ParseFrame(text, frames, unitName + ".form");
        var definition = new FrameDefinition(document.Root.TypeName, unitName, unitName + ".form", document);
        frames.Set(definition);
        return definition;
    }

    /// <summary>A catalog with <c>TCityFrame</c> and <c>TAddressFrame</c> (which contains a city frame).</summary>
    public static FrameCatalog AddressFrames()
    {
        var frames = new FrameCatalog();
        AddFrame(frames, CityFrame, "CityFrameUnit");
        AddFrame(frames, AddressFrame, "AddressFrameUnit");
        return frames;
    }

    /// <summary>The effective value of <paramref name="path"/> as text (strings unquoted, integers decimal, identifiers).</summary>
    public static string Effective(FormObject obj, string path) => PropertyAccess.GetEffective(obj, path) switch
    {
        StringValue s => s.Value,
        IntegerValue i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IdentValue ident => ident.Name,
        var other => other.ToString()!,
    };
}
