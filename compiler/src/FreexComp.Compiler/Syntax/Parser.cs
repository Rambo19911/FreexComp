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
    private readonly List<SyntaxTrivia> _skipped = [];
    private int _position;
    private int _lastTakenEnd;
    private int _lastErrorPosition = -1;
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
            Report(Current.Span, "H1001");
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
    /// <c>{ declSection }</c> / <c>{ interfaceDecl }</c>. Declaration sections are not parsed yet (C-013, C-015): their
    /// tokens are skipped up to the token that ends the declaration part.
    /// </summary>
    private SyntaxList<SyntaxNode> ParseDeclarations(SectionKind section)
    {
        while (!EndsDeclarations(CurrentKind, section))
        {
            SkipToken();
        }

        return default;
    }

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

    /// <summary>Consumes the current token; pending skipped tokens become its leading trivia.</summary>
    private SyntaxToken Take()
    {
        var token = Current;
        if (token.Kind != SyntaxKind.EndOfFileToken)
        {
            _position++;
        }

        if (_skipped.Count > 0)
        {
            token = token.WithLeadingTrivia([.. _skipped, .. token.LeadingTrivia]);
            _skipped.Clear();
        }

        _lastTakenEnd = token.FullSpan.End;
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

    private SyntaxToken Missing(SyntaxKind kind) => SyntaxToken.Missing(kind, _lastTakenEnd, Current.DirectiveState, _text);

    /// <summary>Skips the current token into pending trivia; the first of a contiguous run reports E1100 (LANG-182).</summary>
    private void SkipToken(bool report = true)
    {
        var token = Current;
        if (report && _skipped.Count == 0)
        {
            ReportAt(token, "E1100", token.Text);
        }

        _skipped.AddRange(token.LeadingTrivia);
        _skipped.Add(new SyntaxTrivia(SyntaxKind.SkippedTokensTrivia, token.Text, token.Span.Start));
        _skipped.AddRange(token.TrailingTrivia);
        _position++;
    }

    private void ReportAt(SyntaxToken token, string code, params object[] args)
    {
        // one error per position: the first explains the problem, the rest would be cascades
        if (token.Span.Start == _lastErrorPosition)
        {
            return;
        }

        _lastErrorPosition = token.Span.Start;
        Report(token.Span, code, args);
    }

    private void Report(TextSpan span, string code, params object[] args)
    {
        if (DiagnosticCatalog.Get(code).DefaultSeverity == DiagnosticSeverity.Error)
        {
            _errorCount++;
            if (_errorCount > MaxErrors)
            {
                if (_errorCount == MaxErrors + 1)
                {
                    _diagnostics.Add(Diagnostic.Create("E1199", LocationOf(span)));
                }

                return;
            }
        }

        _diagnostics.Add(Diagnostic.Create(code, LocationOf(span), args));
    }

    private Location LocationOf(TextSpan span) =>
        new(_text.Path, span, new LinePositionSpan(_text.Lines.GetLinePosition(span.Start), _text.Lines.GetLinePosition(span.End)));
}
