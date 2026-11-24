namespace FreexComp.Compiler.Syntax;

// grammar.md §5 (formal parameters are shared by routine headings and procedural types).

/// <summary><c>( [ formalParam { ; formalParam } ] )</c></summary>
public sealed class ParameterListSyntax : SyntaxNode
{
    internal ParameterListSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<ParameterSyntax> parameters, SyntaxToken closeParenToken)
        : base(SyntaxKind.ParameterList)
    {
        OpenParenToken = Add(openParenToken);
        Parameters = Add(parameters);
        CloseParenToken = Add(closeParenToken);
    }

    public SyntaxToken OpenParenToken { get; }

    /// <summary>Separated by <c>;</c>.</summary>
    public SeparatedSyntaxList<ParameterSyntax> Parameters { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitParameterList(this);
}

/// <summary><c>[ const | var | out ] identList [ : paramType [ = constExpr ] ]</c> (no type = untyped).</summary>
public sealed class ParameterSyntax : SyntaxNode
{
    internal ParameterSyntax(SyntaxList<AttributeListSyntax> attributes,
                             SyntaxToken? modifier, SeparatedSyntaxList<IdentifierNameSyntax> identifiers, SyntaxToken? colonToken,
                             TypeSyntax? type, SyntaxToken? equalsToken, ExpressionSyntax? defaultValue)
        : base(SyntaxKind.Parameter)
    {
        Attributes = Add(attributes);
        Modifier = Add(modifier);
        Identifiers = Add(identifiers);
        ColonToken = Add(colonToken);
        Type = AddOptional(type);
        EqualsToken = Add(equalsToken);
        DefaultValue = AddOptional(defaultValue);
    }

    /// <summary>Attribute lists before the declaration (LANG-172).</summary>
    public SyntaxList<AttributeListSyntax> Attributes { get; }

    /// <summary><c>const</c>, <c>var</c>, or an identifier with contextual kind <see cref="SyntaxKind.OutKeyword"/>.</summary>
    public SyntaxToken? Modifier { get; }

    public SeparatedSyntaxList<IdentifierNameSyntax> Identifiers { get; }

    public SyntaxToken? ColonToken { get; }

    /// <summary>Any type syntax (<c>array of T</c> is an open array); the binder restricts it to paramType.</summary>
    public TypeSyntax? Type { get; }

    public SyntaxToken? EqualsToken { get; }

    public ExpressionSyntax? DefaultValue { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitParameter(this);
}

/// <summary>
/// <c>routineHeading ; { routineDirective ; } [ block ; ]</c>. The body is absent after <c>forward</c> or <c>external</c>
/// and in an interface section.
/// </summary>
public sealed class RoutineDeclarationSyntax : SyntaxNode
{
    internal RoutineDeclarationSyntax(SyntaxList<AttributeListSyntax> attributes,
                                      RoutineHeadingSyntax heading, SyntaxToken? semicolonToken, SyntaxList<DirectiveSyntax> directives,
                                      BlockSyntax? block, SyntaxToken? bodySemicolonToken)
        : base(SyntaxKind.RoutineDeclaration)
    {
        Attributes = Add(attributes);
        Heading = Add(heading);
        SemicolonToken = Add(semicolonToken);
        Directives = Add(directives);
        Block = AddOptional(block);
        BodySemicolonToken = Add(bodySemicolonToken);
    }

    /// <summary>Attribute lists before the declaration (LANG-172).</summary>
    public SyntaxList<AttributeListSyntax> Attributes { get; }

    public RoutineHeadingSyntax Heading { get; }

    /// <summary>The <c>;</c> after the heading; null when a directive follows directly (LANG-160).</summary>
    public SyntaxToken? SemicolonToken { get; }

    public SyntaxList<DirectiveSyntax> Directives { get; }

    public BlockSyntax? Block { get; }

