using FreexComp.Compiler.Text;

namespace FreexComp.Compiler;

/// <summary>A source file's path (absolute, normalised, CMP-205) and decoded text.</summary>
public sealed record SourceText(string Path, string Text)
{
    private LineMap? _lines;

    public string Path { get; init => field = PathUtil.Normalize(value); } = PathUtil.Normalize(Path);

    public string Text { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } =
        Text ?? throw new ArgumentNullException(nameof(Text));

    /// <summary>Computed on first use and cached.</summary>
    public LineMap Lines
    {
        get
        {
            if (_lines is null)
            {
                Interlocked.CompareExchange(ref _lines, new LineMap(Text), null);
            }

            return _lines;
        }
    }

    public static SourceText From(string path, string text) => new(path, text);

    public bool Equals(SourceText? other) =>
        other is not null && PathUtil.Comparer.Equals(Path, other.Path) && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(PathUtil.Comparer.GetHashCode(Path), Text.GetHashCode(StringComparison.Ordinal));
}
