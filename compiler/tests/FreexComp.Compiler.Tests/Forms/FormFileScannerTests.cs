using FreexComp.Compiler.Forms;

namespace FreexComp.Compiler.Tests.Forms;

/// <summary>C-114 / LANG-670: the compiler's minimal <c>.form</c> reader (form-format.md §1): objects, event properties, F0001.</summary>
[Trait("Phase", "2")]
public sealed class FormFileScannerTests
{
    private const string Example = """
        object MainForm: TMainForm
          Left = 200
          Top = 120
          Caption = 'Hello'
          ClientWidth = 400
          ClientHeight = 240
          PixelsPerInch = 96
          OnCreate = FormCreate
          object NameEdit: TEdit
            Left = 16
            Top = 16
            Width = 200
            Height = 23
            TabOrder = 0
          end
          object GreetButton: TButton
            Left = 232
            Top = 15
            Width = 75
            Height = 25
            Caption = '&Greet'
            TabOrder = 1
            OnClick = GreetButtonClick
          end
        end

        """;

    private static FormObject Root(string text)
    {
        var result = FormFileScanner.Scan(@"C:\p\F.form", text);
        Assert.Empty(result.Diagnostics);
        return result.Root!;
    }

    [Fact]
    [Trait("Req", "LANG-670")]
    public void Example_ObjectsAndEvents()
    {
        var root = Root(Example);

        Assert.Equal(("object", "MainForm", "TMainForm", 1, 8), (root.Keyword, root.Name, root.ClassName, root.Line, root.Column));
        Assert.Equal([new FormEvent("OnCreate", "FormCreate", 8, 14)], root.Events);
        Assert.Equal(["NameEdit: TEdit", "GreetButton: TButton"], root.Children.Select(c => c.Name + ": " + c.ClassName));
        Assert.Equal((16, 10), (root.Children[1].Line, root.Children[1].Column));
        Assert.Equal([new FormEvent("OnClick", "GreetButtonClick", 23, 15)], root.Children[1].Events);
    }

    [Fact]
    [Trait("Req", "LANG-670")]
    public void EveryValueKind_IsAccepted_AndOnlyPlainIdentifiersOfOnPropertiesAreEvents()
    {
        var root = Root("""
            OBJECT F: TF
              A = -12
              B = $FFFFFFFF
              C = 1.5E+20
              D = 'it''s'#13#10'x' +
                'more'
              E = [akLeft, akTop]
              F = []
              G = (
                'one'
                'two'
              )
              H = {
                0123456789ABCDEF
              }
              I = <
                item
                  Caption = 'c'
                  OnExecute = Ignored
                end
              >
              Font.Height = -16
              OnShow.X = NotAnEvent
              OnHide = Other.Handler
              Onpaint = Painted
              Tag = SomeConstant
              Object Inner: TPanel
                object Deep: TLabel
                end
              End
            end
            """);

        Assert.Equal([new FormEvent("Onpaint", "Painted", 25, 13)], root.Events);
        Assert.Equal(["Inner", "Deep"], root.Descendants().Select(o => o.Name));
    }

    [Fact]
    [Trait("Req", "LANG-670")]
    public void LineEndsBomAndBlankLines_AreAccepted()
    {
        var root = Root("\r\n\r\nobject F: TF\r\n\r\n  OnClick = Go   \r\n\t object B: TB\r\n end\r\nend");

        Assert.Equal("B", Assert.Single(root.Children).Name);
        Assert.Equal("Go", Assert.Single(root.Events).Handler);
        Assert.Equal("F", FormFileScanner.Decode([0xEF, 0xBB, 0xBF, (byte)'F']));
        Assert.Null(FormFileScanner.Decode([0xC3, 0x28]));
    }

    /// <summary>
    /// form-format.md §2.6: an object added to a nested <c>inline</c> object is skipped (LANG-954), the instance's own events are kept
    /// (LANG-953, C-342: they were skipped before); an inherited root is read.
    /// </summary>
    [Fact]
    [Trait("Req", "LANG-671")]
    [Trait("Req", "LANG-953")]
    [Trait("Req", "LANG-954")]
    public void InheritedAndInlineChildren_AreSkipped()
    {
        var root = Root("""
            inherited F: TF
              inline Frame1: TFrame1
                OnClick = Hidden
                object InFrame: TButton
                end
              end
              object Own: TButton
                OnClick = Shown
              end
            end
            """);

        Assert.Equal("inherited", root.Keyword);
        Assert.Equal(["Frame1", "Own"], root.Descendants().Select(o => o.Name));
        Assert.Equal("Hidden", Assert.Single(root.Children[0].Events).Handler);
        Assert.Equal("Shown", Assert.Single(root.Children[1].Events).Handler);
    }

