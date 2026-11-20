namespace FreexComp.Designer.Text;

/// <summary>Maps offsets of a parsed text to 1-based line/column in UTF-16 code units (DSG-FD-030).</summary>
internal sealed class SourceLines
{
    private readonly int[] _starts;

    public SourceLines(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        _starts = [.. starts];
    }

    /// <summary>No text (a document built without one): every offset maps to line 1.</summary>
    public static SourceLines Empty { get; } = new("");

    public (int Line, int Col) LineCol(int position)
    {
        var index = Array.BinarySearch(_starts, position);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return (index + 1, position - _starts[index] + 1);
    }
}
