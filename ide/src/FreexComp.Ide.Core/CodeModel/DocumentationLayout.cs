namespace FreexComp.Ide.Core.CodeModel;

/// <summary>How a line of rendered documentation is styled (UI-ED-133).</summary>
public enum DocumentationLineKind
{
    /// <summary>Plain text (<c>type.body</c>); an empty line is a paragraph gap.</summary>
    Text,

    /// <summary>A label line: <see cref="DocumentationLine.Lead"/> (<c>Parameters:</c>, <c>Returns:</c>, <c>Remarks:</c>) in <c>type.bodyStrong</c>, then the text.</summary>
    Label,

    /// <summary>An indented parameter line <c>  &lt;name&gt; – &lt;text&gt;</c>: the name in <c>type.codeSmall</c>, then <c> – text</c>.</summary>
    Parameter,

    /// <summary>The last line <c>…</c> after a cut (IDE-ED-091).</summary>
    Cut,
}

/// <summary>One shown documentation line: <paramref name="Lead"/> is the styled part (label or parameter name), <paramref name="Rest"/> the remaining text.</summary>
public sealed record DocumentationLine(DocumentationLineKind Kind, string Lead, string Rest);

/// <summary>
/// UI-ED-133 (I-317): the documentation of the hover tooltip and the completion side panel, line by line as the language
/// service rendered it (CMP-732: summary, blank line, <c>Parameters:</c>, <c>  &lt;name&gt; – &lt;text&gt;</c> lines,
/// <c>Returns: …</c>, <c>Remarks: …</c>); in the hover tooltip cut to <see cref="MaxLines"/> lines with a last line
/// <c>…</c> (IDE-ED-091; the completion side panel scrolls instead).
/// </summary>
public static class DocumentationLayout
{
    /// <summary>IDE-ED-091: at most 10 lines of documentation.</summary>
    public const int MaxLines = 10;

    public const string CutMarker = "…";

    private const string ParametersLabel = "Parameters:";
    private const string ParameterIndent = "  ";
    private const string ParameterSeparator = " – ";
    private static readonly string[] TextLabels = ["Returns:", "Remarks:"];

    /// <param name="maxLines">The most lines shown before the cut marker; null = all.</param>
    public static IReadOnlyList<DocumentationLine> Lines(string documentation, int? maxLines = MaxLines)
    {
        ArgumentNullException.ThrowIfNull(documentation);
        var lines = documentation.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        var result = new List<DocumentationLine>();
        var inParameters = false;
        var shown = maxLines ?? lines.Length;
        foreach (var line in lines.Take(shown))
        {
            if (line == ParametersLabel)
            {
                inParameters = true;
                result.Add(new DocumentationLine(DocumentationLineKind.Label, line, ""));
                continue;
            }

            var separator = line.IndexOf(ParameterSeparator, StringComparison.Ordinal);
            if (inParameters && line.StartsWith(ParameterIndent, StringComparison.Ordinal) && separator > ParameterIndent.Length)
            {
                result.Add(new DocumentationLine(DocumentationLineKind.Parameter, line[ParameterIndent.Length..separator], line[separator..]));
                continue;
            }

            inParameters = false;
            if (TextLabels.FirstOrDefault(l => line.StartsWith(l, StringComparison.Ordinal)) is { } label)
                result.Add(new DocumentationLine(DocumentationLineKind.Label, label, line[label.Length..]));
            else
                result.Add(new DocumentationLine(DocumentationLineKind.Text, "", line));
        }

        if (lines.Length > shown)
            result.Add(new DocumentationLine(DocumentationLineKind.Cut, "", CutMarker));
        return result;
    }
}
