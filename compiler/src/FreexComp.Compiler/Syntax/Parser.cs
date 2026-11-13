using System.Collections.Immutable;
using FreexComp.Compiler.Text;

namespace FreexComp.Compiler.Syntax;

/// <summary>
/// Hand-written recursive-descent parser following grammar.md, one method per production (CMP-112). Never throws;
/// always builds a tree for the whole file (LANG-180). Error recovery: a missing required token is inserted as a
/// zero-width missing token with E1101 (LANG-181); unexpected tokens are skipped into <c>SkippedTokensTrivia</c> of the
/// next accepted token with one E1100 per contiguous run (LANG-182); at most 100 errors, then E1199 (LANG-184).
/// Only one error is reported per token position, so a cascade at the same place stays silent.
/// </summary>
internal sealed class Parser
{
    private const int MaxErrors = 100;

    private readonly SourceText _text;
    private readonly ImmutableArray<SyntaxToken> _tokens;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly Dictionary<SourceText, List<SyntaxTrivia>> _skipped = new(ReferenceEqualityComparer.Instance);
    private int _position;
    private bool _skipping;              // inside a contiguous run of skipped tokens
    private int _lastTakenEnd;
    private SourceText? _lastTakenFile;
    private (SourceText? File, int Start) _lastErrorPosition = (null, -1);
    private int _errorCount;

    public Parser(SourceText text, ImmutableArray<SyntaxToken> tokens)
    {
        _text = text;
        _tokens = tokens;
    }

    public ImmutableArray<Diagnostic> Diagnostics => [.. _diagnostics];

    private SyntaxToken Current => _tokens[_position];

    private SyntaxKind CurrentKind => Current.Kind;

    private SyntaxToken Peek(int offset) => _tokens[Math.Min(_position + offset, _tokens.Length - 1)];

    // ------------------------------------------------------------------ grammar.md §1: compilation units

    public CompilationUnitSyntax ParseCompilationUnit()
    {
        if (CurrentKind == SyntaxKind.UnitKeyword)
        {
            return ParseUnit();
        }

        return ParseProgram();
    }

    private ProgramSyntax ParseProgram()
    {
        SyntaxToken? keyword = null;
        IdentifierNameSyntax? name = null;
        ProgramParameterListSyntax? parameters = null;
        SyntaxToken? semicolon = null;

        if (CurrentKind is SyntaxKind.ProgramKeyword or SyntaxKind.LibraryKeyword || IsPackageHeading())
        {
            if (CurrentKind != SyntaxKind.ProgramKeyword)
            {
                // LANG-151
                ReportAt(Current, "E2900", Current.Text.ToLowerInvariant());
            }

            keyword = CurrentKind == SyntaxKind.IdentifierToken ? Take().WithContextualKind(SyntaxKind.PackageKeyword) : Take();
            name = ParseIdentifierName();
            if (CurrentKind == SyntaxKind.OpenParenToken)
            {
                parameters = ParseProgramParameterList();
            }

            semicolon = Expect(SyntaxKind.SemicolonToken);
        }
        else if (!CanStartProgramBody(CurrentKind) && CurrentKind != SyntaxKind.EndOfFileToken)
        {
            ReportAt(Current, "E1111");
        }

        var uses = CurrentKind == SyntaxKind.UsesKeyword ? ParseUsesClause() : null;
        var block = ParseBlock();
        var dot = Expect(SyntaxKind.DotToken);
        return new ProgramSyntax(keyword, name, parameters, semicolon, uses, block, dot, ParseEndOfFile());
    }

    private bool IsPackageHeading() =>
        IsContextual(Current, SyntaxKind.PackageKeyword) && Peek(1).Kind == SyntaxKind.IdentifierToken;

    private static bool CanStartProgramBody(SyntaxKind kind) => kind is SyntaxKind.UsesKeyword or SyntaxKind.BeginKeyword
        or SyntaxKind.LabelKeyword or SyntaxKind.ConstKeyword or SyntaxKind.ResourceStringKeyword or SyntaxKind.TypeKeyword
        or SyntaxKind.VarKeyword or SyntaxKind.ThreadVarKeyword or SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword
        or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword or SyntaxKind.ClassKeyword
        or SyntaxKind.OpenBracketToken;

    private ProgramParameterListSyntax ParseProgramParameterList()
    {
        var open = Take();
        var list = ParseSeparated(ParseIdentifierName, SyntaxKind.CommaToken);
        var close = Expect(SyntaxKind.CloseParenToken);
        return new ProgramParameterListSyntax(open, list, close);
    }

