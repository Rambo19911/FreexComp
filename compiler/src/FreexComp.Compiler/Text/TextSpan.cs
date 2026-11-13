namespace FreexComp.Compiler.Text;

/// <summary>Half-open range [Start, End) of UTF-16 code-unit offsets into <see cref="SourceText.Text"/>.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int Start { get; } = Start >= 0 ? Start : throw new ArgumentOutOfRangeException(nameof(Start));

    public int Length { get; } = Length >= 0 ? Length : throw new ArgumentOutOfRangeException(nameof(Length));

    public int End => Start + Length;

    public bool Contains(int position) => position >= Start && position < End;

    /// <summary>True if the spans overlap or touch (an empty span at either boundary intersects).</summary>
    public bool IntersectsWith(TextSpan other) => other.Start <= End && other.End >= Start;

    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        return new TextSpan(start, end - start);
    }
}

/// <summary>0-based line and UTF-16 column; diagnostics print both +1 (compiler-cli.md §2).</summary>
public readonly record struct LinePosition(int Line, int Column);

public readonly record struct LinePositionSpan(LinePosition Start, LinePosition End);

public readonly record struct Location(string Path, TextSpan Span, LinePositionSpan LineSpan)
{
    private static readonly LinePosition NoPosition = new(-1, -1);

    /// <summary>A location without a source position (printed as <c>0,0</c>, compiler-cli.md §2).</summary>
    public static Location ForFile(string path) => new(path, default, new LinePositionSpan(NoPosition, NoPosition));

    public bool HasPosition => LineSpan.Start != NoPosition;
}
