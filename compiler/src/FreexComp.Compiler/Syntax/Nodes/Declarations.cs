namespace FreexComp.Compiler.Syntax;

// grammar.md §2. Attribute lists (C-019) and generic parameters (C-019) are added to these classes later.

/// <summary><c>label labelId { , labelId } ;</c> — labels are identifier names or integer literals.</summary>
public sealed class LabelSectionSyntax : SyntaxNode
{
    internal LabelSectionSyntax(SyntaxToken labelKeyword, SeparatedSyntaxList<ExpressionSyntax> labels, SyntaxToken semicolonToken)
        : base(SyntaxKind.LabelSection)
    {
        LabelKeyword = Add(labelKeyword);
        Labels = Add(labels);
        SemicolonToken = Add(semicolonToken);
    }

    public SyntaxToken LabelKeyword { get; }

    /// <summary><see cref="IdentifierNameSyntax"/> or integer <see cref="LiteralExpressionSyntax"/>.</summary>
    public SeparatedSyntaxList<ExpressionSyntax> Labels { get; }

    public SyntaxToken SemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitLabelSection(this);
}

/// <summary><c>( const | resourcestring ) constDecl { constDecl }</c></summary>
public sealed class ConstSectionSyntax : SyntaxNode
{
    internal ConstSectionSyntax(SyntaxToken keyword, SyntaxList<ConstDeclarationSyntax> declarations)
        : base(SyntaxKind.ConstSection)
    {
        Keyword = Add(keyword);
        Declarations = Add(declarations);
    }

    /// <summary><c>const</c> or <c>resourcestring</c> (LANG-346).</summary>
    public SyntaxToken Keyword { get; }

    public SyntaxList<ConstDeclarationSyntax> Declarations { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitConstSection(this);
}

/// <summary>
/// <c>ident = constExpr [ hints ] ;</c> (true constant) or <c>ident : type = typedConst [ hints ] ;</c> (typed constant).
/// </summary>
public sealed class ConstDeclarationSyntax : SyntaxNode
{
    internal ConstDeclarationSyntax(SyntaxList<AttributeListSyntax> attributes,
                                    SyntaxToken identifier, SyntaxToken? colonToken, TypeSyntax? type, SyntaxToken equalsToken,
                                    SyntaxNode value, SyntaxList<HintDirectiveSyntax> hintDirectives, SyntaxToken semicolonToken)
        : base(SyntaxKind.ConstDeclaration)
    {
        Attributes = Add(attributes);
        Identifier = Add(identifier);
        ColonToken = Add(colonToken);
        Type = AddOptional(type);
        EqualsToken = Add(equalsToken);
        Value = Add(value);
        HintDirectives = Add(hintDirectives);
        SemicolonToken = Add(semicolonToken);
    }

    /// <summary>Attribute lists before the declaration (LANG-172).</summary>
    public SyntaxList<AttributeListSyntax> Attributes { get; }

    public SyntaxToken Identifier { get; }

    public SyntaxToken? ColonToken { get; }

    /// <summary>Present for a typed constant.</summary>
    public TypeSyntax? Type { get; }

    public SyntaxToken EqualsToken { get; }

    /// <summary>An <see cref="ExpressionSyntax"/>, or for a typed constant also a <see cref="TypedConstantAggregateSyntax"/>.</summary>
    public SyntaxNode Value { get; }

    public SyntaxList<HintDirectiveSyntax> HintDirectives { get; }

    public SyntaxToken SemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitConstDeclaration(this);
}

/// <summary>
/// <c>( typedConst { , typedConst } )</c> (array) or <c>( [ ident : typedConst { ; ident : typedConst } [ ; ] ] )</c>
/// (record; elements are <see cref="RecordConstantFieldSyntax"/>). A single parenthesised expression is a
/// <see cref="ParenthesizedExpressionSyntax"/> instead.
/// </summary>
public sealed class TypedConstantAggregateSyntax : SyntaxNode
{
    internal TypedConstantAggregateSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<SyntaxNode> elements, SyntaxToken closeParenToken)
        : base(SyntaxKind.TypedConstantAggregate)
    {
        OpenParenToken = Add(openParenToken);
        Elements = Add(elements);
        CloseParenToken = Add(closeParenToken);
    }

    public SyntaxToken OpenParenToken { get; }

    /// <summary>Expressions and nested aggregates separated by <c>,</c>, or record fields separated by <c>;</c>.</summary>
    public SeparatedSyntaxList<SyntaxNode> Elements { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitTypedConstantAggregate(this);
}

/// <summary><c>ident : typedConst</c> inside a record constant.</summary>
public sealed class RecordConstantFieldSyntax : SyntaxNode
{
    internal RecordConstantFieldSyntax(IdentifierNameSyntax name, SyntaxToken colonToken, SyntaxNode value)
        : base(SyntaxKind.RecordConstantField)
    {
        Name = Add(name);
        ColonToken = Add(colonToken);
        Value = Add(value);
    }