    private UnitSyntax ParseUnit()
    {
        var unitKeyword = Take();
        var name = ParseQualifiedName();
        var hints = ParseHintDirectives();
        var semicolon = Expect(SyntaxKind.SemicolonToken);
        var interfaceSection = ParseInterfaceSection();
        var implementationSection = ParseImplementationSection();

        InitializationSectionSyntax? initialization = null;
        FinalizationSectionSyntax? finalization = null;
        if (CurrentKind is SyntaxKind.InitializationKeyword or SyntaxKind.BeginKeyword)
        {
            var keyword = Take();
            initialization = new InitializationSectionSyntax(keyword, ParseStatementList());
            if (keyword.Kind == SyntaxKind.InitializationKeyword && CurrentKind == SyntaxKind.FinalizationKeyword)
            {
                var finalizationKeyword = Take();
                finalization = new FinalizationSectionSyntax(finalizationKeyword, ParseStatementList());
            }
        }

        var end = Expect(SyntaxKind.EndKeyword);
        var dot = Expect(SyntaxKind.DotToken);
        return new UnitSyntax(unitKeyword, name, hints, semicolon, interfaceSection, implementationSection,
            initialization, finalization, end, dot, ParseEndOfFile());
    }

    private InterfaceSectionSyntax ParseInterfaceSection()
    {
        var keyword = Expect(SyntaxKind.InterfaceKeyword, "E1112");
        var uses = CurrentKind == SyntaxKind.UsesKeyword ? ParseUsesClause() : null;
        var declarations = ParseDeclarations(SectionKind.Interface);
        return new InterfaceSectionSyntax(keyword, uses, declarations);
    }

    private ImplementationSectionSyntax ParseImplementationSection()
    {
        var keyword = Expect(SyntaxKind.ImplementationKeyword, "E1113");
        var uses = CurrentKind == SyntaxKind.UsesKeyword ? ParseUsesClause() : null;
        var declarations = ParseDeclarations(SectionKind.Implementation);
        return new ImplementationSectionSyntax(keyword, uses, declarations);
    }

    private UsesClauseSyntax ParseUsesClause()
    {
        var uses = Take();
        var units = ParseSeparated(ParseUsedUnit, SyntaxKind.CommaToken);
        var semicolon = Expect(SyntaxKind.SemicolonToken);
        return new UsesClauseSyntax(uses, units, semicolon);
    }

    private UsedUnitSyntax ParseUsedUnit()
    {
        var name = ParseQualifiedName();
        if (CurrentKind != SyntaxKind.InKeyword)
        {
            return new UsedUnitSyntax(name, null, null);
        }

        var inKeyword = Take();
        var path = Expect(SyntaxKind.StringLiteralToken);
        return new UsedUnitSyntax(name, inKeyword, path);
    }

    /// <summary>
    /// After the final <c>.</c>: anything but trivia is skipped with H1001 (LANG-150); the EndOfFileToken carries it.
    /// </summary>
    private SyntaxToken ParseEndOfFile()
    {
        if (CurrentKind != SyntaxKind.EndOfFileToken)
        {
            Report(Current, "H1001");
            while (CurrentKind != SyntaxKind.EndOfFileToken)
            {
                SkipToken(report: false);
            }
        }

        return Take();
    }

    // ------------------------------------------------------------------ grammar.md §2: declarations and blocks

    private enum SectionKind { Block, Interface, Implementation }

    private BlockSyntax ParseBlock()
    {
        var declarations = ParseDeclarations(SectionKind.Block);
        return new BlockSyntax(declarations, ParseCompoundStatement());
    }

    /// <summary>
    /// <c>{ declSection }</c> / <c>{ interfaceDecl }</c>. Routines and attribute lists are not parsed yet (C-015,
    /// C-019): their tokens are skipped up to the next section keyword or the end of the declaration part.
    /// </summary>
    private SyntaxList<SyntaxNode> ParseDeclarations(SectionKind section)
    {
        var declarations = ImmutableArray.CreateBuilder<SyntaxNode>();
        while (!EndsDeclarations(CurrentKind, section))
        {
            switch (CurrentKind)
            {
                case SyntaxKind.LabelKeyword when section != SectionKind.Interface:
                    declarations.Add(ParseLabelSection());
                    break;
                case SyntaxKind.ConstKeyword or SyntaxKind.ResourceStringKeyword:
                    declarations.Add(ParseConstSection());
                    break;
                case SyntaxKind.TypeKeyword:
                    declarations.Add(ParseTypeSection());
                    break;
                case SyntaxKind.VarKeyword or SyntaxKind.ThreadVarKeyword:
                    declarations.Add(ParseVarSection());
                    break;
                default:
                    do
                    {
                        SkipToken();
                    }
                    while (!EndsDeclarations(CurrentKind, section) && !StartsSection(CurrentKind));

                    break;
            }
        }

        return new SyntaxList<SyntaxNode>(declarations.ToImmutable());
    }

