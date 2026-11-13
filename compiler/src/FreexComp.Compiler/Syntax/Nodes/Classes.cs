namespace FreexComp.Compiler.Syntax;

// grammar.md §4: classes, interfaces, helpers and the members they share with records (C-018).

/// <summary>
/// <c>class [ abstract | sealed ] [ heritage ] [ { classMember } end ]</c>. Without body and heritage it is a forward
/// declaration (LANG-155); with heritage but no body an empty class (<c>EFoo = class(Exception);</c>).
/// </summary>
public sealed class ClassTypeSyntax : TypeSyntax
{
    internal ClassTypeSyntax(SyntaxToken classKeyword, SyntaxToken? modifier, HeritageSyntax? heritage, SyntaxList<SyntaxNode> members,
                             SyntaxToken? endKeyword)
        : base(SyntaxKind.ClassType)
    {
        ClassKeyword = Add(classKeyword);
        Modifier = Add(modifier);
        Heritage = AddOptional(heritage);
        Members = Add(members);
        EndKeyword = Add(endKeyword);
    }

    public SyntaxToken ClassKeyword { get; }

    /// <summary>Contextual <c>abstract</c> or <c>sealed</c>.</summary>
    public SyntaxToken? Modifier { get; }

    public HeritageSyntax? Heritage { get; }

    /// <summary>
    /// Members of the default section (before the first visibility keyword), then one
    /// <see cref="VisibilitySectionSyntax"/> per visibility keyword. Members are <see cref="MemberDeclarationSyntax"/>
    /// nodes, <see cref="ConstSectionSyntax"/> or <see cref="TypeSectionSyntax"/>.
    /// </summary>
    public SyntaxList<SyntaxNode> Members { get; }

    /// <summary>Null when there is no body.</summary>
    public SyntaxToken? EndKeyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitClassType(this);
}

/// <summary><c>( typeId { , typeId } )</c> after <c>class</c>, <c>interface</c> or <c>helper</c>.</summary>
public sealed class HeritageSyntax : SyntaxNode
{
    internal HeritageSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<TypeSyntax> types, SyntaxToken closeParenToken)
        : base(SyntaxKind.Heritage)
    {
        OpenParenToken = Add(openParenToken);
        Types = Add(types);
        CloseParenToken = Add(closeParenToken);
    }

    public SyntaxToken OpenParenToken { get; }

    public SeparatedSyntaxList<TypeSyntax> Types { get; }

    public SyntaxToken CloseParenToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitHeritage(this);
}

/// <summary>
/// <c>interface [ ( typeId ) ] [ "[" constExpr "]" ] { methodDecl | propertyDecl } end</c>; without anything after the
/// keyword it is a forward declaration. Members are parsed like class members; the binder rejects the others.
/// </summary>
public sealed class InterfaceTypeSyntax : TypeSyntax
{
    internal InterfaceTypeSyntax(SyntaxToken interfaceKeyword, HeritageSyntax? heritage, GuidSyntax? guid, SyntaxList<SyntaxNode> members,
                                 SyntaxToken? endKeyword)
        : base(SyntaxKind.InterfaceType)
    {
        InterfaceKeyword = Add(interfaceKeyword);
        Heritage = AddOptional(heritage);
        Guid = AddOptional(guid);
        Members = Add(members);
        EndKeyword = Add(endKeyword);
    }

    public SyntaxToken InterfaceKeyword { get; }

    public HeritageSyntax? Heritage { get; }

    public GuidSyntax? Guid { get; }

    public SyntaxList<SyntaxNode> Members { get; }

    /// <summary>Null for a forward declaration.</summary>
    public SyntaxToken? EndKeyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitInterfaceType(this);
}

/// <summary><c>"[" constExpr "]"</c>: the interface GUID (LANG-660).</summary>
public sealed class GuidSyntax : SyntaxNode
{
    internal GuidSyntax(SyntaxToken openBracketToken, ExpressionSyntax value, SyntaxToken closeBracketToken)
        : base(SyntaxKind.Guid)
    {
        OpenBracketToken = Add(openBracketToken);
        Value = Add(value);
        CloseBracketToken = Add(closeBracketToken);
    }

    public SyntaxToken OpenBracketToken { get; }

    public ExpressionSyntax Value { get; }

    public SyntaxToken CloseBracketToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitGuid(this);
}

