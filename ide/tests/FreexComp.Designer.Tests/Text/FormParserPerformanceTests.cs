using System.Globalization;
using System.Text;
using FreexComp.Designer.Text;
using FreexComp.Ide.TestSupport;

namespace FreexComp.Designer.Tests.Text;

/// <summary>DSG-FD-029: parse time of a large form (D-017).</summary>
public sealed partial class FormParserTests
{
    /// <summary>Spec budget for parsing a 5 000-object form (DSG-FD-029), for optimized builds.</summary>
    private const double LargeFormBudgetMilliseconds = 200;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Category", "Performance")]
    [Trait("Req", "DSG-FD-029")]
    public void ParsesLargeFormWithinBudget()
    {
        var text = LargeForm(panels: 50, controlsPerPanel: 99); // 1 root + 50 panels + 4 950 controls = 5 001 objects

        var objects = 0;
        var fastest = PerformanceBudget.MinThreadCpu(() =>
        {
            var result = FormParser.Parse(text, "large.form", Catalog);
            objects = result.Document!.AllObjects().Count();
        });

        Assert.Equal(5001, objects);
        var budget = PerformanceBudget.Of(LargeFormBudgetMilliseconds, typeof(FormParser).Assembly);
        Assert.True(budget.IsMetBy(fastest), budget.Describe("Parsing 5 001 objects took", fastest));
        Assert.Empty(FormParser.Parse(text, "large.form", Catalog).Diagnostics);
    }

    /// <summary>A canonical form with typical property lines of every common value kind.</summary>
    private static string LargeForm(int panels, int controlsPerPanel)
    {
        var s = new StringBuilder();
        s.Append("object LargeForm: TForm\n  Left = 200\n  Top = 120\n  Caption = 'Large'\n  ClientWidth = 1024\n")
            .Append("  ClientHeight = 768\n  Font.Height = -12\n  Font.Name = 'Segoe UI'\n  PixelsPerInch = 96\n  OnCreate = FormCreate\n");
        for (var p = 0; p < panels; p++)
        {
            s.Append(CultureInfo.InvariantCulture, $"  object Panel{p}: TPanel\n    Left = {p * 8}\n    Top = 8\n    Width = 400\n    Height = 600\n")
                .Append("    Anchors = [akLeft, akTop, akRight]\n    Caption = 'Panel'\n    TabOrder = 0\n");
            for (var c = 0; c < controlsPerPanel; c++)
            {
                var name = $"P{p}C{c}";
                switch (c % 3)
                {
                    case 0:
                        s.Append(CultureInfo.InvariantCulture, $"    object {name}: TButton\n      Left = 8\n      Top = {c * 4}\n      Width = 75\n      Height = 25\n")
                            .Append(CultureInfo.InvariantCulture, $"      Caption = 'Button {c} with a caption'#13#10'second line'\n      TabOrder = {c}\n      OnClick = {name}Click\n    end\n");
                        break;
                    case 1:
                        s.Append(CultureInfo.InvariantCulture, $"    object {name}: TEdit\n      Left = 100\n      Top = {c * 4}\n      Width = 121\n      Height = 23\n")
                            .Append("      Color = $00C0FFEE\n      Font.Style = [fsBold, fsItalic]\n      ParentFont = False\n")
                            .Append(CultureInfo.InvariantCulture, $"      TabOrder = {c}\n      Text = 'Edit'\n      OnChange = {name}Change\n    end\n");
                        break;
                    default:
                        s.Append(CultureInfo.InvariantCulture, $"    object {name}: TMemo\n      Left = 240\n      Top = {c * 4}\n      Width = 185\n      Height = 89\n")
                            .Append("      Lines = (\n        'first line'\n        'it''s the second'\n      )\n")
                            .Append(CultureInfo.InvariantCulture, $"      TabOrder = {c}\n    end\n");
                        break;
                }
            }

            s.Append("  end\n");
        }

        return s.Append("end\n").ToString();
    }
}
