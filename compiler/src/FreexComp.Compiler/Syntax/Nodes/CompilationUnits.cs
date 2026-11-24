namespace FreexComp.Compiler.Syntax;

// grammar.md §1. Every compilation unit owns the EndOfFileToken (CMP-220; api.md addendum C-011).

/// <summary><c>[ program ident [ ( identList ) ] ; ] [ usesClause ] block .</c></summary>
public sealed class ProgramSyntax : CompilationUnitSyntax
{
    internal ProgramSyntax(SyntaxToken? programKeyword, IdentifierNameSyntax? name, ProgramParameterListSyntax? parameterList,
                           SyntaxToken? semicolonToken, UsesClauseSyntax? usesClause, BlockSyntax block, SyntaxToken dotToken,
                           SyntaxToken endOfFileToken)
        : base(SyntaxKind.Program)
    {
        ProgramKeyword = Add(programKeyword);
        Name = AddOptional(name);
        ParameterList = AddOptional(parameterList);
        SemicolonToken = Add(semicolonToken);
        UsesClause = AddOptional(usesClause);
        Block = Add(block);
        DotToken = Add(dotToken);
        EndOfFileToken = Add(endOfFileToken);
    }

    /// <summary><c>program</c>; also <c>package</c>, which is reported as E2900 (LANG-151).</summary>
    public SyntaxToken? ProgramKeyword { get; }

    public IdentifierNameSyntax? Name { get; }

    /// <summary>The ignored program parameter list.</summary>
    public ProgramParameterListSyntax? ParameterList { get; }

    public SyntaxToken? SemicolonToken { get; }

    public UsesClauseSyntax? UsesClause { get; }

    public BlockSyntax Block { get; }

    public SyntaxToken DotToken { get; }

    public SyntaxToken EndOfFileToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitProgram(this);
}

/// <summary><c>( identList )</c> after the program name (ignored, grammar.md §1).</summary>
public sealed class ProgramParameterListSyntax : SyntaxNode
{
    internal ProgramParameterListSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<IdentifierNameSyntax> parameters,
                                        SyntaxToken closeParenToken)
        : base(SyntaxKind.ProgramParameterList)
    {
        OpenParenToken = Add(openParenToken);
        Parameters = Add(parameters);
        CloseParenToken = Add(closeParenToken);
    }

    public SyntaxToken OpenParenToken { get; }

    public SeparatedSyntaxList<IdentifierNameSyntax> Parameters { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitProgramParameterList(this);
}

/// <summary>
/// <c>library qualIdent ; [ usesClause ] { declSection | exportsClause } ( compoundStmt | end ) .</c> (LANG-540, 541; CMP-1056).
/// </summary>
public sealed class LibrarySyntax : CompilationUnitSyntax
{
    internal LibrarySyntax(SyntaxToken libraryKeyword, NameSyntax name, SyntaxToken semicolonToken, UsesClauseSyntax? usesClause,
                           BlockSyntax block, SyntaxToken dotToken, SyntaxToken endOfFileToken)
        : base(SyntaxKind.Library)
    {
        LibraryKeyword = Add(libraryKeyword);
        Name = Add(name);
        SemicolonToken = Add(semicolonToken);
        UsesClause = AddOptional(usesClause);
        Block = Add(block);
        DotToken = Add(dotToken);
        EndOfFileToken = Add(endOfFileToken);
    }

    public SyntaxToken LibraryKeyword { get; }

    /// <summary>The (possibly dotted) library name; informative, it need not match the file name (LANG-540).</summary>
    public NameSyntax Name { get; }

    public SyntaxToken SemicolonToken { get; }

    public UsesClauseSyntax? UsesClause { get; }

    /// <summary>
    /// The declarations (with the <see cref="ExportsClauseSyntax"/> nodes among them, in source order) and the library block. The
    /// <c>end</c>-only form has a compound statement whose <c>begin</c> is a missing token and that has no statements (LANG-541).
    /// </summary>
    public BlockSyntax Block { get; }

    public SyntaxToken DotToken { get; }

    public SyntaxToken EndOfFileToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitLibrary(this);
}

/// <summary><c>exports exportItem { , exportItem } ;</c> (LANG-544).</summary>
public sealed class ExportsClauseSyntax : SyntaxNode
{
    internal ExportsClauseSyntax(SyntaxToken exportsKeyword, SeparatedSyntaxList<ExportItemSyntax> items, SyntaxToken semicolonToken)
        : base(SyntaxKind.ExportsClause)
    {
        ExportsKeyword = Add(exportsKeyword);
        Items = Add(items);
        SemicolonToken = Add(semicolonToken);
    }

    public SyntaxToken ExportsKeyword { get; }

    public SeparatedSyntaxList<ExportItemSyntax> Items { get; }

