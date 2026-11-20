using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Editing;

/// <summary>Property changes through <see cref="FormEditor"/> (form-document.md §7; D-019).</summary>
public sealed partial class FormEditorTests
{
    private static readonly ComponentCatalog Catalog = TestCatalog.Instance;

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-080")]
    [Trait("Req", "DSG-FD-043")]
    [Trait("Req", "DSG-FD-044")]
    [Trait("Req", "DSG-FD-012")]
    public void SetPropertyEqualToDefaultRemovesIt()
    {
        var (document, editor) = Open(Hello);
        var original = FormWriter.Write(document);
        var button = document.FindByName("GreetButton")!;

        // A value equal to the metadata default is removed; the inverse restores the line with its raw text.
        var inverse = editor.Apply(new SetProperty("GreetButton", "Caption", new StringValue("")));
        Assert.Null(button.FindProperty("Caption"));
        Assert.DoesNotContain("Caption = '&Greet'", FormWriter.Write(document), StringComparison.Ordinal);
        var undo = Assert.IsType<SetProperty>(inverse);
        Assert.Equal(("GreetButton", "Caption", new StringValue("&Greet")), (undo.ObjectName, undo.Path, undo.Value));
        var redo = editor.Apply(inverse);
        Assert.Equal("'&Greet'", button.FindProperty("Caption")!.RawText); // lexical fidelity
        Assert.Equal(original, FormWriter.Write(document));
        Assert.Equal(new RemoveProperty("GreetButton", "Caption"), Strip(redo)); // redo removes it again

        // A non-default value is stored with metadata casing and no raw text; a stored property keeps its position and casing.
        var undoCancel = editor.Apply(new SetProperty("greetbutton", "cancel", new IdentValue("True")));
        Assert.Equal(("Cancel", null), (button.FindProperty("Cancel")!.Path, button.FindProperty("Cancel")!.RawText));
        Assert.Equal(new RemoveProperty("GreetButton", "Cancel"), Strip(undoCancel)); // it was not stored
        editor.Apply(undoCancel);
        editor.Apply(new SetProperty("GreetButton", "CAPTION", new StringValue("OK")));
        Assert.Equal(["Left", "Top", "Width", "Height", "Caption", "TabOrder", "OnClick"], button.Properties.Select(p => p.Path));
        Assert.Contains("    Caption = 'OK'\n", FormWriter.Write(document), StringComparison.Ordinal);

        // Setting a default that is not stored changes nothing; its inverse is a RemoveProperty.
        Assert.Equal(new RemoveProperty("GreetButton", "WordWrap"), Strip(editor.Apply(new SetProperty("GreetButton", "WordWrap", new IdentValue("False")))));
        Assert.Null(button.FindProperty("WordWrap"));

        // DSG-FD-044: always-written properties stay stored even at their zero value.
        editor.Apply(new SetProperty("GreetButton", "Left", new IntegerValue(0, false)));
        Assert.Equal(new IntegerValue(0, false), button.FindProperty("Left")!.Value);
        editor.Apply(new SetProperty("MainForm", "PixelsPerInch", new IntegerValue(96, false)));
        Assert.NotNull(document.Root.FindProperty("PixelsPerInch"));

        // RemoveProperty = reset to default; its inverse restores the stored line.
        var restoreTabOrder = editor.Apply(new RemoveProperty("NameEdit", "TabOrder"));
        Assert.Null(document.FindByName("NameEdit")!.FindProperty("TabOrder"));
        editor.Apply(restoreTabOrder);
        Assert.Equal("0", document.FindByName("NameEdit")!.FindProperty("TabOrder")!.RawText);

        // Events are stored as given and removed by RemoveProperty.
        editor.Apply(new SetProperty("NameEdit", "onchange", new IdentValue("NameEditChange")));
        Assert.True(document.FindByName("NameEdit")!.FindProperty("OnChange")!.IsEvent);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-043")]
    [Trait("Req", "DSG-FD-080")]
    public void PairedPropertiesFollowTheirParentFlag()
    {
        var (document, editor) = Open("""
            object F: TForm
              Left = 0
              Top = 0
              ClientWidth = 100
              ClientHeight = 100
              PixelsPerInch = 96
              object L: TLabel
                Left = 0
                Top = 0
                Width = 34
                Height = 15
                Caption = 'x'
              end
              object B: TButton
                Left = 0
                Top = 0
                Width = 75
                Height = 25
                Font.Size = 12
                TabOrder = 0
              end
            end
            """);
        var original = FormWriter.Write(document);
        var label = document.FindByName("L")!;

        // form-format.md §2.2.4: assigning Font.* / Color / ShowHint also stores Parent… = False, in the same change;
        // its single inverse undoes both lines.
        var undoFont = editor.Apply(new SetProperty("L", "Font.Height", new IntegerValue(-16, false)));
        Assert.Equal(new IdentValue("False"), label.FindProperty("ParentFont")!.Value);
        Assert.Equal("Font.Height", label.FindProperty("font.height")!.Path);
        editor.Apply(undoFont);
        Assert.Null(label.FindProperty("ParentFont"));
        Assert.Equal(original, FormWriter.Write(document));

        var undo = new Stack<FormChange>();
        undo.Push(editor.Apply(new SetProperty("L", "Font.Height", new IntegerValue(-16, false))));
        undo.Push(editor.Apply(new SetProperty("L", "Color", new IdentValue("clRed"))));
        undo.Push(editor.Apply(new SetProperty("L", "ShowHint", new IdentValue("True"))));
        Assert.Equal(["False", "False"], new[] { "ParentColor", "ParentShowHint" }.Select(p => ((IdentValue)label.FindProperty(p)!.Value).Name));

        // Setting the flag back to True (its default) removes it and the dependent lines; so does resetting it.
        undo.Push(editor.Apply(new SetProperty("L", "ParentFont", new IdentValue("True"))));
        Assert.Null(label.FindProperty("ParentFont"));
        Assert.Null(label.FindProperty("Font.Height"));
        undo.Push(editor.Apply(new RemoveProperty("L", "ParentColor")));
        Assert.Null(label.FindProperty("Color"));
        Assert.NotNull(label.FindProperty("ShowHint"));
        while (undo.TryPop(out var change))
        {
            editor.Apply(change);
        }

        Assert.Equal(original, FormWriter.Write(document));

        // A class without the flag is not restricted (the form has no ParentFont).
        editor.Apply(new SetProperty("F", "Font.Height", new IntegerValue(-14, false)));
        Assert.Null(document.Root.FindProperty("ParentFont"));

        // DSG-FD-043: a "stored": false line read from the file is kept until the object is edited, then dropped (undo restores it).
        Assert.Contains("Font.Size = 12", FormWriter.Write(document), StringComparison.Ordinal);
        var undoCaption = editor.Apply(new SetProperty("B", "Caption", new StringValue("Go")));
        Assert.Null(document.FindByName("B")!.FindProperty("Font.Size"));
        editor.Apply(undoCaption);
        Assert.Equal("12", document.FindByName("B")!.FindProperty("Font.Size")!.RawText);
        editor.Apply(new SetProperty("B", "Font.Size", new IntegerValue(20, false))); // never stored
        Assert.Null(document.FindByName("B")!.FindProperty("Font.Size"));

        // DSG-FD-012: the first client-size change of a legacy root writes both client values and drops Width/Height.
        var (legacy, legacyEditor) = Open("object F: TForm\n  Left = 0\n  Top = 0\n  Width = 416\n  Height = 279\n  PixelsPerInch = 96\nend\n");
        var legacyText = FormWriter.Write(legacy);
        legacyEditor.Apply(new SetProperty("F", "Caption", new StringValue("Kept")));
        Assert.NotNull(legacy.Root.FindProperty("Width")); // other edits keep the legacy size
        var undoClient = legacyEditor.Apply(new SetProperty("F", "ClientWidth", new IntegerValue(500, false)));
        Assert.Equal(
            "object F: TForm\n  Left = 0\n  Top = 0\n  Caption = 'Kept'\n  ClientWidth = 500\n  ClientHeight = 240\n  PixelsPerInch = 96\nend\n",
            FormWriter.Write(legacy));
        legacyEditor.Apply(undoClient);
        legacyEditor.Apply(new RemoveProperty("F", "Caption"));
        Assert.Equal(legacyText, FormWriter.Write(legacy));
    }

    private const string Hello = "forms/canonical/hello.form";

    private static (FormDocument Document, FormEditor Editor) Open(string fileOrText)
    {
        var text = fileOrText.EndsWith(".form", StringComparison.Ordinal) ? File.ReadAllText(TestPaths.Combine(fileOrText)) : fileOrText;
        var result = FormParser.Parse(text, "test.form", Catalog);
        Assert.False(result.HasSyntaxErrors, string.Join("; ", result.Diagnostics));
        return (result.Document!, new FormEditor(result.Document!, Catalog));
    }

    /// <summary>The public part of a change (inverses carry an internal snapshot that records compare by reference).</summary>
    private static FormChange Strip(FormChange change) => change switch
    {
        SetProperty s => new SetProperty(s.ObjectName, s.Path, s.Value),
        RemoveProperty r => new RemoveProperty(r.ObjectName, r.Path),
        _ => change,
    };
}