    private static bool StartsSection(SyntaxKind kind) => kind is SyntaxKind.LabelKeyword or SyntaxKind.ConstKeyword
        or SyntaxKind.ResourceStringKeyword or SyntaxKind.TypeKeyword or SyntaxKind.VarKeyword or SyntaxKind.ThreadVarKeyword;

    /// <summary><c>label labelId { , labelId } ;</c></summary>
    private LabelSectionSyntax ParseLabelSection()
    {
        var keyword = Take();
        var labels = ParseSeparated(ParseLabel, SyntaxKind.CommaToken);
        return new LabelSectionSyntax(keyword, labels, Expect(SyntaxKind.SemicolonToken));
    }

    /// <summary><c>labelId = ident | integerLiteral</c>; anything else is E1117.</summary>
    private ExpressionSyntax ParseLabel()
    {
        switch (CurrentKind)
        {
            case SyntaxKind.IntegerLiteralToken:
                return new LiteralExpressionSyntax(Take());
            case SyntaxKind.IdentifierToken:
                return new IdentifierNameSyntax(Take());
            default:
                ReportAt(Current, CurrentKind == SyntaxKind.EndOfFileToken ? "E1108" : "E1117");
                return new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));
        }
    }

    /// <summary><c>( const | resourcestring ) constDecl { constDecl }</c></summary>
    private ConstSectionSyntax ParseConstSection()
    {
        var keyword = Take();
        var declarations = ImmutableArray.CreateBuilder<ConstDeclarationSyntax>();
        do
        {
            declarations.Add(ParseConstDeclaration());
        }
        while (CurrentKind == SyntaxKind.IdentifierToken);

        return new ConstSectionSyntax(keyword, new SyntaxList<ConstDeclarationSyntax>(declarations.ToImmutable()));
    }

    /// <summary><c>ident = constExpr [ hints ] ;</c> or <c>ident : type = typedConst [ hints ] ;</c></summary>
    private ConstDeclarationSyntax ParseConstDeclaration()
    {
        var identifier = ExpectIdentifier();
        SyntaxToken? colon = null;
        TypeSyntax? type = null;
        SyntaxNode value;
        SyntaxToken equals;
        if (CurrentKind == SyntaxKind.ColonToken)
        {
            colon = Take();
            type = ParseType();
            equals = Expect(SyntaxKind.EqualsToken);
            value = ParseTypedConstant();
        }
        else
        {
            equals = Expect(SyntaxKind.EqualsToken);
            value = ParseExpression();
        }

        var hints = ParseHintDirectives();
        return new ConstDeclarationSyntax(identifier, colon, type, equals, value, hints, Expect(SyntaxKind.SemicolonToken));
    }

    /// <summary>
    /// <c>typedConst = constExpr | ( typedConst { , typedConst } ) | ( [ ident : typedConst { ; ident : typedConst } [ ; ] ] )</c>.
    /// <c>(</c> followed by <c>ident :</c> or <c>)</c> is a record constant; a single parenthesised expression stays
    /// an expression and may continue with operators (<c>(1 + 2) * 3</c>).
    /// </summary>
    private SyntaxNode ParseTypedConstant()
    {
        if (CurrentKind != SyntaxKind.OpenParenToken)
        {
            return ParseExpression();
        }

        var open = Take();
        var items = ImmutableArray.CreateBuilder<SyntaxNodeOrToken>();
        if (CurrentKind == SyntaxKind.CloseParenToken || (CurrentKind == SyntaxKind.IdentifierToken && Peek(1).Kind == SyntaxKind.ColonToken))
        {
            while (CurrentKind == SyntaxKind.IdentifierToken && Peek(1).Kind == SyntaxKind.ColonToken)
            {
                var name = ParseIdentifierName();
                var colon = Take();
                items.Add(new RecordConstantFieldSyntax(name, colon, ParseTypedConstant()));
                if (CurrentKind != SyntaxKind.SemicolonToken)
                {
                    break;
                }

                items.Add(Take());
            }

            return new TypedConstantAggregateSyntax(open, new SeparatedSyntaxList<SyntaxNode>(items.ToImmutable()),
                Expect(SyntaxKind.CloseParenToken));
        }

        var first = ParseTypedConstant();
        items.Add(first);
        if (CurrentKind != SyntaxKind.CommaToken)
        {
            var close = Expect(SyntaxKind.CloseParenToken);
            return first is ExpressionSyntax expression
                ? ParseExpression(new ParenthesizedExpressionSyntax(open, expression, close))
                : new TypedConstantAggregateSyntax(open, new SeparatedSyntaxList<SyntaxNode>(items.ToImmutable()), close);
        }

        while (CurrentKind == SyntaxKind.CommaToken)
        {
            items.Add(Take());
            items.Add(ParseTypedConstant());
        }

        return new TypedConstantAggregateSyntax(open, new SeparatedSyntaxList<SyntaxNode>(items.ToImmutable()),
            Expect(SyntaxKind.CloseParenToken));
    }

    /// <summary><c>type typeDecl { typeDecl }</c></summary>
    private TypeSectionSyntax ParseTypeSection()
    {
        var keyword = Take();
        var declarations = ImmutableArray.CreateBuilder<TypeDeclarationSyntax>();
        do
        {
            declarations.Add(ParseTypeDeclaration());
        }
        while (CurrentKind == SyntaxKind.IdentifierToken);

        return new TypeSectionSyntax(keyword, new SyntaxList<TypeDeclarationSyntax>(declarations.ToImmutable()));
    }

    /// <summary><c>ident = [ type ] type [ hints ] ;</c></summary>
    private TypeDeclarationSyntax ParseTypeDeclaration()
    {
        var identifier = ExpectIdentifier();
        var equals = Expect(SyntaxKind.EqualsToken);
        var typeKeyword = CurrentKind == SyntaxKind.TypeKeyword ? Take() : (SyntaxToken?)null;
        var type = ParseType();
        var hints = ParseHintDirectives();
        return new TypeDeclarationSyntax(identifier, equals, typeKeyword, type, hints, Expect(SyntaxKind.SemicolonToken));
    }

    /// <summary><c>( var | threadvar ) varDecl { varDecl }</c></summary>
    private VarSectionSyntax ParseVarSection()
    {
        var keyword = Take();
        var declarations = ImmutableArray.CreateBuilder<VarDeclarationSyntax>();
        do
        {
            declarations.Add(ParseVarDeclaration());
        }
        while (CurrentKind == SyntaxKind.IdentifierToken);

        return new VarSectionSyntax(keyword, new SyntaxList<VarDeclarationSyntax>(declarations.ToImmutable()));
    }

    /// <summary><c>identList : type [ = typedConst ] [ hints ] ;</c></summary>
    private VarDeclarationSyntax ParseVarDeclaration()
    {
        var identifiers = ParseSeparated(ParseIdentifierName, SyntaxKind.CommaToken);
        var colon = Expect(SyntaxKind.ColonToken);
        var type = ParseType();
        SyntaxToken? equals = null;
        SyntaxNode? initializer = null;
        if (CurrentKind == SyntaxKind.EqualsToken)
        {
            equals = Take();
            initializer = ParseTypedConstant();
        }

        var hints = ParseHintDirectives();
        return new VarDeclarationSyntax(identifiers, colon, type, equals, initializer, hints, Expect(SyntaxKind.SemicolonToken));
    }

    // ------------------------------------------------------------------ grammar.md §3: types

    /// <summary>
    /// <c>type</c>. Type identifiers and <c>string [ "[" constExpr "]" ]</c> so far (C-014 adds the rest); anything else
    /// is E1106 and a missing named type.
    /// </summary>
    private TypeSyntax ParseType()
    {
        switch (CurrentKind)
        {
            case SyntaxKind.IdentifierToken:
                return new NamedTypeSyntax(ParseQualifiedName());
            case SyntaxKind.StringKeyword:
                var keyword = Take();
                if (CurrentKind != SyntaxKind.OpenBracketToken)
                {
                    return new StringTypeSyntax(keyword, null, null, null);
                }

                var open = Take();
                var length = ParseExpression();
                return new StringTypeSyntax(keyword, open, length, Expect(SyntaxKind.CloseBracketToken));
            default:
                ReportAt(Current, CurrentKind == SyntaxKind.EndOfFileToken ? "E1108" : "E1106", ExpectedArgs());
                return new NamedTypeSyntax(new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken)));
        }
    }

    // ------------------------------------------------------------------ grammar.md §7: expressions

    /// <summary>
    /// <c>expression = simpleExpr [ relOp simpleExpr ]</c>; a second relational operator is E1110 (LANG-170) and is
    /// parsed left-associatively. <paramref name="first"/>: an already parsed leading factor.
    /// </summary>
    private ExpressionSyntax ParseExpression(ExpressionSyntax? first = null)
    {
        var left = ParseSimpleExpression(first);
        var chained = false;
        while (SyntaxFacts.GetBinaryPrecedence(CurrentKind) == 2)
        {
            if (chained)
            {
                ReportAt(Current, "E1110");
            }

            var op = Take();
            left = new BinaryExpressionSyntax(left, op, ParseSimpleExpression(null));
            chained = true;
        }

        return left;
    }

    /// <summary><c>simpleExpr = [ + | - ] term { addOp term }</c>; the leading sign covers the first term (LANG-171).</summary>
    private ExpressionSyntax ParseSimpleExpression(ExpressionSyntax? first)
    {
        var left = first is null && CurrentKind is SyntaxKind.PlusToken or SyntaxKind.MinusToken
            ? new UnaryExpressionSyntax(Take(), ParseTerm(null))
            : ParseTerm(first);
        while (SyntaxFacts.GetBinaryPrecedence(CurrentKind) == 3)
        {
            var op = Take();
            left = new BinaryExpressionSyntax(left, op, ParseTerm(null));
        }

        return left;
    }

    /// <summary><c>term = factor { mulOp factor }</c></summary>
    private ExpressionSyntax ParseTerm(ExpressionSyntax? first)
    {
        var left = first ?? ParseFactor();
        while (SyntaxFacts.GetBinaryPrecedence(CurrentKind) == 4)
        {
            var op = Take();
            left = new BinaryExpressionSyntax(left, op, ParseFactor());
        }

        return left;
    }

    /// <summary>
    /// <c>factor</c>: <c>not</c>/sign factor, literal, <c>nil</c>, <c>( expression )</c>, identifier. Designators,
    /// calls, set constructors, <c>@</c>, <c>inherited</c> follow with C-016; anything else is E1103.
    /// </summary>
    private ExpressionSyntax ParseFactor()
    {
        switch (CurrentKind)
        {
            case SyntaxKind.NotKeyword or SyntaxKind.PlusToken or SyntaxKind.MinusToken:
                var op = Take();
                return new UnaryExpressionSyntax(op, ParseFactor());
            case SyntaxKind.IntegerLiteralToken or SyntaxKind.RealLiteralToken or SyntaxKind.StringLiteralToken or SyntaxKind.NilKeyword:
                return new LiteralExpressionSyntax(Take());
            case SyntaxKind.OpenParenToken:
                var open = Take();
                var expression = ParseExpression();
                return new ParenthesizedExpressionSyntax(open, expression, Expect(SyntaxKind.CloseParenToken));
            case SyntaxKind.IdentifierToken:
                return new IdentifierNameSyntax(Take());
            default:
                ReportAt(Current, CurrentKind == SyntaxKind.EndOfFileToken ? "E1108" : "E1103", ExpectedArgs());
                return new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));
        }
    }

    /// <summary>Arguments for "… expected but '%s' found" codes; none for E1108 at the end of the file.</summary>
    private object[] ExpectedArgs() => CurrentKind == SyntaxKind.EndOfFileToken ? [] : [Current.Text];

    private static bool EndsDeclarations(SyntaxKind kind, SectionKind section) => kind switch
    {
        SyntaxKind.EndOfFileToken or SyntaxKind.BeginKeyword => true,
        SyntaxKind.ImplementationKeyword or SyntaxKind.InitializationKeyword or SyntaxKind.FinalizationKeyword
            or SyntaxKind.EndKeyword => section != SectionKind.Block || kind == SyntaxKind.EndKeyword,
        _ => false,
    };

    private SyntaxList<HintDirectiveSyntax> ParseHintDirectives()
    {
        var hints = ImmutableArray.CreateBuilder<HintDirectiveSyntax>();
        while (true)
        {
            if (IsContextual(Current, SyntaxKind.DeprecatedKeyword))
            {
                var keyword = Take().WithContextualKind(SyntaxKind.DeprecatedKeyword);
                var message = CurrentKind == SyntaxKind.StringLiteralToken ? Take() : (SyntaxToken?)null;
                hints.Add(new HintDirectiveSyntax(keyword, message));
            }
            else if (IsContextual(Current, SyntaxKind.PlatformKeyword) || IsContextual(Current, SyntaxKind.ExperimentalKeyword))
            {
                var kind = IsContextual(Current, SyntaxKind.PlatformKeyword) ? SyntaxKind.PlatformKeyword : SyntaxKind.ExperimentalKeyword;
                hints.Add(new HintDirectiveSyntax(Take().WithContextualKind(kind), null));
            }
            else
            {
                return new SyntaxList<HintDirectiveSyntax>(hints.ToImmutable());
            }
        }
    }

    // ------------------------------------------------------------------ grammar.md §6: statements

    private CompoundStatementSyntax ParseCompoundStatement()
    {
        var begin = Expect(SyntaxKind.BeginKeyword);
        var statements = ParseStatementList();
        var end = Expect(SyntaxKind.EndKeyword);
        return new CompoundStatementSyntax(begin, statements, end);
    }

    /// <summary><c>[ statement ] { ; [ statement ] }</c>; an absent statement next to a <c>;</c> is an empty statement.</summary>
    private SeparatedSyntaxList<StatementSyntax> ParseStatementList()
    {
        var items = ImmutableArray.CreateBuilder<SyntaxNodeOrToken>();
        if (EndsStatementList(CurrentKind))
        {
            return default;
        }

        while (true)
        {
            items.Add(ParseStatement());
            if (CurrentKind == SyntaxKind.SemicolonToken)
            {
                items.Add(Take());
            }
            else if (EndsStatementList(CurrentKind))
            {
                break;
            }
            else
            {
                // only a statement start can be here (ParseStatement skips everything else): ';' is missing
                items.Add(Expect(SyntaxKind.SemicolonToken));
            }
        }

        return new SeparatedSyntaxList<StatementSyntax>(items.ToImmutable());
    }

    private static bool EndsStatementList(SyntaxKind kind) => kind is SyntaxKind.EndKeyword or SyntaxKind.UntilKeyword
        or SyntaxKind.FinallyKeyword or SyntaxKind.ExceptKeyword or SyntaxKind.FinalizationKeyword or SyntaxKind.EndOfFileToken;

    /// <summary>
    /// A statement. Only compound and empty statements exist yet (C-017 adds the rest): other tokens are skipped up to a
    /// statement-level synchronisation point (LANG-183) or a <c>begin</c>.
    /// </summary>
    private StatementSyntax ParseStatement()
    {
        if (CurrentKind is not (SyntaxKind.BeginKeyword or SyntaxKind.SemicolonToken) && !EndsStatementList(CurrentKind))
        {
            do
            {
                SkipToken();
            }
            while (CurrentKind is not (SyntaxKind.BeginKeyword or SyntaxKind.SemicolonToken or SyntaxKind.ElseKeyword)
                   && !EndsStatementList(CurrentKind));
        }

        return CurrentKind == SyntaxKind.BeginKeyword ? ParseCompoundStatement() : new EmptyStatementSyntax();
    }

    // ------------------------------------------------------------------ names

    private IdentifierNameSyntax ParseIdentifierName() => new(ExpectIdentifier());

    /// <summary><c>qualIdent = ident { . ident }</c> (LANG-152).</summary>
    private NameSyntax ParseQualifiedName()
    {
        NameSyntax name = ParseIdentifierName();
        while (CurrentKind == SyntaxKind.DotToken && Peek(1).Kind == SyntaxKind.IdentifierToken)
        {
            var dot = Take();
            name = new QualifiedNameSyntax(name, dot, ParseIdentifierName());
        }

        return name;
    }

    private SeparatedSyntaxList<T> ParseSeparated<T>(Func<T> parseItem, SyntaxKind separator)
        where T : SyntaxNode
    {
        var items = ImmutableArray.CreateBuilder<SyntaxNodeOrToken>();
        items.Add(parseItem());
        while (CurrentKind == separator)
        {
            items.Add(Take());
            items.Add(parseItem());
        }

        return new SeparatedSyntaxList<T>(items.ToImmutable());
    }

    // ------------------------------------------------------------------ token handling and recovery

    private static bool IsContextual(SyntaxToken token, SyntaxKind kind) =>
        token.Kind == SyntaxKind.IdentifierToken && !token.Text.StartsWith('&')
        && SyntaxFacts.GetKeywordKind(token.Text) == kind;

    /// <summary>
    /// Consumes the current token; pending skipped tokens of the same file become its leading trivia (tokens of
    /// include files are children of this tree too, but trivia never crosses files).
    /// </summary>
    private SyntaxToken Take()
    {
        var token = Current;
        if (token.Kind != SyntaxKind.EndOfFileToken)
        {
            _position++;
        }

        var file = FileOf(token);
        if (_skipped.TryGetValue(file, out var pending) && pending.Count > 0)
        {
            token = token.WithLeadingTrivia([.. pending, .. token.LeadingTrivia]);
            pending.Clear();
        }

        _skipping = false;
        _lastTakenEnd = token.FullSpan.End;
        _lastTakenFile = file;
        return token;
    }

    /// <summary>The current token if it has <paramref name="kind"/>; else a missing token and E1101 (E1108 at EOF).</summary>
    private SyntaxToken Expect(SyntaxKind kind, string code = "E1101")
    {
        if (CurrentKind == kind)
        {
            return Take();
        }

        if (CurrentKind == SyntaxKind.EndOfFileToken)
        {
            ReportAt(Current, "E1108");
        }
        else if (code == "E1101")
        {
            ReportAt(Current, code, SyntaxFacts.GetText(kind), Current.Text);
        }
        else
        {
            ReportAt(Current, code, Current.Text);
        }

        return Missing(kind);
    }

    private SyntaxToken ExpectIdentifier()
    {
        if (CurrentKind == SyntaxKind.IdentifierToken)
        {
            return Take();
        }

        if (CurrentKind == SyntaxKind.EndOfFileToken)
        {
            ReportAt(Current, "E1108");
        }
        else
        {
            ReportAt(Current, "E1102", Current.Text);
        }

        return Missing(SyntaxKind.IdentifierToken);
    }

    /// <summary>A zero-width token right after the last accepted token, in that token's file.</summary>
    private SyntaxToken Missing(SyntaxKind kind) =>
        SyntaxToken.Missing(kind, _lastTakenEnd, Current.DirectiveState, _lastTakenFile ?? _text);

    /// <summary>Skips the current token into pending trivia; the first of a contiguous run reports E1100 (LANG-182).</summary>
    private void SkipToken(bool report = true)
    {
        var token = Current;
        if (report && !_skipping)
        {
            ReportAt(token, "E1100", token.Text);
        }

        var file = FileOf(token);
        if (!_skipped.TryGetValue(file, out var pending))
        {
            pending = [];
            _skipped.Add(file, pending);
        }

        pending.AddRange(token.LeadingTrivia);
        pending.Add(new SyntaxTrivia(SyntaxKind.SkippedTokensTrivia, token.Text, token.Span.Start));
        pending.AddRange(token.TrailingTrivia);
        _skipping = true;
        _position++;
    }

    private SourceText FileOf(SyntaxToken token) => token.File ?? _text;

    private void ReportAt(SyntaxToken token, string code, params object[] args)
    {
        // one error per position: the first explains the problem, the rest would be cascades
        var position = (FileOf(token), token.Span.Start);
        if (position == _lastErrorPosition)
        {
            return;
        }

        _lastErrorPosition = position;
        Report(token, code, args);
    }

    /// <summary>A diagnostic at <paramref name="token"/>'s span in the token's own file.</summary>
    private void Report(SyntaxToken token, string code, params object[] args)
    {
        var file = FileOf(token);
        var span = token.Span;
        var location = new Location(file.Path, span,
            new LinePositionSpan(file.Lines.GetLinePosition(span.Start), file.Lines.GetLinePosition(span.End)));
        if (DiagnosticCatalog.Get(code).DefaultSeverity == DiagnosticSeverity.Error)
        {
            _errorCount++;
            if (_errorCount > MaxErrors)
            {
                if (_errorCount == MaxErrors + 1)
                {
                    _diagnostics.Add(Diagnostic.Create("E1199", location));
                }

                return;
            }
        }

        _diagnostics.Add(Diagnostic.Create(code, location, args));
    }
}
