using System.Collections.Frozen;

namespace FreexComp.Ide.Core.Composition;

/// <summary>
/// The phase-8 items that work (phase8-delivery.md, IDE-P8-001…006). Phase 8 is delivered card by card, so
/// <see cref="ServiceGraph.ImplementedPhase"/> stays 7 until the backlog is empty; this list is the only place that
/// says which catalogued phase-8 command, Options page or field, tool window, toolbar or setting is treated as
/// implemented. Every phase comparison of such an item goes through <see cref="IsAvailable"/>.
/// </summary>
public static class Phase8Features
{
    /// <summary>
    /// IDE-P8-002: a command id as catalogued; <c>options:&lt;PageId&gt;</c>; <c>options:&lt;PageId&gt;.&lt;FieldId&gt;</c>;
    /// <c>toolwindow:&lt;Id&gt;</c>; <c>toolbar:&lt;Id&gt;</c>; <c>setting:&lt;key&gt;</c>. Ordinal order, one id per line, the
    /// card id as a trailing comment. A command in the list needs its real handler in the same change (IDE-P8-004).
    /// </summary>
    internal static readonly IReadOnlyList<string> DeliveredIds =
    [
    ];

    private static readonly FrozenSet<string> Shipped = DeliveredIds.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The set of a test scope (<see cref="WithDelivered"/>); it follows the test's own execution flow only.</summary>
    private static readonly AsyncLocal<IReadOnlySet<string>?> Scoped = new();

    /// <summary>The delivered ids (ordinal comparison).</summary>
    public static IReadOnlySet<string> Delivered => Scoped.Value ?? Shipped;

    /// <summary>
    /// IDE-P8-003: whether the item <paramref name="id"/> of phase <paramref name="phase"/> is treated as implemented by a
    /// composition of <paramref name="implementedPhase"/> (the value tests may lower: below 7 no phase-8 item is available).
    /// </summary>
    public static bool IsAvailable(string id, int phase, int implementedPhase) =>
        phase <= implementedPhase || (phase == 8 && implementedPhase >= 7 && Delivered.Contains(id));

    /// <summary>Test seam: until the result is disposed, <see cref="Delivered"/> is exactly <paramref name="ids"/> for the calling flow.</summary>
    internal static IDisposable WithDelivered(params string[] ids)
    {
        var scope = new Scope(Scoped.Value);
        Scoped.Value = ids.ToFrozenSet(StringComparer.Ordinal);
        return scope;
    }

    private sealed class Scope(IReadOnlySet<string>? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }
}
