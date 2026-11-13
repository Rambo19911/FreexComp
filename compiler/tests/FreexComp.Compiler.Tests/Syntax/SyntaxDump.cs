using System.Text;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Compiler.Tests.Syntax;

/// <summary>
/// S-expression dump of a tree shape (testing.md §3): a node is <c>(Kind child …)</c> with the kind name without
/// "Syntax"; a token is its text, a missing token <c>&lt;missing Kind&gt;</c>, the end of file <c>&lt;eof&gt;</c>.
/// Trivia is not shown.
/// </summary>
internal static class SyntaxDump
{
    public static string Of(SyntaxNode node)
    {
        var builder = new StringBuilder();
        Write(builder, node);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, SyntaxNode node)
    {
        builder.Append('(').Append(node.Kind);
        foreach (var child in node.ChildNodesAndTokens())
        {
            builder.Append(' ');
            if (child.AsNode() is { } childNode)
            {
                Write(builder, childNode);
            }
            else
            {
                var token = child.AsToken();
                builder.Append(
                    token.IsMissing ? $"<missing {token.Kind}>"
                    : token.Kind == SyntaxKind.EndOfFileToken ? "<eof>"
                    : token.Text);
            }
        }

        builder.Append(')');
    }
}