    public IdentifierNameSyntax Name { get; }

    public SyntaxToken ColonToken { get; }

    /// <summary>An <see cref="ExpressionSyntax"/> or a <see cref="TypedConstantAggregateSyntax"/>.</summary>
    public SyntaxNode Value { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitRecordConstantField(this);
}

/// <summary><c>type typeDecl { typeDecl }</c></summary>
public sealed class TypeSectionSyntax : SyntaxNode
{
    internal TypeSectionSyntax(SyntaxToken typeKeyword, SyntaxList<TypeDeclarationSyntax> declarations)
        : base(SyntaxKind.TypeSection)
    {
        TypeKeyword = Add(typeKeyword);
        Declarations = Add(declarations);
    }

    public SyntaxToken TypeKeyword { get; }

    public SyntaxList<TypeDeclarationSyntax> Declarations { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitTypeSection(this);
}

/// <summary><c>ident = [ type ] type [ hints ] ;</c> (the inner <c>type</c> makes a distinct type).</summary>
public sealed class TypeDeclarationSyntax : SyntaxNode
{
    internal TypeDeclarationSyntax(SyntaxList<AttributeListSyntax> attributes,
                                   SyntaxToken identifier, TypeParameterListSyntax? typeParameterList, SyntaxToken equalsToken, SyntaxToken? typeKeyword, TypeSyntax type,
                                   SyntaxList<HintDirectiveSyntax> hintDirectives, SyntaxToken semicolonToken)
        : base(SyntaxKind.TypeDeclaration)
    {
        Attributes = Add(attributes);
        Identifier = Add(identifier);
        TypeParameterList = AddOptional(typeParameterList);
        EqualsToken = Add(equalsToken);
        TypeKeyword = Add(typeKeyword);
        Type = Add(type);
        HintDirectives = Add(hintDirectives);
        SemicolonToken = Add(semicolonToken);
    }

    /// <summary>Attribute lists before the declaration (LANG-172).</summary>
    public SyntaxList<AttributeListSyntax> Attributes { get; }

    public SyntaxToken Identifier { get; }

    public TypeParameterListSyntax? TypeParameterList { get; }

    public SyntaxToken EqualsToken { get; }

    public SyntaxToken? TypeKeyword { get; }

    public TypeSyntax Type { get; }

    public SyntaxList<HintDirectiveSyntax> HintDirectives { get; }

    public SyntaxToken SemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitTypeDeclaration(this);
}

/// <summary><c>( var | threadvar ) varDecl { varDecl }</c></summary>
public sealed class VarSectionSyntax : SyntaxNode
{
    internal VarSectionSyntax(SyntaxToken keyword, SyntaxList<VarDeclarationSyntax> declarations)
        : base(SyntaxKind.VarSection)
    {
        Keyword = Add(keyword);
        Declarations = Add(declarations);
    }

    /// <summary><c>var</c> or <c>threadvar</c> (LANG-345).</summary>
    public SyntaxToken Keyword { get; }

    public SyntaxList<VarDeclarationSyntax> Declarations { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitVarSection(this);
}

/// <summary><c>identList : type [ = typedConst ] [ hints ] ;</c></summary>
public sealed class VarDeclarationSyntax : SyntaxNode
{
    internal VarDeclarationSyntax(SyntaxList<AttributeListSyntax> attributes,
                                  SeparatedSyntaxList<IdentifierNameSyntax> identifiers, SyntaxToken colonToken, TypeSyntax type,
                                  SyntaxToken? equalsToken, SyntaxNode? initializer, SyntaxList<HintDirectiveSyntax> hintDirectives,
                                  SyntaxToken semicolonToken, DirectiveSyntax? publicDirective = null)
        : base(SyntaxKind.VarDeclaration)
    {
        Attributes = Add(attributes);
        Identifiers = Add(identifiers);
        ColonToken = Add(colonToken);
        Type = Add(type);
        EqualsToken = Add(equalsToken);
        Initializer = AddOptional(initializer);
        HintDirectives = Add(hintDirectives);
        SemicolonToken = Add(semicolonToken);
        PublicDirective = AddOptional(publicDirective);
    }

    /// <summary>Attribute lists before the declaration (LANG-172).</summary>
    public SyntaxList<AttributeListSyntax> Attributes { get; }

    public SeparatedSyntaxList<IdentifierNameSyntax> Identifiers { get; }

    public SyntaxToken ColonToken { get; }

    public TypeSyntax Type { get; }

    public SyntaxToken? EqualsToken { get; }

    /// <summary>The initialiser (an expression or aggregate); the one-identifier/global-only rule is the binder's.</summary>
    public SyntaxNode? Initializer { get; }

    public SyntaxList<HintDirectiveSyntax> HintDirectives { get; }

    public SyntaxToken SemicolonToken { get; }

    /// <summary><c>public name 'sym';</c> after the declaration (LANG-470); the global-only rule is the binder's.</summary>
    public DirectiveSyntax? PublicDirective { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitVarDeclaration(this);
}
