using FreexComp.Designer.CodeSync;
using FreexComp.Designer.Model;
using FreexComp.Designer.Surface;
using FreexComp.Designer.Tests.Support;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Surface;

/// <summary>Copy, cut and paste on the design surface (design-surface.md §10.1, §10.2; D-048, D-049).</summary>
public sealed class ClipboardTests
{
    private const string Form = """
        object F: TForm
          ClientWidth = 400
          ClientHeight = 300
          object Panel1: TPanel
            Left = 8
            Top = 48
            Width = 200
            Height = 150
            TabOrder = 0
            object Button1: TButton
              Left = 16
              Top = 24
              Width = 75
              Height = 25
              Caption = 'Button1'
              TabOrder = 0
            end
          end
          object Edit1: TEdit
            Left = 240
            Top = 48
            Width = 121
            Height = 23
            TabOrder = 1
          end
          object Timer1: TTimer
            DesignLeft = 8
            DesignTop = 8
          end
        end

        """;

    private const string Components = ClipboardFormats.Components;

    private const string PlainText = ClipboardFormats.Text;

    /// <summary>The status messages shown, each with its arguments joined by commas.</summary>
    private static List<(string Id, string Args)> Statuses(DesignerHarness h) =>
        h.Host.Statuses.Select(s => (s.Id, string.Join(",", s.Args))).ToList();

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-110")]
    [Trait("Req", "DSG-SUR-111")]
    [Trait("Req", "DSG-SUR-112")]
    public void CopyWritesFormSyntaxInBothFormats()
    {
        using var h = DesignerHarness.FromText(Form);
        Assert.False(h.Controller.CanExecute(Commands.Copy)); // the form alone cannot be copied

        // Selected in reverse order and with a child of a selected parent: the effective set in creation order.
        h.Select("Edit1", "Button1", "Panel1");
        Assert.True(h.Controller.CanExecute(Commands.Copy));
        h.Execute(Commands.Copy);

        const string expected = """
            object Panel1: TPanel
              Left = 8
              Top = 48
              Width = 200
              Height = 150
              TabOrder = 0
              object Button1: TButton
                Left = 16
                Top = 24
                Width = 75
                Height = 25
                Caption = 'Button1'
                TabOrder = 0
              end
            end
            object Edit1: TEdit
              Left = 240
              Top = 48
              Width = 121
              Height = 23
              TabOrder = 1
            end

            """;
        Assert.Equal(expected, h.Host.Clipboard.GetData(PlainText));
        Assert.Equal(FormWriter.WriteObjects([h.Object("Panel1"), h.Object("Edit1")]), h.Host.Clipboard.GetData(PlainText));

        // The custom format: the same text after the header with the surface origin of the primary's parent client area.
        Assert.Equal("; parent-origin 17 47\n" + expected, h.Host.Clipboard.GetData(Components));
        Assert.Equal(2, h.Host.Clipboard.Formats.Count);
        Assert.Equal(0, h.Undo.UndoCount); // copy changes nothing

        // A nested primary: the header names the client origin of its parent (the panel) in surface coordinates.
        h.Select("Button1");
        h.Execute(Commands.Copy);
        var origin = h.Controller.Geometry.GetClientRect(h.Object("Panel1"));
        var custom = h.Host.Clipboard.GetData(Components)!;
        Assert.StartsWith($"; parent-origin {origin.Left} {origin.Top}\nobject Button1: TButton\n", custom);
        Assert.DoesNotContain(";", h.Host.Clipboard.GetData(PlainText)!);

        // The copied text is a valid fragment: it parses back into the same objects.
        var fragment = FormParser.ParseFragment(custom, "clipboard", TestCatalog.Instance);
        Assert.False(fragment.HasSyntaxErrors);
        Assert.Equal(["Button1"], fragment.Objects.Select(o => o.Name));
        Assert.Equal("TButton", fragment.Objects[0].TypeName);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-120")]
    public void PasteGarbageReportsNothing()
    {
        using var h = DesignerHarness.FromText(Form);
        var before = h.Text;

        // Plain text that is not form syntax: paste is disabled.
        h.Host.Clipboard.SetText("hello world");
        Assert.False(h.Controller.CanExecute(Commands.Paste));
        h.Host.Clipboard.SetText("; parent-origin 1 2\n  objects");
        Assert.False(h.Controller.CanExecute(Commands.Paste));
        h.Host.Clipboard.SetData(new Dictionary<string, string>());
        Assert.False(h.Controller.CanExecute(Commands.Paste));

        // Plain text beginning with the keyword after blank and ';' lines: enabled; a syntax error reports nothing pasted.
        h.Host.Clipboard.SetText("\n  ; comment\n  OBJECT Button9: TButton\n  Left = \nend\n");
        Assert.True(h.Controller.CanExecute(Commands.Paste));
        h.Execute(Commands.Paste);
        Assert.Equal([(StatusIds.PasteNothing, "")], Statuses(h));

        // The custom format enables paste whatever it holds; nothing parsed is reported the same way.
        h.Host.Clipboard.SetData(new Dictionary<string, string> { [Components] = "; parent-origin 17 47\n\n" });
        Assert.True(h.Controller.CanExecute(Commands.Paste));
        h.Execute(Commands.Paste);
        Assert.Equal(2, h.Host.Statuses.Count(s => s.Id == StatusIds.PasteNothing));

        Assert.Equal(before, h.Text);
        Assert.Equal(0, h.Undo.UndoCount);
        Assert.Equal(["F"], h.Selected);

        // The fragment parser: ';' lines skipped, several objects, a syntax error gives no objects and one F0001.
        var two = FormParser.ParseFragment("; header\nobject A: TButton\nend\nobject B: TEdit\nend\n", "clipboard", TestCatalog.Instance);
        Assert.Equal(["A", "B"], two.Objects.Select(o => o.Name));
        var broken = FormParser.ParseFragment("object A: TButton\n  Left = 1\n", "clipboard", TestCatalog.Instance);
        Assert.True(broken.HasSyntaxErrors);
        Assert.Empty(broken.Objects);
        Assert.Equal("F0001", Assert.Single(broken.Diagnostics).Code);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-123")]
    [Trait("Req", "DSG-SUR-125")]
    [Trait("Req", "DSG-SUR-127")]
    public void PasteRenamesAndOffsets()
    {
        using var h = DesignerHarness.FromText(Form);
        var original = h.Text;
        h.Select("Button1");
        h.Execute(Commands.Copy);

        // Into the container it was copied from: offset by one grid step; the name gets the next free number; Caption
        // stays as copied.
        h.Execute(Commands.Paste);
        Assert.Equal(["Button2"], h.Selected);
        Assert.Equal("Panel1", h.Parent("Button2"));
        Assert.Equal((24, 32), h.Position("Button2"));
        Assert.Equal((75, 25), h.Size("Button2"));
        Assert.Equal(new StringValue("Button1"), h.Object("Button2").FindProperty("Caption")!.Value);
        Assert.Equal(1, PropertyAccess.GetInt(h.Object("Button2"), "TabOrder"));
        Assert.Equal(["Button1", "Button2"], h.ChildNames("Panel1"));
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal(UndoTitles.Paste, h.Undo.UndoTitleKey);

        // Consecutive pastes of the same clipboard content into the same parent: k = 2, 3, ...; members of the form
        // class are not free (DSG-CS-031).
        h.Execute(Commands.Paste);
        Assert.Equal(["Button3"], h.Selected);
        Assert.Equal((32, 40), h.Position("Button3"));
        h.Host.CodeSync.Members["Button4"] = MemberLookup.Field;
        h.Execute(Commands.Paste);
        Assert.Equal(["Button5"], h.Selected);
        Assert.Equal((40, 48), h.Position("Button5"));
        Assert.Equal(3, PropertyAccess.GetInt(h.Object("Button5"), "TabOrder"));

        // A new copy restarts the count.
        h.Select("Button1");
        h.Execute(Commands.Copy);
        h.Execute(Commands.Paste);
        Assert.Equal(["Button6"], h.Selected);
        Assert.Equal((24, 32), h.Position("Button6"));

        // Undo removes the pasted objects one paste at a time and restores the selection.
        h.ExecuteUndo();
        Assert.Equal(["Button1"], h.Selected);
        for (var i = 0; i < 3; i++)
        {
            h.ExecuteUndo();
        }

        Assert.Equal(original, h.Text);
        h.ExecuteRedo();
        Assert.Equal(["Button2"], h.Selected);
        Assert.Equal((24, 32), h.Position("Button2"));

        // Into another container: the bounding box goes to (gridX, gridY), repeated pastes add the grid offset.
        using var other = DesignerHarness.FromText(Form);
        other.Select("Button1");
        other.Execute(Commands.Copy);
        other.Select("F");
        other.Execute(Commands.Paste);
        Assert.Equal("F", other.Parent("Button2"));
        Assert.Equal((8, 8), other.Position("Button2"));
        other.Execute(Commands.Paste); // the primary is now Button2, whose parent is the form again
        Assert.Equal("F", other.Parent("Button3"));
        Assert.Equal((16, 16), other.Position("Button3"));

        // Text from another form: a free name is kept; a taken name loses its trailing digits and gets the next number.
        other.Host.Clipboard.SetText("""
            object Memo7: TMemo
              Left = 100
              Top = 50
              Width = 50
              Height = 50
            end
            object Edit: TEdit
              Left = 120
              Top = 150
              Width = 40
              Height = 20
            end
            object Panel1: TPanel
              Left = 200
              Top = 60
              Width = 40
              Height = 40
            end

            """);
        other.Select("F");
        other.Execute(Commands.Paste);
        Assert.Equal(["Memo7", "Edit", "Panel2"], other.Selected);
        Assert.Equal((8, 8), other.Position("Memo7")); // B = (100, 50, ...) moves to (8, 8)
        Assert.Equal((28, 108), other.Position("Edit"));
        Assert.Equal((108, 18), other.Position("Panel2"));
        other.Host.Clipboard.SetText("object Edit: TEdit\n  Left = 1\n  Top = 1\n  Width = 40\n  Height = 20\nend\n");
        other.Execute(Commands.Paste);
        Assert.Equal(["Edit2"], other.Selected); // "Edit" and "Edit1" are taken
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-121")]
    [Trait("Req", "DSG-SUR-126")]
    [Trait("Req", "DSG-SUR-127")]
    public void PasteIntoSelectedContainer()
    {
        using var h = DesignerHarness.FromText(Form);
        h.Select("Edit1", "Timer1");
        h.Execute(Commands.Copy);

        // The primary is a container: the visual objects go into it; non-visual ones always to the form's tray, into
        // the next free tray slot (Timer1 holds the first one).
        h.Select("Panel1");
        h.Execute(Commands.Paste);
        Assert.Equal(["Edit2", "Timer2"], h.Selected);
        Assert.Equal("Panel1", h.Parent("Edit2"));
        Assert.Equal((8, 8), h.Position("Edit2"));
        Assert.Equal(1, PropertyAccess.GetInt(h.Object("Edit2"), "TabOrder"));
        Assert.Equal("F", h.Parent("Timer2"));
        Assert.Equal((88, 8), SurfaceGeometry.TrayPosition(h.Object("Timer2")));
        Assert.Equal(["Panel1", "Edit1", "Timer1", "Timer2"], h.ChildNames("F"));

        // The primary is a control that is not a container: its parent. A tray item as primary: the form.
        h.Select("Button1");
        h.Execute(Commands.Paste);
        Assert.Equal("Panel1", h.Parent("Edit3"));
        Assert.Equal((168, 8), SurfaceGeometry.TrayPosition(h.Object("Timer3")));
        h.Select("Timer1");
        h.Execute(Commands.Paste);
        Assert.Equal("F", h.Parent("Edit4"));
        Assert.Equal((248, 56), h.Position("Edit4")); // copied from the form: offset by one grid step

        // A pasted container keeps its children, at their relative positions; tab orders inside it stay as copied.
        h.Select("Panel1");
        h.Execute(Commands.Copy);
        h.Select("F");
        h.Execute(Commands.Paste);
        Assert.Equal(["Panel2"], h.Selected);
        Assert.Equal((16, 56), h.Position("Panel2"));
        Assert.Equal(["Button2", "Edit5", "Edit6"], h.ChildNames("Panel2"));
        Assert.Equal((16, 24), h.Position("Button2"));
        Assert.Equal(0, PropertyAccess.GetInt(h.Object("Button2"), "TabOrder"));
        Assert.Equal(3, PropertyAccess.GetInt(h.Object("Panel2"), "TabOrder"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-123")]
    [Trait("Req", "DSG-SUR-124")]
    public void PasteFixesInternalReferencesAndDropsUnknownEvents()
    {
        const string form = """
            object F: TForm
              ClientWidth = 400
              ClientHeight = 300
              object Label1: TLabel
                Left = 8
                Top = 8
                Width = 40
                Height = 15
                FocusControl = Edit1
              end
              object Edit1: TEdit
                Left = 56
                Top = 8
                Width = 121
                Height = 23
                TabOrder = 0
                OnChange = Edit1Missing
                OnKeyPress = Edit1Change
              end
              object Button1: TButton
                Left = 8
                Top = 40
                Width = 75
                Height = 25
                PopupMenu = Popup1
                TabOrder = 1
                OnClick = Button1Click
              end
              object Popup1: TPopupMenu
                DesignLeft = 8
                DesignTop = 8
              end
            end

            """;
        using var h = DesignerHarness.FromText(form);
        h.Host.CodeSync.Methods["Button1Click"] = "TNotifyEvent";
        h.Host.CodeSync.Methods["Edit1Change"] = "TNotifyEvent"; // OnKeyPress has another signature
        h.Select("Label1", "Edit1", "Button1");
        h.Execute(Commands.Copy);
        h.Execute(Commands.Paste);

        Assert.Equal(["Label2", "Edit2", "Button2"], h.Selected);
        Assert.Equal(new IdentValue("Edit2"), h.Object("Label2").FindProperty("FocusControl")!.Value); // inside the set: renamed
        Assert.Equal(new IdentValue("Popup1"), h.Object("Button2").FindProperty("PopupMenu")!.Value); // outside, exists: kept
        Assert.Equal(new IdentValue("Button1Click"), h.Object("Button2").FindProperty("OnClick")!.Value); // compatible method
        Assert.Null(h.Object("Edit2").FindProperty("OnChange")); // no such method
        Assert.Null(h.Object("Edit2").FindProperty("OnKeyPress")); // incompatible signature
        Assert.Equal(new IdentValue("Edit1"), h.Object("Label1").FindProperty("FocusControl")!.Value); // originals untouched
        Assert.Equal(1, h.Undo.UndoCount);

        // From another form: references to components this form does not have are removed.
        using var other = DesignerHarness.FromText(Form);
        other.Host.Clipboard.SetData(new Dictionary<string, string> { [Components] = h.Host.Clipboard.GetData(Components)! });
        other.Select("F");
        other.Execute(Commands.Paste);
        Assert.Equal(["Label1", "Edit2", "Button2"], other.Selected);
        Assert.Equal(new IdentValue("Edit2"), other.Object("Label1").FindProperty("FocusControl")!.Value);
        Assert.Null(other.Object("Button2").FindProperty("PopupMenu"));
        Assert.Null(other.Object("Button2").FindProperty("OnClick")); // the other form class has no Button1Click
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-122")]
    public void PasteSkipsUnknownClasses()
    {
        using var h = DesignerHarness.FromText(Form);
        h.Host.Clipboard.SetText("""
            object Gadget1: TGadget
              Left = 0
              Top = 0
              object Inner1: TButton
                Left = 0
                Top = 0
                Width = 10
                Height = 10
              end
            end
            object Button7: TButton
              Left = 40
              Top = 40
              Width = 75
              Height = 25
            end
            object Widget1: TWidget
            end

            """);
        h.Select("F");
        h.Execute(Commands.Paste);
        Assert.Equal(["Button7"], h.Selected);
        Assert.Null(h.Document.FindByName("Gadget1"));
        Assert.Null(h.Document.FindByName("Inner1"));
        Assert.Null(h.Document.FindByName("Widget1"));
        Assert.Equal([(StatusIds.PasteUnknownClass, "2")], Statuses(h));
        Assert.Equal((8, 8), h.Position("Button7")); // the skipped objects do not count for the bounding box

        // An unknown class inside a known container: the container is pasted without that subtree.
        h.Host.Clipboard.SetText("""
            object Panel5: TPanel
              Left = 0
              Top = 0
              Width = 50
              Height = 50
              object Gadget2: TGadget
              end
              object Button8: TButton
                Left = 4
                Top = 4
                Width = 20
                Height = 20
              end
            end

            """);
        h.Execute(Commands.Paste);
        Assert.Equal(["Button8"], h.ChildNames("Panel5"));
        Assert.Equal((StatusIds.PasteUnknownClass, "1"), Statuses(h)[^1]);

        // Only unknown classes: nothing is pasted, no undo unit.
        var units = h.Undo.UndoCount;
        h.Host.Clipboard.SetText("object Widget2: TWidget\nend\n");
        h.Execute(Commands.Paste);
        Assert.Equal(units, h.Undo.UndoCount);
        Assert.Equal((StatusIds.PasteUnknownClass, "1"), Statuses(h)[^1]);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-SUR-113")]
    public void CutIsOneUndoUnit()
    {
        using var h = DesignerHarness.FromText(Form);
        var original = h.Text;
        Assert.False(h.Controller.CanExecute(Commands.Cut));
        h.Select("Edit1");
        Assert.True(h.Controller.CanExecute(Commands.Cut));
        h.Execute(Commands.Cut);

        Assert.Null(h.Document.FindByName("Edit1"));
        Assert.StartsWith("object Edit1: TEdit\n", h.Host.Clipboard.GetData(PlainText));
        Assert.Equal(["F"], h.Selected);
        Assert.Equal(1, h.Undo.UndoCount);
        Assert.Equal(UndoTitles.Cut, h.Undo.UndoTitleKey);

        h.ExecuteUndo();
        Assert.Equal(original, h.Text);
        Assert.Equal(["Edit1"], h.Selected);
        h.ExecuteRedo();
        Assert.Null(h.Document.FindByName("Edit1"));

        // Paste after cut: the name is free again and is kept.
        h.Execute(Commands.Paste);
        Assert.Equal(["Edit1"], h.Selected);
        Assert.Equal((248, 56), h.Position("Edit1"));
        Assert.Equal(1, PropertyAccess.GetInt(h.Object("Edit1"), "TabOrder"));
    }
}