    public SyntaxToken SemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitExportsClause(this);
}

/// <summary><c>qualIdent { index constExpr | name constExpr }</c>: each specifier at most once, in either order (LANG-546).</summary>
public sealed class ExportItemSyntax : SyntaxNode
{
    internal ExportItemSyntax(NameSyntax name, ExportSpecifierSyntax? first, ExportSpecifierSyntax? second)
        : base(SyntaxKind.ExportItem)
    {
        Name = Add(name);
        var (one, two) = (AddOptional(first), AddOptional(second));
        IndexSpecifier = one?.Keyword.ContextualKind == SyntaxKind.IndexKeyword ? one : two?.Keyword.ContextualKind == SyntaxKind.IndexKeyword ? two : null;
        NameSpecifier = one?.Keyword.ContextualKind == SyntaxKind.NameKeyword ? one : two?.Keyword.ContextualKind == SyntaxKind.NameKeyword ? two : null;
    }

    /// <summary>The exported routine: <c>Proc</c> or <c>UnitName.Proc</c> (LANG-545).</summary>
    public NameSyntax Name { get; }

    /// <summary><c>index N</c>.</summary>
    public ExportSpecifierSyntax? IndexSpecifier { get; }

    /// <summary><c>name S</c>.</summary>
    public ExportSpecifierSyntax? NameSpecifier { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitExportItem(this);
}

/// <summary><c>index constExpr</c> or <c>name constExpr</c> of an export item.</summary>
public sealed class ExportSpecifierSyntax : SyntaxNode
{
    internal ExportSpecifierSyntax(SyntaxToken keyword, ExpressionSyntax value)
        : base(SyntaxKind.ExportSpecifier)
    {
        Keyword = Add(keyword);
        Value = Add(value);
    }

    /// <summary>The contextual keyword <c>index</c> or <c>name</c>.</summary>
    public SyntaxToken Keyword { get; }

    public ExpressionSyntax Value { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitExportSpecifier(this);
}

/// <summary><c>unit qualIdent [ hintDirectives ] ; interfaceSection implementationSection unitEnd .</c></summary>
public sealed class UnitSyntax : CompilationUnitSyntax
{
    internal UnitSyntax(SyntaxToken unitKeyword, NameSyntax name, SyntaxList<HintDirectiveSyntax> hintDirectives,
                        SyntaxToken semicolonToken, InterfaceSectionSyntax interfaceSection,
                        ImplementationSectionSyntax implementationSection, InitializationSectionSyntax? initializationSection,
                        FinalizationSectionSyntax? finalizationSection, SyntaxToken endKeyword, SyntaxToken dotToken,
                        SyntaxToken endOfFileToken)
        : base(SyntaxKind.Unit)
    {
        UnitKeyword = Add(unitKeyword);
        Name = Add(name);
        HintDirectives = Add(hintDirectives);
        SemicolonToken = Add(semicolonToken);
        InterfaceSection = Add(interfaceSection);
        ImplementationSection = Add(implementationSection);
        InitializationSection = AddOptional(initializationSection);
        FinalizationSection = AddOptional(finalizationSection);
        EndKeyword = Add(endKeyword);
        DotToken = Add(dotToken);
        EndOfFileToken = Add(endOfFileToken);
    }

    public SyntaxToken UnitKeyword { get; }

    /// <summary>The (possibly dotted) unit name (LANG-152).</summary>
    public NameSyntax Name { get; }

    public SyntaxList<HintDirectiveSyntax> HintDirectives { get; }

    public SyntaxToken SemicolonToken { get; }

    public InterfaceSectionSyntax InterfaceSection { get; }

    public ImplementationSectionSyntax ImplementationSection { get; }

    /// <summary><c>initialization …</c>, or the <c>begin …</c> form (then <see cref="InitializationSectionSyntax.Keyword"/> is <c>begin</c>).</summary>
    public InitializationSectionSyntax? InitializationSection { get; }

    public FinalizationSectionSyntax? FinalizationSection { get; }

    public SyntaxToken EndKeyword { get; }

    public SyntaxToken DotToken { get; }

    public SyntaxToken EndOfFileToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitUnit(this);
}

/// <summary>
/// The root of an included file's own tree (<see cref="SyntaxTree.IncludedTrees"/>): its tokens as a flat sequence,
/// the last one the EndOfFileToken. For the IDE's view of the file; the program is parsed from the main file.
/// </summary>
public sealed class IncludeFileSyntax : CompilationUnitSyntax
{
    internal IncludeFileSyntax(IEnumerable<SyntaxToken> tokens)
        : base(SyntaxKind.IncludeFile)
    {
        foreach (var token in tokens)
        {
            EndOfFileToken = Add(token);
        }
    }