/// <summary><c>( class | record ) helper [ ( typeId ) ] for typeId { classMember } end</c> (LANG-750).</summary>
public sealed class HelperTypeSyntax : TypeSyntax
{
    internal HelperTypeSyntax(SyntaxToken keyword, SyntaxToken helperKeyword, HeritageSyntax? heritage, SyntaxToken forKeyword,
                              TypeSyntax extendedType, SyntaxList<SyntaxNode> members, SyntaxToken endKeyword)
        : base(SyntaxKind.HelperType)
    {
        Keyword = Add(keyword);
        HelperKeyword = Add(helperKeyword);
        Heritage = AddOptional(heritage);
        ForKeyword = Add(forKeyword);
        ExtendedType = Add(extendedType);
        Members = Add(members);
        EndKeyword = Add(endKeyword);
    }

    /// <summary><c>class</c> or <c>record</c>.</summary>
    public SyntaxToken Keyword { get; }

    /// <summary>Contextual <c>helper</c>.</summary>
    public SyntaxToken HelperKeyword { get; }

    public HeritageSyntax? Heritage { get; }

    public SyntaxToken ForKeyword { get; }

    public TypeSyntax ExtendedType { get; }

    /// <summary>As <see cref="ClassTypeSyntax.Members"/>.</summary>
    public SyntaxList<SyntaxNode> Members { get; }

    public SyntaxToken EndKeyword { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitHelperType(this);
}

/// <summary><c>[ strict ] ( private | protected ) | public | published</c> and the members up to the next visibility keyword.</summary>
public sealed class VisibilitySectionSyntax : SyntaxNode
{
    internal VisibilitySectionSyntax(SyntaxToken? strictKeyword, SyntaxToken visibilityKeyword, SyntaxList<SyntaxNode> members)
        : base(SyntaxKind.VisibilitySection)
    {
        StrictKeyword = Add(strictKeyword);
        VisibilityKeyword = Add(visibilityKeyword);
        Members = Add(members);
    }

    /// <summary>Contextual <c>strict</c>.</summary>
    public SyntaxToken? StrictKeyword { get; }

    /// <summary>An identifier whose <see cref="SyntaxToken.ContextualKind"/> is the visibility keyword.</summary>
    public SyntaxToken VisibilityKeyword { get; }

    /// <summary>As the default-section members of <see cref="ClassTypeSyntax.Members"/>.</summary>
    public SyntaxList<SyntaxNode> Members { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitVisibilitySection(this);
}

/// <summary><c>[ class ] methodHeading ; { methodDirective ; }</c>: a method declaration in a class, record, interface or helper.</summary>
public sealed class MethodDeclarationSyntax : MemberDeclarationSyntax
{
    internal MethodDeclarationSyntax(RoutineHeadingSyntax heading, SyntaxToken? semicolonToken, SyntaxList<DirectiveSyntax> directives)
        : base(SyntaxKind.MethodDeclaration)
    {
        Heading = Add(heading);
        SemicolonToken = Add(semicolonToken);
        Directives = Add(directives);
    }

    public RoutineHeadingSyntax Heading { get; }

    /// <summary>The <c>;</c> after the heading; null when a directive follows directly (LANG-160).</summary>
    public SyntaxToken? SemicolonToken { get; }

    public SyntaxList<DirectiveSyntax> Directives { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitMethodDeclaration(this);
}

/// <summary><c>( procedure | function ) qualIdent = ident ;</c>: maps an interface method to a method (LANG-661).</summary>
public sealed class MethodResolutionSyntax : MemberDeclarationSyntax
{
    internal MethodResolutionSyntax(SyntaxToken keyword, NameSyntax interfaceMethod, SyntaxToken equalsToken, IdentifierNameSyntax target,
                                    SyntaxToken semicolonToken)
        : base(SyntaxKind.MethodResolution)
    {
        Keyword = Add(keyword);
        InterfaceMethod = Add(interfaceMethod);
        EqualsToken = Add(equalsToken);
        Target = Add(target);
        SemicolonToken = Add(semicolonToken);
    }

    /// <summary><c>procedure</c> or <c>function</c>.</summary>
    public SyntaxToken Keyword { get; }

    /// <summary><c>IFoo.Bar</c>.</summary>
    public NameSyntax InterfaceMethod { get; }

    public SyntaxToken EqualsToken { get; }

    public IdentifierNameSyntax Target { get; }

