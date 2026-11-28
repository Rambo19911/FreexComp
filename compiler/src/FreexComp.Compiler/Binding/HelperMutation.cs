using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Binding;

/// <summary>
/// LANG-996 (b): a write use of <c>Self</c> in the body of the record-helper method <paramref name="Method"/> (or of a routine
/// nested in it) — rules 1, 2, 3 and 5 when <paramref name="Callee"/> is null; rule 4 otherwise: a part of <c>Self</c> is the
/// receiver of the record-helper method <paramref name="Callee"/>, a write use when that method is mutating.
/// </summary>
internal readonly record struct SelfUse(MethodSymbol Method, MethodSymbol? Callee);

/// <summary>
/// LANG-996 (c): a call of the record-helper method <paramref name="Callee"/> on a read-only location or a constant;
/// <paramref name="Diagnostic"/> is its E2752, reported when the method is mutating.
/// </summary>
internal readonly record struct ReadOnlyCall(MethodSymbol Callee, Diagnostic Diagnostic);

/// <summary>
/// LANG-996 (b), (c) (C-443): which instance methods of record helpers are mutating, and the E2752 of their calls on read-only
/// values. A call may be bound before the body of the method it calls (a later body, another unit bound at the same time, a unit
/// of an implementation-uses cycle), so the binder only records what it sees (<see cref="DiagnosticBag.SelfUses"/>,
/// <see cref="DiagnosticBag.ReadOnlyCalls"/>); the classification runs once every body of the compilation is bound and the
/// diagnostics follow from it — the result does not depend on the order of declarations, bodies or units.
/// </summary>
internal static class HelperMutation
{
    /// <summary>
    /// Marks the mutating methods (<see cref="MethodSymbol.MutatesSelf"/>): the smallest set that holds every method with a
    /// direct write use and every method that calls a member of the set on a part of its <c>Self</c>. Methods read from a
    /// <c>.pcu</c> are marked already (CMP-1490 (c)). Marks are only ever added, so classifying the units of an earlier run again
    /// changes nothing.
    /// </summary>
    public static void Classify(IEnumerable<BindingResult> units)
    {
        var callers = new Dictionary<MethodSymbol, List<MethodSymbol>>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<MethodSymbol>();
        foreach (var use in units.SelectMany(u => u.SelfUses))
        {
            if (use.Callee is null)
            {
                if (!use.Method.MutatesSelf)
                {
                    use.Method.MutatesSelf = true;
                    pending.Push(use.Method);
                }
            }
            else
            {
                if (!callers.TryGetValue(use.Callee, out var list))
                {
                    callers[use.Callee] = list = [];
                }

                list.Add(use.Method);
            }
        }

        foreach (var callee in callers.Keys.Where(c => c.MutatesSelf))
        {
            pending.Push(callee);                                           // marked by an earlier run, or read from a .pcu
        }

        while (pending.Count > 0)
        {
            if (!callers.TryGetValue(pending.Pop(), out var list))
            {
                continue;
            }

            foreach (var caller in list.Where(c => !c.MutatesSelf))
            {
                caller.MutatesSelf = true;
                pending.Push(caller);
            }
        }
    }

    /// <summary>
    /// The E2752 diagnostics of <paramref name="unit"/> after <see cref="Classify"/>: one per call of a mutating method on a
    /// read-only value (a call bound twice is reported once), in file and position order.
    /// </summary>
    public static ImmutableArray<Diagnostic> Errors(BindingResult unit)
    {
        var seen = new HashSet<(string Path, int Start, string Code)>();
        return
        [
            .. unit.ReadOnlyCalls.Where(c => c.Callee.MutatesSelf).Select(c => c.Diagnostic)
                .Where(d => seen.Add((d.Location.Path.ToUpperInvariant(), d.Location.Span.Start, d.Code)))
                .OrderBy(d => d.Location.Path, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Location.Span.Start),
        ];
    }

    /// <summary><paramref name="unit"/> with <see cref="Errors"/> among its diagnostics (file and position order).</summary>
    public static BindingResult WithErrors(BindingResult unit, ImmutableArray<Diagnostic> errors) =>
        errors.IsEmpty
            ? unit
            : unit with { Diagnostics = [.. unit.Diagnostics.Concat(errors).OrderBy(d => d.Location.Path, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Location.Span.Start)] };

    /// <summary>
    /// What the classification of other units' methods can depend on in <paramref name="unit"/>: its write uses by method, as
    /// text (CMP-130: a later snapshot takes other units over only while this is unchanged).
    /// </summary>
    public static string Facts(BindingResult unit) =>
        string.Join("\n", unit.SelfUses.Select(u => Name(u.Method) + (u.Callee is null ? "" : " -> " + Name(u.Callee))).Distinct().Order(StringComparer.Ordinal));

    private static string Name(MethodSymbol method) =>
        (method.ContainingUnit?.Name ?? "") + "." + method.Owner.Name + "." + method.Name + "#" + Math.Max(0, TypeMembers.Methods(method.Owner).IndexOf(method));
}
