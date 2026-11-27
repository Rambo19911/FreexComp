namespace FreexComp.Ide.Core.Diff;

/// <summary>IDE-DIFF-009: counts the steps of a diff and checks the cancellation token every 1 000 of them.</summary>
internal sealed class DiffSteps(CancellationToken cancellationToken)
{
    public const int CheckInterval = 1000;

    private int _count;

    public void Step()
    {
        if (++_count >= CheckInterval)
        {
            _count = 0;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}

/// <summary>
/// IDE-DIFF-003: diff of two sequences of keys (equal key = equal element). The result is a mark per element:
/// <c>removed[i]</c> for an element of <c>a</c> without a partner, <c>added[j]</c> for one of <c>b</c>; the unmarked
/// elements of both, in order, are pairwise equal (a common subsequence).
/// </summary>
internal static class SequenceDiff
{
    /// <summary>Recursion depth after which <see cref="Patience"/> diffs a gap with Myers (bounds the stack).</summary>
    private const int MaxPatienceDepth = 64;

    /// <summary>
    /// Myers: the common prefix and suffix are trimmed, then the middle snake of the O((N+M)·D) greedy algorithm (searched
    /// from both ends, linear space) splits the range and both halves are diffed recursively. Of two equal choices the
    /// forward search takes the deletion. Minimal: the marked elements are N + M − 2·LCS.
    /// </summary>
    public static void Myers(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, bool[] removed, bool[] added, DiffSteps steps)
    {
        while (true)
        {
            while (aLo < aHi && bLo < bHi && a[aLo] == b[bLo])
            {
                aLo++;
                bLo++;
                steps.Step();
            }

            while (aLo < aHi && bLo < bHi && a[aHi - 1] == b[bHi - 1])
            {
                aHi--;
                bHi--;
                steps.Step();
            }

            if (aLo == aHi || bLo == bHi)
            {
                Array.Fill(removed, true, aLo, aHi - aLo);
                Array.Fill(added, true, bLo, bHi - bLo);
                return;
            }

            var (x, y) = MiddleSnake(a, aLo, aHi - aLo, b, bLo, bHi - bLo, steps);
            if (x < 0)
            {
                Array.Fill(removed, true, aLo, aHi - aLo);
                Array.Fill(added, true, bLo, bHi - bLo);
                return;
            }

            Myers(a, aLo, aLo + x, b, bLo, bLo + y, removed, added, steps);
            (aLo, bLo) = (aLo + x, bLo + y); // the second half: loop instead of recursing
        }
    }

    /// <summary>
    /// A point (relative to the range) on an optimal edit path, where the forward and the reverse furthest-reaching paths
    /// meet; (−1, −1) when the ranges have nothing in common. Both ranges are non-empty and start and end with different
    /// elements, so the edit distance is at least 2 and the point lies strictly inside the path.
    /// </summary>
    private static (int X, int Y) MiddleSnake(int[] a, int aLo, int n, int[] b, int bLo, int m, DiffSteps steps)
    {
        var maxD = (n + m + 1) / 2;
        var offset = maxD;
        var length = 2 * maxD + 2;
        var forward = new int[length];
        var reverse = new int[length];
        Array.Fill(forward, -1);
        Array.Fill(reverse, -1);
        forward[offset + 1] = 0;
        reverse[offset + 1] = 0;
        var delta = n - m;
        var odd = (delta & 1) != 0; // odd: the paths meet in the forward pass, even: in the reverse pass
        int forwardStart = 0, forwardEnd = 0, reverseStart = 0, reverseEnd = 0;
        for (var d = 0; d < maxD; d++)
        {
            for (var k = -d + forwardStart; k <= d - forwardEnd; k += 2)
            {
                steps.Step();
                var ko = offset + k;
                var x = k == -d || (k != d && forward[ko - 1] < forward[ko + 1]) ? forward[ko + 1] : forward[ko - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[aLo + x] == b[bLo + y])
                {
                    x++;
                    y++;
                    steps.Step();
                }

                forward[ko] = x;
                if (x > n)
                    forwardEnd += 2; // ran off the right edge
                else if (y > m)
                    forwardStart += 2; // ran off the bottom edge
                else if (odd)
                {
                    var ro = offset + delta - k;
                    if (ro >= 0 && ro < length && reverse[ro] != -1 && x >= n - reverse[ro])
                        return (x, y);
                }
            }

            for (var k = -d + reverseStart; k <= d - reverseEnd; k += 2)
            {
                steps.Step();
                var ko = offset + k;
                var x = k == -d || (k != d && reverse[ko - 1] < reverse[ko + 1]) ? reverse[ko + 1] : reverse[ko - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[aLo + n - x - 1] == b[bLo + m - y - 1])
                {
                    x++;
                    y++;
                    steps.Step();
                }

                reverse[ko] = x;
                if (x > n)
                    reverseEnd += 2;
                else if (y > m)
                    reverseStart += 2;
                else if (!odd)
                {
                    var fo = offset + delta - k;
                    if (fo >= 0 && fo < length && forward[fo] != -1)
                    {
                        var fx = forward[fo];
                        var fy = offset + fx - fo;
                        if (fx >= n - x)
                            return (fx, fy);
                    }
                }
            }
        }

        return (-1, -1);
    }

    /// <summary>
    /// Patience: the elements occurring exactly once on each side form anchor pairs (no prefix / suffix trimming first:
    /// a common last line must not pull a moved block apart); the longest increasing subsequence of their right positions (patience sorting; of equal lengths the
    /// one ending in the last pile's top) is matched, and each gap between anchors is diffed recursively. A range without
    /// such an element is diffed with <see cref="Myers"/>.
    /// </summary>
    public static void Patience(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, bool[] removed, bool[] added, DiffSteps steps, int depth = 0)
    {
        if (aLo == aHi || bLo == bHi)
        {
            Array.Fill(removed, true, aLo, aHi - aLo);
            Array.Fill(added, true, bLo, bHi - bLo);
            return;
        }

        var anchors = depth < MaxPatienceDepth ? Anchors(a, aLo, aHi, b, bLo, bHi, steps) : [];
        if (anchors.Count == 0)
        {
            Myers(a, aLo, aHi, b, bLo, bHi, removed, added, steps);
            return;
        }

        foreach (var (i, j) in anchors)
        {
            Patience(a, aLo, i, b, bLo, j, removed, added, steps, depth + 1);
            (aLo, bLo) = (i + 1, j + 1);
        }

        Patience(a, aLo, aHi, b, bLo, bHi, removed, added, steps, depth + 1);
    }

    /// <summary>The matched unique pairs (absolute positions), in order.</summary>
    private static List<(int I, int J)> Anchors(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, DiffSteps steps)
    {
        var counts = new Dictionary<int, (int InA, int AtA, int InB, int AtB)>();
        for (var i = aLo; i < aHi; i++)
        {
            steps.Step();
            counts[a[i]] = counts.TryGetValue(a[i], out var c) ? c with { InA = c.InA + 1 } : (1, i, 0, -1);
        }

        for (var j = bLo; j < bHi; j++)
        {
            steps.Step();
            if (counts.TryGetValue(b[j], out var c))
                counts[b[j]] = c with { InB = c.InB + 1, AtB = j };
        }

        var pairs = new List<(int I, int J)>();
        for (var i = aLo; i < aHi; i++)
        {
            if (counts[a[i]] is { InA: 1, InB: 1, AtB: var j })
                pairs.Add((i, j));
        }

        if (pairs.Count == 0)
            return pairs;

        // Patience sorting over the right positions: pile tops (indices into pairs) and back pointers.
        var tops = new List<int>();
        var previous = new int[pairs.Count];
        for (var p = 0; p < pairs.Count; p++)
        {
            steps.Step();
            int lo = 0, hi = tops.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (pairs[tops[mid]].J < pairs[p].J)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            previous[p] = lo > 0 ? tops[lo - 1] : -1;
            if (lo == tops.Count)
                tops.Add(p);
            else
                tops[lo] = p;
        }

        var result = new List<(int I, int J)>(tops.Count);
        for (var p = tops[^1]; p >= 0; p = previous[p])
            result.Add(pairs[p]);
        result.Reverse();
        return result;
    }
}