    /// <summary>The <c>;</c> after the body.</summary>
    public SyntaxToken? BodySemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitRoutineDeclaration(this);
}

/// <summary>
/// <c>[ class ] ( procedure | function | constructor | destructor | operator ) routineName [ formalParams ] [ : resultType ]</c>;
/// <c>routineName = ident | typeNamePath . ident</c> (a <see cref="QualifiedNameSyntax"/> for method implementations).
/// </summary>
public sealed class RoutineHeadingSyntax : SyntaxNode
{
    internal RoutineHeadingSyntax(SyntaxToken? classKeyword, SyntaxToken keyword, NameSyntax name, TypeParameterListSyntax? typeParameterList,
                                  ParameterListSyntax? parameterList,
                                  SyntaxToken? colonToken, TypeSyntax? resultType)
        : base(SyntaxKind.RoutineHeading)
    {
        ClassKeyword = Add(classKeyword);
        Keyword = Add(keyword);
        Name = Add(name);
        TypeParameterList = AddOptional(typeParameterList);
        ParameterList = AddOptional(parameterList);
        ColonToken = Add(colonToken);
        ResultType = AddOptional(resultType);
    }

    public SyntaxToken? ClassKeyword { get; }

    /// <summary><c>procedure</c>, <c>function</c>, <c>constructor</c>, <c>destructor</c>, or the contextual <c>operator</c>.</summary>
    public SyntaxToken Keyword { get; }

    public NameSyntax Name { get; }

    /// <summary>Type parameters of a generic method (LANG-701), on the last name segment.</summary>
    public TypeParameterListSyntax? TypeParameterList { get; }

    public ParameterListSyntax? ParameterList { get; }

    public SyntaxToken? ColonToken { get; }

    /// <summary>Absent for procedures and for functions completing a forward / interface declaration.</summary>
    public TypeSyntax? ResultType { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitRoutineHeading(this);
}

/// <summary>
/// A routine or method directive with its <c>;</c>: <c>overload</c>, <c>inline</c>, a calling convention, a hint,
/// <c>external [ lib [ name n ] [ delayed ] ]</c>, <c>public name n</c>, <c>message id</c>, <c>virtual</c>, ….
/// </summary>
public class DirectiveSyntax : SyntaxNode
{
    internal DirectiveSyntax(SyntaxToken keyword, ExpressionSyntax? value, SyntaxToken? nameKeyword, ExpressionSyntax? nameValue,
                             SyntaxToken? delayedKeyword, SyntaxToken? semicolonToken, SyntaxKind kind = SyntaxKind.Directive)
        : base(kind)
    {
        Keyword = Add(keyword);
        Value = AddOptional(value);
        NameKeyword = Add(nameKeyword);
        NameValue = AddOptional(nameValue);
        DelayedKeyword = Add(delayedKeyword);
        SemicolonToken = Add(semicolonToken);
    }

    /// <summary>The <c>inline</c> keyword or an identifier whose <see cref="SyntaxToken.ContextualKind"/> is the directive.</summary>
    public SyntaxToken Keyword { get; }

    /// <summary>The library of <c>external</c>, the id of <c>message</c>, the text of <c>deprecated</c>.</summary>
    public ExpressionSyntax? Value { get; }

    /// <summary>Contextual <c>name</c> of <c>external … name</c> / <c>public name</c>.</summary>
    public SyntaxToken? NameKeyword { get; }

    public ExpressionSyntax? NameValue { get; }

    public SyntaxToken? DelayedKeyword { get; }

    /// <summary>Null when another directive follows directly (LANG-160).</summary>
    public SyntaxToken? SemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitDirective(this);
}

/// <summary>
/// <c>dispid constExpr ;</c> on a method of a dispatch interface or of an interface (com-interfaces.md LANG-694):
/// <see cref="DirectiveSyntax.Value"/> is the number.
/// </summary>
public sealed class DispIdDirectiveSyntax : DirectiveSyntax
{
    internal DispIdDirectiveSyntax(SyntaxToken keyword, ExpressionSyntax value, SyntaxToken? semicolonToken)
        : base(keyword, value, null, null, null, semicolonToken, SyntaxKind.DispIdDirective)
    {
    }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitDispIdDirective(this);
}
