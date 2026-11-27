using FreexComp.Debugger.Values.TypeInfo;

namespace FreexComp.Debugger.Values.Visualizers;

/// <summary>
/// A component that, for values of the types it is registered for, replaces the plain rendering by its own one-line
/// summary, optionally its own children and optionally a text source (visualizers.md §2, §3). Internal: there is no
/// public or user registration (DBG-501).
/// </summary>
internal interface IValueVisualizer
{
    /// <summary>Stable, lower case: "string", "test.list". Once per table.</summary>
    string Id { get; }

    /// <summary>Refines the registration; reads no memory (DBG-504).</summary>
    bool Matches(PdiType type);

    /// <summary>The one-line summary; with <see cref="VisualizerContext.Nested"/> the form inside a parent's summary (DBG-507).</summary>
    Task<string> SummaryAsync(ValueNode value, VisualizerContext context, CancellationToken ct);

    /// <summary>The children of the value; null: the plain children (DBG-506). <see cref="VisualizerChildren.ChildAt"/> is called lazily.</summary>
    Task<VisualizerChildren?> ChildrenAsync(ValueNode value, VisualizerContext context, CancellationToken ct);

    /// <summary>The long text of the value for the text viewer; null: no text view.</summary>
    ITextSource? TextSource(ValueNode value, VisualizerContext context);
}

/// <summary>A long text a visualiser offers for the text viewer (members come with the text calls, task G-092).</summary>
internal interface ITextSource;

/// <summary>
/// One row of the visualiser table (DBG-501, DBG-502). The table order is the priority within a selection group.
/// </summary>
/// <param name="Visualizer">The visualiser.</param>
/// <param name="Unit">When set, the type's unit must be this one (case-insensitive).</param>
/// <param name="NamePattern">An exact type name, or a generic base followed by <c>&lt;*&gt;</c>.</param>
/// <param name="Kinds">When set, the type's kind must be one of these.</param>
/// <param name="Transparent">Summary and children are the plain ones; the framework never calls them (§2).</param>
/// <param name="SummarizesNested">The visualiser also renders the value inside a parent's summary (DBG-507).</param>
internal sealed record VisualizerRegistration(IValueVisualizer Visualizer, string? Unit, string? NamePattern,
    IReadOnlySet<PdiTypeKind>? Kinds, bool Transparent, bool SummarizesNested);

/// <summary>The children of a visualised value: their number and the child at an index, made only when asked for.</summary>
internal sealed record VisualizerChildren(int Count, Func<int, CancellationToken, Task<VisualizerChild>> ChildAt);

/// <summary>A child of a visualised value. <paramref name="EvaluateName"/> null: the row has no expression.</summary>
internal sealed record VisualizerChild(string Name, ValueNode Node, string? EvaluateName);
