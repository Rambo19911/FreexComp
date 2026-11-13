using System.Collections.Immutable;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;

namespace FreexComp.LanguageService;

/// <summary>
/// The only list of token classes in the product (language-service.md CMP-710). Lexical kinds come first, then the
/// semantic kinds (Phase 3+).
/// </summary>
public enum ClassificationKind
{
    Keyword,
    ContextualKeyword,
    Identifier,
    NumberLiteral,
    StringLiteral,
    CharLiteral,
    Operator,
    Punctuation,
    Comment,
    DocComment,
    Directive,
    DisabledText,
    AsmText,
    Invalid,
    UnitName,
    TypeName,
    ClassName,
    InterfaceName,
    EnumName,
    EnumMember,
    TypeParameter,
    Constant,
    GlobalVariable,
    LocalVariable,
    Parameter,
    Field,
    Property,
    Routine,
    Method,
    Label,
    Attribute,
    DeprecatedSymbol,
    UnresolvedIdentifier,
}

public readonly record struct ClassifiedSpan(TextSpan Span, ClassificationKind Kind);

/// <summary>Syntax highlighting (language-service.md §2). Phase 1: the lexical classifier over the token stream.</summary>
public static class Classifier
{
    /// <summary>CMP-713 (b): directives recognised after <c>property</c> within the same declaration.</summary>
    private static readonly string[] PropertySpecifiers = ["read", "write", "default", "stored", "index", "nodefault", "implements"];

    /// <summary>
    /// Classifies <paramref name="tokens"/> (from <see cref="Lexer.Tokenize"/>) without a parser (CMP-712). Spans are
    /// sorted, do not overlap, cover every non-whitespace character once (CMP-711) and are clipped to
    /// <paramref name="range"/>; only spans intersecting it are returned. Never throws.
    /// </summary>
    public static ImmutableArray<ClassifiedSpan> GetLexicalClassifications(ImmutableArray<SyntaxToken> tokens, TextSpan range)
    {
        if (tokens.IsDefaultOrEmpty || range.Length == 0)
        {
            return [];
        }

        var builder = new SpanBuilder(range);
        var inAsm = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.FullSpan.Start >= range.End)
            {
                break;
            }

            AddTrivia(builder, token.LeadingTrivia, inAsm);
            if (inAsm && token.Kind is not (SyntaxKind.EndKeyword or SyntaxKind.EndOfFileToken))
            {
                builder.AddAsm(token.Span);   // CMP-714: everything between asm and end, one span per line
            }
            else
            {
                if (token.Kind == SyntaxKind.EndKeyword)
                {
                    inAsm = false;
                }

                if (IsAsm(token))
                {
                    inAsm = true;
                    builder.Add(token.Span, ClassificationKind.Keyword);
                }
                else
                {
                    ClassifyToken(builder, tokens, i);
                }
            }

