using FreexComp.MetadataTesting;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// U-138 (component-metadata.md §5, DSG-CS-055): the optional file key <c>eventSignatureUnits</c> of the design metadata names, for every
/// non-system type used in an <c>eventSignatures</c> text of that file, the unit whose interface really declares the type.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DesignMetadataTests
{
    [Fact]
    public void EventSignatureTypesHaveUnits()
    {
        var problems = MetadataDecls.EventSignatureTypesHaveUnits(MetadataRoot.Load(MetadataTests.DesignDirectory), MetadataTests.Source.Value);

        Assert.True(problems.Count == 0, MetadataMatch.Report(problems));
    }

    [Fact]
    public void SignatureTypesSkipSystemTypes()
    {
        var types = MetadataDecls.SignatureTypes("procedure(Sender: TObject; var Key: Word; const S: string; Shift: TShiftState; X, Y: Integer)");

        Assert.Equal(["TShiftState"], types);
    }

    /// <summary>U-151 (UIF-1378): <c>TAction</c> lists <c>SecondaryShortCuts</c> as a string-list property of the category Action.</summary>
    [Fact]
    [Trait("Req", "UIF-1378")]
    public void ActionHasSecondaryShortCutsRow()
    {
        var action = MetadataRoot.Load(MetadataTests.DesignDirectory).Find("TAction");

        Assert.NotNull(action);
        var row = Assert.Single(action.Properties, p => p.GetProperty("name").GetString() == "SecondaryShortCuts");
        Assert.Equal("TStrings", row.GetProperty("type").GetString());
        Assert.Equal("Action", row.GetProperty("category").GetString());
    }
    /// <summary>
    /// U-154 (UIF-1395, frames): <c>TCustomFrame</c> is abstract on <c>TScrollingWinControl</c>; <c>TFrame</c> is a container with the
    /// renderer <c>frame</c>, 320 x 240 and no palette entry (the designer offers frames itself), and it lists exactly the properties
    /// and events of <c>TScrollBox</c> without <c>BorderStyle</c> (UIF-1391), with the same rows.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1395")]
    public void FrameEntries()
    {
        var root = MetadataRoot.Load(MetadataTests.DesignDirectory);
        var custom = root.Find("TCustomFrame");
        var frame = root.Find("TFrame");
        var box = root.Find("TScrollBox");

        Assert.NotNull(custom);
        Assert.NotNull(frame);
        Assert.NotNull(box);
        Assert.True(custom.Abstract);
        Assert.Equal("TScrollingWinControl", custom.Ancestor);
        Assert.Equal("Forms.components.json", custom.File.Name);
        Assert.False(frame.Abstract);
        Assert.Equal("TCustomFrame", frame.Ancestor);
        Assert.Equal("container", frame.Json.GetProperty("kind").GetString());
        Assert.True(frame.Json.GetProperty("acceptsChildren").GetBoolean());
        Assert.Equal("frame", frame.Json.GetProperty("renderer").GetString());
        Assert.Equal(320, frame.Json.GetProperty("defaultSize").GetProperty("width").GetInt32());
        Assert.Equal(240, frame.Json.GetProperty("defaultSize").GetProperty("height").GetInt32());
        Assert.False(frame.Json.TryGetProperty("palette", out _));
        Assert.Equal(
            box.Properties.Where(p => p.GetProperty("name").GetString() != "BorderStyle").Select(p => p.GetRawText()),
            frame.Properties.Select(p => p.GetRawText()));
        Assert.Equal(box.Events.Select(e => e.GetRawText()), frame.Events.Select(e => e.GetRawText()));
    }

    /// <summary>standard-actions.md §6 table: class, caption, shortcut value (0 = none), hint, category.</summary>
    public static readonly TheoryData<string, string, int, string, string> StandardActions = new()
    {
        { "TEditCut", "Cu&t", 16472, "Cut", "Edit" },
        { "TEditCopy", "&Copy", 16451, "Copy", "Edit" },
        { "TEditPaste", "&Paste", 16470, "Paste", "Edit" },
        { "TEditSelectAll", "Select &All", 16449, "Select all", "Edit" },
        { "TEditUndo", "&Undo", 16474, "Undo", "Edit" },
        { "TEditDelete", "&Delete", 46, "Delete", "Edit" },
        { "TFileOpen", "&Open…", 16463, "Open a file", "File" },
        { "TFileSaveAs", "Save &As…", 0, "Save under another name", "File" },
        { "TFileExit", "E&xit", 0, "Close the application", "File" },
    };

    /// <summary>
    /// U-152, U-153 (UIF-1379): a standard action is a non-visual child of an action list, not on the palette, named after its class;
    /// the edit actions descend from the abstract <c>TEditAction</c>, which adds the component reference <c>Control</c>; the two
    /// dialog actions add their three events, <c>OnAccept</c> being the default one.
    /// </summary>
    [Theory]
    [Trait("Req", "UIF-1379")]
    [MemberData(nameof(StandardActions))]
    public void StandardActionEntry(string className, string caption, int shortCut, string hint, string category)
    {
        _ = (caption, shortCut, hint, category);
        var root = MetadataRoot.Load(MetadataTests.DesignDirectory);
        var entry = root.Find(className);

        Assert.NotNull(entry);
        Assert.Equal("StdActns.components.json", entry.File.Name);
        Assert.False(entry.Abstract);
        Assert.Equal("nonvisual", entry.Json.GetProperty("kind").GetString());
        Assert.Equal("nonvisual", entry.Json.GetProperty("renderer").GetString());
        Assert.Equal(["TActionList"], entry.Json.GetProperty("parentClasses").EnumerateArray().Select(x => x.GetString()));
        Assert.False(entry.Json.TryGetProperty("palette", out _));
        Assert.Equal(className[1..], entry.Json.GetProperty("namePrefix").GetString());
        var chain = root.Chain(entry).Select(c => c.Class).ToArray();
        string[] expected = className.StartsWith("TEdit", StringComparison.Ordinal)
            ? [className, "TEditAction", "TAction", "TCustomAction", "TContainedAction", "TBasicAction"]
            : [className, "TAction", "TCustomAction", "TContainedAction", "TBasicAction"];
        Assert.Equal(expected, chain);
        var names = root.Effective(entry, "properties").Select(p => p["name"].GetString()).ToArray();
        var action = root.Effective(root.Find("TAction")!, "properties").Select(p => p["name"].GetString()).ToArray();
        Assert.Equal(className.StartsWith("TEdit", StringComparison.Ordinal) ? [.. action, "Control"] : action, names);
        string[] events = [.. root.Effective(entry, "events").Select(e => e["name"].GetString()!)];
        var defaultEvent = root.Effective(entry, "events").Last(e => e.TryGetValue("default", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.True)["name"].GetString();
        if (className is "TFileOpen" or "TFileSaveAs")
        {
            Assert.Equal(["OnExecute", "OnHint", "OnUpdate", "BeforeExecute", "OnAccept", "OnCancel"], events);
            Assert.Equal("OnAccept", defaultEvent);
        }
        else
        {
            Assert.Equal(["OnExecute", "OnHint", "OnUpdate"], events);
            Assert.Equal("OnExecute", defaultEvent);
        }
    }

    /// <summary>U-152 (UIF-1379): <c>TEditAction</c> is abstract and declares <c>Control</c>; the file follows the files of the units it uses.</summary>
    [Fact]
    [Trait("Req", "UIF-1379")]
    public void StandardActionsFileAndBase()
    {
        var root = MetadataRoot.Load(MetadataTests.DesignDirectory);
        var files = root.Files.Select(f => f.Name).ToList();
        var at = files.IndexOf("StdActns.components.json");

        Assert.True(at > files.IndexOf("ActnList.components.json"));
        Assert.True(at > files.IndexOf("StdCtrls.components.json"));
        Assert.True(at > files.IndexOf("Dialogs.components.json"));
        var edit = root.Find("TEditAction");
        Assert.NotNull(edit);
        Assert.True(edit.Abstract);
        Assert.Equal("TAction", edit.Ancestor);
        var control = Assert.Single(edit.Properties);
        Assert.Equal("Control", control.GetProperty("name").GetString());
        Assert.Equal("TCustomEdit", control.GetProperty("type").GetString());
        Assert.Equal("component", control.GetProperty("kind").GetString());
    }

    /// <summary>
    /// U-152, U-153 (UIF-1380): the texts a new standard action receives in the designer are the <c>initial</c> values of the table; a
    /// shortcut is written as its number, the form in which the designer converts a <c>TShortCut</c>.
    /// </summary>
    [Theory]
    [Trait("Req", "UIF-1380")]
    [MemberData(nameof(StandardActions))]
    public void StandardActionInitialValues(string className, string caption, int shortCut, string hint, string category)
    {
        var root = MetadataRoot.Load(MetadataTests.DesignDirectory);
        var entry = root.Find(className);

        Assert.NotNull(entry);
        var initial = root.Effective(entry, "properties")
            .Where(p => p.ContainsKey("initial"))
            .ToDictionary(p => p["name"].GetString()!, p => p["initial"].GetString(), StringComparer.Ordinal);
        var expected = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Caption"] = caption, ["Category"] = category, ["Hint"] = hint };
        if (shortCut != 0)
        {
            expected["ShortCut"] = shortCut.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        Assert.Equal(expected.OrderBy(x => x.Key, StringComparer.Ordinal), initial.OrderBy(x => x.Key, StringComparer.Ordinal));
        var types = root.Effective(entry, "properties").ToDictionary(p => p["name"].GetString()!, p => p["type"].GetString(), StringComparer.Ordinal);
        Assert.Equal("string", types["Caption"]);
        Assert.Equal("string", types["Category"]);
        Assert.Equal("string", types["Hint"]);
        Assert.Equal("TShortCut", types["ShortCut"]);
    }
}
