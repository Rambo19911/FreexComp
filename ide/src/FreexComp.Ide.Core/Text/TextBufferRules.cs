using FreexComp.Ide.Core.Services;

namespace FreexComp.Ide.Core.Text;

/// <summary>Rules shared by every <see cref="ITextBuffer"/> implementation.</summary>
public static class TextBufferRules
{
    /// <summary>
    /// Validates <paramref name="edits"/> against a text of <paramref name="length"/> units (IDE-ARCH-032) and returns
    /// them by descending offset. Out of range → <see cref="ArgumentOutOfRangeException"/>; overlapping or sharing an
    /// offset (order would be ambiguous) → <see cref="ArgumentException"/>.
    /// </summary>
    public static TextEdit[] SortForApply(IReadOnlyList<TextEdit> edits, int length)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var sorted = edits.ToArray();
        foreach (var e in sorted)
        {
            if (e.NewText is null)
                throw new ArgumentException("TextEdit.NewText is null.", nameof(edits));
            if (e.Offset < 0 || e.Length < 0 || e.Offset > length - e.Length)
                throw new ArgumentOutOfRangeException(nameof(edits), $"Edit [{e.Offset}, +{e.Length}) is outside the text (length {length}).");
        }
        Array.Sort(sorted, (a, b) => b.Offset.CompareTo(a.Offset));
        for (int i = 1; i < sorted.Length; i++)
        {
            var later = sorted[i - 1];
            var earlier = sorted[i];
            if (earlier.Offset == later.Offset || earlier.Offset + earlier.Length > later.Offset)
                throw new ArgumentException($"Edits at {earlier.Offset} and {later.Offset} overlap.", nameof(edits));
        }
        return sorted;
    }

    /// <summary>CRLF if CRLF count ≥ lone-LF count; <paramref name="whenNone"/> without either. A lone CR is not counted.</summary>
    public static LineEndingKind DominantLineEnding(string text, LineEndingKind whenNone) =>
        LineEndingDetector.Dominant(LineEndingDetector.Count(text), whenNone);
}
