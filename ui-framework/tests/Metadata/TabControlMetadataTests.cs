using System.Text.Json;
using FreexComp.MetadataTesting;

namespace FreexComp.UiFramework.Tests;

/// <summary>
/// U-178 (phase8/tab-control.md §8, UIF-1519): the design metadata of the tab controls in <c>ComCtrls.components.json</c> —
/// the type <c>TTabStyle</c>, the two event signatures and the units of their types, the <c>TPageControl</c> additions with
/// their categories and <c>affectsLayout</c>, and the component <c>TTabControl</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TabControlMetadataTests
{
    private static MetadataFile ComCtrls()
    {
        var file = MetadataRoot.Load(MetadataTests.DesignDirectory).FindFile("ComCtrls");
        Assert.NotNull(file);
        Assert.Equal("ComCtrls.components.json", file.Name);
        return file;
    }

    private static JsonElement Row(IEnumerable<JsonElement> rows, string name) =>
        Assert.Single(rows, r => r.GetProperty("name").GetString() == name);

    private static string? Text(JsonElement row, string key) =>
        row.TryGetProperty(key, out var v) ? v.ToString() : null;

    private static bool Flag(JsonElement row, string key) =>
        row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    [Fact]
    [Trait("Req", "UIF-1519")]
    public void TabStyleTypeAndEventSignatures()
    {
        var file = ComCtrls();

        var style = Assert.Single(file.Types, t => t.Name == "TTabStyle").Value;
        Assert.Equal("enum", style.GetProperty("kind").GetString());
        Assert.Equal(["tsTabs", "tsButtons", "tsFlatButtons"], style.GetProperty("values").EnumerateArray().Select(v => v.GetString()));

        Assert.Equal(
            "procedure(Control: TCustomTabControl; TabIndex: Integer; const Rect: TRect; Active: Boolean)",
            Assert.Single(file.EventSignatures, s => s.Name == "TDrawTabEvent").Value.GetString());
        Assert.Equal(
            "procedure(Sender: TObject; TabIndex: Integer; var ImageIndex: Integer)",
            Assert.Single(file.EventSignatures, s => s.Name == "TTabGetImageEvent").Value.GetString());

        Assert.Equal("ComCtrls", Assert.Single(file.EventSignatureUnits, u => u.Name == "TCustomTabControl").Value.GetString());
        Assert.Equal("Types", Assert.Single(file.EventSignatureUnits, u => u.Name == "TRect").Value.GetString());
    }

    /// <summary>Name, type, default, category, affectsLayout of the rows UIF-1519 adds to or changes in <c>TPageControl</c>.</summary>
    public static readonly TheoryData<string, string, string, string, bool> PageControlRows = new()
    {
        { "OwnerDraw", "Boolean", "False", "Appearance", false },
        { "RaggedRight", "Boolean", "False", "Appearance", false },
        { "ScrollOpposite", "Boolean", "False", "Behavior", true },
        { "Style", "TTabStyle", "tsTabs", "Appearance", true },
        { "MultiLine", "Boolean", "False", "Behavior", true },
        { "TabPosition", "TTabPosition", "tpTop", "Behavior", true },
    };

    [Theory]
    [Trait("Req", "UIF-1519")]
    [MemberData(nameof(PageControlRows))]
    public void PageControlRow(string name, string type, string defaultValue, string category, bool affectsLayout)
    {
        var page = MetadataRoot.Load(MetadataTests.DesignDirectory).Find("TPageControl");
        Assert.NotNull(page);

        var row = Row(page.Properties, name);
        Assert.Equal(type, Text(row, "type"));
        Assert.Equal(defaultValue, Text(row, "default"));
        Assert.Equal(category, Text(row, "category"));
        Assert.Equal(affectsLayout, Flag(row, "affectsLayout"));
    }

    [Fact]
    [Trait("Req", "UIF-1519")]
    public void PageControlEvents()
    {
        var page = MetadataRoot.Load(MetadataTests.DesignDirectory).Find("TPageControl");
        Assert.NotNull(page);

        Assert.Equal("TDrawTabEvent", Text(Row(page.Events, "OnDrawTab"), "signature"));
        Assert.Equal("TTabGetImageEvent", Text(Row(page.Events, "OnGetImageIndex"), "signature"));
    }

    /// <summary>
    /// <c>TTabControl</c>: a container on <c>TCustomTabControl</c> without child classes or designer verbs, on the Win32 page before
    /// <c>TPageControl</c>; its properties are those of <c>TPageControl</c> without <c>ActivePage</c> plus <c>Tabs</c> and
    /// <c>TabIndex</c> (UIF-1505), its events exactly those of <c>TPageControl</c>.
    /// </summary>
    [Fact]
    [Trait("Req", "UIF-1519")]
    public void TabControlEntry()
    {
        var root = MetadataRoot.Load(MetadataTests.DesignDirectory);
        var tab = root.Find("TTabControl");
        var page = root.Find("TPageControl");
        Assert.NotNull(tab);
        Assert.NotNull(page);

        Assert.Equal("ComCtrls.components.json", tab.File.Name);
        Assert.Equal("TCustomTabControl", tab.Ancestor);
        Assert.False(tab.Abstract);
        Assert.Equal("container", Text(tab.Json, "kind"));
        Assert.True(Flag(tab.Json, "acceptsChildren"));
        Assert.False(tab.Json.TryGetProperty("childClasses", out _));
        Assert.False(tab.Json.TryGetProperty("designerVerbs", out _));
        var palette = tab.Json.GetProperty("palette");
        Assert.Equal("Win32", Text(palette, "page"));
        Assert.Equal(5, palette.GetProperty("order").GetInt32());
        Assert.Equal("icons/TTabControl.svg", Text(palette, "icon"));
        Assert.True(palette.GetProperty("order").GetInt32() < page.Json.GetProperty("palette").GetProperty("order").GetInt32());
        Assert.True(File.Exists(Path.Combine(MetadataTests.DesignDirectory, "icons", "TTabControl.svg")));
        Assert.Equal(289, tab.Json.GetProperty("defaultSize").GetProperty("width").GetInt32());
        Assert.Equal(193, tab.Json.GetProperty("defaultSize").GetProperty("height").GetInt32());
        Assert.Equal("TabControl", Text(tab.Json, "namePrefix"));
        Assert.Equal("tabcontrol", Text(tab.Json, "renderer"));

        var tabs = Row(tab.Properties, "Tabs");
        Assert.Equal("TStrings", Text(tabs, "type"));
        Assert.Equal("strings", Text(tabs, "editor"));
        Assert.Equal("Data", Text(tabs, "category"));
        Assert.True(Flag(tabs, "localizable"));
        Assert.True(Flag(tabs, "affectsLayout"));

        var index = Row(tab.Properties, "TabIndex");
        Assert.Equal("Integer", Text(index, "type"));
        Assert.Equal(-1, index.GetProperty("default").GetInt32());
        Assert.Equal(-1, index.GetProperty("min").GetInt32());
        Assert.Equal("Data", Text(index, "category"));

        Assert.Equal(
            page.Properties.Select(p => p.GetProperty("name").GetString()!).Where(n => n != "ActivePage").Append("Tabs").Append("TabIndex").Order(StringComparer.Ordinal),
            tab.Properties.Select(p => p.GetProperty("name").GetString()!).Order(StringComparer.Ordinal));
        foreach (var row in page.Properties.Where(p => p.GetProperty("name").GetString() != "ActivePage"))
        {
            Assert.Equal(row.GetRawText(), Row(tab.Properties, row.GetProperty("name").GetString()!).GetRawText());
        }

        Assert.Equal(page.Events.Select(e => e.GetRawText()), tab.Events.Select(e => e.GetRawText()));
        Assert.True(Flag(Row(tab.Events, "OnChange"), "default"));
    }
}