    /// <summary>
    /// LANG-952…954 (C-342): the override blocks of an instance, nested ones too, are kept with their events and marked
    /// <see cref="FormObject.IsOverride"/>; an object added inside one is skipped; an <c>inherited</c> object outside an instance keeps
    /// only itself; a property after a skipped object of an instance is still a syntax error (form-format.md §1).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "LANG-952")]
    [Trait("Req", "LANG-953")]
    [Trait("Req", "LANG-954")]
    public void InlineOverrideBlocks_AreKeptAndMarked()
    {
        var root = Root("""
            object F: TF
              inline Frame1: TFrame1
                inherited Panel: TPanel
                  OnResize = PanelResized
                  inherited Street: TEdit
                    OnChange = StreetChanged
                    object Added: TButton
                      OnClick = Hidden
                    end
                  end
                end
              end
              object Box: TBox
                inherited Old: TButton
                  OnClick = Hidden
                end
              end
            end
            """);

        Assert.Equal(["Frame1", "Panel", "Street", "Box", "Old"], root.Descendants().Select(o => o.Name));
        Assert.Equal([false, true, true, false, false], root.Descendants().Select(o => o.IsOverride));
        Assert.Equal(["PanelResized", "StreetChanged"], root.Descendants().SelectMany(o => o.Events).Select(e => e.Handler));
        Assert.Equal((6, 20), (root.Descendants().ElementAt(2).Events[0].Line, root.Descendants().ElementAt(2).Events[0].Column));

        var late = FormFileScanner.Scan(@"C:\p\F.form", "object F: TF\n  inline A: TA\n    object B: TB\n    end\n    X = 1\n  end\nend\n");
        Assert.Equal(("F0001", 5, 5), (Assert.Single(late.Diagnostics).Code, late.Diagnostics[0].Location.LineSpan.Start.Line + 1,
            late.Diagnostics[0].Location.LineSpan.Start.Column + 1));
    }

    /// <summary>
    /// CMP-1507 (C-450): every kept header — the root, an <c>object</c>, an <c>inherited</c> object, an <c>inline</c> instance and its
    /// override blocks — gives the 1-based position of its class name (spacing around the colon as written).
    /// </summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "CMP-1507")]
    public void ClassNamePositions()
    {
        var root = Root("""
            object F: TF
              object Edit1 : TEdit
              end
              inline Frame1:TFrame1
                inherited Panel: TPanel
                end
              end
              object Box: TBox
                inherited Old:  TButton
                end
              end
            end
            """);

        Assert.Equal((1, 11), (root.ClassLine, root.ClassColumn));
        Assert.Equal(["Edit1 2:18", "Frame1 4:17", "Panel 5:22", "Box 8:15", "Old 9:21"],
            root.Descendants().Select(o => $"{o.Name} {o.ClassLine}:{o.ClassColumn}"));

        var descendant = Root("inherited Form2: TForm2\n  inherited Button1: TButton\n  end\nend\n");
        Assert.Equal((1, 18), (descendant.ClassLine, descendant.ClassColumn));
        Assert.Equal((2, 22), (descendant.Children[0].ClassLine, descendant.Children[0].ClassColumn));
    }

    [Theory]
    [Trait("Req", "LANG-670")]
    [InlineData("object F: TF\n  X = 1 2\nend\n", 2, 9)]
    [InlineData("object F TF\nend\n", 1, 10)]
    [InlineData("object F: TF\n  X = 99999999999999999999\nend\n", 2, 7)]
    [InlineData("object F: TF\n  X = 'open\nend\n", 2, 7)]
    [InlineData("object F: TF\n  X = 1 + 2\nend\n", 2, 9)]
    [InlineData("object F: TF\n  X = ~\nend\n", 2, 7)]
    [InlineData("object F: TF\n", 2, 1)]
    [InlineData("object F: TF\nend\nobject G: TG\nend\n", 3, 1)]
    [InlineData("object F: TF\n  object A: TA\n  end\n  X = 1\nend\n", 4, 3)]
    public void SyntaxError_IsOneF0001AtItsPosition(string text, int line, int column)
    {
        var result = FormFileScanner.Scan(@"C:\p\F.form", text);

        Assert.Null(result.Root);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(("F0001", DiagnosticSeverity.Error, @"C:\p\F.form"), (error.Code, error.Severity, error.Location.Path));
        Assert.StartsWith("Syntax error: ", error.Message, StringComparison.Ordinal);
        Assert.Equal((line, column), (error.Location.LineSpan.Start.Line + 1, error.Location.LineSpan.Start.Column + 1));
    }
}