    public SyntaxToken EndOfFileToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitIncludeFile(this);
}

/// <summary>
/// The root of a tree made by <c>SyntaxFactory.ParseNode&lt;T&gt;</c>: one fragment node (expression, statement, type,
/// member or declaration) and the EndOfFileToken, which carries any surplus input as skipped trivia.
/// </summary>
public sealed class FragmentSyntax : CompilationUnitSyntax
{
    internal FragmentSyntax(SyntaxNode node, SyntaxToken endOfFileToken)
        : base(SyntaxKind.Fragment)
    {
        Node = Add(node);
        EndOfFileToken = Add(endOfFileToken);
    }

    public SyntaxNode Node { get; }

    public SyntaxToken EndOfFileToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitFragment(this);
}

/// <summary><c>uses usedUnit { , usedUnit } ;</c></summary>
public sealed class UsesClauseSyntax : SyntaxNode
{
    internal UsesClauseSyntax(SyntaxToken usesKeyword, SeparatedSyntaxList<UsedUnitSyntax> units, SyntaxToken semicolonToken)
        : base(SyntaxKind.UsesClause)
    {
        UsesKeyword = Add(usesKeyword);
        Units = Add(units);
        SemicolonToken = Add(semicolonToken);
    }

    public SyntaxToken UsesKeyword { get; }

    public SeparatedSyntaxList<UsedUnitSyntax> Units { get; }

    public SyntaxToken SemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitUsesClause(this);
}

/// <summary><c>qualIdent [ in stringLiteral ]</c></summary>
public sealed class UsedUnitSyntax : SyntaxNode
{
    internal UsedUnitSyntax(NameSyntax name, SyntaxToken? inKeyword, SyntaxToken? path)
        : base(SyntaxKind.UsedUnit)
    {
        Name = Add(name);
        InKeyword = Add(inKeyword);
        Path = Add(path);
    }

    public NameSyntax Name { get; }

    public SyntaxToken? InKeyword { get; }

    /// <summary>The string literal after <c>in</c>.</summary>
    public SyntaxToken? Path { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitUsedUnit(this);
}

/// <summary><c>interface [ usesClause ] { interfaceDecl }</c></summary>
public sealed class InterfaceSectionSyntax : SyntaxNode
{
    internal InterfaceSectionSyntax(SyntaxToken interfaceKeyword, UsesClauseSyntax? usesClause, SyntaxList<SyntaxNode> declarations)
        : base(SyntaxKind.InterfaceSection)
    {
        InterfaceKeyword = Add(interfaceKeyword);
        UsesClause = AddOptional(usesClause);
        Declarations = Add(declarations);
    }

    public SyntaxToken InterfaceKeyword { get; }

    public UsesClauseSyntax? UsesClause { get; }

    public SyntaxList<SyntaxNode> Declarations { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitInterfaceSection(this);
}

/// <summary><c>implementation [ usesClause ] { declSection }</c></summary>
public sealed class ImplementationSectionSyntax : SyntaxNode
{
    internal ImplementationSectionSyntax(SyntaxToken implementationKeyword, UsesClauseSyntax? usesClause,
                                         SyntaxList<SyntaxNode> declarations)
        : base(SyntaxKind.ImplementationSection)
    {
        ImplementationKeyword = Add(implementationKeyword);
        UsesClause = AddOptional(usesClause);
        Declarations = Add(declarations);
    }

    public SyntaxToken ImplementationKeyword { get; }

    public UsesClauseSyntax? UsesClause { get; }

    public SyntaxList<SyntaxNode> Declarations { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitImplementationSection(this);
}

/// <summary><c>initialization stmtList</c>, or <c>begin stmtList</c> before the unit's final <c>end</c>.</summary>
public sealed class InitializationSectionSyntax : SyntaxNode
{
    internal InitializationSectionSyntax(SyntaxToken keyword, SeparatedSyntaxList<StatementSyntax> statements)
        : base(SyntaxKind.InitializationSection)
    {
        Keyword = Add(keyword);
        Statements = Add(statements);
    }

    /// <summary><c>initialization</c> or <c>begin</c>.</summary>
    public SyntaxToken Keyword { get; }

    public SeparatedSyntaxList<StatementSyntax> Statements { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitInitializationSection(this);
}

/// <summary><c>finalization stmtList</c></summary>
public sealed class FinalizationSectionSyntax : SyntaxNode
{
    internal FinalizationSectionSyntax(SyntaxToken finalizationKeyword, SeparatedSyntaxList<StatementSyntax> statements)
        : base(SyntaxKind.FinalizationSection)
    {
        FinalizationKeyword = Add(finalizationKeyword);
        Statements = Add(statements);
    }

    public SyntaxToken FinalizationKeyword { get; }

    public SeparatedSyntaxList<StatementSyntax> Statements { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitFinalizationSection(this);
}
