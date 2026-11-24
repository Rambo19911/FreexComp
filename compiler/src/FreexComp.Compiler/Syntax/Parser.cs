using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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
    private ImmutableArray<SyntaxToken> _tokens;   // not readonly: '>=' may be split (LANG-706)
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

        if (CurrentKind == SyntaxKind.LibraryKeyword)
        {
            return ParseLibrary();
        }

        return ParseProgram();
    }

    /// <summary>
    /// A fragment for <c>SyntaxFactory.ParseNode&lt;T&gt;</c> (api.md §4.1): the production chosen by
    /// <paramref name="category"/>, then the end of the text; surplus input is skipped with one E1100 on its first token.
    /// </summary>
    public FragmentSyntax ParseFragment(Type category)
    {
        if (CurrentKind == SyntaxKind.EndOfFileToken)
        {
            // C-245: an empty fragment says what it lacks (the category's own code), not E1108 'end.' expected; the parser's
            // further reports at the same position are cascades and stay silent
            const string endOfFile = "end of file";
            if (category == typeof(UsesClauseSyntax))
            {
                ReportAt(Current, "E1101", SyntaxFacts.GetText(SyntaxKind.UsesKeyword), endOfFile);
            }
            else
            {
                ReportAt(Current, category.IsAssignableTo(typeof(ExpressionSyntax)) ? "E1103"
                    : category.IsAssignableTo(typeof(StatementSyntax)) ? "E1104"
                    : category.IsAssignableTo(typeof(TypeSyntax)) ? "E1106"
                    : "E1107", endOfFile);
            }
        }

        SyntaxNode node;
        if (category.IsAssignableTo(typeof(ExpressionSyntax)))
        {
            node = ParseExpression();
        }
        else if (category.IsAssignableTo(typeof(StatementSyntax)))
        {
            node = ParseStatement();
        }
        else if (category.IsAssignableTo(typeof(TypeSyntax)))
        {
            node = ParseType();
        }
        else if (category.IsAssignableTo(typeof(MemberDeclarationSyntax)))
        {
            var attributes = CurrentKind == SyntaxKind.OpenBracketToken ? ParseAttributeLists() : default;
            node = ParseMember(attributes) ?? ParseFieldDeclaration(attributes);
        }
        else if (category == typeof(RoutineDeclarationSyntax))
        {
            node = ParseRoutineDeclaration(hasBody: true, CurrentKind == SyntaxKind.OpenBracketToken ? ParseAttributeLists() : default);
        }
        else if (category == typeof(TypeDeclarationSyntax))
        {
            node = ParseTypeDeclaration();
        }
        else if (category == typeof(VarDeclarationSyntax))
        {
            node = ParseVarDeclaration();
        }
        else if (category == typeof(ConstDeclarationSyntax))
        {
            node = ParseConstDeclaration();
        }
        else if (category == typeof(UsesClauseSyntax))
        {
            node = CurrentKind == SyntaxKind.UsesKeyword
                ? ParseUsesClause()
                : new UsesClauseSyntax(Expect(SyntaxKind.UsesKeyword), ParseSeparated(ParseUsedUnit, SyntaxKind.CommaToken),
                    Expect(SyntaxKind.SemicolonToken));
        }
        else
        {
            throw new ArgumentException($"'{category.Name}' is not a fragment that ParseNode can parse.", nameof(category));
        }

        if (CurrentKind != SyntaxKind.EndOfFileToken)
        {
            ReportAt(Current, "E1100", Current.Text);
            while (CurrentKind != SyntaxKind.EndOfFileToken)
            {
                SkipToken(report: false);
            }
        }

        return new FragmentSyntax(node, Take());
    }

    private ProgramSyntax ParseProgram()
    {
        SyntaxToken? keyword = null;
        IdentifierNameSyntax? name = null;
        ProgramParameterListSyntax? parameters = null;
        SyntaxToken? semicolon = null;

        if (CurrentKind == SyntaxKind.ProgramKeyword || IsPackageHeading())
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

    /// <summary>
    /// LANG-540, 541: <c>library qualIdent ; [ usesClause ] { declSection | exportsClause } ( compoundStmt | end ) .</c> The
    /// <c>end</c>-only form gives a compound statement whose <c>begin</c> is a missing token (no diagnostic).
    /// </summary>
    private LibrarySyntax ParseLibrary()
    {
        var keyword = Take();
        var name = ParseQualifiedName();
        var semicolon = Expect(SyntaxKind.SemicolonToken);
        var uses = CurrentKind == SyntaxKind.UsesKeyword ? ParseUsesClause() : null;
        var declarations = ParseDeclarations(SectionKind.Library);
        var body = CurrentKind == SyntaxKind.EndKeyword
            ? new CompoundStatementSyntax(Missing(SyntaxKind.BeginKeyword), default, Take())
            : ParseCompoundStatement();
        var dot = Expect(SyntaxKind.DotToken);
        return new LibrarySyntax(keyword, name, semicolon, uses, new BlockSyntax(declarations, body), dot, ParseEndOfFile());
    }

    /// <summary>
    /// LANG-544, 546: <c>exports exportItem { , exportItem } ;</c>. Outside the declaration part of a library file the clause is
    /// E2211 at the keyword and still parsed.
    /// </summary>
    private ExportsClauseSyntax ParseExportsClause(SectionKind section)
    {
        if (section != SectionKind.Library)
        {
            ReportAt(Current, "E2211");
        }

        var keyword = Take();
        var items = ParseSeparated(ParseExportItem, SyntaxKind.CommaToken);
        return new ExportsClauseSyntax(keyword, items, Expect(SyntaxKind.SemicolonToken));
    }

    /// <summary><c>qualIdent { index constExpr | name constExpr }</c>; a repeated specifier is skipped with E1100 (LANG-546).</summary>
    private ExportItemSyntax ParseExportItem()
    {
        var name = ParseQualifiedName();
        ExportSpecifierSyntax? first = null, second = null;
        while (IsContextual(Current, SyntaxKind.IndexKeyword) || IsContextual(Current, SyntaxKind.NameKeyword))
        {
            var kind = SyntaxFacts.GetKeywordKind(Current.Text);
            if (first?.Keyword.ContextualKind == kind || second?.Keyword.ContextualKind == kind)
            {
                do
                {
                    SkipToken();
                }
                while (CurrentKind is not (SyntaxKind.CommaToken or SyntaxKind.SemicolonToken or SyntaxKind.EndOfFileToken or SyntaxKind.BeginKeyword
                           or SyntaxKind.EndKeyword)
                       && !IsContextual(Current, SyntaxKind.IndexKeyword) && !IsContextual(Current, SyntaxKind.NameKeyword));
                continue;
            }

            var specifier = new ExportSpecifierSyntax(Take().WithContextualKind(kind), ParseExpression());
            if (first is null)
            {
                first = specifier;
            }
            else
            {
                second = specifier;
            }
        }

        return new ExportItemSyntax(name, first, second);
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

    private enum SectionKind { Block, Interface, Implementation, Library }

    private BlockSyntax ParseBlock()
    {
        if (TooDeep())
        {
            AbandonRest();
            return new BlockSyntax(default, new CompoundStatementSyntax(Missing(SyntaxKind.BeginKeyword), default, Missing(SyntaxKind.EndKeyword)));
        }

        _depth++;
        try
        {
            return ParseBlockCore();
        }
        finally
        {
            _depth--;
        }
    }

    private BlockSyntax ParseBlockCore()
    {
        var declarations = ParseDeclarations(SectionKind.Block);
        return new BlockSyntax(declarations, ParseCompoundStatement());
    }

    /// <summary>
    /// <c>{ declSection }</c> / <c>{ interfaceDecl }</c>. Attribute lists before a routine belong to it; before anything
    /// else they stay stand-alone <see cref="AttributeListSyntax"/> entries (they apply to the next declaration, LANG-172).
    /// Other tokens are skipped up to the next section or routine keyword or the end of the declaration part.
    /// </summary>
    private SyntaxList<SyntaxNode> ParseDeclarations(SectionKind section)
    {
        var declarations = ImmutableArray.CreateBuilder<SyntaxNode>();
        while (!EndsDeclarations(CurrentKind, section) || IsStrayEnd(section))
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
                case var _ when StartsRoutine():
                    declarations.Add(ParseRoutineDeclaration(hasBody: section != SectionKind.Interface));
                    break;
                case SyntaxKind.ExportsKeyword:
                    declarations.Add(ParseExportsClause(section));
                    break;
                case SyntaxKind.OpenBracketToken:
                    var attributes = ParseAttributeLists();
                    if (StartsRoutine())
                    {
                        declarations.Add(ParseRoutineDeclaration(hasBody: section != SectionKind.Interface, attributes));
                    }
                    else
                    {
                        declarations.AddRange(attributes);
                    }

                    break;
                default:
                    do
                    {
                        SkipToken();
                    }
                    while ((!EndsDeclarations(CurrentKind, section) || IsStrayEnd(section)) && !StartsSection(CurrentKind) && !StartsRoutine()
                           && CurrentKind is not (SyntaxKind.OpenBracketToken or SyntaxKind.ExportsKeyword));

                    break;
            }
        }

        return new SyntaxList<SyntaxNode>(declarations.ToImmutable());
    }

    /// <summary>
    /// C-250 (LANG-183): in an interface or implementation section, <c>end ;</c> with more input after it cannot end the
    /// unit (that is <c>end .</c>) — it is the leftover end of a routine body closed too early by a half-typed line, so it
    /// is skipped like other garbage and the section goes on at the next declaration, instead of ending the unit there and
    /// skipping the rest of the file after the missing <c>.</c>.
    /// </summary>
    private bool IsStrayEnd(SectionKind section) =>
        section is SectionKind.Interface or SectionKind.Implementation && CurrentKind == SyntaxKind.EndKeyword
        && Peek(1).Kind == SyntaxKind.SemicolonToken && Peek(2).Kind != SyntaxKind.EndOfFileToken;

    private static bool StartsSection(SyntaxKind kind) => kind is SyntaxKind.LabelKeyword or SyntaxKind.ConstKeyword
        or SyntaxKind.ResourceStringKeyword or SyntaxKind.TypeKeyword or SyntaxKind.VarKeyword or SyntaxKind.ThreadVarKeyword;

    private static bool IsRoutineKeyword(SyntaxKind kind) => kind is SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword
        or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword;

    /// <summary><c>procedure</c> …, or <c>class</c> followed by a routine keyword or the contextual <c>operator</c>.</summary>
    private bool StartsRoutine() =>
        IsRoutineKeyword(CurrentKind)
        || (CurrentKind == SyntaxKind.ClassKeyword && (IsRoutineKeyword(Peek(1).Kind) || IsContextual(Peek(1), SyntaxKind.OperatorKeyword)));

    // ------------------------------------------------------------------ grammar.md §5: routines

    /// <summary>
    /// <c>routineHeading ; { routineDirective ; } [ block ; ]</c>. The body is parsed when <paramref name="hasBody"/>
    /// (not in an interface section) and no <c>forward</c> / <c>external</c> directive is present.
    /// </summary>
    private RoutineDeclarationSyntax ParseRoutineDeclaration(bool hasBody, SyntaxList<AttributeListSyntax> attributes = default)
    {
        var heading = ParseRoutineHeading();
        SyntaxToken? semicolon = IsDirectiveStart() ? null : Expect(SyntaxKind.SemicolonToken);
        var directives = ImmutableArray.CreateBuilder<DirectiveSyntax>();
        while (IsDirectiveStart())
        {
            var directive = ParseDirective();
            if (!hasBody && directive.Keyword.ContextualKind == SyntaxKind.ForwardKeyword)
            {
                ReportAt(directive.Keyword, "E1114", directive.Keyword.Text);
            }

            CheckDuplicateDirective(directives, directive);
            directives.Add(directive);
        }

        if (!hasBody || directives.Any(d => d.Keyword.ContextualKind is SyntaxKind.ForwardKeyword or SyntaxKind.ExternalKeyword))
        {
            return new RoutineDeclarationSyntax(attributes, heading, semicolon, new SyntaxList<DirectiveSyntax>(directives.ToImmutable()), null, null);
        }

        var block = ParseBlock();
        return new RoutineDeclarationSyntax(attributes, heading, semicolon, new SyntaxList<DirectiveSyntax>(directives.ToImmutable()), block,
            Expect(SyntaxKind.SemicolonToken));
    }

    /// <summary>
    /// <c>[ class ] ( procedure | function | constructor | destructor | operator ) routineName [ genericParams ] [ formalParams ]
    /// [ : resultType ]</c>.
    /// </summary>
    private RoutineHeadingSyntax ParseRoutineHeading()
    {
        var classKeyword = CurrentKind == SyntaxKind.ClassKeyword ? Take() : (SyntaxToken?)null;
        var keyword = IsRoutineKeyword(CurrentKind) ? Take() : TakeContextual();
        if (classKeyword is not null && IsContextual(keyword, SyntaxKind.OperatorKeyword))
        {
            OperatorNameAsIdentifier();
        }

        var name = ParseRoutineName();
        var typeParameters = CurrentKind == SyntaxKind.LessThanToken ? ParseTypeParameterList() : null;
        var parameters = CurrentKind == SyntaxKind.OpenParenToken ? ParseParameterList() : null;
        SyntaxToken? colon = null;
        TypeSyntax? result = null;
        if (CurrentKind == SyntaxKind.ColonToken)
        {
            // also after 'procedure': the binder reports E2103 instead of a syntax error
            colon = Take();
            result = ParseType();
        }

        return new RoutineHeadingSyntax(classKeyword, keyword, name, typeParameters, parameters, colon, result);
    }

    /// <summary>
    /// C-220 (LANG-731): after <c>class operator</c> the reserved word <c>in</c> names the <c>In</c> operator — as the routine name
    /// or as the last segment of <c>TFoo.In</c> — so it is re-tokenised as an identifier with the same text and trivia.
    /// </summary>
    private void OperatorNameAsIdentifier()
    {
        var offset = 0;
        while (Peek(offset).Kind == SyntaxKind.IdentifierToken && Peek(offset + 1).Kind == SyntaxKind.DotToken)
        {
            offset += 2;
        }

        if (Peek(offset) is { Kind: SyntaxKind.InKeyword } token)
        {
            _tokens = _tokens.SetItem(_position + offset, new SyntaxToken(SyntaxKind.IdentifierToken, token.Text, token.Text, token.Span.Start,
                token.LeadingTrivia, token.TrailingTrivia, token.DirectiveState, token.File));
        }
    }

    private static readonly FrozenSet<SyntaxKind> DirectiveKinds = new[]
    {
        SyntaxKind.ForwardKeyword, SyntaxKind.OverloadKeyword, SyntaxKind.NoReturnKeyword, SyntaxKind.AssemblerKeyword,
        SyntaxKind.VarArgsKeyword, SyntaxKind.RegisterKeyword, SyntaxKind.PascalKeyword, SyntaxKind.CdeclKeyword,
        SyntaxKind.StdCallKeyword, SyntaxKind.SafeCallKeyword, SyntaxKind.WinApiKeyword, SyntaxKind.DeprecatedKeyword,
        SyntaxKind.PlatformKeyword, SyntaxKind.ExperimentalKeyword, SyntaxKind.ExternalKeyword, SyntaxKind.PublicKeyword,
        SyntaxKind.VirtualKeyword, SyntaxKind.DynamicKeyword, SyntaxKind.OverrideKeyword, SyntaxKind.AbstractKeyword,
        SyntaxKind.FinalKeyword, SyntaxKind.ReintroduceKeyword, SyntaxKind.StaticKeyword, SyntaxKind.MessageKeyword,
    }.ToFrozenSet();

    /// <summary>
    /// A routine or method directive (grammar.md §4, §5): <c>inline</c> or a directive word not followed by <c>:</c>,
    /// <c>,</c> or <c>=</c> (then it is a name); <c>public</c> only as <c>public name</c> and never in a type body
    /// (<paramref name="inMember"/>), where it is a visibility keyword.
    /// </summary>
    private bool IsDirectiveStart(bool inMember = false)
    {
        if (inMember && IsContextual(Current, SyntaxKind.PublicKeyword))
        {
            return false;
        }

        if (CurrentKind == SyntaxKind.InlineKeyword)
        {
            return true;
        }

        if (CurrentKind != SyntaxKind.IdentifierToken || Current.Text.StartsWith('&')
            || !DirectiveKinds.Contains(SyntaxFacts.GetKeywordKind(Current.Text))
            || Peek(1).Kind is SyntaxKind.ColonToken or SyntaxKind.CommaToken or SyntaxKind.EqualsToken)
        {
            return false;
        }

        return !IsContextual(Current, SyntaxKind.PublicKeyword) || IsContextual(Peek(1), SyntaxKind.NameKeyword);
    }

    /// <summary>E1115 when the same directive was already given for this declaration.</summary>
    private void CheckDuplicateDirective(ImmutableArray<DirectiveSyntax>.Builder directives, DirectiveSyntax directive)
    {
        if (directives.Any(d => d.Keyword.ContextualKind == directive.Keyword.ContextualKind))
        {
            ReportAt(directive.Keyword, "E1115", directive.Keyword.Text);
        }
    }

    /// <summary>One directive with its arguments and its <c>;</c> (optional when another directive follows, LANG-160).</summary>
    private DirectiveSyntax ParseDirective(bool inMember = false)
    {
        var keyword = CurrentKind == SyntaxKind.InlineKeyword ? Take().WithContextualKind(SyntaxKind.InlineKeyword) : TakeContextual();
        ExpressionSyntax? value = null;
        SyntaxToken? nameKeyword = null;
        ExpressionSyntax? nameValue = null;
        SyntaxToken? delayed = null;
        switch (keyword.ContextualKind)
        {
            case SyntaxKind.ExternalKeyword:
                // `external name 'sym'` (LANG-463: no library, `name` allowed); `external name;` names a library constant
                if (IsContextual(Current, SyntaxKind.NameKeyword) && Peek(1).Kind is not (SyntaxKind.SemicolonToken or SyntaxKind.EndOfFileToken)
                    && !IsContextual(Peek(1), SyntaxKind.NameKeyword) && !IsContextual(Peek(1), SyntaxKind.DelayedKeyword))
                {
                    nameKeyword = TakeContextual();
                    nameValue = ParseExpression();
                }
                else if (CurrentKind is not (SyntaxKind.SemicolonToken or SyntaxKind.EndOfFileToken) && !IsDirectiveStart())
                {
                    value = ParseExpression();
                    if (IsContextual(Current, SyntaxKind.NameKeyword))
                    {
                        nameKeyword = TakeContextual();
                        nameValue = ParseExpression();
                    }

                    if (IsContextual(Current, SyntaxKind.DelayedKeyword))
                    {
                        delayed = TakeContextual();
                    }
                }

                break;
            case SyntaxKind.PublicKeyword:
                nameKeyword = TakeContextual();
                nameValue = ParseExpression();
                break;
            case SyntaxKind.MessageKeyword:
                value = ParseExpression();
                break;
            case SyntaxKind.DeprecatedKeyword when CurrentKind == SyntaxKind.StringLiteralToken:
                value = new LiteralExpressionSyntax(Take());
                break;
        }

        SyntaxToken? semicolon = CurrentKind != SyntaxKind.SemicolonToken && IsDirectiveStart(inMember) ? null : Expect(SyntaxKind.SemicolonToken);
        return new DirectiveSyntax(keyword, value, nameKeyword, nameValue, delayed, semicolon);
    }

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
        while (StartsDeclarationName());

        return new ConstSectionSyntax(keyword, new SyntaxList<ConstDeclarationSyntax>(declarations.ToImmutable()));
    }

    /// <summary><c>ident = constExpr [ hints ] ;</c> or <c>ident : type = typedConst [ hints ] ;</c></summary>
    private ConstDeclarationSyntax ParseConstDeclaration()
    {
        var attributes = ParseAttributeLists();
        var identifier = ExpectIdentifier(declaresName: true);
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
        return new ConstDeclarationSyntax(attributes, identifier, colon, type, equals, value, hints, Expect(SyntaxKind.SemicolonToken));
    }

    /// <summary>
    /// <c>typedConst = constExpr | ( typedConst { , typedConst } ) | ( [ ident : typedConst { ; ident : typedConst } [ ; ] ] )</c>.
    /// <c>(</c> followed by <c>ident :</c> or <c>)</c> is a record constant; a single parenthesised expression stays
    /// an expression and may continue with operators (<c>(1 + 2) * 3</c>).
    /// </summary>
    private SyntaxNode ParseTypedConstant()
    {
        if (TooDeep())
        {
            AbandonRest();
            return new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));
        }

        _depth++;
        try
        {
            return ParseTypedConstantCore();
        }
        finally
        {
            _depth--;
        }
    }

    private SyntaxNode ParseTypedConstantCore()
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
        while (StartsDeclarationName());

        return new TypeSectionSyntax(keyword, new SyntaxList<TypeDeclarationSyntax>(declarations.ToImmutable()));
    }

    /// <summary><c>[ attributeList ] ident [ genericParams ] = [ type ] type [ hints ] ;</c></summary>
    private TypeDeclarationSyntax ParseTypeDeclaration()
    {
        var attributes = ParseAttributeLists();
        var identifier = ExpectIdentifier(declaresName: true);
        var typeParameters = CurrentKind == SyntaxKind.LessThanToken ? ParseTypeParameterList() : null;
        var equals = Expect(SyntaxKind.EqualsToken);
        var typeKeyword = CurrentKind == SyntaxKind.TypeKeyword ? Take() : (SyntaxToken?)null;
        var type = ParseType();
        var hints = ParseHintDirectives();
        return new TypeDeclarationSyntax(attributes, identifier, typeParameters, equals, typeKeyword, type, hints, Expect(SyntaxKind.SemicolonToken));
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
        while (StartsDeclarationName());

        return new VarSectionSyntax(keyword, new SyntaxList<VarDeclarationSyntax>(declarations.ToImmutable()));
    }

    /// <summary><c>identList : type [ = typedConst ] [ hints ] ; [ public name constExpr ; ]</c> (LANG-470).</summary>
    private VarDeclarationSyntax ParseVarDeclaration()
    {
        var attributes = ParseAttributeLists();
        var identifiers = ParseSeparated(ParseDeclaredName, SyntaxKind.CommaToken);
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
        var semicolon = Expect(SyntaxKind.SemicolonToken);
        var publicDirective = IsContextual(Current, SyntaxKind.PublicKeyword) && IsDirectiveStart() ? ParseDirective() : null;
        return new VarDeclarationSyntax(attributes, identifiers, colon, type, equals, initializer, hints, semicolon, publicDirective);
    }

    // ------------------------------------------------------------------ grammar.md §3: types

    /// <summary>
    /// <c>type</c> (grammar.md §3, §4); anything that cannot start a type is E1106 and a missing named type.
    /// </summary>
    private TypeSyntax ParseType()
    {
        if (TooDeep())
        {
            AbandonRest();
            return MissingNamedType();
        }

        _depth++;
        try
        {
            return ParseTypeCore();
        }
        finally
        {
            _depth--;
        }
    }

    private TypeSyntax ParseTypeCore()
    {
        switch (CurrentKind)
        {
            case SyntaxKind.ClassKeyword or SyntaxKind.RecordKeyword when IsHelperStart():
                return ParseHelperType();
            case SyntaxKind.IdentifierToken when IsContextual(Current, SyntaxKind.ReferenceKeyword) && Peek(1).Kind == SyntaxKind.ToKeyword:
                return ParseProceduralType();
            case SyntaxKind.IdentifierToken when !NameContinuesAsExpression():
                return new NamedTypeSyntax(ParseTypeName());
            case SyntaxKind.StringKeyword:
                return ParseStringType();
            case SyntaxKind.OpenParenToken:
                return ParseEnumType();
            case SyntaxKind.CaretToken:
                var caret = Take();
                // C-179: '^string' — the reserved type name is a type identifier here (LANG-265)
                return new PointerTypeSyntax(caret, CurrentKind == SyntaxKind.StringKeyword ? ParseStringType() : ParseNamedType());
            case SyntaxKind.PackedKeyword when Peek(1).Kind is SyntaxKind.ArrayKeyword or SyntaxKind.RecordKeyword
                or SyntaxKind.SetKeyword or SyntaxKind.FileKeyword:
                return ParseStructuredType(Take());
            case SyntaxKind.PackedKeyword:
                // E1106 at the token after 'packed'; 'packed' becomes skipped text, then whatever type follows
                var after = Peek(1);
                ReportAt(after, after.Kind == SyntaxKind.EndOfFileToken ? "E1108" : "E1106",
                    after.Kind == SyntaxKind.EndOfFileToken ? [] : [after.Text]);
                SkipToken(report: false);
                return ParseType();
            case SyntaxKind.ArrayKeyword or SyntaxKind.RecordKeyword or SyntaxKind.SetKeyword or SyntaxKind.FileKeyword:
                return ParseStructuredType(null);
            case SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword:
                return ParseProceduralType();
            case SyntaxKind.ClassKeyword when Peek(1).Kind == SyntaxKind.OfKeyword:
                var classKeyword = Take();
                var of = Take();
                return new ClassRefTypeSyntax(classKeyword, of, ParseNamedType());
            case SyntaxKind.ClassKeyword:
                return ParseClassType();
            case SyntaxKind.InterfaceKeyword:
                return ParseInterfaceType();
            case SyntaxKind.IdentifierToken or SyntaxKind.IntegerLiteralToken or SyntaxKind.RealLiteralToken
                or SyntaxKind.StringLiteralToken or SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.NotKeyword
                or SyntaxKind.NilKeyword:
                // bounds are simple expressions: a following '=' belongs to a typed constant
                var low = ParseSimpleExpression(null);
                var dotDot = Expect(SyntaxKind.DotDotToken);
                return new SubrangeTypeSyntax(low, dotDot, ParseSimpleExpression(null));
            default:
                ReportAt(Current, CurrentKind == SyntaxKind.EndOfFileToken ? "E1108" : "E1106", ExpectedArgs());
                return MissingNamedType();
        }
    }

    private NamedTypeSyntax MissingNamedType() => new(new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken)));

    /// <summary><c>typeId</c> where only a type name is allowed (after <c>^</c>, <c>class of</c>, a variant tag).</summary>
    private NamedTypeSyntax ParseNamedType()
    {
        if (CurrentKind == SyntaxKind.IdentifierToken)
        {
            return new NamedTypeSyntax(ParseTypeName());
        }

        ReportAt(Current, CurrentKind == SyntaxKind.EndOfFileToken ? "E1108" : "E1106", ExpectedArgs());
        return MissingNamedType();
    }

    /// <summary>
    /// After <c>ident { . ident }</c> at the start of a type: does an expression continue (so the type is a subrange such
    /// as <c>Low..High</c> or <c>A + 1..B</c>)? <c>=</c> does not count: it follows the type of a typed constant.
    /// </summary>
    private bool NameContinuesAsExpression()
    {
        var offset = 0;
        while (Peek(offset + 1).Kind == SyntaxKind.DotToken && Peek(offset + 2).Kind == SyntaxKind.IdentifierToken)
        {
            offset += 2;
        }

        var next = Peek(offset + 1).Kind;
        if (next == SyntaxKind.LessThanToken
            || (_typeArgumentDepth > 0 && next is SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken))
        {
            return false;   // generic arguments (LANG-705: in a type position '<' always starts them) or their end
        }

        return next is SyntaxKind.DotDotToken or SyntaxKind.OpenParenToken
               || (SyntaxFacts.GetBinaryPrecedence(next) > 0 && next is not (SyntaxKind.EqualsToken or SyntaxKind.InKeyword));
    }

    private StringTypeSyntax ParseStringType()
    {
        var keyword = Take();
        if (CurrentKind != SyntaxKind.OpenBracketToken)
        {
            return new StringTypeSyntax(keyword, null, null, null);
        }

        var open = Take();
        var length = ParseExpression();
        return new StringTypeSyntax(keyword, open, length, Expect(SyntaxKind.CloseBracketToken));
    }

    /// <summary><c>( ident [ = constExpr ] { , … } )</c></summary>
    private EnumTypeSyntax ParseEnumType()
    {
        var open = Take();
        var members = ParseSeparated(ParseEnumMember, SyntaxKind.CommaToken);
        return new EnumTypeSyntax(open, members, Expect(SyntaxKind.CloseParenToken));
    }

    private EnumMemberSyntax ParseEnumMember()
    {
        var identifier = ExpectIdentifier();
        if (CurrentKind != SyntaxKind.EqualsToken)
        {
            return new EnumMemberSyntax(identifier, null, null);
        }

        var equals = Take();
        return new EnumMemberSyntax(identifier, equals, ParseExpression());
    }

    /// <summary><c>[ packed ] ( arrayType | recordType | setType | fileType )</c>; after a bad <c>packed</c>, any type.</summary>
    private TypeSyntax ParseStructuredType(SyntaxToken? packed)
    {
        switch (CurrentKind)
        {
            case SyntaxKind.ArrayKeyword:
                return ParseArrayType(packed);
            case SyntaxKind.RecordKeyword:
                return ParseRecordType(packed);
            case SyntaxKind.SetKeyword:
                var set = Take();
                var setOf = Expect(SyntaxKind.OfKeyword);
                return new SetTypeSyntax(packed, set, setOf, ParseType());
            case SyntaxKind.FileKeyword:
                var file = Take();
                if (CurrentKind != SyntaxKind.OfKeyword)
                {
                    return new FileTypeSyntax(packed, file, null, null);
                }

                var fileOf = Take();
                return new FileTypeSyntax(packed, file, fileOf, ParseType());
            default:
                return ParseType();
        }
    }

    /// <summary><c>array [ "[" indexType { , indexType } "]" ] of ( type | const )</c></summary>
    private ArrayTypeSyntax ParseArrayType(SyntaxToken? packed)
    {
        var array = Take();
        SyntaxToken? open = null;
        SyntaxToken? close = null;
        SeparatedSyntaxList<TypeSyntax> indexes = default;
        if (CurrentKind == SyntaxKind.OpenBracketToken)
        {
            open = Take();
            indexes = ParseSeparated(ParseType, SyntaxKind.CommaToken);
            close = Expect(SyntaxKind.CloseBracketToken);
        }

        var of = Expect(SyntaxKind.OfKeyword);
        if (CurrentKind == SyntaxKind.ConstKeyword)
        {
            return new ArrayTypeSyntax(packed, array, open, indexes, close, of, null, Take());
        }

        return new ArrayTypeSyntax(packed, array, open, indexes, close, of, ParseType(), null);
    }

    /// <summary><c>record { recordMember } [ variantPart ] end</c>; members as in classes (LANG-730).</summary>
    private RecordTypeSyntax ParseRecordType(SyntaxToken? packed)
    {
        var record = Take();
        var members = ParseMemberList(isRecord: true);
        var variantPart = CurrentKind == SyntaxKind.CaseKeyword ? ParseVariantPart() : null;
        return new RecordTypeSyntax(packed, record, members, variantPart, Expect(SyntaxKind.EndKeyword));
    }

    // ------------------------------------------------------------------ grammar.md §4: classes, interfaces, helpers

    /// <summary>
    /// <c>class [ abstract | sealed ] [ heritage ] [ { classMember } end ]</c>. A <c>;</c> after the heading means no
    /// body: a forward declaration (LANG-155) or, with heritage, an empty class.
    /// </summary>
    private ClassTypeSyntax ParseClassType()
    {
        var classKeyword = Take();
        SyntaxToken? modifier = null;
        if ((IsContextual(Current, SyntaxKind.AbstractKeyword) || IsContextual(Current, SyntaxKind.SealedKeyword))
            && Peek(1).Kind is not (SyntaxKind.ColonToken or SyntaxKind.CommaToken))
        {
            modifier = TakeContextual();
        }

        var heritage = CurrentKind == SyntaxKind.OpenParenToken ? ParseHeritage() : null;
        if (CurrentKind == SyntaxKind.SemicolonToken)
        {
            return new ClassTypeSyntax(classKeyword, modifier, heritage, default, null);
        }

        var members = ParseMemberList(isRecord: false);
        return new ClassTypeSyntax(classKeyword, modifier, heritage, members, Expect(SyntaxKind.EndKeyword));
    }

    /// <summary><c>( typeId { , typeId } )</c></summary>
    private HeritageSyntax ParseHeritage()
    {
        var open = Take();
        var types = ParseSeparated<TypeSyntax>(ParseNamedType, SyntaxKind.CommaToken);
        return new HeritageSyntax(open, types, Expect(SyntaxKind.CloseParenToken));
    }

    /// <summary>
    /// <c>interface [ ( typeId ) ] [ "[" constExpr "]" ] { methodDecl | propertyDecl } end</c>; a <c>;</c> after the heading
    /// means no body (forward declaration).
    /// </summary>
    private InterfaceTypeSyntax ParseInterfaceType()
    {
        var interfaceKeyword = Take();
        var heritage = CurrentKind == SyntaxKind.OpenParenToken ? ParseHeritage() : null;
        if (CurrentKind == SyntaxKind.SemicolonToken)
        {
            return new InterfaceTypeSyntax(interfaceKeyword, heritage, null, default, null);
        }

        GuidSyntax? guid = null;
        if (CurrentKind == SyntaxKind.OpenBracketToken)
        {
            var open = Take();
            var value = ParseExpression();
            guid = new GuidSyntax(open, value, Expect(SyntaxKind.CloseBracketToken));
        }

        var members = ParseMemberList(isRecord: false);
        return new InterfaceTypeSyntax(interfaceKeyword, heritage, guid, members, Expect(SyntaxKind.EndKeyword));
    }

    /// <summary><c>class helper</c> / <c>record helper</c> followed by <c>for</c> or a heritage list.</summary>
    private bool IsHelperStart() =>
        IsContextual(Peek(1), SyntaxKind.HelperKeyword) && Peek(2).Kind is SyntaxKind.ForKeyword or SyntaxKind.OpenParenToken;

    /// <summary><c>( class | record ) helper [ ( typeId ) ] for typeId { classMember } end</c></summary>
    private HelperTypeSyntax ParseHelperType()
    {
        var keyword = Take();
        var helper = TakeContextual();
        var heritage = CurrentKind == SyntaxKind.OpenParenToken ? ParseHeritage() : null;
        var forKeyword = Expect(SyntaxKind.ForKeyword);
        var extended = CurrentKind == SyntaxKind.StringKeyword ? (TypeSyntax)ParseStringType() : ParseNamedType();     // record helper for string (C-124)
        var members = ParseMemberList(isRecord: false);
        return new HelperTypeSyntax(keyword, helper, heritage, forKeyword, extended, members, Expect(SyntaxKind.EndKeyword));
    }

    /// <summary>
    /// The members of a class, record, interface or helper body: default-section members, then one
    /// <see cref="VisibilitySectionSyntax"/> per visibility keyword. Stops at <c>end</c>, at <c>case</c> in a record and
    /// at tokens that cannot be inside a type (<c>begin</c>, <c>implementation</c>, … — an unterminated body).
    /// </summary>
    private SyntaxList<SyntaxNode> ParseMemberList(bool isRecord)
    {
        var members = ImmutableArray.CreateBuilder<SyntaxNode>();
        ParseMembers(members, isRecord);
        while (StartsVisibility())
        {
            var strict = IsContextual(Current, SyntaxKind.StrictKeyword) ? TakeContextual() : (SyntaxToken?)null;
            var keyword = TakeContextual();
            var sectionMembers = ImmutableArray.CreateBuilder<SyntaxNode>();
            ParseMembers(sectionMembers, isRecord);
            members.Add(new VisibilitySectionSyntax(strict, keyword, new SyntaxList<SyntaxNode>(sectionMembers.ToImmutable())));
        }

        return new SyntaxList<SyntaxNode>(members.ToImmutable());
    }

    /// <summary>Members up to the next visibility keyword or the end of the body; other tokens are skipped (E1100).</summary>
    private void ParseMembers(ImmutableArray<SyntaxNode>.Builder members, bool isRecord)
    {
        while (!EndsMembers(isRecord) && !StartsVisibility())
        {
            if (CurrentKind == SyntaxKind.OpenBracketToken)
            {
                var attributes = ParseAttributeLists();
                if (TakesAttributes())
                {
                    members.Add(ParseMember(attributes)!);
                }
                else
                {
                    members.AddRange(attributes);
                }
            }
            else if (ParseMember() is { } member)
            {
                members.Add(member);
            }
            else
            {
                SkipToken();
            }
        }
    }

    private bool EndsMembers(bool isRecord) => CurrentKind is SyntaxKind.EndKeyword or SyntaxKind.EndOfFileToken or SyntaxKind.BeginKeyword
        or SyntaxKind.ImplementationKeyword or SyntaxKind.InitializationKeyword or SyntaxKind.FinalizationKeyword
        || (isRecord && CurrentKind == SyntaxKind.CaseKeyword);

    /// <summary>
    /// <c>[ strict ] ( private | protected ) | public | published</c>. A visibility word followed by <c>:</c>, <c>,</c>
    /// or <c>=</c> is a name (a field, constant or type).
    /// </summary>
    private bool StartsVisibility()
    {
        if (CurrentKind != SyntaxKind.IdentifierToken || Current.Text.StartsWith('&'))
        {
            return false;
        }

        var kind = SyntaxFacts.GetKeywordKind(Current.Text);
        if (kind == SyntaxKind.StrictKeyword)
        {
            return IsContextual(Peek(1), SyntaxKind.PrivateKeyword) || IsContextual(Peek(1), SyntaxKind.ProtectedKeyword);
        }

        return kind is SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.PublicKeyword or SyntaxKind.PublishedKeyword
               && Peek(1).Kind is not (SyntaxKind.ColonToken or SyntaxKind.CommaToken or SyntaxKind.EqualsToken);
    }

    /// <summary>A member that can carry attribute lists: field, method (not a resolution clause) or property.</summary>
    private bool TakesAttributes() =>
        CurrentKind == SyntaxKind.PropertyKeyword
        || (CurrentKind == SyntaxKind.ClassKeyword && Peek(1).Kind == SyntaxKind.PropertyKeyword)
        || (StartsRoutine() && !IsMethodResolution())
        || (CurrentKind == SyntaxKind.IdentifierToken && !StartsVisibility());

    /// <summary>One class/record member, or null when the current token cannot start one.</summary>
    private SyntaxNode? ParseMember(SyntaxList<AttributeListSyntax> attributes = default)
    {
        switch (CurrentKind)
        {
            case SyntaxKind.ConstKeyword:
                return ParseConstSection();
            case SyntaxKind.TypeKeyword:
                return ParseTypeSection();
            case SyntaxKind.VarKeyword:
                return ParseClassVarSection(null);
            case SyntaxKind.ClassKeyword when Peek(1).Kind == SyntaxKind.VarKeyword:
                return ParseClassVarSection(Take());
            case SyntaxKind.PropertyKeyword:
            case SyntaxKind.ClassKeyword when Peek(1).Kind == SyntaxKind.PropertyKeyword:
                return ParsePropertyDeclaration(attributes);
            case var _ when StartsRoutine():
                return IsMethodResolution() ? ParseMethodResolution() : ParseMethodDeclaration(attributes);
            case SyntaxKind.IdentifierToken:
                return ParseFieldDeclaration(attributes);
            default:
                return null;
        }
    }

    /// <summary><c>[ class ] var { fieldDecl }</c></summary>
    private ClassVarSectionSyntax ParseClassVarSection(SyntaxToken? classKeyword)
    {
        var varKeyword = Take();
        var fields = ImmutableArray.CreateBuilder<FieldDeclarationSyntax>();
        while (StartsDeclarationName())
        {
            fields.Add(ParseFieldDeclaration());
        }

        return new ClassVarSectionSyntax(classKeyword, varKeyword, new SyntaxList<FieldDeclarationSyntax>(fields.ToImmutable()));
    }

    /// <summary><c>[ class ] methodHeading ; { methodDirective ; }</c></summary>
    private MethodDeclarationSyntax ParseMethodDeclaration(SyntaxList<AttributeListSyntax> attributes)
    {
        var heading = ParseRoutineHeading();
        SyntaxToken? semicolon = IsDirectiveStart(inMember: true) ? null : Expect(SyntaxKind.SemicolonToken);
        var directives = ImmutableArray.CreateBuilder<DirectiveSyntax>();
        while (IsDirectiveStart(inMember: true))
        {
            var directive = ParseDirective(inMember: true);
            if (directive.Keyword.ContextualKind == SyntaxKind.ForwardKeyword)
            {
                ReportAt(directive.Keyword, "E1114", directive.Keyword.Text);
            }

            CheckDuplicateDirective(directives, directive);
            directives.Add(directive);
        }

        return new MethodDeclarationSyntax(attributes, heading, semicolon, new SyntaxList<DirectiveSyntax>(directives.ToImmutable()));
    }

    /// <summary><c>( procedure | function ) ident . ident { . ident } =</c> ahead.</summary>
    private bool IsMethodResolution()
    {
        if (CurrentKind is not (SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword) || Peek(1).Kind != SyntaxKind.IdentifierToken)
        {
            return false;
        }

        var offset = 1;
        while (Peek(offset + 1).Kind == SyntaxKind.DotToken && Peek(offset + 2).Kind == SyntaxKind.IdentifierToken)
        {
            offset += 2;
        }

        return offset > 1 && Peek(offset + 1).Kind == SyntaxKind.EqualsToken;
    }

    /// <summary><c>( procedure | function ) qualIdent = ident ;</c></summary>
    private MethodResolutionSyntax ParseMethodResolution()
    {
        var keyword = Take();
        var name = ParseQualifiedName();
        var equals = Take();
        var target = ParseIdentifierName();
        return new MethodResolutionSyntax(keyword, name, equals, target, Expect(SyntaxKind.SemicolonToken));
    }

    /// <summary>
    /// <c>[ class ] property ident [ "[" formalParamList "]" ] [ : type ] { specifier } ; [ default ; ]</c>. A token that is
    /// not a specifier before the <c>;</c> is E1118; the rest up to the <c>;</c> is skipped.
    /// </summary>
    private PropertyDeclarationSyntax ParsePropertyDeclaration(SyntaxList<AttributeListSyntax> attributes)
    {
        var classKeyword = CurrentKind == SyntaxKind.ClassKeyword ? Take() : (SyntaxToken?)null;
        var propertyKeyword = Take();
        var identifier = ExpectIdentifier(declaresName: true);
        SyntaxToken? open = null;
        SyntaxToken? close = null;
        SeparatedSyntaxList<ParameterSyntax> parameters = default;
        if (CurrentKind == SyntaxKind.OpenBracketToken)
        {
            open = Take();
            parameters = ParseSeparated(ParseParameter, SyntaxKind.SemicolonToken);
            close = Expect(SyntaxKind.CloseBracketToken);
        }

        SyntaxToken? colon = null;
        TypeSyntax? type = null;
        if (CurrentKind == SyntaxKind.ColonToken)
        {
            colon = Take();
            type = ParseType();
        }

        var specifiers = ImmutableArray.CreateBuilder<PropertySpecifierSyntax>();
        while (IsPropertySpecifier())
        {
            specifiers.Add(ParsePropertySpecifier());
        }

        if (CurrentKind != SyntaxKind.SemicolonToken && !EndsMembers(isRecord: false) && !StartsVisibility())
        {
            ReportAt(Current, "E1118", Current.Text);
            do
            {
                SkipToken(report: false);
            }
            while (CurrentKind != SyntaxKind.SemicolonToken && !EndsMembers(isRecord: false) && !StartsVisibility());
        }

        var semicolon = Expect(SyntaxKind.SemicolonToken);
        SyntaxToken? defaultKeyword = null;
        SyntaxToken? defaultSemicolon = null;
        if (IsContextual(Current, SyntaxKind.DefaultKeyword) && Peek(1).Kind == SyntaxKind.SemicolonToken)
        {
            defaultKeyword = TakeContextual();
            defaultSemicolon = Take();
        }

        return new PropertyDeclarationSyntax(attributes, classKeyword, propertyKeyword, identifier, open, parameters, close, colon, type,
            new SyntaxList<PropertySpecifierSyntax>(specifiers.ToImmutable()), semicolon, defaultKeyword, defaultSemicolon);
    }

    private bool IsPropertySpecifier() =>
        CurrentKind == SyntaxKind.IdentifierToken && !Current.Text.StartsWith('&')
        && SyntaxFacts.GetKeywordKind(Current.Text) is SyntaxKind.IndexKeyword or SyntaxKind.ReadKeyword or SyntaxKind.WriteKeyword
            or SyntaxKind.StoredKeyword or SyntaxKind.DefaultKeyword or SyntaxKind.NoDefaultKeyword or SyntaxKind.ImplementsKeyword;

    /// <summary>
    /// <c>index constExpr</c> | <c>read path</c> | <c>write path</c> | <c>stored ( path | constExpr )</c> |
    /// <c>default constExpr</c> | <c>nodefault</c> | <c>implements typeId { , typeId }</c>.
    /// </summary>
    private PropertySpecifierSyntax ParsePropertySpecifier()
    {
        var keyword = TakeContextual();
        var values = keyword.ContextualKind switch
        {
            SyntaxKind.NoDefaultKeyword => default,
            SyntaxKind.ReadKeyword or SyntaxKind.WriteKeyword => new SeparatedSyntaxList<ExpressionSyntax>([ParseQualifiedName()]),
            SyntaxKind.ImplementsKeyword => ParseSeparated<ExpressionSyntax>(ParseTypeName, SyntaxKind.CommaToken),
            _ => new SeparatedSyntaxList<ExpressionSyntax>([ParseExpression()]),
        };
        return new PropertySpecifierSyntax(keyword, values);
    }

    /// <summary><c>identList : type [ hints ] [ ; ]</c> — <c>;</c> optional before <c>end</c>, <c>)</c>, <c>case</c>.</summary>
    private FieldDeclarationSyntax ParseFieldDeclaration(SyntaxList<AttributeListSyntax>? attributeLists = null)
    {
        var attributes = attributeLists ?? ParseAttributeLists();
        var identifiers = ParseSeparated(ParseDeclaredName, SyntaxKind.CommaToken);
        var colon = Expect(SyntaxKind.ColonToken);
        var type = ParseType();
        var hints = ParseHintDirectives();
        SyntaxToken? semicolon = CurrentKind is SyntaxKind.EndKeyword or SyntaxKind.CloseParenToken or SyntaxKind.CaseKeyword
            ? null
            : Expect(SyntaxKind.SemicolonToken);
        return new FieldDeclarationSyntax(attributes, identifiers, colon, type, hints, semicolon);
    }

    /// <summary><c>case [ ident : ] typeId of variant { ; variant } [ ; ]</c></summary>
    private VariantPartSyntax ParseVariantPart()
    {
        var caseKeyword = Take();
        SyntaxToken? tag = null;
        SyntaxToken? colon = null;
        if (CurrentKind == SyntaxKind.IdentifierToken && Peek(1).Kind == SyntaxKind.ColonToken)
        {
            tag = Take();
            colon = Take();
        }

        var tagType = ParseNamedType();
        var of = Expect(SyntaxKind.OfKeyword);
        var variants = ImmutableArray.CreateBuilder<SyntaxNodeOrToken>();
        while (true)
        {
            variants.Add(ParseVariant());
            if (CurrentKind != SyntaxKind.SemicolonToken)
            {
                break;
            }

            variants.Add(Take());
            if (CurrentKind is SyntaxKind.EndKeyword or SyntaxKind.CloseParenToken or SyntaxKind.EndOfFileToken)
            {
                break;
            }
        }

        return new VariantPartSyntax(caseKeyword, tag, colon, tagType, of, new SeparatedSyntaxList<VariantSyntax>(variants.ToImmutable()));
    }

    /// <summary><c>constExpr { , constExpr } : ( { fieldDecl } [ variantPart ] )</c></summary>
    private VariantSyntax ParseVariant()
    {
        var labels = ParseSeparated(() => ParseExpression(), SyntaxKind.CommaToken);
        var colon = Expect(SyntaxKind.ColonToken);
        var open = Expect(SyntaxKind.OpenParenToken);
        var fields = ImmutableArray.CreateBuilder<FieldDeclarationSyntax>();
        while (CurrentKind == SyntaxKind.IdentifierToken)
        {
            fields.Add(ParseFieldDeclaration());
        }

        var nested = CurrentKind == SyntaxKind.CaseKeyword ? ParseVariantPart() : null;
        return new VariantSyntax(labels, colon, open, new SyntaxList<FieldDeclarationSyntax>(fields.ToImmutable()), nested,
            Expect(SyntaxKind.CloseParenToken));
    }

    /// <summary>
    /// <c>( procedure [ formalParams ] | function [ formalParams ] : resultType ) [ of object ] { [ ; ] callConv }</c> or
    /// <c>reference to ( procedure [ formalParams ] | function [ formalParams ] : resultType )</c>.
    /// </summary>
    private ProceduralTypeSyntax ParseProceduralType()
    {
        SyntaxToken? reference = null;
        SyntaxToken? to = null;
        if (CurrentKind == SyntaxKind.IdentifierToken)
        {
            reference = Take().WithContextualKind(SyntaxKind.ReferenceKeyword);
            to = Take();
        }

        var keyword = CurrentKind is SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword
            ? Take()
            : Expect(SyntaxKind.ProcedureKeyword);
        var parameters = CurrentKind == SyntaxKind.OpenParenToken ? ParseParameterList() : null;
        SyntaxToken? colon = null;
        TypeSyntax? result = null;
        if (keyword.Kind == SyntaxKind.FunctionKeyword)
        {
            colon = Expect(SyntaxKind.ColonToken);
            result = ParseType();
        }

        SyntaxToken? of = null;
        SyntaxToken? obj = null;
        var conventions = ImmutableArray.CreateBuilder<CallingConventionSyntax>();
        if (reference is null)
        {
            if (CurrentKind == SyntaxKind.OfKeyword && Peek(1).Kind == SyntaxKind.ObjectKeyword)
            {
                of = Take();
                obj = Take();
            }

            while (true)
            {
                if (IsCallingConvention(Current))
                {
                    conventions.Add(new CallingConventionSyntax(null, TakeContextual()));
                }
                else if (CurrentKind == SyntaxKind.SemicolonToken && IsCallingConvention(Peek(1)))
                {
                    var semicolon = Take();
                    conventions.Add(new CallingConventionSyntax(semicolon, TakeContextual()));
                }
                else
                {
                    break;
                }
            }
        }

        return new ProceduralTypeSyntax(reference, to, keyword, parameters, colon, result, of, obj,
            new SyntaxList<CallingConventionSyntax>(conventions.ToImmutable()));
    }

    private static bool IsCallingConvention(SyntaxToken token) =>
        token.Kind == SyntaxKind.IdentifierToken && !token.Text.StartsWith('&')
        && SyntaxFacts.GetKeywordKind(token.Text) is SyntaxKind.RegisterKeyword or SyntaxKind.PascalKeyword
            or SyntaxKind.CdeclKeyword or SyntaxKind.StdCallKeyword or SyntaxKind.SafeCallKeyword or SyntaxKind.WinApiKeyword;

    /// <summary>Takes an identifier used as a contextual keyword and records that kind on it.</summary>
    private SyntaxToken TakeContextual()
    {
        var kind = SyntaxFacts.GetKeywordKind(Current.Text);
        return Take().WithContextualKind(kind);
    }

    // ------------------------------------------------------------------ grammar.md §5: formal parameters

    /// <summary><c>( [ formalParam { ; formalParam } ] )</c></summary>
    private ParameterListSyntax ParseParameterList()
    {
        var open = Take();
        if (CurrentKind == SyntaxKind.CloseParenToken)
        {
            return new ParameterListSyntax(open, default, Take());
        }

        var parameters = ParseSeparated(ParseParameter, SyntaxKind.SemicolonToken);
        return new ParameterListSyntax(open, parameters, Expect(SyntaxKind.CloseParenToken));
    }

    /// <summary><c>[ const | var | out ] identList [ : paramType [ = constExpr ] ]</c></summary>
    private ParameterSyntax ParseParameter()
    {
        var attributes = ParseAttributeLists();
        SyntaxToken? modifier = null;
        if (CurrentKind is SyntaxKind.ConstKeyword or SyntaxKind.VarKeyword)
        {
            modifier = Take();
        }
        else if (IsContextual(Current, SyntaxKind.OutKeyword) && Peek(1).Kind == SyntaxKind.IdentifierToken)
        {
            modifier = TakeContextual();
        }

        var identifiers = ParseSeparated(ParseDeclaredName, SyntaxKind.CommaToken);
        if (CurrentKind != SyntaxKind.ColonToken)
        {
            return new ParameterSyntax(attributes, modifier, identifiers, null, null, null, null);
        }

        var colon = Take();
        var type = ParseType();
        if (CurrentKind != SyntaxKind.EqualsToken)
        {
            return new ParameterSyntax(attributes, modifier, identifiers, colon, type, null, null);
        }

        var equals = Take();
        return new ParameterSyntax(attributes, modifier, identifiers, colon, type, equals, ParseExpression());
    }
    // ------------------------------------------------------------------ grammar.md §7: expressions

    /// <summary>
    /// <c>expression = simpleExpr [ relOp simpleExpr ]</c>; a second relational operator is E1110 (LANG-170) and is
    /// parsed left-associatively. <paramref name="first"/>: an already parsed leading factor.
    /// </summary>
    /// <summary>
    /// A <c>{$IF}</c> condition (LANG-862): one expression filling the whole token list; a token after it is E1100
    /// (then the expression is not used, see <see cref="Diagnostics"/>).
    /// </summary>
    public ExpressionSyntax ParseDirectiveCondition()
    {
        var expression = ParseExpression();
        if (CurrentKind != SyntaxKind.EndOfFileToken)
        {
            ReportAt(Current, "E1100", Current.Text);
        }

        return expression;
    }

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

    /// <summary>
    /// <c>term = factor { mulOp factor }</c>; an already parsed <paramref name="first"/> (a parenthesised typed-constant
    /// expression) may still take designator suffixes.
    /// </summary>
    private ExpressionSyntax ParseTerm(ExpressionSyntax? first)
    {
        var left = first is null ? ParseFactor() : ParseDesignatorSuffixes(first);
        while (SyntaxFacts.GetBinaryPrecedence(CurrentKind) == 4)
        {
            var op = Take();
            left = new BinaryExpressionSyntax(left, op, ParseFactor());
        }

        return left;
    }

    /// <summary>
    /// <c>factor</c>: <c>@</c>/<c>not</c>/sign factor, literal, <c>nil</c>, set constructor, <c>( expression )</c>,
    /// <c>inherited [ ident ]</c>, <c>string ( expression )</c>, a bare <c>string</c> or designator; all but the bare
    /// <c>string</c> and the literals with designator suffixes.
    /// An anonymous method, too (LANG-720); anything else is E1103.
    /// </summary>
    private ExpressionSyntax ParseFactor()
    {
        if (TooDeep())
        {
            AbandonRest();
            return new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));
        }

        _depth++;
        try
        {
            return ParseFactorCore();
        }
        finally
        {
            _depth--;
        }
    }

    private ExpressionSyntax ParseFactorCore()
    {
        switch (CurrentKind)
        {
            case SyntaxKind.AtToken:
                var at = Take();
                return new AddressOfExpressionSyntax(at, ParseFactor());
            case SyntaxKind.NotKeyword or SyntaxKind.PlusToken or SyntaxKind.MinusToken:
                var op = Take();
                return new UnaryExpressionSyntax(op, ParseFactor());
            case SyntaxKind.IntegerLiteralToken or SyntaxKind.RealLiteralToken or SyntaxKind.StringLiteralToken or SyntaxKind.NilKeyword:
                return new LiteralExpressionSyntax(Take());
            case SyntaxKind.OpenBracketToken:
                return ParseSetConstructor();
            case SyntaxKind.OpenParenToken:
                var open = Take();
                var expression = ParseExpression();
                return ParseDesignatorSuffixes(new ParenthesizedExpressionSyntax(open, expression, Expect(SyntaxKind.CloseParenToken)));
            case SyntaxKind.InheritedKeyword:
                var inherited = Take();
                var name = CurrentKind == SyntaxKind.IdentifierToken ? ParseIdentifierName() : null;
                return ParseDesignatorSuffixes(new InheritedExpressionSyntax(inherited, name));
            case SyntaxKind.StringKeyword when Peek(1).Kind == SyntaxKind.OpenParenToken:
                var stringKeyword = Take();
                var castOpen = Take();
                var operand = ParseExpression();
                return ParseDesignatorSuffixes(
                    new StringCastExpressionSyntax(stringKeyword, castOpen, operand, Expect(SyntaxKind.CloseParenToken)));
            case SyntaxKind.StringKeyword:
                return new PredefinedTypeExpressionSyntax(Take());
            case SyntaxKind.IdentifierToken:
                return ParseDesignatorSuffixes(ParseSimpleNameInExpression());
            case SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword:
                return ParseAnonymousMethod();
            default:
                ReportAt(Current, CurrentKind == SyntaxKind.EndOfFileToken ? "E1108" : "E1103", ExpectedArgs());
                return new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken));
        }
    }

    /// <summary>
    /// <c>{ designatorSuffix }</c>: <c>. ident</c>, <c>[ expression { , expression } ]</c>, <c>^</c>, <c>( [ argList ] )</c>,
    /// applied left to right.
    /// </summary>
    private ExpressionSyntax ParseDesignatorSuffixes(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (CurrentKind)
            {
                case SyntaxKind.DotToken:
                    var dot = Take();
                    expression = new MemberAccessExpressionSyntax(expression, dot, ParseSimpleNameInExpression());
                    break;
                case SyntaxKind.OpenBracketToken:
                    var open = Take();
                    var indexes = ParseSeparated(() => ParseExpression(), SyntaxKind.CommaToken);
                    expression = new ElementAccessExpressionSyntax(expression, open, indexes, Expect(SyntaxKind.CloseBracketToken));
                    break;
                case SyntaxKind.CaretToken:
                    expression = new DereferenceExpressionSyntax(expression, Take());
                    break;
                case SyntaxKind.OpenParenToken:
                    expression = new InvocationExpressionSyntax(expression, ParseArgumentList());
                    break;
                default:
                    return expression;
            }
        }
    }

    /// <summary><c>( [ argument { , argument } ] )</c></summary>
    private ArgumentListSyntax ParseArgumentList()
    {
        var open = Take();
        if (CurrentKind == SyntaxKind.CloseParenToken)
        {
            return new ArgumentListSyntax(open, default, Take());
        }

        var arguments = ParseSeparated(ParseArgument, SyntaxKind.CommaToken);
        return new ArgumentListSyntax(open, arguments, Expect(SyntaxKind.CloseParenToken));
    }

    /// <summary><c>expression [ : expression [ : expression ] ]</c></summary>
    private ArgumentSyntax ParseArgument()
    {
        var expression = ParseExpression();
        if (CurrentKind != SyntaxKind.ColonToken)
        {
            return new ArgumentSyntax(expression, null, null, null, null);
        }

        var widthColon = Take();
        var width = ParseExpression();
        if (CurrentKind != SyntaxKind.ColonToken)
        {
            return new ArgumentSyntax(expression, widthColon, width, null, null);
        }

        var decimalsColon = Take();
        return new ArgumentSyntax(expression, widthColon, width, decimalsColon, ParseExpression());
    }

    /// <summary><c>[ [ setElement { , setElement } ] ]</c>, <c>setElement = expression [ .. expression ]</c></summary>
    private SetConstructorSyntax ParseSetConstructor()
    {
        var open = Take();
        if (CurrentKind == SyntaxKind.CloseBracketToken)
        {
            return new SetConstructorSyntax(open, default, Take());
        }

        var elements = ParseSeparated(ParseSetElement, SyntaxKind.CommaToken);
        return new SetConstructorSyntax(open, elements, Expect(SyntaxKind.CloseBracketToken));
    }

    private SetElementSyntax ParseSetElement()
    {
        var expression = ParseExpression();
        if (CurrentKind != SyntaxKind.DotDotToken)
        {
            return new SetElementSyntax(expression, null, null);
        }

        var dotDot = Take();
        return new SetElementSyntax(expression, dotDot, ParseExpression());
    }

    /// <summary>Arguments for "… expected but '%s' found" codes; none for E1108 at the end of the file.</summary>
    private object[] ExpectedArgs() => CurrentKind == SyntaxKind.EndOfFileToken ? [] : [Current.Text];

    private static bool EndsDeclarations(SyntaxKind kind, SectionKind section) => kind switch
    {
        SyntaxKind.EndOfFileToken or SyntaxKind.BeginKeyword => true,
        SyntaxKind.ImplementationKeyword or SyntaxKind.InitializationKeyword or SyntaxKind.FinalizationKeyword
            or SyntaxKind.EndKeyword => section is SectionKind.Interface or SectionKind.Implementation || kind == SyntaxKind.EndKeyword,
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

    // Statement context (LANG-165). _elseMayFollow: an 'else' right after the statement belongs to an enclosing
    // if/case/try, so the statement ends there (then-branches, case arms, exception handlers and statements nested in
    // them). _semicolonElseIsOuter: '; else' after an if belongs to an enclosing case or handler block (no E1105).
    // Both are reset inside statement lists.
    private bool _elseMayFollow;
    private bool _semicolonElseIsOuter;

    /// <summary><c>[ statement ] { ; [ statement ] }</c>; an absent statement next to a <c>;</c> is an empty statement.</summary>
    private SeparatedSyntaxList<StatementSyntax> ParseStatementList()
    {
        var items = ImmutableArray.CreateBuilder<SyntaxNodeOrToken>();
        if (EndsStatementList(CurrentKind))
        {
            return default;
        }

        var (elseMayFollow, semicolonElseIsOuter) = (_elseMayFollow, _semicolonElseIsOuter);
        (_elseMayFollow, _semicolonElseIsOuter) = (false, false);
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
                // ParseStatement consumed what it could: ';' is missing before the next token
                items.Add(Expect(SyntaxKind.SemicolonToken));
            }
        }

        (_elseMayFollow, _semicolonElseIsOuter) = (elseMayFollow, semicolonElseIsOuter);
        return new SeparatedSyntaxList<StatementSyntax>(items.ToImmutable());
    }

    /// <summary>
    /// List ends and the declaration-level synchronisation points that cannot start a statement (LANG-183): a statement
    /// list that meets <c>procedure</c>, <c>type</c>, <c>implementation</c>, … is missing its <c>end</c>.
    /// </summary>
    private static bool EndsStatementList(SyntaxKind kind) => kind is SyntaxKind.EndKeyword or SyntaxKind.UntilKeyword
        or SyntaxKind.FinallyKeyword or SyntaxKind.ExceptKeyword or SyntaxKind.FinalizationKeyword or SyntaxKind.EndOfFileToken
        or SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword or SyntaxKind.ConstructorKeyword or SyntaxKind.DestructorKeyword
        or SyntaxKind.TypeKeyword or SyntaxKind.LabelKeyword or SyntaxKind.ResourceStringKeyword or SyntaxKind.ThreadVarKeyword
        or SyntaxKind.ImplementationKeyword or SyntaxKind.InitializationKeyword;

    /// <summary>A statement nested in another one, with the given statement context.</summary>
    private StatementSyntax ParseNestedStatement(bool elseMayFollow, bool semicolonElseIsOuter)
    {
        var saved = (_elseMayFollow, _semicolonElseIsOuter);
        (_elseMayFollow, _semicolonElseIsOuter) = (elseMayFollow, semicolonElseIsOuter);
        var statement = ParseStatement();
        (_elseMayFollow, _semicolonElseIsOuter) = saved;
        return statement;
    }

    /// <summary>
    /// <c>statement = [ labelId : ] [ unlabelled ]</c>. An absent statement (before <c>;</c>, a list end or an
    /// <c>else</c> that may follow) is an empty statement; a token that cannot start a statement is skipped with E1100
    /// up to a statement-level synchronisation point (LANG-183) or a <c>begin</c>.
    /// </summary>
    private StatementSyntax ParseStatement()
    {
        if (TooDeep())
        {
            AbandonRest();
            return new EmptyStatementSyntax();
        }

        _depth++;
        try
        {
            return ParseStatementCore();
        }
        finally
        {
            _depth--;
        }
    }

    private StatementSyntax ParseStatementCore()
    {
        switch (CurrentKind)
        {
            case SyntaxKind.SemicolonToken:
            case SyntaxKind.ElseKeyword when _elseMayFollow:
            case var _ when EndsStatementList(CurrentKind):
                return new EmptyStatementSyntax();
            case SyntaxKind.IdentifierToken or SyntaxKind.IntegerLiteralToken when Peek(1).Kind == SyntaxKind.ColonToken:
                var label = ParseLabel();
                var colon = Take();
                return new LabeledStatementSyntax(label, colon, ParseStatement());
            case SyntaxKind.IdentifierToken or SyntaxKind.OpenParenToken or SyntaxKind.InheritedKeyword:
            case SyntaxKind.StringKeyword when Peek(1).Kind == SyntaxKind.OpenParenToken:      // C-206: string(S) := …
                return ParseSimpleStatement();
            case SyntaxKind.BeginKeyword:
                return ParseCompoundStatement();
            case SyntaxKind.IfKeyword:
                return ParseIfStatement();
            case SyntaxKind.CaseKeyword:
                return ParseCaseStatement();
            case SyntaxKind.RepeatKeyword:
                return ParseRepeatStatement();
            case SyntaxKind.WhileKeyword:
                return ParseWhileStatement();
            case SyntaxKind.ForKeyword:
                return ParseForStatement();
            case SyntaxKind.WithKeyword:
                return ParseWithStatement();
            case SyntaxKind.TryKeyword:
                return ParseTryStatement();
            case SyntaxKind.GotoKeyword:
                var gotoKeyword = Take();
                return new GotoStatementSyntax(gotoKeyword, ParseLabel());
            case SyntaxKind.RaiseKeyword:
                return ParseRaiseStatement();
            case SyntaxKind.VarKeyword:
                return ParseInlineVarStatement();
            case SyntaxKind.ConstKeyword:
                return ParseInlineConstStatement();
            default:
                do
                {
                    SkipToken();
                }
                while (CurrentKind is not (SyntaxKind.BeginKeyword or SyntaxKind.SemicolonToken or SyntaxKind.ElseKeyword)
                       && !EndsStatementList(CurrentKind));

                return CurrentKind == SyntaxKind.BeginKeyword ? ParseCompoundStatement() : new EmptyStatementSyntax();
        }
    }

    /// <summary>
    /// <c>designator := expression</c>, <c>designator</c> (a call) or <c>inherited [ ident [ ( [ argList ] ) ] ]</c>.
    /// </summary>
    private StatementSyntax ParseSimpleStatement()
    {
        var target = ParseFactor();
        if (CurrentKind == SyntaxKind.ColonEqualsToken)
        {
            var colonEquals = Take();
            return new AssignmentStatementSyntax(target, colonEquals, ParseExpression());
        }

        var isInherited = target is InheritedExpressionSyntax
            || target is InvocationExpressionSyntax { Expression: InheritedExpressionSyntax { Name: not null } };
        return isInherited ? new InheritedStatementSyntax(target) : new ExpressionStatementSyntax(target);
    }

    /// <summary>
    /// <c>if expression then statement [ else statement ]</c>. A <c>;</c> before <c>else</c> is E1105 (LANG-165) unless
    /// the <c>else</c> belongs to an enclosing case or handler block; the <c>;</c> is then skipped and the <c>else</c>
    /// taken.
    /// </summary>
    private IfStatementSyntax ParseIfStatement()
    {
        var ifKeyword = Take();
        var condition = ParseExpression();
        var then = Expect(SyntaxKind.ThenKeyword);
        var statement = ParseNestedStatement(elseMayFollow: true, _semicolonElseIsOuter);
        if (CurrentKind == SyntaxKind.SemicolonToken && Peek(1).Kind == SyntaxKind.ElseKeyword && !_semicolonElseIsOuter)
        {
            ReportAt(Current, "E1105");
            SkipToken(report: false);
        }

        if (CurrentKind != SyntaxKind.ElseKeyword)
        {
            return new IfStatementSyntax(ifKeyword, condition, then, statement, null, null);
        }

        var elseKeyword = Take();
        return new IfStatementSyntax(ifKeyword, condition, then, statement, elseKeyword,
            ParseNestedStatement(_elseMayFollow, _semicolonElseIsOuter));
    }

    /// <summary><c>case expression of caseArm { ; caseArm } [ ; ] [ else stmtList ] end</c></summary>
    private CaseStatementSyntax ParseCaseStatement()
    {
        var caseKeyword = Take();
        var selector = ParseExpression();
        var of = Expect(SyntaxKind.OfKeyword);
        var arms = ImmutableArray.CreateBuilder<SyntaxNodeOrToken>();
        do
        {
            arms.Add(ParseCaseArm());
            if (CurrentKind == SyntaxKind.SemicolonToken)
            {
                arms.Add(Take());
            }
            else if (CanStartFactor(CurrentKind))
            {
                arms.Add(Expect(SyntaxKind.SemicolonToken));   // another arm follows: the ';' is missing
            }
            else
            {
                break;
            }
        }
        while (CurrentKind != SyntaxKind.ElseKeyword && !EndsStatementList(CurrentKind));

        SyntaxToken? elseKeyword = null;
        SeparatedSyntaxList<StatementSyntax> elseStatements = default;
        if (CurrentKind == SyntaxKind.ElseKeyword)
        {
            elseKeyword = Take();
            elseStatements = ParseStatementList();
        }

        return new CaseStatementSyntax(caseKeyword, selector, of, new SeparatedSyntaxList<CaseArmSyntax>(arms.ToImmutable()),
            elseKeyword, elseStatements, Expect(SyntaxKind.EndKeyword));
    }

    /// <summary><c>caseLabel { , caseLabel } : statement</c>, <c>caseLabel = constExpr [ .. constExpr ]</c></summary>
    private CaseArmSyntax ParseCaseArm()
    {
        var labels = ParseSeparated(ParseCaseLabel, SyntaxKind.CommaToken);
        var colon = Expect(SyntaxKind.ColonToken);
        return new CaseArmSyntax(labels, colon, ParseNestedStatement(elseMayFollow: true, semicolonElseIsOuter: true));
    }

    private CaseLabelSyntax ParseCaseLabel()
    {
        var value = ParseExpression();
        if (CurrentKind != SyntaxKind.DotDotToken)
        {
            return new CaseLabelSyntax(value, null, null);
        }

        var dotDot = Take();
        return new CaseLabelSyntax(value, dotDot, ParseExpression());
    }

    /// <summary><c>repeat stmtList until expression</c></summary>
    private RepeatStatementSyntax ParseRepeatStatement()
    {
        var repeat = Take();
        var statements = ParseStatementList();
        var until = Expect(SyntaxKind.UntilKeyword);
        return new RepeatStatementSyntax(repeat, statements, until, ParseExpression());
    }

    /// <summary><c>while expression do statement</c></summary>
    private WhileStatementSyntax ParseWhileStatement()
    {
        var whileKeyword = Take();
        var condition = ParseExpression();
        var doKeyword = Expect(SyntaxKind.DoKeyword);
        return new WhileStatementSyntax(whileKeyword, condition, doKeyword, ParseNestedStatement(_elseMayFollow, _semicolonElseIsOuter));
    }

    /// <summary>
    /// <c>for forVar := expression ( to | downto ) expression do statement</c> or <c>for forVar in expression do statement</c>;
    /// <c>forVar = ident | var ident [ : type ]</c>.
    /// </summary>
    private StatementSyntax ParseForStatement()
    {
        var forKeyword = Take();
        var varKeyword = CurrentKind == SyntaxKind.VarKeyword ? Take() : (SyntaxToken?)null;
        var variable = ParseIdentifierName();
        SyntaxToken? colon = null;
        TypeSyntax? type = null;
        if (varKeyword is not null && CurrentKind == SyntaxKind.ColonToken)
        {
            colon = Take();
            type = ParseType();
        }

        if (CurrentKind == SyntaxKind.InKeyword)
        {
            var inKeyword = Take();
            var collection = ParseExpression();
            var inDo = Expect(SyntaxKind.DoKeyword);
            return new ForInStatementSyntax(forKeyword, varKeyword, variable, colon, type, inKeyword, collection, inDo,
                ParseNestedStatement(_elseMayFollow, _semicolonElseIsOuter));
        }

        var colonEquals = Expect(SyntaxKind.ColonEqualsToken);
        var start = ParseExpression();
        var direction = CurrentKind == SyntaxKind.DownToKeyword ? Take() : Expect(SyntaxKind.ToKeyword);
        var finish = ParseExpression();
        var doKeyword = Expect(SyntaxKind.DoKeyword);
        return new ForStatementSyntax(forKeyword, varKeyword, variable, colon, type, colonEquals, start, direction, finish, doKeyword,
            ParseNestedStatement(_elseMayFollow, _semicolonElseIsOuter));
    }

    /// <summary><c>with designator { , designator } do statement</c></summary>
    private WithStatementSyntax ParseWithStatement()
    {
        var withKeyword = Take();
        var expressions = ParseSeparated(() => ParseExpression(), SyntaxKind.CommaToken);
        var doKeyword = Expect(SyntaxKind.DoKeyword);
        return new WithStatementSyntax(withKeyword, expressions, doKeyword, ParseNestedStatement(_elseMayFollow, _semicolonElseIsOuter));
    }

    /// <summary>
    /// <c>try stmtList ( finally stmtList | except exceptBlock ) end</c>; without either keyword a missing
    /// <c>finally</c> is inserted. The handler form is used iff the block starts with <c>on ident</c> (LANG-166).
    /// </summary>
    private StatementSyntax ParseTryStatement()
    {
        var tryKeyword = Take();
        var statements = ParseStatementList();
        if (CurrentKind != SyntaxKind.ExceptKeyword)
        {
            var finallyKeyword = Expect(SyntaxKind.FinallyKeyword);
            var finallyStatements = ParseStatementList();
            return new TryFinallyStatementSyntax(tryKeyword, statements, finallyKeyword, finallyStatements, Expect(SyntaxKind.EndKeyword));
        }

        var exceptKeyword = Take();
        var handlers = ImmutableArray.CreateBuilder<SyntaxNodeOrToken>();
        SyntaxToken? elseKeyword = null;
        SeparatedSyntaxList<StatementSyntax> exceptStatements = default;
        if (StartsHandler())
        {
            do
            {
                handlers.Add(ParseExceptionHandler());
                if (CurrentKind != SyntaxKind.SemicolonToken)
                {
                    break;
                }

                handlers.Add(Take());
            }
            while (StartsHandler());

            if (CurrentKind == SyntaxKind.ElseKeyword)
            {
                elseKeyword = Take();
                exceptStatements = ParseStatementList();
            }
        }
        else
        {
            exceptStatements = ParseStatementList();
        }

        return new TryExceptStatementSyntax(tryKeyword, statements, exceptKeyword,
            new SeparatedSyntaxList<ExceptionHandlerSyntax>(handlers.ToImmutable()), elseKeyword, exceptStatements,
            Expect(SyntaxKind.EndKeyword));
    }

    private bool StartsHandler() => IsContextual(Current, SyntaxKind.OnKeyword) && Peek(1).Kind == SyntaxKind.IdentifierToken;

    /// <summary><c>on [ ident : ] typeId do statement</c></summary>
    private ExceptionHandlerSyntax ParseExceptionHandler()
    {
        var on = TakeContextual();
        SyntaxToken? identifier = null;
        SyntaxToken? colon = null;
        if (Peek(1).Kind == SyntaxKind.ColonToken)
        {
            identifier = Take();
            colon = Take();
        }

        var type = ParseNamedType();
        var doKeyword = Expect(SyntaxKind.DoKeyword);
        return new ExceptionHandlerSyntax(on, identifier, colon, type, doKeyword,
            ParseNestedStatement(elseMayFollow: true, semicolonElseIsOuter: true));
    }

    /// <summary><c>raise [ expression [ at expression ] ]</c>; <c>at</c> is recognised only here (LANG-167).</summary>
    private RaiseStatementSyntax ParseRaiseStatement()
    {
        var raise = Take();
        if (CurrentKind is SyntaxKind.SemicolonToken or SyntaxKind.ElseKeyword || EndsStatementList(CurrentKind))
        {
            return new RaiseStatementSyntax(raise, null, null, null);
        }

        var exception = ParseExpression();
        if (!IsContextual(Current, SyntaxKind.AtKeyword))
        {
            return new RaiseStatementSyntax(raise, exception, null, null);
        }

        var at = TakeContextual();
        return new RaiseStatementSyntax(raise, exception, at, ParseExpression());
    }

    /// <summary><c>var identList [ : type ] [ := expression ]</c>; neither <c>:</c> nor <c>:=</c> inserts a missing <c>:</c>.</summary>
    private InlineVarStatementSyntax ParseInlineVarStatement()
    {
        var varKeyword = Take();
        var identifiers = ParseSeparated(ParseIdentifierName, SyntaxKind.CommaToken);
        SyntaxToken? colon = null;
        TypeSyntax? type = null;
        if (CurrentKind != SyntaxKind.ColonEqualsToken)
        {
            colon = Expect(SyntaxKind.ColonToken);
            type = ParseType();
        }

        if (CurrentKind != SyntaxKind.ColonEqualsToken)
        {
            return new InlineVarStatementSyntax(varKeyword, identifiers, colon, type, null, null);
        }

        var colonEquals = Take();
        return new InlineVarStatementSyntax(varKeyword, identifiers, colon, type, colonEquals, ParseExpression());
    }

    /// <summary><c>const ident [ : type ] = constExpr</c></summary>
    private InlineConstStatementSyntax ParseInlineConstStatement()
    {
        var constKeyword = Take();
        var identifier = ExpectIdentifier();
        SyntaxToken? colon = null;
        TypeSyntax? type = null;
        if (CurrentKind == SyntaxKind.ColonToken)
        {
            colon = Take();
            type = ParseType();
        }

        var equals = Expect(SyntaxKind.EqualsToken);
        return new InlineConstStatementSyntax(constKeyword, identifier, colon, type, equals, ParseExpression());
    }

    // ------------------------------------------------------------------ names

    private IdentifierNameSyntax ParseIdentifierName() => new(ExpectIdentifier());

    /// <summary>The name being declared by a var, field or parameter list (E1109 recovery for reserved words).</summary>
    private IdentifierNameSyntax ParseDeclaredName() => new(ExpectIdentifier(declaresName: true));

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

    // ------------------------------------------------------------------ grammar.md §7: generics (LANG-705, 706)

    private int _typeArgumentDepth;   // > 0 while parsing type arguments: '>' / '>=' end a type there

    /// <summary><c>typeId = ident [ genericArgs ] { . ident [ genericArgs ] }</c>; in a type position <c>&lt;</c> always starts generic arguments.</summary>
    private NameSyntax ParseTypeName()
    {
        NameSyntax name = ParseTypeNameSegment();
        while (CurrentKind == SyntaxKind.DotToken && Peek(1).Kind == SyntaxKind.IdentifierToken)
        {
            var dot = Take();
            name = new QualifiedNameSyntax(name, dot, ParseTypeNameSegment());
        }

        return name;
    }

    private SimpleNameSyntax ParseTypeNameSegment()
    {
        var identifier = ExpectIdentifier();
        return CurrentKind == SyntaxKind.LessThanToken
            ? new GenericNameSyntax(identifier, ParseTypeArgumentList())
            : new IdentifierNameSyntax(identifier);
    }

    /// <summary>
    /// <c>routineName = ident | typeNamePath . ident</c> with <c>typeNamePath = ident [ &lt; identList &gt; ] { . ident [ … ] }</c>:
    /// a <c>&lt;…&gt;</c> followed by <c>.</c> belongs to the path (a <see cref="GenericNameSyntax"/>); one after the last
    /// segment is the heading's type parameter list.
    /// </summary>
    private NameSyntax ParseRoutineName()
    {
        NameSyntax name = ParseRoutineNameSegment();
        while (CurrentKind == SyntaxKind.DotToken && Peek(1).Kind == SyntaxKind.IdentifierToken)
        {
            var dot = Take();
            name = new QualifiedNameSyntax(name, dot, ParseRoutineNameSegment());
        }

        return name;
    }

    private SimpleNameSyntax ParseRoutineNameSegment()
    {
        var identifier = ExpectIdentifier(declaresName: true);
        if (CurrentKind == SyntaxKind.LessThanToken && ClosingAngleOffset(0) is var close and >= 0
            && Peek(close).Kind == SyntaxKind.GreaterThanToken && Peek(close + 1).Kind == SyntaxKind.DotToken)
        {
            return new GenericNameSyntax(identifier, ParseTypeArgumentList());
        }

        return new IdentifierNameSyntax(identifier);
    }

    /// <summary>Offset of the <c>&gt;</c> / <c>&gt;=</c> closing the <c>&lt;</c> at <paramref name="offset"/>, or -1.</summary>
    private int ClosingAngleOffset(int offset)
    {
        var depth = 0;
        for (var i = offset; ; i++)
        {
            switch (Peek(i).Kind)
            {
                case SyntaxKind.LessThanToken:
                    depth++;
                    break;
                case SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken:
                    if (--depth == 0)
                    {
                        return i;
                    }

                    break;
                case SyntaxKind.IdentifierToken or SyntaxKind.CommaToken or SyntaxKind.DotToken or SyntaxKind.ColonToken
                    or SyntaxKind.SemicolonToken or SyntaxKind.StringKeyword or SyntaxKind.CaretToken or SyntaxKind.ClassKeyword
                    or SyntaxKind.RecordKeyword or SyntaxKind.ConstructorKeyword:
                    break;
                default:
                    return -1;
            }
        }
    }

    /// <summary><c>&lt; type { , type } &gt;</c></summary>
    private TypeArgumentListSyntax ParseTypeArgumentList()
    {
        var lessThan = Take();
        _typeArgumentDepth++;
        var arguments = ParseSeparated(ParseType, SyntaxKind.CommaToken);
        _typeArgumentDepth--;
        return new TypeArgumentListSyntax(lessThan, arguments, ExpectGreaterThan());
    }

    /// <summary><c>&lt; genericParam { ; genericParam } &gt;</c>, <c>genericParam = identList [ : constraint { , constraint } ]</c></summary>
    private TypeParameterListSyntax ParseTypeParameterList()
    {
        var lessThan = Take();
        var parameters = ParseSeparated(ParseTypeParameter, SyntaxKind.SemicolonToken);
        return new TypeParameterListSyntax(lessThan, parameters, ExpectGreaterThan());
    }

    private TypeParameterSyntax ParseTypeParameter()
    {
        var identifiers = ParseSeparated(ParseIdentifierName, SyntaxKind.CommaToken);
        if (CurrentKind != SyntaxKind.ColonToken)
        {
            return new TypeParameterSyntax(identifiers, null, default);
        }

        var colon = Take();
        return new TypeParameterSyntax(identifiers, colon, ParseSeparated(ParseConstraint, SyntaxKind.CommaToken));
    }

    /// <summary><c>constraint = typeId | class | record | constructor</c></summary>
    private SyntaxNode ParseConstraint() => CurrentKind is SyntaxKind.ClassKeyword or SyntaxKind.RecordKeyword or SyntaxKind.ConstructorKeyword
        ? new KeywordConstraintSyntax(Take())
        : ParseNamedType();

    /// <summary>The <c>&gt;</c> closing a generic list; a <c>&gt;=</c> is split into <c>&gt;</c> and <c>=</c> first (LANG-706).</summary>
    private SyntaxToken ExpectGreaterThan()
    {
        if (CurrentKind == SyntaxKind.GreaterThanEqualsToken)
        {
            var token = Current;
            var greaterThan = new SyntaxToken(SyntaxKind.GreaterThanToken, ">", null, token.Span.Start, token.LeadingTrivia, [],
                token.DirectiveState, token.File);
            var equals = new SyntaxToken(SyntaxKind.EqualsToken, "=", null, token.Span.Start + 1, [], token.TrailingTrivia,
                token.DirectiveState, token.File);
            _tokens = _tokens.SetItem(_position, greaterThan).Insert(_position + 1, equals);
        }

        return Expect(SyntaxKind.GreaterThanToken);
    }

    /// <summary>An identifier in an expression, with generic arguments when LANG-705 says the <c>&lt;</c> starts them.</summary>
    private SimpleNameSyntax ParseSimpleNameInExpression()
    {
        var identifier = ExpectIdentifier();
        return CurrentKind == SyntaxKind.LessThanToken && !identifier.IsMissing && StartsTypeArgumentsInExpression()
            ? new GenericNameSyntax(identifier, ParseTypeArgumentList())
            : new IdentifierNameSyntax(identifier);
    }

    /// <summary>
    /// LANG-705: the <c>&lt;</c> here starts generic arguments iff <c>type { , type } &gt;</c> can be read (types: qualified
    /// identifiers with nested generic arguments, <c>string</c>, <c>^ident</c>) and the token after the <c>&gt;</c> cannot
    /// start a factor or is one of the listed followers. A closing <c>&gt;=</c> counts as <c>&gt;</c> followed by <c>=</c>.
    /// </summary>
    private bool StartsTypeArgumentsInExpression()
    {
        var end = ScanTypeArguments(0, out var closedBySplit);
        if (end < 0)
        {
            return false;
        }

        if (closedBySplit)
        {
            return true;   // the follower is the '=' of the split '>='
        }

        var next = Peek(end).Kind;
        return next is SyntaxKind.OpenParenToken or SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken or SyntaxKind.DotToken
                   or SyntaxKind.SemicolonToken or SyntaxKind.CommaToken or SyntaxKind.ColonEqualsToken or SyntaxKind.CaretToken
                   or SyntaxKind.EqualsToken or SyntaxKind.LessThanGreaterThanToken or SyntaxKind.EndKeyword or SyntaxKind.ThenKeyword
                   or SyntaxKind.DoKeyword or SyntaxKind.OfKeyword or SyntaxKind.ElseKeyword or SyntaxKind.ToKeyword
                   or SyntaxKind.DownToKeyword or SyntaxKind.EndOfFileToken
               || !CanStartFactor(next);
    }

    private static bool CanStartFactor(SyntaxKind kind) => kind is SyntaxKind.IdentifierToken or SyntaxKind.IntegerLiteralToken
        or SyntaxKind.RealLiteralToken or SyntaxKind.StringLiteralToken or SyntaxKind.NilKeyword or SyntaxKind.OpenParenToken
        or SyntaxKind.OpenBracketToken or SyntaxKind.AtToken or SyntaxKind.NotKeyword or SyntaxKind.PlusToken or SyntaxKind.MinusToken
        or SyntaxKind.InheritedKeyword or SyntaxKind.StringKeyword or SyntaxKind.ProcedureKeyword or SyntaxKind.FunctionKeyword;

    /// <summary>
    /// Scans <c>&lt; type { , type } &gt;</c> starting at the <c>&lt;</c> at <paramref name="offset"/>; returns the offset after
    /// the closing <c>&gt;</c> (or of a closing <c>&gt;=</c>, with <paramref name="closedBySplit"/>), or -1.
    /// </summary>
    private int ScanTypeArguments(int offset, out bool closedBySplit)
    {
        closedBySplit = false;
        var i = offset + 1;
        while (true)
        {
            i = ScanTypeArgument(i);
            if (i < 0)
            {
                return -1;
            }

            switch (Peek(i).Kind)
            {
                case SyntaxKind.CommaToken:
                    i++;
                    break;
                case SyntaxKind.GreaterThanToken:
                    return i + 1;
                case SyntaxKind.GreaterThanEqualsToken:
                    closedBySplit = true;
                    return i;
                default:
                    return -1;
            }
        }
    }

    /// <summary>One restricted type of LANG-705 at <paramref name="i"/>; the offset after it, or -1.</summary>
    private int ScanTypeArgument(int i)
    {
        switch (Peek(i).Kind)
        {
            case SyntaxKind.StringKeyword:
                return i + 1;
            case SyntaxKind.CaretToken:
                return Peek(i + 1).Kind == SyntaxKind.IdentifierToken ? i + 2 : -1;
            case SyntaxKind.IdentifierToken:
                i++;
                while (true)
                {
                    if (Peek(i).Kind == SyntaxKind.LessThanToken)
                    {
                        i = ScanTypeArguments(i, out var split);
                        if (i < 0 || split)
                        {
                            return -1;
                        }
                    }

                    if (Peek(i).Kind != SyntaxKind.DotToken || Peek(i + 1).Kind != SyntaxKind.IdentifierToken)
                    {
                        return i;
                    }

                    i += 2;
                }

            default:
                return -1;
        }
    }

    // ------------------------------------------------------------------ attributes (LANG-172, 760) and anonymous methods

    /// <summary><c>{ "[" attribute { , attribute } "]" }</c>; empty when the current token is not <c>[</c>.</summary>
    private SyntaxList<AttributeListSyntax> ParseAttributeLists()
    {
        if (CurrentKind != SyntaxKind.OpenBracketToken)
        {
            return default;
        }

        var lists = ImmutableArray.CreateBuilder<AttributeListSyntax>();
        while (CurrentKind == SyntaxKind.OpenBracketToken)
        {
            var open = Take();
            var attributes = ParseSeparated(() => ParseAttribute(open), SyntaxKind.CommaToken);
            lists.Add(new AttributeListSyntax(open, attributes, Expect(SyntaxKind.CloseBracketToken)));
        }

        return new SyntaxList<AttributeListSyntax>(lists.ToImmutable());
    }

    /// <summary>
    /// <c>qualIdent [ ( [ constExpr { , constExpr } ] ) ]</c>; without a name it is E1119 at the list's <c>[</c> and the
    /// tokens up to <c>,</c> / <c>]</c> are skipped.
    /// </summary>
    private AttributeSyntax ParseAttribute(SyntaxToken openBracket)
    {
        if (CurrentKind != SyntaxKind.IdentifierToken)
        {
            ReportAt(openBracket, "E1119");
            while (CurrentKind is not (SyntaxKind.CommaToken or SyntaxKind.CloseBracketToken or SyntaxKind.SemicolonToken
                   or SyntaxKind.EndOfFileToken))
            {
                SkipToken(report: false);
            }

            return new AttributeSyntax(new IdentifierNameSyntax(Missing(SyntaxKind.IdentifierToken)), null);
        }

        var name = ParseQualifiedName();
        return new AttributeSyntax(name, CurrentKind == SyntaxKind.OpenParenToken ? ParseArgumentList() : null);
    }

    /// <summary>
    /// In a const/type/var/field list: the next declaration starts here — an identifier that is not a visibility keyword,
    /// or attribute lists followed by an identifier.
    /// </summary>
    private bool StartsDeclarationName()
    {
        if (CurrentKind == SyntaxKind.IdentifierToken)
        {
            return !StartsVisibility();
        }

        if (CurrentKind != SyntaxKind.OpenBracketToken)
        {
            return false;
        }

        var offset = 0;
        while (Peek(offset).Kind == SyntaxKind.OpenBracketToken)
        {
            var depth = 0;
            do
            {
                switch (Peek(offset).Kind)
                {
                    case SyntaxKind.OpenBracketToken:
                        depth++;
                        break;
                    case SyntaxKind.CloseBracketToken:
                        depth--;
                        break;
                    case SyntaxKind.EndOfFileToken or SyntaxKind.SemicolonToken:
                        return false;
                }

                offset++;
            }
            while (depth > 0);
        }

        return Peek(offset).Kind == SyntaxKind.IdentifierToken;
    }

    /// <summary><c>procedure [ formalParams ] block</c> | <c>function [ formalParams ] : resultType block</c> (LANG-720)</summary>
    private AnonymousMethodExpressionSyntax ParseAnonymousMethod()
    {
        var keyword = Take();
        var parameters = CurrentKind == SyntaxKind.OpenParenToken ? ParseParameterList() : null;
        SyntaxToken? colon = null;
        TypeSyntax? result = null;
        if (keyword.Kind == SyntaxKind.FunctionKeyword)
        {
            colon = Expect(SyntaxKind.ColonToken);
            result = ParseType();
        }

        var depth = _typeArgumentDepth;
        _typeArgumentDepth = 0;
        var block = ParseBlock();
        _typeArgumentDepth = depth;
        return new AnonymousMethodExpressionSyntax(keyword, parameters, colon, result, block);
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

    private const int MaxDepth = 1000;
    private int _depth;   // nesting of the guarded recursive productions (blocks, types, typed constants, factors, statements)

    /// <summary>
    /// Nesting so deep that the parser could exhaust the stack: LANG-180 forbids crashing, so the rest of the file is
    /// given up (<see cref="AbandonRest"/>).
    /// </summary>
    private bool TooDeep() => _depth >= MaxDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack();

    /// <summary>E1120 at the current token, then every remaining token becomes skipped trivia of the end-of-file token.</summary>
    private void AbandonRest()
    {
        if (CurrentKind == SyntaxKind.EndOfFileToken)
        {
            return;
        }

        ReportAt(Current, "E1120");
        while (CurrentKind != SyntaxKind.EndOfFileToken)
        {
            SkipToken(report: false);
        }
    }

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

    /// <summary>
    /// The current identifier, else a missing one with E1102 (E1108 at EOF). Where a name is being declared
    /// (<paramref name="declaresName"/>), a reserved word followed by <c>: , = ( ; := &lt;</c> is taken as the name with
    /// E1109 so that the declaration still parses.
    /// </summary>
    private SyntaxToken ExpectIdentifier(bool declaresName = false)
    {
        if (CurrentKind == SyntaxKind.IdentifierToken)
        {
            return Take();
        }

        if (declaresName && SyntaxFacts.IsKeyword(CurrentKind)
            && Peek(1).Kind is SyntaxKind.ColonToken or SyntaxKind.CommaToken or SyntaxKind.EqualsToken or SyntaxKind.OpenParenToken
                or SyntaxKind.SemicolonToken or SyntaxKind.ColonEqualsToken or SyntaxKind.LessThanToken)
        {
            ReportAt(Current, "E1109", Current.Text);
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