            AddTrivia(builder, token.TrailingTrivia, inAsm);
        }

        return builder.ToImmutable();
    }

    /// <summary>The colour token of <paramref name="kind"/> (last column of CMP-710); "" = no colour of its own.</summary>
    public static string GetColorToken(ClassificationKind kind) => kind switch
    {
        ClassificationKind.Keyword or ClassificationKind.ContextualKeyword => "syntax.keyword",
        ClassificationKind.NumberLiteral => "syntax.number",
        ClassificationKind.StringLiteral => "syntax.string",
        ClassificationKind.CharLiteral => "syntax.char",
        ClassificationKind.Operator or ClassificationKind.Punctuation => "syntax.symbol",
        ClassificationKind.Comment or ClassificationKind.DocComment => "syntax.comment",
        ClassificationKind.Directive => "syntax.directive",
        ClassificationKind.DisabledText => "syntax.inactive",
        ClassificationKind.AsmText => "syntax.asm",
        ClassificationKind.Invalid => "syntax.invalid",
        ClassificationKind.TypeName or ClassificationKind.ClassName or ClassificationKind.InterfaceName
            or ClassificationKind.EnumName or ClassificationKind.TypeParameter => "syntax.type",
        ClassificationKind.EnumMember or ClassificationKind.Constant => "syntax.constant",
        ClassificationKind.Routine or ClassificationKind.Method => "syntax.routine",
        ClassificationKind.Label => "syntax.label",
        ClassificationKind.Attribute => "syntax.attribute",
        ClassificationKind.DeprecatedSymbol => "",
        _ => "syntax.text",
    };

    private static void ClassifyToken(SpanBuilder builder, ImmutableArray<SyntaxToken> tokens, int index)
    {
        var token = tokens[index];
        switch (token.Kind)
        {
            case SyntaxKind.EndOfFileToken:
                return;
            case SyntaxKind.IdentifierToken:
                builder.Add(token.Span, IsContextualKeywordHere(tokens, index) ? ClassificationKind.ContextualKeyword : ClassificationKind.Identifier);
                return;
            case SyntaxKind.IntegerLiteralToken or SyntaxKind.RealLiteralToken:
                builder.Add(token.Span, ClassificationKind.NumberLiteral);
                return;
            case SyntaxKind.StringLiteralToken:
                ClassifyString(builder, token);
                return;
        }

        if (SyntaxFacts.IsKeyword(token.Kind))
        {
            builder.Add(token.Span, ClassificationKind.Keyword);
        }
        else if (SyntaxFacts.IsPunctuation(token.Kind))
        {
            builder.Add(token.Span, ClassificationKind.Punctuation);
        }
        else
        {
            builder.Add(token.Span, ClassificationKind.Operator);   // operators and the lone symbols # $ &
        }
    }

    /// <summary>CMP-713, the lexical approximation of "directive position".</summary>
    private static bool IsContextualKeywordHere(ImmutableArray<SyntaxToken> tokens, int index)
    {
        var text = tokens[index].Text;
        if (!SyntaxFacts.IsContextualKeyword(text))
        {
            return false;
        }

        if (index > 0 && tokens[index - 1].Kind is SyntaxKind.SemicolonToken or SyntaxKind.CloseParenToken)
        {
            return true;
        }

        if (!PropertySpecifiers.Any(s => SyntaxFacts.GetKeywordKind(text) == SyntaxFacts.GetKeywordKind(s)))
        {
            return false;
        }

        var depth = 0;
        for (var i = index - 1; i >= 0; i--)
        {
            switch (tokens[i].Kind)
            {
                case SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken:
                    depth++;
                    break;
                case SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken:
                    depth = Math.Max(0, depth - 1);
                    break;
                case SyntaxKind.SemicolonToken when depth == 0:
                    return false;
                case SyntaxKind.PropertyKeyword when depth == 0:
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Quoted parts are StringLiteral, <c>#n</c> parts CharLiteral; a literal that is a single quoted part of one code
    /// unit is CharLiteral; an unterminated quoted part or a code above $FFFF is Invalid. Adjacent parts of the same
    /// kind form one span.
    /// </summary>
    private static void ClassifyString(SpanBuilder builder, SyntaxToken token)
    {
        var text = token.Text;
        var start = token.Span.Start;
        var singleChar = token.Value is string { Length: 1 } && text[0] == '\'' && QuotedPartEnd(text, 0) == text.Length;
        var i = 0;
        while (i < text.Length)
        {
            ClassificationKind kind;
            var partStart = i;
            if (text[i] == '\'')
            {
                i = QuotedPartEnd(text, i);
                var terminated = i - partStart >= 2 && text[i - 1] == '\'';
                kind = !terminated ? ClassificationKind.Invalid
                    : singleChar ? ClassificationKind.CharLiteral
                    : ClassificationKind.StringLiteral;
            }
            else
            {
                i++; // '#'
                var hex = i < text.Length && text[i] == '$';
                if (hex)
                {
                    i++;
                }

                var digitsStart = i;
                while (i < text.Length && (hex ? char.IsAsciiHexDigit(text[i]) : char.IsAsciiDigit(text[i])))
                {
                    i++;
                }

                var inRange = ulong.TryParse(text.AsSpan(digitsStart, i - digitsStart),
                                             hex ? System.Globalization.NumberStyles.AllowHexSpecifier : System.Globalization.NumberStyles.None,
                                             System.Globalization.CultureInfo.InvariantCulture, out var code) && code <= 0xFFFF;
                kind = inRange ? ClassificationKind.CharLiteral : ClassificationKind.Invalid;
            }

            builder.Add(new TextSpan(start + partStart, i - partStart), kind, mergeWithPrevious: true);
        }
    }

    /// <summary>End (exclusive) of the quoted part starting at <paramref name="quote"/>; '' is an escaped quote.</summary>
    private static int QuotedPartEnd(string text, int quote)
    {
        var i = quote + 1;
        while (i < text.Length)
        {
            if (text[i] == '\'')
            {
                if (i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return i;
    }

    private static bool IsAsm(SyntaxToken token) =>
        token.Kind == SyntaxKind.IdentifierToken && token.Text.Length == 3
        && (token.Text[0] | 0x20) == 'a' && (token.Text[1] | 0x20) == 's' && (token.Text[2] | 0x20) == 'm';

    private static void AddTrivia(SpanBuilder builder, ImmutableArray<SyntaxTrivia> trivia, bool inAsm)
    {
        foreach (var piece in trivia)
        {
            ClassificationKind kind;
            switch (piece.Kind)
            {
                case SyntaxKind.WhitespaceTrivia:
                    continue;
                case SyntaxKind.EndOfLineTrivia:
                    builder.EndAsmLine();
                    continue;
                case SyntaxKind.LineCommentTrivia or SyntaxKind.BlockCommentTrivia:
                    kind = ClassificationKind.Comment;
                    break;
                case SyntaxKind.DocCommentTrivia:
                    kind = ClassificationKind.DocComment;
                    break;
                case SyntaxKind.DirectiveTrivia or SyntaxKind.IncludeDirectiveTrivia:
                    kind = ClassificationKind.Directive;
                    break;
                case SyntaxKind.DisabledTextTrivia:
                    kind = ClassificationKind.DisabledText;
                    break;
                default:
                    // SkippedTokensTrivia (illegal characters): part of the asm text inside an asm block.
                    if (inAsm)
                    {
                        builder.AddAsm(piece.Span);
                        continue;
                    }

                    kind = ClassificationKind.Invalid;
                    break;
            }

            builder.Add(piece.Span, kind);
        }
    }

    /// <summary>Collects spans in order, merges asm text per line and clips to the requested range.</summary>
    private sealed class SpanBuilder(TextSpan range)
    {
        private readonly ImmutableArray<ClassifiedSpan>.Builder _spans = ImmutableArray.CreateBuilder<ClassifiedSpan>();
        private TextSpan? _asmLine;
        private ClassifiedSpan? _last;

        public void Add(TextSpan span, ClassificationKind kind, bool mergeWithPrevious = false)
        {
            EndAsmLine();
            if (mergeWithPrevious && _last is { } last && last.Kind == kind && last.Span.End == span.Start)
            {
                _last = new ClassifiedSpan(TextSpan.FromBounds(last.Span.Start, span.End), kind);
                return;
            }

            Flush();
            _last = new ClassifiedSpan(span, kind);
        }

        /// <summary>Extends the current asm line span (whitespace between tokens of one line is included).</summary>
        public void AddAsm(TextSpan span)
        {
            if (_asmLine is { } line)
            {
                _asmLine = TextSpan.FromBounds(line.Start, span.End);
                return;
            }

            Flush();
            _asmLine = span;
        }

        public void EndAsmLine()
        {
            if (_asmLine is { } line)
            {
                _asmLine = null;
                Flush();
                _last = new ClassifiedSpan(line, ClassificationKind.AsmText);
            }
        }

        public ImmutableArray<ClassifiedSpan> ToImmutable()
        {
            EndAsmLine();
            Flush();
            return _spans.ToImmutable();
        }

        private void Flush()
        {
            if (_last is not { } last)
            {
                return;
            }

            _last = null;
            var start = Math.Max(last.Span.Start, range.Start);
            var end = Math.Min(last.Span.End, range.End);
            if (start < end)
            {
                _spans.Add(new ClassifiedSpan(TextSpan.FromBounds(start, end), last.Kind));
            }
        }
    }
}
