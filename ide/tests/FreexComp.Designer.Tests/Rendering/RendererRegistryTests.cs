using FreexComp.Designer.Rendering;

namespace FreexComp.Designer.Tests.Rendering;

/// <summary><see cref="RendererRegistry"/> (design-renderers.md §2, DSG-RND-008; D-022).</summary>
public sealed class RendererRegistryTests
{
    internal static readonly string[] RendererIds =
    [
        "form", "label", "edit", "memo", "button", "checkbox", "radiobutton", "listbox", "combobox", "groupbox", "panel",
        "image", "nonvisual", "generic", "unknown",
    ];

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

        // Every renderer of design-renderers.md §4.1 … 4.15 resolves to itself (the §4.16 ids follow with D-093).
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
