using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>Replace <see cref="Span"/> of a text with <see cref="NewText"/> (api.md §4.2).</summary>
public readonly record struct TextChange(TextSpan Span, string NewText)
{
    /// <summary>Nothing to do: an empty span and no new text.</summary>
    public bool IsEmpty => Span.Length == 0 && string.IsNullOrEmpty(NewText);
}
