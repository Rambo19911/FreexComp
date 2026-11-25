namespace FreexComp.Designer.Rendering;

/// <summary>Draws one kind of component, selected by the metadata <c>renderer</c> id (design-renderers.md §2).</summary>
public interface IDesignRenderer
{
    /// <summary>The metadata <c>renderer</c> value.</summary>
    string Id { get; }

    /// <summary>Draws within (0, 0, Width, Height); the canvas origin is the control's top-left. Never throws.</summary>
    void Render(RenderContext ctx, IDesignCanvas canvas);

    /// <summary>The children's area: <see cref="RenderContext.ClientRect"/> (DSG-RND-004).</summary>
    Rect GetClientRect(RenderContext ctx);

    /// <summary>The y offset of the text baseline from the control's top, or null.</summary>
    int? GetBaseline(RenderContext ctx);
}

/// <summary>Renderers whose control can size itself to its text (DSG-RND-016).</summary>
public interface IAutoSizeRenderer
{
    Size GetPreferredSize(RenderContext ctx, IDesignCanvas canvas);
}

/// <summary>Renderer lookup by id; unknown or missing ids resolve to the generic renderer (DSG-RND-008).</summary>
public sealed class RendererRegistry
{
    private readonly Dictionary<string, IDesignRenderer> _renderers = new(StringComparer.Ordinal);

    public RendererRegistry() => Register(Generic);

    /// <summary>The fallback renderer (§4.14).</summary>
    public static IDesignRenderer Generic { get; } = new GenericRenderer();

    /// <summary>A registry with every built-in renderer of this phase.</summary>
    public static RendererRegistry CreateDefault()
    {
        var registry = new RendererRegistry();
        IDesignRenderer[] builtIn =
        [
            new UnknownRenderer(), new FormRenderer(), new LabelRenderer(), new ButtonRenderer(), new EditRenderer(),
            new MemoRenderer(), new CheckBoxRenderer(), new RadioButtonRenderer(), new ListBoxRenderer(),
            new ComboBoxRenderer(), new GroupBoxRenderer(), new PanelRenderer(), new ImageRenderer(), new NonVisualRenderer(),
            new ScrollBarRenderer(), new StaticTextRenderer(), new ShapeRenderer(), new BevelRenderer(), new SplitterRenderer(),
            new ScrollBoxRenderer(), new PageControlRenderer(), new TabSheetRenderer(), new StringGridRenderer(),
            new TrackBarRenderer(), new ProgressBarRenderer(), new TreeViewRenderer(), new ListViewRenderer(),
            new StatusBarRenderer(), new ToolBarRenderer(), new ToolButtonRenderer(),
            new DbGridRenderer(), new DbNavigatorRenderer(), // Phase 7 (data design-time.md DAT-339)
            new FrameRenderer(), // Phase 8 (frames-designer.md DSG-RND-019)
        ];
        foreach (var renderer in builtIn)
        {
            registry.Register(renderer);
        }

        return registry;
    }

    /// <summary>The registered ids in registration order.</summary>
    public IEnumerable<string> Ids => _renderers.Keys;

    /// <summary>Adds a renderer; a later registration of the same id replaces the earlier one.</summary>
    public void Register(IDesignRenderer renderer) => _renderers[renderer.Id] = renderer;

    public IDesignRenderer Resolve(string? rendererId) =>
        rendererId is not null && _renderers.TryGetValue(rendererId, out var renderer) ? renderer : Generic;
}
