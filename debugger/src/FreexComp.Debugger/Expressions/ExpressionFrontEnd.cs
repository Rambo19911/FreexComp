using System.Globalization;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Syntax;

namespace FreexComp.Debugger.Expressions;

/// <summary>Format suffix of a watch or evaluate expression (Pascal values §9.1).</summary>
internal enum FormatSpec { None, Hex, Decimal, Count }

/// <summary>An expression parsed by the compiler's parser, with its format suffix split off.</summary>
/// <param name="Text">The expression text without the suffix, trimmed.</param>
/// <param name="Root">The compiler's syntax node; free of syntax errors.</param>
/// <param name="Format">The suffix, if any.</param>
/// <param name="Count">The element count of a <see cref="FormatSpec.Count"/> suffix.</param>
internal sealed record ParsedExpression(string Text, ExpressionSyntax Root, FormatSpec Format, int Count);

/// <summary>
/// Parses expression text with the compiler's expression parser (the debugger has no lexer or parser of its own) and
/// reports the first syntax error as <see cref="DebugErrorCode.ExpressionSyntax"/> with a 1-based column.
/// </summary>
/// <remarks>
/// The library may use only the compiler's syntax namespaces (DBG-002), and the compiler's diagnostic type lies
/// outside them. The error is therefore read off the tree itself: what the parser could not use is there as skipped
/// trivia, what it missed as a missing token, and a node knows whether a diagnostic lies inside it.
/// </remarks>
internal static partial class ExpressionFrontEnd
{
    public static ParsedExpression Parse(string text, bool allowFormatSuffix = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        var format = FormatSpec.None;
        var count = 0;
        // The suffix is split off before parsing; the expression keeps its place so that columns are those of the input.
        var expression = text;
        if (allowFormatSuffix && FindSuffix(text) is { } suffix)
        {
            expression = text[..suffix.Comma];
            format = suffix.Format;
            count = suffix.Count;
        }

        if (string.IsNullOrWhiteSpace(expression))
            throw new DebuggerException(DebugErrorCode.ExpressionSyntax, Messages.ExpressionExpected);

        var root = SyntaxFactory.ParseNode<ExpressionSyntax>(expression);
        if (FirstError(root) is { } error)
            throw new DebuggerException(DebugErrorCode.ExpressionSyntax, Messages.AtColumn(error.Message, error.Position + 1));
        return new ParsedExpression(expression.Trim(), root, format, count);
    }

