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

    /// <summary><c>program</c>; also <c>library</c> / <c>package</c>, which are reported as E2900 (LANG-151).</summary>
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
