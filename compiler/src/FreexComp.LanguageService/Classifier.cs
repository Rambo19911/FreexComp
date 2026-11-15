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

    /// <summary>The (never read) file name of a skipped token lexed again on its own.</summary>
    private const string SkippedPath = @"C:\skipped.pas";

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

        return Classify(tokens, range, IsContextualKeywordHere);
    }

    /// <summary>
    /// The same kinds as <see cref="GetLexicalClassifications"/>, decided with the parse tree (CMP-712): an identifier is a
    /// <see cref="ClassificationKind.ContextualKeyword"/> exactly when the parser used it as one
    /// (<see cref="SyntaxToken.ContextualKind"/>). Tokens read from include files and missing tokens are not part of the
    /// tree's own text and are skipped. Never throws.
    /// </summary>
    public static ImmutableArray<ClassifiedSpan> GetSyntacticClassifications(SyntaxTree tree, TextSpan range)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (range.Length == 0)
        {
            return [];
        }

        return Classify(TokensAround(tree, range), range, static (list, i) =>
            list[i].ContextualKind != list[i].Kind && SyntaxFacts.IsContextualKeyword(list[i].ContextualKind));
    }

    /// <summary>
    /// The tree's own, non-missing tokens from the last <c>end</c> keyword before <paramref name="range"/> (or the file start)
    /// up to the first token starting at or after its end. <see cref="Classify"/> leaves an asm block at every <c>end</c>, so
    /// starting there gives the state a scan from the file start would have (CMP-702: the cost follows the range, not the file).
    /// </summary>
    private static ImmutableArray<SyntaxToken> TokensAround(SyntaxTree tree, TextSpan range)
    {
        var file = tree.Text;
        bool Usable(SyntaxToken t) => !t.IsMissing && SyntaxNode.IsOwn(t, file);

        var first = tree.FindToken(range.Start);
        var start = first;
        for (var previous = first.GetPreviousToken(); previous.Kind != SyntaxKind.None; previous = previous.GetPreviousToken())
        {
            if (!Usable(previous))
            {
                continue;
            }

            start = previous;
            if (previous.Kind == SyntaxKind.EndKeyword)
            {
                break;
            }
        }

        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
        for (var token = start; token.Kind != SyntaxKind.None; token = token.GetNextToken())
        {
            if (!Usable(token))
            {
                continue;
            }

            tokens.Add(token);
            if (token.FullSpan.Start >= range.End)
            {
                break;
            }
        }

        return tokens.ToImmutable();
    }

    /// <summary>
    /// The semantic overlay (CMP-710 "semantic" rows, CMP-711): one span per identifier token in <paramref name="range"/> that names a
    /// symbol, classified by what it binds to; <see cref="ClassificationKind.UnresolvedIdentifier"/> for a name reported as E2003;
    /// an additional <see cref="ClassificationKind.DeprecatedSymbol"/> span for a deprecated symbol. Identifiers used as
    /// directives and names in attribute lists (<see cref="ClassificationKind.Attribute"/>) are handled syntactically.
    /// </summary>
    public static ImmutableArray<ClassifiedSpan> GetSemanticClassifications(Document doc, TextSpan range, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var model = doc.SemanticModel;
        var unresolved = model.GetDiagnostics(range, ct).Where(d => d.Code == "E2003").Select(d => d.Location.Span.Start).ToHashSet();
        var spans = ImmutableArray.CreateBuilder<ClassifiedSpan>();
        foreach (var token in model.SyntaxTree.Root.OwnTokens())
        {
            if (token.Span.End <= range.Start)
            {
                continue;
            }

            if (token.Span.Start >= range.End)
            {
                break;
            }

            ct.ThrowIfCancellationRequested();
            if (token.Kind != SyntaxKind.IdentifierToken || token.IsMissing || token.ContextualKind != token.Kind || SyntaxFacts.IsReservedWord(token.Text))
            {
                continue;
            }

            if (token.Parent?.FirstAncestorOrSelf<Compiler.Syntax.AttributeSyntax>() is { } attribute && attribute.Name.Span.Contains(token.Span.Start))
            {
                spans.Add(new ClassifiedSpan(token.Span, ClassificationKind.Attribute));
                continue;
            }

            if (unresolved.Contains(token.Span.Start))
            {
                spans.Add(new ClassifiedSpan(token.Span, ClassificationKind.UnresolvedIdentifier));
                continue;
            }

            var symbol = model.GetDeclaredSymbol(token, ct) ?? (token.Parent is { } parent ? model.GetSymbolInfo(parent, ct).Symbol : null);
            if (symbol is not null && KindOf(symbol) is { } kind)
            {
                spans.Add(new ClassifiedSpan(token.Span, kind));
                if (symbol.IsDeprecated)
                {
                    spans.Add(new ClassifiedSpan(token.Span, ClassificationKind.DeprecatedSymbol));
                }
            }
        }

        return spans.ToImmutable();
    }

    private static ClassificationKind? KindOf(Compiler.Symbols.Symbol symbol) => symbol switch
    {
        Compiler.Symbols.UnitSymbol => ClassificationKind.UnitName,
        Compiler.Types.ClassTypeSymbol => ClassificationKind.ClassName,
        Compiler.Types.InterfaceTypeSymbol => ClassificationKind.InterfaceName,
        Compiler.Types.EnumTypeSymbol => ClassificationKind.EnumName,
        Compiler.Symbols.TypeParameterSymbol or Compiler.Types.TypeSymbol { TypeKind: Compiler.Types.TypeKind.TypeParameter } => ClassificationKind.TypeParameter,
        Compiler.Types.TypeSymbol => ClassificationKind.TypeName,
        Compiler.Symbols.EnumMemberSymbol => ClassificationKind.EnumMember,
        Compiler.Symbols.ConstantSymbol => ClassificationKind.Constant,
        Compiler.Symbols.VariableSymbol { IsGlobal: true } => ClassificationKind.GlobalVariable,
        Compiler.Symbols.VariableSymbol => ClassificationKind.LocalVariable,
        Compiler.Symbols.ParameterSymbol => ClassificationKind.Parameter,
        Compiler.Symbols.FieldSymbol { IsClassVar: true } => ClassificationKind.GlobalVariable,
        Compiler.Symbols.FieldSymbol => ClassificationKind.Field,
        Compiler.Symbols.PropertySymbol => ClassificationKind.Property,
        Compiler.Symbols.MethodSymbol => ClassificationKind.Method,
        Compiler.Symbols.RoutineSymbol or Compiler.Symbols.IntrinsicSymbol => ClassificationKind.Routine,
        { Kind: Compiler.Symbols.SymbolKind.Label } => ClassificationKind.Label,
        _ => null,
    };

    private static ImmutableArray<ClassifiedSpan> Classify(ImmutableArray<SyntaxToken> tokens, TextSpan range,
                                                          Func<ImmutableArray<SyntaxToken>, int, bool> isContextualKeyword)
    {
        var builder = new SpanBuilder(range);
        var inAsm = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.FullSpan.Start >= range.End)
            {
                break;
            }

            AddTrivia(builder, token.LeadingTrivia, ref inAsm);
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
                    ClassifyToken(builder, tokens, i, isContextualKeyword);
                }
            }

            AddTrivia(builder, token.TrailingTrivia, ref inAsm);
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

    /// <param name="offset">Added to the token's positions: non-zero for a token re-lexed from skipped trivia.</param>
    private static void ClassifyToken(SpanBuilder builder, ImmutableArray<SyntaxToken> tokens, int index,
                                      Func<ImmutableArray<SyntaxToken>, int, bool> isContextualKeyword, int offset = 0)
    {
        var token = tokens[index];
        var span = new TextSpan(token.Span.Start + offset, token.Span.Length);
        switch (token.Kind)
        {
            case SyntaxKind.EndOfFileToken:
                return;
            case SyntaxKind.IdentifierToken when SyntaxFacts.IsReservedWord(token.Text):
                builder.Add(span, ClassificationKind.Keyword);   // a reserved word the parser took as a name (operator In)
                return;
            case SyntaxKind.IdentifierToken:
                builder.Add(span, isContextualKeyword(tokens, index) ? ClassificationKind.ContextualKeyword : ClassificationKind.Identifier);
                return;
            case SyntaxKind.IntegerLiteralToken or SyntaxKind.RealLiteralToken:
                builder.Add(span, ClassificationKind.NumberLiteral);
                return;
            case SyntaxKind.StringLiteralToken:
                ClassifyString(builder, token, span.Start);
                return;
        }

        if (SyntaxFacts.IsKeyword(token.Kind))
        {
            builder.Add(span, ClassificationKind.Keyword);
        }
        else if (SyntaxFacts.IsPunctuation(token.Kind))
        {
            builder.Add(span, ClassificationKind.Punctuation);
        }
        else
        {
            builder.Add(span, ClassificationKind.Operator);   // operators and the lone symbols # $ &
        }
    }

    /// <summary>
    /// A <c>SkippedTokensTrivia</c> piece (C-248, CMP-710): the lexer's illegal characters, or a whole token the parser skipped
    /// while recovering. The text is lexed again on its own: what the lexer rejects (illegal characters, unterminated
    /// quoted parts) is <see cref="ClassificationKind.Invalid"/>, a skipped token keeps its lexical class (no contextual
    /// keyword: the parser did not use it as one).
    /// </summary>
    private static void ClassifySkipped(SpanBuilder builder, SyntaxTrivia piece, ref bool inAsm)
    {
        var tokens = Lexer.Tokenize(Compiler.SourceText.From(SkippedPath, piece.Text));
        var offset = piece.Span.Start;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            AddRelexedTrivia(builder, token.LeadingTrivia, offset, inAsm);
            var span = new TextSpan(token.Span.Start + offset, token.Span.Length);
            if (token.Kind == SyntaxKind.EndOfFileToken)
            {
                // nothing of its own
            }
            else if (inAsm && token.Kind != SyntaxKind.EndKeyword)
            {
                builder.AddAsm(span);           // CMP-714 inside a skipped asm block (C-253)
            }
            else if (IsAsm(token))
            {
                inAsm = true;
                builder.Add(span, ClassificationKind.Keyword);
            }
            else
            {
                inAsm = false;                  // not in asm, or its closing end
                ClassifyToken(builder, tokens, i, static (_, _) => false, offset);
            }

            AddRelexedTrivia(builder, token.TrailingTrivia, offset, inAsm);
        }
    }

    /// <summary>
    /// Trivia of a re-lexed skipped token: only illegal characters (Invalid; asm text inside an asm block) can occur
    /// besides whitespace.
    /// </summary>
    private static void AddRelexedTrivia(SpanBuilder builder, ImmutableArray<SyntaxTrivia> trivia, int offset, bool inAsm)
    {
        foreach (var piece in trivia)
        {
            var span = new TextSpan(piece.Span.Start + offset, piece.Span.Length);
            if (piece.Kind == SyntaxKind.EndOfLineTrivia)
            {
                builder.EndAsmLine();
            }
            else if (piece.Kind == SyntaxKind.WhitespaceTrivia)
            {
                // uncovered
            }
            else if (inAsm)
            {
                builder.AddAsm(span);
            }
            else
            {
                builder.Add(span, ClassificationKind.Invalid);
            }
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
    private static void ClassifyString(SpanBuilder builder, SyntaxToken token, int start)
    {
        var text = token.Text;
        var singleChar = token.Value is string { Length: 1 } && text[0] == '\'' && QuotedPartEnd(text, 0, out _) == text.Length;
        var i = 0;
        while (i < text.Length)
        {
            ClassificationKind kind;
            var partStart = i;
            if (text[i] == '\'')
            {
                i = QuotedPartEnd(text, i, out var terminated);
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

    /// <summary>
    /// End (exclusive) of the quoted part starting at <paramref name="quote"/>; '' is an escaped quote. Scans exactly
    /// as the lexer does (<c>PascalLexer.ScanString</c>): <paramref name="terminated"/> is true only when a closing
    /// quote was found, i.e. when the lexer reported no E1020 for this part; a part ending in an escaped <c>''</c> at
    /// the end of the token is unterminated (C-171).
    /// </summary>
    private static int QuotedPartEnd(string text, int quote, out bool terminated)
    {
        terminated = false;
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

                terminated = true;
                return i + 1;
            }

            i++;
        }

        return i;
    }

    private static bool IsAsm(SyntaxToken token) =>
        token.Kind == SyntaxKind.IdentifierToken && token.Text.Length == 3
        && (token.Text[0] | 0x20) == 'a' && (token.Text[1] | 0x20) == 's' && (token.Text[2] | 0x20) == 'm';

    private static void AddTrivia(SpanBuilder builder, ImmutableArray<SyntaxTrivia> trivia, ref bool inAsm)
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
                    // SkippedTokensTrivia: re-lexed (C-248), with the asm state (C-253)
                    ClassifySkipped(builder, piece, ref inAsm);
                    continue;
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
