using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Tests.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Editing;

/// <summary>Object changes, inverses, validation and change events of <see cref="FormEditor"/> (form-document.md §7; D-020).</summary>
public sealed partial class FormEditorTests
{
    [Theory]
    [InlineData("forms/canonical/all-value-kinds.form")]
    [InlineData("forms/canonical/nested.form")]
    [InlineData("forms/canonical/nonvisual.form")]
    [InlineData("forms/semantic/unknown-class.form")]
    [InlineData("forms/semantic/type-mismatch.form")]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-080")]
    [Trait("Req", "DSG-FD-081")]
    [Trait("Req", "DSG-FD-082")]
    [Trait("Req", "DSG-FD-085")]
    public void EveryChangeReturnsExactInverse(string file)
    {
        var (document, editor) = Open(file);
        var originalText = FormWriter.Write(document);
        var originalDiagnostics = document.Diagnostics.ToList();
        var random = new Random(20261120); // deterministic (TESTING.md)
        var history = new Stack<FormChange>();
        var applied = 0;
        for (var step = 0; step < 400; step++)
        {
            if (RandomChange(document, random, step) is not { } change)
            {
                continue;
            }

            var before = FormWriter.Write(document);
            var inverse = editor.Apply(change);
            var after = FormWriter.Write(document);
            var redo = editor.Apply(inverse);
            Assert.True(before == FormWriter.Write(document), $"step {step}: the inverse of {change} did not restore the text");
            history.Push(editor.Apply(redo));
            Assert.True(after == FormWriter.Write(document), $"step {step}: the redo of {change} did not restore the text");
            applied++;
        }

        Assert.True(applied > 300, $"only {applied} changes were generated");
        Assert.False(FormParser.Parse(FormWriter.Write(document), file, Catalog).HasSyntaxErrors);

        // Undoing the whole history restores the original text and the original diagnostics.
        while (history.TryPop(out var undo))
        {
            editor.Apply(undo);
        }

        Assert.Equal(originalText, FormWriter.Write(document));
        Assert.Equal(originalDiagnostics, document.Diagnostics);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-082")]
    public void RenameUpdatesComponentReferences()
    {
        const string Text = """
            object ValuesForm: TForm
              Left = 200
              Top = 120
              ActiveControl = memo1
              ClientWidth = 400
              ClientHeight = 300
              Menu = MainMenu1
              PixelsPerInch = 96
              object Memo1: TMemo
                Left = 8
                Top = 8
                Width = 185
                Height = 89
                PopupMenu = ValuesForm.Popup1
                TabOrder = 0
              end
              object Label1: TLabel
                Left = 8
                Top = 100
                Width = 34
                Height = 15
                FocusControl = Memo1
                OnClick = Memo1Click
              end
              object Label2: TLabel
                Left = 8
                Top = 120
                Width = 34
                Height = 15
                FocusControl = OtherForm.Memo1
              end
              object Popup1: TPopupMenu
                DesignLeft = 16
                DesignTop = 8
                OnPopup = Popup1Popup
              end
              object MainMenu1: TMainMenu
                DesignLeft = 48
                DesignTop = 8
              end
            end

            """;
        var (document, editor) = Open(Text);
        FormChangedEventArgs? changed = null;
        document.Changed += (_, e) => changed = e;
        var undo = new Stack<FormChange>();

        undo.Push(editor.Apply(new RenameObject("MEMO1", "Notes")));
        var memo = document.FindByName("notes")!;
        Assert.Equal("Notes", memo.Name);
        Assert.Null(document.FindByName("Memo1"));
        Assert.Equal(new IdentValue("Notes"), document.Root.FindProperty("ActiveControl")!.Value);
        Assert.Equal(new IdentValue("Notes"), document.FindByName("Label1")!.FindProperty("FocusControl")!.Value);
        Assert.Equal(new QualifiedIdentValue(["OtherForm", "Memo1"]), document.FindByName("Label2")!.FindProperty("FocusControl")!.Value); // another form
        Assert.Equal(new IdentValue("Memo1Click"), document.FindByName("Label1")!.FindProperty("OnClick")!.Value); // handlers stay
        Assert.Equal(FormChangeFlags.Name, changed!.Flags);
        Assert.Equal(["Memo1", "Notes", "ValuesForm", "Label1"], changed.ObjectNames);

        undo.Push(editor.Apply(new RenameObject("Popup1", "Context")));
        Assert.Equal(new QualifiedIdentValue(["ValuesForm", "Context"]), memo.FindProperty("PopupMenu")!.Value); // this form, qualified
        undo.Push(editor.Apply(new RenameObject("ValuesForm", "MainForm")));
        Assert.Equal(new QualifiedIdentValue(["MainForm", "Context"]), memo.FindProperty("PopupMenu")!.Value);
        Assert.Equal("MainForm", document.Root.Name);
        undo.Push(editor.Apply(new RenameObject("MainMenu1", "mainmenu1"))); // a change of casing only
        Assert.Equal(new IdentValue("mainmenu1"), document.Root.FindProperty("Menu")!.Value);
        Assert.Contains("object mainmenu1: TMainMenu", FormWriter.Write(document), StringComparison.Ordinal);

        // The inverses restore the exact text, including the reference spelled in another casing.
        while (undo.TryPop(out var change))
        {
            editor.Apply(change);
        }

        Assert.Equal(Text, FormWriter.Write(document));
        Assert.Equal("memo1", document.Root.FindProperty("ActiveControl")!.RawText);

        // References inside collection items follow too.
        var restricted = FormValidatorTests.RestrictedCatalog();
        var header = FormParser.Parse("object F: TForm\n  object Header1: THeader\n    Sections = <\n      item\n        Partner = Header1\n      end\n    >\n  end\nend\n", "h.form", restricted).Document!;
        new FormEditor(header, restricted).Apply(new RenameObject("Header1", "Top1"));
        var item = ((CollectionValue)header.FindByName("Top1")!.FindProperty("Sections")!.Value).Items.Single();
        Assert.Equal(new IdentValue("Top1"), item.Properties.Single().Value);
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-085")]
    public void MoveObjectReparentsAndReorders()
    {
        var (document, editor) = Open("forms/canonical/nested.form");
        var original = FormWriter.Write(document);
        FormChangedEventArgs? changed = null;
        document.Changed += (_, e) => changed = e;
        var options = document.FindByName("OptionsBox")!;
        var outer = document.FindByName("OuterPanel")!;

        // Same parent: z-order only; the index counts after removal from the old position.
        var undoOrder = editor.Apply(new MoveObject("FastCheck", "OptionsBox", 1));
        Assert.Equal(["SafeRadio", "FastCheck"], options.Children.Select(c => c.Name));
        Assert.Equal(new MoveObject("FastCheck", "OptionsBox", 0), undoOrder);
        Assert.Equal(FormChangeFlags.Structure | FormChangeFlags.Geometry, changed!.Flags);

        // Another parent: re-parented, its own properties untouched.
        var label = document.FindByName("InfoLabel")!;
        var undoParent = editor.Apply(new MoveObject("InfoLabel", "OptionsBox", 0));
        Assert.Same(options, label.Parent);
        Assert.Equal(["InfoLabel", "SafeRadio", "FastCheck"], options.Children.Select(c => c.Name));
        Assert.Equal(["OptionsBox"], outer.Children.Select(c => c.Name));
        Assert.Equal((216, 16), (PropertyAccess.GetInt(label, "Left"), PropertyAccess.GetInt(label, "Top")));
        Assert.Equal(new MoveObject("InfoLabel", "OuterPanel", 1), undoParent);
        Assert.Equal(["InfoLabel", "OuterPanel", "OptionsBox"], changed.ObjectNames);
        Assert.Contains("      object InfoLabel: TLabel\n        Left = 216\n", FormWriter.Write(document), StringComparison.Ordinal);

        // To the end of the root, and back.
        var undoRoot = editor.Apply(new MoveObject("OptionsBox", "NestedForm", 1));
        Assert.Equal(["OuterPanel", "OptionsBox"], document.Root.Children.Select(c => c.Name));
        editor.Apply(undoRoot);
        editor.Apply(undoParent);
        editor.Apply(undoOrder);
        Assert.Equal(original, FormWriter.Write(document));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-083")]
    public void RejectsInvalidChanges()
    {
        var (document, editor) = Open("forms/semantic/unknown-class.form");
        var text = FormWriter.Write(document);
        var version = document.Version;
        var button = new FormObjectData(FormObjectKind.Object, "Button9", "TButton", [], []);
        FormChange[] invalid =
        [
            new SetProperty("Missing", "Caption", new StringValue("x")),       // object not found
            new RemoveProperty("Missing", "Caption"),
            new RemoveObject("Missing"),
            new MoveObject("Missing", "MainForm", 0),
            new MoveObject("OkButton", "Missing", 0),
            new RenameObject("Missing", "Other"),
            new AddObject("Missing", 0, button),
            new RenameObject("OkButton", "mainform"),                          // name not unique (ASCII case-insensitive)
            new RenameObject("OkButton", "Series1"),                           // ... including preserved objects
            new AddObject("MainForm", 0, button with { Name = "OKBUTTON" }),
            new AddObject("MainForm", 0, button with { Children = [button] }), // duplicate inside the added subtree
            new RenameObject("OkButton", "1st"),                               // not an identifier
            new RenameObject("OkButton", "Ok Button"),
            new RenameObject("OkButton", ""),
            new RenameObject("OkButton", "Käse"),
            new RenameObject("OkButton", "begin"),                             // reserved word
            new AddObject("MainForm", 0, button with { Name = "end" }),
            new MoveObject("MainForm", "MainForm", 0),                         // the root cannot move or go
            new RemoveObject("MainForm"),
            new SetProperty("Chart1", "Left", new IntegerValue(1, false)),     // target preserved
            new RemoveProperty("Chart1", "Left"),
            new RenameObject("Chart1", "Chart2"),
            new AddObject("Chart1", 0, button),
            new MoveObject("OkButton", "Chart1", 0),
            new RemoveObject("Series1"),                                       // inside a preserved object
            new MoveObject("Series1", "MainForm", 0),
            new SetProperty("OkButton", "Nope", new IntegerValue(1, false)),   // property path unknown for a known class
            new SetProperty("OkButton", "Font.Nope", new IntegerValue(1, false)),
            new SetProperty("OkButton", "Caption.Size", new IntegerValue(1, false)),
            new SetProperty("OkButton", "Font", new IntegerValue(1, false)),   // an object property has no value of its own
            new RemoveProperty("OkButton", "Nope"),
            new AddObject("MainForm", 99, button),                             // index out of range
            new AddObject("MainForm", -1, button),
            new MoveObject("OkButton", "MainForm", 2),                         // 2 children, 1 after removal
        ];
        foreach (var change in invalid)
        {
            Assert.Throws<InvalidFormChangeException>(() => editor.Apply(change));
            Assert.Equal(text, FormWriter.Write(document));
            Assert.Equal(version, document.Version);
        }

        // A container cannot move into itself or its descendant.
        var (nested, nestedEditor) = Open("forms/canonical/nested.form");
        Assert.Throws<InvalidFormChangeException>(() => nestedEditor.Apply(new MoveObject("OuterPanel", "OuterPanel", 0)));
        Assert.Throws<InvalidFormChangeException>(() => nestedEditor.Apply(new MoveObject("OuterPanel", "OptionsBox", 0)));
        Assert.Equal(0, nested.Version);

        // Removing and moving a preserved object are allowed; renaming to a different casing of the own name too.
        var undoMove = editor.Apply(new MoveObject("Chart1", "MainForm", 1));
        var undoRemove = editor.Apply(new RemoveObject("Chart1"));
        Assert.Null(document.FindByName("Series1"));
        editor.Apply(undoRemove);
        editor.Apply(undoMove);
        editor.Apply(editor.Apply(new RenameObject("OkButton", "OKBUTTON")));
        Assert.Equal(text, FormWriter.Write(document));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-FD-084")]
    [Trait("Req", "DSG-FD-070")]
    public void RaisesSingleChangedEventPerBatch()
    {
        var (document, editor) = Open("forms/canonical/all-value-kinds.form");
        var original = FormWriter.Write(document);
        var events = new List<FormChangedEventArgs>();
        document.Changed += (_, e) => events.Add(e);

        // One change: one event, one version step.
        editor.Apply(new SetProperty("Memo1", "Left", new IntegerValue(10, false)));
        var single = Assert.Single(events);
        Assert.Equal((1, FormChangeFlags.Geometry), (document.Version, single.Flags));
        Assert.Equal(["Memo1"], single.ObjectNames);
        editor.Apply(new SetProperty("Edit1", "Text", new StringValue("t")));
        Assert.Equal(FormChangeFlags.Appearance, events[^1].Flags);
        editor.Apply(new SetProperty("Edit1", "OnChange", new IdentValue("TextChanged")));
        Assert.Equal(FormChangeFlags.Events, events[^1].Flags);
        events.Clear();
        var start = FormWriter.Write(document);
        var diagnosticsBefore = document.Diagnostics.ToList(); // the parser's F0102 lines of Image1

        // A batch: one event with every affected name and the union of the flags, inverses in reverse order.
        var batch = new FormChange[]
        {
            new AddObject("ValuesForm", 0, new FormObjectData(FormObjectKind.Object, "Button1", "TButton",
                [new FormPropertyData("Left", new IntegerValue(8, false)), new FormPropertyData("Caption", new StringValue("Go"))], [])),
            new SetProperty("Button1", "Caption", new StringValue("Start")),
            new MoveObject("Button1", "ValuesForm", 2),
            new RemoveObject("MainMenu1"),
        };
        var inverses = editor.Apply(batch);
        var e = Assert.Single(events);
        Assert.Equal(4, document.Version);
        Assert.Equal(["Button1", "ValuesForm", "MainMenu1"], e.ObjectNames);
        Assert.Equal(FormChangeFlags.Structure | FormChangeFlags.Geometry | FormChangeFlags.Appearance, e.Flags);
        Assert.Equal(4, inverses.Count);
        Assert.IsType<AddObject>(inverses[0]); // the inverse of the last change comes first
        Assert.IsType<RemoveObject>(inverses[3]);
        Assert.Equal("Start", ((StringValue)document.FindByName("Button1")!.FindProperty("Caption")!.Value).Value);
        Assert.Same(document.Root.Children[2], document.FindByName("Button1"));

        // The document is validated once per batch: the reference to the removed menu is now unresolved.
        Assert.Equal(
            ["test.form(17,10): warning F0105: Component MainMenu1 not found"],
            document.Diagnostics.Except(diagnosticsBefore).Select(d => d.ToString()));
        Assert.Equal(diagnosticsBefore.Count + 1, document.Diagnostics.Count);

        editor.Apply(inverses);
        Assert.Equal(2, events.Count);
        Assert.Equal(diagnosticsBefore, document.Diagnostics);
        Assert.Equal(start, FormWriter.Write(document));

        // A batch with an invalid change throws, leaves the document as it was and raises nothing.
        events.Clear();
        FormChange[] broken = [new SetProperty("Memo1", "Top", new IntegerValue(99, false)), new RemoveObject("Missing")];
        Assert.Throws<InvalidFormChangeException>(() => editor.Apply(broken));
        Assert.Empty(events);
        Assert.Equal(5, document.Version);
        Assert.Equal(start, FormWriter.Write(document));
        Assert.NotEqual(original, start);
    }

    private static readonly string[] Words = ["", "OK", "it's", "Line 1\r\nLine 2", "A string that is long enough to need a continuation line in the writer"];

    /// <summary>A random valid change for the current document, or null when the drawn kind has no candidate.</summary>
    private static FormChange? RandomChange(FormDocument document, Random random, int step)
    {
        var all = document.AllObjects().ToList();
        var editable = all.Where(o => !o.IsPreserved && o.Info is not null).ToList();
        var containers = editable.Where(o => o.Info!.AcceptsChildren).ToList();
        var movable = all.Where(o => o.Parent is { IsPreserved: false }).ToList();
        T Pick<T>(IReadOnlyList<T> list) => list[random.Next(list.Count)];

        switch (random.Next(8))
        {
            case 0 or 1 or 2:
            {
                var obj = Pick(editable);
                var candidates = new List<(string Path, FormValue Value)>
                {
                    ("Caption", new StringValue(Pick(Words))),
                    ("Hint", new StringValue(Pick(Words))),
                    ("Left", new IntegerValue(random.Next(-5, 300), false)),
                    ("Top", new IntegerValue(random.Next(0, 3), false)),
                    ("Width", new IntegerValue(random.Next(0, 300), false)),
                    ("ClientWidth", new IntegerValue(random.Next(0, 300), false)),
                    ("DesignLeft", new IntegerValue(random.Next(0, 3) * 8, false)),
                    ("Visible", new IdentValue(random.Next(2) == 0 ? "True" : "False")),
                    ("ShowHint", new IdentValue(random.Next(2) == 0 ? "True" : "False")),
                    ("ParentFont", new IdentValue(random.Next(2) == 0 ? "True" : "False")),
                    ("ParentColor", new IdentValue(random.Next(2) == 0 ? "True" : "False")),
                    ("Font.Height", new IntegerValue(random.Next(-14, -11), false)),
                    ("Font.Style", new SetValue(random.Next(2) == 0 ? [] : ["fsItalic", "fsBold"])),
                    ("Color", new IdentValue(random.Next(2) == 0 ? "clRed" : "clBtnFace")),
                    ("Tag", new IntegerValue(random.Next(0, 3), false)),
                    ("Align", new IdentValue(random.Next(2) == 0 ? "alNone" : "alTop")),
                    ("Lines", new StringListValue(random.Next(2) == 0 ? [] : ["a", "b'c"])),
                    ("PopupMenu", new IdentValue(random.Next(2) == 0 ? "nil" : Pick(all).Name)),
                    ("OnClick", new IdentValue(obj.Name + "Click")),
                    ("OnChange", new IdentValue("Changed" + step)),
                };
                var fitting = candidates.Where(c => obj.Info!.FindEvent(c.Path) is not null
                    || obj.Catalog.FindPropertyPath(obj.Info!, c.Path) is { Kind: not PropertyKind.Object }).ToList();
                if (fitting.Count == 0)
                {
                    return null;
                }

                var (path, value) = Pick(fitting);
                return new SetProperty(obj.Name, path, value);
            }

            case 3:
            {
                var stored = editable.SelectMany(o => o.Properties.Where(p => p.Info is not null || p.IsEvent).Select(p => (o, p))).ToList();
                if (stored.Count == 0)
                {
                    return null;
                }

                var (owner, property) = Pick(stored);
                return new RemoveProperty(owner.Name, property.Path);
            }

            case 4:
            {
                var parent = Pick(containers);
                var type = Pick(new[] { "TButton", "TLabel", "TPanel", "TTimer" });
                var data = new FormObjectData(FormObjectKind.Object, "New" + step, type,
                    type == "TTimer" ? [] : [new FormPropertyData("Left", new IntegerValue(step, false)), new FormPropertyData("Caption", new StringValue("n" + step))],
                    type == "TPanel" ? [new FormObjectData(FormObjectKind.Object, "Inner" + step, "TLabel", [], [])] : []);
                return new AddObject(parent.Name, random.Next(parent.Children.Count + 1), data);
            }

            case 5:
                return movable.Count < 4 ? null : new RemoveObject(Pick(movable).Name);

            case 6:
            {
                if (movable.Count == 0)
                {
                    return null;
                }

                var obj = Pick(movable);
                var targets = containers.Where(c => !IsSelfOrDescendant(c, obj)).ToList();
                var target = Pick(targets);
                var count = target.Children.Count - (ReferenceEquals(target, obj.Parent) ? 1 : 0);
                return new MoveObject(obj.Name, target.Name, random.Next(count + 1));
            }

            default:
                return new RenameObject(Pick(editable).Name, "Renamed" + step);
        }
    }

    private static bool IsSelfOrDescendant(FormObject candidate, FormObject obj)
    {
        for (var o = candidate; o is not null; o = o.Parent)
        {
            if (ReferenceEquals(o, obj))
            {
                return true;
            }
        }

        return false;
    }
}