    public SyntaxToken SemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitMethodResolution(this);
}

/// <summary>
/// <c>[ class ] property ident [ "[" formalParamList "]" ] [ : type ] { specifier } ; [ default ; ]</c>; without type and
/// specifiers it redeclares (promotes) an inherited property (LANG-613).
/// </summary>
public sealed class PropertyDeclarationSyntax : MemberDeclarationSyntax
{
    internal PropertyDeclarationSyntax(SyntaxToken? classKeyword, SyntaxToken propertyKeyword, SyntaxToken identifier,
                                       SyntaxToken? openBracketToken, SeparatedSyntaxList<ParameterSyntax> parameters,
                                       SyntaxToken? closeBracketToken, SyntaxToken? colonToken, TypeSyntax? type,
                                       SyntaxList<PropertySpecifierSyntax> specifiers, SyntaxToken semicolonToken,
                                       SyntaxToken? defaultKeyword, SyntaxToken? defaultSemicolonToken)
        : base(SyntaxKind.PropertyDeclaration)
    {
        ClassKeyword = Add(classKeyword);
        PropertyKeyword = Add(propertyKeyword);
        Identifier = Add(identifier);
        OpenBracketToken = Add(openBracketToken);
        Parameters = Add(parameters);
        CloseBracketToken = Add(closeBracketToken);
        ColonToken = Add(colonToken);
        Type = AddOptional(type);
        Specifiers = Add(specifiers);
        SemicolonToken = Add(semicolonToken);
        DefaultKeyword = Add(defaultKeyword);
        DefaultSemicolonToken = Add(defaultSemicolonToken);
    }

    public SyntaxToken? ClassKeyword { get; }

    public SyntaxToken PropertyKeyword { get; }

    public SyntaxToken Identifier { get; }

    public SyntaxToken? OpenBracketToken { get; }

    /// <summary>Index parameters of an array property, separated by <c>;</c>.</summary>
    public SeparatedSyntaxList<ParameterSyntax> Parameters { get; }

    public SyntaxToken? CloseBracketToken { get; }

    public SyntaxToken? ColonToken { get; }

    /// <summary>Any type syntax (the binder restricts it to typeId and <c>string</c>).</summary>
    public TypeSyntax? Type { get; }

    public SyntaxList<PropertySpecifierSyntax> Specifiers { get; }

    public SyntaxToken SemicolonToken { get; }

    /// <summary>Contextual <c>default</c> of <c>; default;</c> (default array property, LANG-642).</summary>
    public SyntaxToken? DefaultKeyword { get; }

    public SyntaxToken? DefaultSemicolonToken { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitPropertyDeclaration(this);
}

/// <summary>
/// One property specifier: <c>index c</c>, <c>read path</c>, <c>write path</c>, <c>stored x</c>, <c>default c</c>,
/// <c>nodefault</c>, <c>implements T { , T }</c>. Paths and type names are <see cref="NameSyntax"/> values.
/// </summary>
public sealed class PropertySpecifierSyntax : SyntaxNode
{
    internal PropertySpecifierSyntax(SyntaxToken keyword, SeparatedSyntaxList<ExpressionSyntax> values)
        : base(SyntaxKind.PropertySpecifier)
    {
        Keyword = Add(keyword);
        Values = Add(values);
    }

    /// <summary>An identifier whose <see cref="SyntaxToken.ContextualKind"/> is the specifier keyword.</summary>
    public SyntaxToken Keyword { get; }

    /// <summary>Empty for <c>nodefault</c>; several only for <c>implements</c>.</summary>
    public SeparatedSyntaxList<ExpressionSyntax> Values { get; }

    /// <summary>The first value, or null.</summary>
    public ExpressionSyntax? Value => Values.Count > 0 ? Values[0] : null;

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitPropertySpecifier(this);
}

/// <summary><c>[ class ] var { fieldDecl }</c> inside a class, record or helper.</summary>
public sealed class ClassVarSectionSyntax : MemberDeclarationSyntax
{
    internal ClassVarSectionSyntax(SyntaxToken? classKeyword, SyntaxToken varKeyword, SyntaxList<FieldDeclarationSyntax> fields)
        : base(SyntaxKind.ClassVarSection)
    {
        ClassKeyword = Add(classKeyword);
        VarKeyword = Add(varKeyword);
        Fields = Add(fields);
    }

    /// <summary>Null for a plain <c>var</c> section (instance fields).</summary>
    public SyntaxToken? ClassKeyword { get; }

    public SyntaxToken VarKeyword { get; }

    public SyntaxList<FieldDeclarationSyntax> Fields { get; }

    public override TResult Accept<TResult>(SyntaxVisitor<TResult> visitor) => visitor.VisitClassVarSection(this);
}
