using FreexComp.Compiler;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Ide.Core.Editing;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Languages;

/// <summary>
/// P3 Object Pascal fold regions from the syntax tree (IDE-P1-060, IDE-ED-051; I-113): routines, type declarations,
/// unit sections, uses clauses and statement blocks are syntax nodes; comments, <c>{$REGION}</c> pairs and conditional
/// blocks are trivia, collected as the token provider does. For valid code the regions are those of
/// <see cref="PascalFoldingProvider"/> (test <c>FoldingTests.TokenAndTreeProvidersAgree</c>); a text with syntax errors
/// has no reliable tree, so it falls back to the token pairing, which degrades gracefully while typing.
/// </summary>
public sealed class PascalTreeFoldingProvider : IFoldingProvider
{
    /// <summary>The parser needs an absolute path; it does not affect the tree's shape (see <see cref="PascalClassifier"/>).</summary>
    private const string PlaceholderPath = @"C:\Untitled.pas";

    private readonly PascalFoldingProvider _tokens = new();

    public IReadOnlyList<FoldRegion> GetRegions(ITextBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var text = buffer.Text;
        var source = SourceText.From(PlaceholderPath, text);
        SyntaxTree tree;
        try
        {
            tree = SyntaxTree.Parse(source);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return _tokens.GetRegions(buffer); // never throw (IFoldingProvider)
        }

        if (tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return _tokens.GetRegions(buffer);

        int Line(int offset) => buffer.GetPosition(Math.Clamp(offset, 0, text.Length)).Line;
        var regions = new List<FoldRegion>();
        PascalFoldingProvider.AddTriviaRegions(buffer, text, Lexer.Tokenize(source), regions, Line);
        if (tree.Root is UnitSyntax unit)
            AddSections(buffer, unit, regions, Line);
        foreach (var node in tree.Root.DescendantNodes())
        {
            switch (node)
            {
                case RoutineDeclarationSyntax { Block: { } block } routine:
                    regions.Add(buffer, Line(routine.Heading.Span.Start), Line(block.Body.EndKeyword.Span.Start), FoldKind.Routine);
                    break;
                case TypeDeclarationSyntax declaration when TypeEnd(declaration.Type) is { IsMissing: false } end:
                    regions.Add(buffer, Line(declaration.Identifier.Span.Start), Line(end.Span.Start), FoldKind.Type);
                    break;
                case UsesClauseSyntax uses when Line(uses.Span.End) - Line(uses.Span.Start) >= 2:
                    regions.Add(buffer, Line(uses.Span.Start), Line(uses.Span.End), FoldKind.Uses);
                    break;
                case CompoundStatementSyntax { Parent: BlockSyntax { Parent: RoutineDeclarationSyntax } }:
                    break; // the routine's region covers its body (IDE-ED-051 e)
                case CompoundStatementSyntax or TryFinallyStatementSyntax or TryExceptStatementSyntax or CaseStatementSyntax or RepeatStatementSyntax
                    when Line(node.Span.End) - Line(node.Span.Start) >= 2:
                    regions.Add(buffer, Line(node.Span.Start), Line(LastTokenStart(node)), FoldKind.Block);
                    break;
            }
        }

        return FoldRegions.Sorted(regions);
    }

    /// <summary>The <c>end</c> of a class, record, object, interface or helper type body; null for other types and forward declarations.</summary>
    private static SyntaxToken? TypeEnd(TypeSyntax type) => type switch
    {
        ClassTypeSyntax c => c.EndKeyword,
        InterfaceTypeSyntax i => i.EndKeyword,
        RecordTypeSyntax r => r.EndKeyword,
        HelperTypeSyntax h => h.EndKeyword,
        _ => null,
    };

    /// <summary>The start of the node's closing keyword (<c>end</c>, <c>until</c> …): the region ends on that line.</summary>
    private static int LastTokenStart(SyntaxNode node) => node switch
    {
        CompoundStatementSyntax c => c.EndKeyword.Span.Start,
        RepeatStatementSyntax r => r.UntilKeyword.Span.Start,
        _ => node.Span.End - 1,
    };

    /// <summary>IDE-ED-051 (c): each section runs to the line before the next section keyword or the final <c>end</c>.</summary>
    private static void AddSections(ITextBuffer buffer, UnitSyntax unit, List<FoldRegion> regions, Func<int, int> line)
    {
        var starts = new List<int> { line(unit.InterfaceSection.InterfaceKeyword.Span.Start), line(unit.ImplementationSection.ImplementationKeyword.Span.Start) };
        if (unit.InitializationSection is { } initialization)
            starts.Add(line(initialization.Keyword.Span.Start));
        if (unit.FinalizationSection is { } finalization)
            starts.Add(line(finalization.FinalizationKeyword.Span.Start));
        starts.Add(line(unit.EndKeyword.Span.Start));
        for (int i = 0; i + 1 < starts.Count; i++)
            regions.Add(buffer, starts[i], starts[i + 1] - 1, FoldKind.Section);
    }
}
