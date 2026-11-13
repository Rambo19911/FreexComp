namespace FreexComp.Compiler.Text;

/// <summary>Maps offsets to line/column positions. Line breaks: CR LF, LF, lone CR (LANG-102).</summary>
public sealed class LineMap
{
    private readonly int[] _starts;    // offset of the first character of each line
    private readonly int[] _ends;      // offset of the line break (or text end) of each line
    private readonly int _textLength;

    internal LineMap(string text)
    {
        var starts = new List<int> { 0 };
        var ends = new List<int>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is '\r' or '\n')
            {
                ends.Add(i);
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                starts.Add(i);
            }
            else
            {
                i++;
            }
        }

        ends.Add(text.Length);
        _starts = [.. starts];
        _ends = [.. ends];
        _textLength = text.Length;
    }

    public int LineCount => _starts.Length;

    public LinePosition GetLinePosition(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _textLength);
        var index = Array.BinarySearch(_starts, offset);
        var line = index >= 0 ? index : ~index - 1;
        return new LinePosition(line, offset - _starts[line]);
    }

    /// <summary>Column may address the line break characters themselves (inverse of <see cref="GetLinePosition"/>).</summary>
    public int GetOffset(LinePosition position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position.Line, nameof(position));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position.Line, LineCount, nameof(position));
        var start = _starts[position.Line];
        var limit = position.Line + 1 < LineCount ? _starts[position.Line + 1] - 1 : _textLength;
        ArgumentOutOfRangeException.ThrowIfNegative(position.Column, nameof(position));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position.Column, limit - start, nameof(position));
        return start + position.Column;
    }

    /// <summary>Span of the line without its line break.</summary>
    public TextSpan GetLineSpan(int line)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, LineCount);
        return TextSpan.FromBounds(_starts[line], _ends[line]);
    }
}
