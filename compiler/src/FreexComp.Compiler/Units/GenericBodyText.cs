using System.Collections.Immutable;
using System.Text;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Units;

/// <summary>
/// CMP-162 (C-119): the source of a generic as a <c>.pcu</c> stores it — the pieces of its unit that make a parsable unit with only
/// the generic in it (<c>unit U;</c>, <c>interface</c>, the <c>type</c> keyword of its section, its declaration,
/// <c>implementation</c>, its method implementations, <c>end.</c>), each with its original line and column. The reader puts every
/// piece back at its line and column, so diagnostics in instances bound against the <c>.pcu</c> point into the original file.
/// </summary>
internal static class GenericBodyText
{
    /// <summary>One piece: 1-based line and column of its first character, and its text.</summary>
    internal sealed record Piece(int Line, int Col, string Text);

    /// <summary>The pieces of <paramref name="generic"/>, or empty when it has no declaration of a unit's interface (nothing to store).</summary>
    public static ImmutableArray<Piece> Pieces(GenericTypeSymbol generic)
    {
        if (generic.Declaration is not { Parent: TypeSectionSyntax section } declaration || declaration.SyntaxTree.Root is not UnitSyntax unit)
        {
            return [];
        }

        var pieces = ImmutableArray.CreateBuilder<Piece>();
        void Add(SyntaxToken first, SyntaxToken last, string? text = null)
        {
            var file = first.File ?? first.Parent!.SyntaxTree.Text;
            var position = file.Lines.GetLinePosition(first.Span.Start);
            pieces.Add(new Piece(position.Line + 1, position.Column + 1, text ?? file.Text[first.Span.Start..Math.Max(first.Span.Start, last.Span.End)]));
        }

        Add(unit.UnitKeyword, unit.SemicolonToken);
        Add(unit.InterfaceSection.InterfaceKeyword, unit.InterfaceSection.InterfaceKeyword);
        Add(section.TypeKeyword, section.TypeKeyword);
        Add(declaration.FirstToken(), declaration.LastToken());
        Add(unit.ImplementationSection.ImplementationKeyword, unit.ImplementationSection.ImplementationKeyword);
        foreach (var implementation in generic.Implementations.OrderBy(i => i.Declaration.Span.Start))
        {
            Add(implementation.Declaration.FirstToken(), implementation.Declaration.LastToken());
        }

        Add(unit.EndKeyword, unit.EndKeyword, unit.EndKeyword.Text + ".");
        return pieces.ToImmutable();
    }

    /// <summary>The pieces at their lines and columns (newlines, then spaces before each; one space when a piece would overlap).</summary>
    public static string Reconstruct(IEnumerable<Piece> pieces)
    {
        var text = new StringBuilder();
        int line = 1, col = 1;
        foreach (var piece in pieces)
        {
            if (line > piece.Line || (line == piece.Line && col > piece.Col))
            {
                text.Append(' ');
                col++;
            }

            for (; line < piece.Line; line++, col = 1)
            {
                text.Append('\n');
            }

            for (; col < piece.Col; col++)
            {
                text.Append(' ');
            }

            text.Append(piece.Text);
            foreach (var c in piece.Text)
            {
                if (c == '\n')
                {
                    line++;
                    col = 1;
                }
                else if (c != '\r')
                {
                    col++;
                }
            }
        }

        return text.ToString();
    }

    /// <summary>The generic declaration and the method implementations of a reconstructed unit text.</summary>
    public static (TypeDeclarationSyntax? Declaration, ImmutableArray<RoutineDeclarationSyntax> Implementations) Parse(string path, string text)
    {
        var tree = SyntaxTree.Parse(SourceText.From(path, text));
        if (tree.Root is not UnitSyntax unit || tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return (null, []);
        }

        var declaration = unit.InterfaceSection.Declarations.OfType<TypeSectionSyntax>().SelectMany(s => s.Declarations).FirstOrDefault();
        return (declaration, [.. unit.ImplementationSection.Declarations.OfType<RoutineDeclarationSyntax>()]);
    }
}
