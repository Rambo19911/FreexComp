namespace FreexComp.Designer.Surface;

/// <summary>
/// The clipboard as the design surface uses it (design-surface.md §1, §10): named formats holding UTF-16 text.
/// Implemented by the IDE over the system clipboard; in memory in tests.
/// </summary>
public interface IDesignerClipboard
{
    /// <summary>Changes whenever the clipboard content changes, by this or any other application (DSG-SUR-125).</summary>
    long SequenceNumber { get; }

    /// <summary>The text of <paramref name="format"/>, or null when the clipboard does not hold it.</summary>
    string? GetData(string format);

    /// <summary>Replaces the whole clipboard content with these formats.</summary>
    void SetData(IReadOnlyDictionary<string, string> formats);
}

/// <summary>The clipboard format ids of design-surface.md DSG-SUR-110.</summary>
public static class ClipboardFormats
{
    /// <summary>The designer's own format: the form text with a <c>; parent-origin</c> header line (DSG-SUR-111).</summary>
    public const string Components = "application/x-freexcomp-components";

    /// <summary>Plain text.</summary>
    public const string Text = "Text";
}
