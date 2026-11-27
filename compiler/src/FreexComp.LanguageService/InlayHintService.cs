using System.Collections.Immutable;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.LanguageService;

/// <summary>The kind of an inlay hint (CMP-1414): <c>Type</c> = the inferred type of an inline variable.</summary>
public enum InlayHintKind { Type }

/// <summary>An inlay hint (CMP-1414): <paramref name="Label"/> to show at the offset <paramref name="Position"/>.</summary>
public readonly record struct InlayHint(int Position, string Label, InlayHintKind Kind);

/// <summary>Inlay hints (language-service.md §6a, CMP-1414, C-385); whether the editor shows them is the editor's choice.</summary>
public static class InlayHintService
{
    /// <summary>
    /// One <see cref="InlayHintKind.Type"/> hint <c>": T"</c> just after the identifier of every inline variable without a written type
    /// (<c>var X := e</c>, <c>for var X := …</c>, <c>for var X in …</c>) whose identifier starts inside <paramref name="range"/>; none for a
    /// written type, an inline constant or a declaration with the error type (E2075). Sorted by position.
    /// </summary>
    public static ImmutableArray<InlayHint> GetInlayHints(Document doc, TextSpan range, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ct.ThrowIfCancellationRequested();
        var model = doc.SemanticModel;
        var hints = ImmutableArray.CreateBuilder<InlayHint>();
        void Add(SyntaxToken identifier)
        {
            if (range.Contains(identifier.Span.Start)
                && model.GetDeclaredSymbol(identifier, ct) is VariableSymbol { Type: { TypeKind: not TypeKind.Error } type })
            {
                hints.Add(new InlayHint(identifier.Span.End, ": " + type.Name, InlayHintKind.Type));
            }
        }

        foreach (var node in model.SyntaxTree.Root.DescendantNodes(n => n.Span.Start < range.End && n.Span.End >= range.Start))
        {
            ct.ThrowIfCancellationRequested();
            switch (node)
            {
                case InlineVarStatementSyntax { Type: null, Initializer: not null } inline:
                    foreach (var name in inline.Identifiers)
                    {
                        Add(name.Identifier);
                    }

                    break;
                case ForStatementSyntax { VarKeyword: not null, Type: null } loop:
                    Add(loop.Variable.Identifier);
                    break;
                case ForInStatementSyntax { VarKeyword: not null, Type: null } loop:
                    Add(loop.Variable.Identifier);
                    break;
            }
        }

        hints.Sort((a, b) => a.Position.CompareTo(b.Position));
        return hints.ToImmutable();
    }
}
