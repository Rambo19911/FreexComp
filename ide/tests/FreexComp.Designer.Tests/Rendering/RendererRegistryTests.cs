using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Tests.Rendering;

/// <summary><see cref="RendererRegistry"/> (design-renderers.md §2, DSG-RND-008; D-022).</summary>
public sealed class RendererRegistryTests
{
    internal static readonly string[] RendererIds =
    [
        "form", "label", "edit", "memo", "button", "checkbox", "radiobutton", "listbox", "combobox", "groupbox", "panel",
        "image", "nonvisual", "generic", "unknown",
        "scrollbar", "statictext", "shape", "bevel", "splitter", "scrollbox", "pagecontrol", "tabcontrol", "tabsheet", "stringgrid",
        "trackbar", "progressbar", "treeview", "listview", "statusbar", "toolbar", "toolbutton",
        "dbgrid", "dbnavigator", // Phase 7 (data design-time.md DAT-339)
    ];

    /// <summary>
    /// D-093: every renderer id the shipped framework metadata names has its own renderer (design-renderers.md §4.16);
    /// none falls back to <c>generic</c> unless it is <c>generic</c>.
    /// </summary>
    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-008")]
    [Trait("Req", "DSG-RND-017")]
    [Trait("Req", "DSG-RND-022")]
    public void AllFrameworkRendererIdsAreRegistered()
    {
        var registry = RendererRegistry.CreateDefault();
        var ids = Layout.LayoutHarness.Catalog.All.Select(c => c.RendererId).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Contains("pagecontrol", ids);
        Assert.Contains("tabcontrol", ids); // DSG-RND-022 (U-173)
        Assert.IsType<TabControlRenderer>(registry.Resolve("tabcontrol"));
        Assert.True(ids.Count >= 30, "framework metadata renderer ids: " + string.Join(", ", ids));
        Assert.Equal(ids, ids.Select(id => registry.Resolve(id).Id));
    }

    /// <summary>DSG-RND-019: the <c>frame</c> id of the frame class's metadata resolves to its own renderer (before U-154 lands).</summary>
    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "DSG-RND-019")]
    public void FrameRendererRegistered()
    {
        var registry = RendererRegistry.CreateDefault();

        Assert.Contains("frame", registry.Ids);
        Assert.Equal("frame", registry.Resolve("frame").Id);
        Assert.Equal("frame", registry.Resolve(Support.FrameFixtures.Components.Find("TFrame")!.RendererId).Id);
        Assert.IsNotType<GenericRenderer>(registry.Resolve("frame"));
    }

    [Fact]
    [Trait("Phase", "5")]
    [Trait("Req", "DSG-RND-008")]
    public void UnknownIdFallsBackToGeneric()
    {
        var registry = RendererRegistry.CreateDefault();

        Assert.Same(RendererRegistry.Generic, registry.Resolve(null));
        Assert.Same(RendererRegistry.Generic, registry.Resolve("no-such-renderer"));
        Assert.Same(RendererRegistry.Generic, registry.Resolve("BUTTON")); // ids are exact
        Assert.Equal("generic", registry.Resolve("generic").Id);
        Assert.Equal("unknown", registry.Resolve("unknown").Id);

        // Every renderer of design-renderers.md §4.1 … 4.15 resolves to itself and §4.16.
        foreach (var id in RendererIds)
        {
            Assert.Equal(id, registry.Resolve(id).Id);
        }

        // A later registration replaces the earlier one; an empty registry still has the fallback.
        var replacement = new FixedIdRenderer("button");
        registry.Register(replacement);
        Assert.Same(replacement, registry.Resolve("button"));
        Assert.Same(RendererRegistry.Generic, new RendererRegistry().Resolve("button"));
    }

    private sealed class FixedIdRenderer(string id) : IDesignRenderer
    {
        public string Id => id;

        public void Render(RenderContext ctx, IDesignCanvas canvas)
        {
        }

        public Rect GetClientRect(RenderContext ctx) => ctx.ClientRect;

        public int? GetBaseline(RenderContext ctx) => null;
    }
}
