using FreexComp.Designer.Layout;
using static FreexComp.Designer.Tests.Layout.LayoutHarness;

namespace FreexComp.Designer.Tests.Layout;

/// <summary>The layout dump document (layout-engine.md §7.1, ui-framework layout.md UIF-320 … 324; D-033).</summary>
public sealed class LayoutDumpTests
{
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-LAY-050")]
    [Trait("Req", "DSG-LAY-051")]
    [Trait("Req", "DSG-LAY-052")]
    [Trait("Req", "DSG-LAY-053")]
    public void SerializesInSpecifiedShapeAndOrder()
    {
        // The fixture of the run-time writer's test Dump_Format (DSG-LAY-050): the same content written by both writers.
        var fixture = new LayoutDumpForm("TMainForm", "MainForm", 400, 240,
        [
            new LayoutDumpControl("TBox", "NameEdit", 16, 16, 200, 23, true, []),
            new LayoutDumpControl("TBox", "Panel", -5, 50, 300, 100, true,
            [
                new LayoutDumpControl("TDot", "Dot", 2, 3, 10, 10, false, []),
            ]),
            new LayoutDumpControl("TDot", "Last", 0, 230, 400, 10, true, []),
        ]);
        var text = LayoutDump.Serialize([fixture]);
        Assert.Equal(RuntimeFixtureText(), text);
        Assert.DoesNotContain('\r', text);
        Assert.Equal("{\n  \"formatVersion\": 1,\n  \"dpi\": 96,\n  \"forms\": []\n}\n", LayoutDump.Serialize([]));

        // String escaping of UIF-320: quote, backslash, control characters as \u00XX; non-ASCII and '/' literally.
        var escaped = LayoutDump.Serialize([new LayoutDumpForm("T", "a\"b\\c\u0001\t\n\u001F xä€/", 0, 0, [])]);
        Assert.Contains("\"name\": \"a\\\"b\\\\c\\u0001\\u0009\\u000A\\u001F xä€/\",", escaped);

        // A document read back compares equal; differences are reported by path (DSG-LAY-054), names case-insensitively.
        var parsed = LayoutDump.Parse(text);
        Assert.Empty(LayoutDump.Compare(parsed, LayoutDump.Parse(text.Replace("\"Dot\"", "\"DOT\""))));
        var changed = LayoutDump.Parse(text.Replace("\"left\": 2,", "\"left\": 7,").Replace("\"visible\": false", "\"visible\": true"));
        Assert.Equal(["MainForm.Panel.Dot: left designer=2 runtime=7", "MainForm.Panel.Dot: visible designer=false runtime=true"],
            LayoutDump.Compare(parsed, changed));

        // ToDump of a form: creation order, nesting, client-origin coordinates, own Visible, active page only,
        // no non-visual components; the root class/name come from the root header.
        var form = Parse("""
            object MainForm: TForm
              ClientWidth = 400
              ClientHeight = 240
              object NameEdit: TEdit
                Left = 16
                Top = 16
                Width = 200
                Height = 30
              end
              object Panel: TPanel
                Left = -5
                Top = 50
                Width = 300
                Height = 100
                BorderStyle = bsSingle
                object Dot: TLabel
                  Left = 2
                  Top = 3
                  Width = 10
                  Height = 10
                  Visible = False
                end
              end
              object Clock: TTimer
              end
              object Book: TPageControl
                Left = 0
                Top = 0
                Width = 100
                Height = 100
                ActivePage = Second
                object First: TTabSheet
                end
                object Second: TTabSheet
                end
              end
            end
            """);
        var dump = Engine.Compute(form, LayoutMode.RunTime).ToDump();
        Assert.Equal(("TForm", "MainForm", 400, 240), (dump.Class, dump.Name, dump.ClientWidth, dump.ClientHeight));
        Assert.Equal(["NameEdit", "Panel", "Book"], dump.Controls.Select(c => c.Name));
        Assert.Equal(new LayoutDumpControl("TEdit", "NameEdit", 16, 16, 200, 23, true, []) with { Controls = dump.Controls[0].Controls },
            dump.Controls[0]);
        var dot = Assert.Single(dump.Controls[1].Controls);
        Assert.Equal(("TLabel", "Dot", 2, 3, 10, 10, false), (dot.Class, dot.Name, dot.Left, dot.Top, dot.Width, dot.Height, dot.Visible));
        Assert.Equal([false, true], dump.Controls[2].Controls.Select(c => c.Visible));
    }

    /// <summary>The <c>MainForm</c> dump printed by the ui-framework test <c>Dump_Format</c> (its <c>.out</c> fixture).</summary>
    private static string RuntimeFixtureText()
    {
        var lines = File.ReadAllText(Path.Combine(RepoRoot, "ui-framework", "tests", "pascal", "Dump_Format.out")).Replace("\r\n", "\n").Split('\n');
        var start = Array.IndexOf(lines, "# dump of MainForm follows") + 1;
        Assert.True(start > 0, "Dump_Format.out has no 'dump of MainForm' section");
        var end = Array.FindIndex(lines, start, l => l.StartsWith('#'));
        return string.Concat(lines[start..end].Select(l => l + "\n"));
    }
}