    /// <summary>
    /// The last comma outside parentheses, brackets and quotes, when what follows it is a format: <c>x</c> / <c>h</c>,
    /// <c>d</c> or an element count.
    /// </summary>
    private static (int Comma, FormatSpec Format, int Count)? FindSuffix(string text)
    {
        var comma = -1;
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'')
                quoted = !quoted;
            else if (quoted)
                continue;
            else if (c is '(' or '[')
                depth++;
            else if (c is ')' or ']')
                depth--;
            else if (c == ',' && depth == 0)
                comma = i;
        }

        if (comma < 0)
            return null;
        var spec = text[(comma + 1)..].Trim();
        if (spec.Equals("x", StringComparison.OrdinalIgnoreCase) || spec.Equals("h", StringComparison.OrdinalIgnoreCase))
            return (comma, FormatSpec.Hex, 0);
        if (spec.Equals("d", StringComparison.OrdinalIgnoreCase))
            return (comma, FormatSpec.Decimal, 0);
        if (spec.Length > 0 && spec.All(char.IsAsciiDigit)
            && int.TryParse(spec, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            return (comma, FormatSpec.Count, count);
        return null;
    }

    /// <summary>The first syntax error of the tree in source order, or null for a well-formed expression.</summary>
    private static (int Position, string Message)? FirstError(ExpressionSyntax root)
    {
        var fragment = root.SyntaxTree.Root;
        var errors = new List<(int Position, int Rank, string Message)>();
        var starts = new List<int>();          // where the real tokens and the skipped input begin
        var missing = new List<SyntaxToken>();
        foreach (var token in fragment.DescendantTokens())
        {
            foreach (var trivia in token.LeadingTrivia)
                AddTrivia(trivia);
            if (token.IsMissing)
                missing.Add(token);
            else
                starts.Add(token.Span.Start);
            foreach (var trivia in token.TrailingTrivia)
                AddTrivia(trivia);
        }

        // A missing token is reported where the parser stood: at the next thing in the input.
        starts.Sort();
        foreach (var token in missing)
        {
            var position = starts.FirstOrDefault(start => start >= token.Span.Start, token.Span.Start);
            var message = token.Kind != SyntaxKind.IdentifierToken ? Messages.Expected(SyntaxFacts.GetText(token.Kind))
                : token.Parent?.Parent is MemberAccessExpressionSyntax ? Messages.IdentifierExpected
                : Messages.ExpressionExpected;
            errors.Add((position, 1, message));
        }

        foreach (var node in root.DescendantNodes().Prepend(root))
        {
            switch (node)
            {
                // A malformed literal: the lexer reported it inside the token. A report at the end of the input lies
                // inside the last token's extent too: there the literal's own text decides.
                case LiteralExpressionSyntax literal when literal.ContainsDiagnostics && !HasSkippedTrivia(literal.Token)
                    && (!errors.Exists(e => e.Rank == 1 && e.Position == literal.FullSpan.End) || IsMalformed(literal.Token)):
                    errors.Add((literal.Token.Span.Start, 0, literal.Token.Kind == SyntaxKind.StringLiteralToken
                        ? Messages.InvalidStringLiteral
                        : Messages.InvalidNumber(literal.Token.Text)));
                    break;
                // The grammar has one relational operator per expression: "a = b = c" needs parentheses.
                case BinaryExpressionSyntax { Left: BinaryExpressionSyntax inner } outer
                    when IsRelational(outer.OperatorToken) && IsRelational(inner.OperatorToken):
                    errors.Add((outer.OperatorToken.Span.Start, 1, Messages.Unexpected(outer.OperatorToken.Text)));
                    break;
            }
        }

        if (errors.Count == 0)
        {
            if (!fragment.ContainsDiagnostics)
                return null;
            // Something the rules above do not know: never accepted silently.
            var innermost = root.DescendantNodes().LastOrDefault(node => node.ContainsDiagnostics) ?? root;
            return (innermost.Span.Start, Messages.SyntaxError);
        }

        var first = errors.OrderBy(e => e.Position).ThenBy(e => e.Rank).First();
        return (first.Position, first.Message);

        void AddTrivia(SyntaxTrivia trivia)
        {
            if (trivia.Kind == SyntaxKind.SkippedTokensTrivia)
                starts.Add(trivia.Span.Start);
            // Skipped input, and comments and directives: an expression has none (§9.1).
            if (!IsBlank(trivia.Kind))
                errors.Add((trivia.Span.Start, 2, Messages.Unexpected(trivia.Text)));
        }
    }

    private static bool IsBlank(SyntaxKind kind) => kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia;

    private static bool HasSkippedTrivia(SyntaxToken token) =>
        token.LeadingTrivia.Any(trivia => !IsBlank(trivia.Kind)) || token.TrailingTrivia.Any(trivia => !IsBlank(trivia.Kind));

    /// <summary>An unterminated string (odd number of quotes) or a number with characters that do not belong to it.</summary>
    private static bool IsMalformed(SyntaxToken literal) =>
        literal.Kind == SyntaxKind.StringLiteralToken
            ? literal.Text.Count(c => c == '\'') % 2 != 0
            : !WellFormedNumber().IsMatch(literal.Text);

    [GeneratedRegex(@"^(\$[0-9A-Fa-f_]+|%[01_]+|&[0-7]+|[0-9][0-9_]*(\.[0-9][0-9_]*)?([eE][+-]?[0-9]+)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex WellFormedNumber();

    private static bool IsRelational(SyntaxToken op) => SyntaxFacts.GetBinaryPrecedence(op.Kind) == 2;
}
