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
    public static ImmutableArray<Piece> Pieces(GenericTypeSymbol generic) =>
        generic.Declaration is { } declaration ? Pieces(declaration, generic.Implementations.Select(i => i.Declaration)) : [];

    /// <summary>
    /// The pieces of a type declaration of a unit's interface and the given method implementations (C-227: also a plain class with
    /// generic methods and their implementations); empty when the declaration is not in a unit's type section.
    /// </summary>
    public static ImmutableArray<Piece> Pieces(TypeDeclarationSyntax declaration, IEnumerable<RoutineDeclarationSyntax> implementations)
    {
        if (declaration is not { Parent: TypeSectionSyntax section } || declaration.SyntaxTree.Root is not UnitSyntax unit)
        {
            return [];
        }

        var pieces = ImmutableArray.CreateBuilder<Piece>();
        void Add(SyntaxToken first, SyntaxToken last, string? text = null, SyntaxNode? node = null)
        {
            var file = first.File ?? first.Parent!.SyntaxTree.Text;
            var position = file.Lines.GetLinePosition(first.Span.Start);
            var end = Math.Max(first.Span.Start, last.Span.End);
            pieces.Add(new Piece(position.Line + 1, position.Column + 1, text ?? (node is null ? file.Text[first.Span.Start..end] : Resolved(file, node, first.Span.Start, end))));
        }

        Add(unit.UnitKeyword, unit.SemicolonToken);
        Add(unit.InterfaceSection.InterfaceKeyword, unit.InterfaceSection.InterfaceKeyword);
        Add(section.TypeKeyword, section.TypeKeyword);
        Add(declaration.FirstToken(), declaration.LastToken(), node: declaration);
        Add(unit.ImplementationSection.ImplementationKeyword, unit.ImplementationSection.ImplementationKeyword);
        foreach (var implementation in implementations.OrderBy(i => i.Span.Start))
        {
            Add(implementation.FirstToken(), implementation.LastToken(), node: implementation);
        }

        Add(unit.EndKeyword, unit.EndKeyword, unit.EndKeyword.Text + ".");
        return pieces.ToImmutable();
    }

    /// <summary>The conditional directives (LANG-140…): resolved when a body is stored.</summary>
    private static readonly HashSet<string> Conditionals = new(StringComparer.OrdinalIgnoreCase) { "IFDEF", "IFNDEF", "IF", "ELSE", "ELSEIF", "ENDIF", "IFEND", "IFOPT" };

    /// <summary>
    /// C-227: the text from <paramref name="start"/> to <paramref name="end"/> as the declaring compilation saw it — the text its
    /// conditionals skipped and the conditional directives themselves blanked (line breaks kept, so positions stay), so an
    /// instance bound against the <c>.pcu</c> gets the declaring compilation's branches whatever its own defines are.
    /// </summary>
    private static string Resolved(SourceText file, SyntaxNode node, int start, int end)
    {
        var chars = file.Text[start..end].ToCharArray();
        foreach (var token in node.DescendantTokens().Where(t => t.File is null || ReferenceEquals(t.File, file)))
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                if (trivia.Kind != SyntaxKind.DisabledTextTrivia && !(trivia is { Kind: SyntaxKind.DirectiveTrivia, Directive.Name: var name } && Conditionals.Contains(name)))
                {
                    continue;
                }

                for (var i = Math.Max(trivia.Span.Start, start); i < Math.Min(trivia.Span.End, end); i++)
                {
                    if (chars[i - start] is not ('\r' or '\n'))
                    {
                        chars[i - start] = ' ';
                    }
                }
            }
        }

        return new string(chars);
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
