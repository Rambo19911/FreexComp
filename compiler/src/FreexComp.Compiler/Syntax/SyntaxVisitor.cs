namespace FreexComp.Compiler.Syntax;

/// <summary>
/// Double-dispatch visitor (api.md §4): one virtual <c>VisitXxx</c> per node class, each defaulting to
/// <see cref="DefaultVisit"/>. Node classes added later add their method here.
/// </summary>
public abstract class SyntaxVisitor<TResult>
{
    /// <summary><paramref name="node"/>.Accept(this), or <c>default</c> for null.</summary>
    public virtual TResult Visit(SyntaxNode? node) => node is null ? default! : node.Accept(this);

    public virtual TResult DefaultVisit(SyntaxNode node) => default!;

    public virtual TResult VisitIdentifierName(IdentifierNameSyntax node) => DefaultVisit(node);

    public virtual TResult VisitQualifiedName(QualifiedNameSyntax node) => DefaultVisit(node);

    public virtual TResult VisitProgram(ProgramSyntax node) => DefaultVisit(node);

    public virtual TResult VisitProgramParameterList(ProgramParameterListSyntax node) => DefaultVisit(node);

    public virtual TResult VisitUnit(UnitSyntax node) => DefaultVisit(node);

    public virtual TResult VisitIncludeFile(IncludeFileSyntax node) => DefaultVisit(node);

    public virtual TResult VisitUsesClause(UsesClauseSyntax node) => DefaultVisit(node);

    public virtual TResult VisitUsedUnit(UsedUnitSyntax node) => DefaultVisit(node);

    public virtual TResult VisitInterfaceSection(InterfaceSectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitImplementationSection(ImplementationSectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitInitializationSection(InitializationSectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitFinalizationSection(FinalizationSectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitHintDirective(HintDirectiveSyntax node) => DefaultVisit(node);

    public virtual TResult VisitBlock(BlockSyntax node) => DefaultVisit(node);

    public virtual TResult VisitCompoundStatement(CompoundStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitEmptyStatement(EmptyStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitLabelSection(LabelSectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitConstSection(ConstSectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitConstDeclaration(ConstDeclarationSyntax node) => DefaultVisit(node);

    public virtual TResult VisitTypedConstantAggregate(TypedConstantAggregateSyntax node) => DefaultVisit(node);

    public virtual TResult VisitRecordConstantField(RecordConstantFieldSyntax node) => DefaultVisit(node);

    public virtual TResult VisitTypeSection(TypeSectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitTypeDeclaration(TypeDeclarationSyntax node) => DefaultVisit(node);

    public virtual TResult VisitVarSection(VarSectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitVarDeclaration(VarDeclarationSyntax node) => DefaultVisit(node);

    public virtual TResult VisitNamedType(NamedTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitStringType(StringTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitLiteralExpression(LiteralExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitBinaryExpression(BinaryExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitUnaryExpression(UnaryExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitParenthesizedExpression(ParenthesizedExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitEnumType(EnumTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitEnumMember(EnumMemberSyntax node) => DefaultVisit(node);

    public virtual TResult VisitSubrangeType(SubrangeTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitPointerType(PointerTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitArrayType(ArrayTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitSetType(SetTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitFileType(FileTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitRecordType(RecordTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitFieldDeclaration(FieldDeclarationSyntax node) => DefaultVisit(node);

    public virtual TResult VisitVariantPart(VariantPartSyntax node) => DefaultVisit(node);

    public virtual TResult VisitVariant(VariantSyntax node) => DefaultVisit(node);

    public virtual TResult VisitProceduralType(ProceduralTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitCallingConvention(CallingConventionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitClassRefType(ClassRefTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitParameterList(ParameterListSyntax node) => DefaultVisit(node);

    public virtual TResult VisitParameter(ParameterSyntax node) => DefaultVisit(node);
}

/// <summary>Depth-first walk over every node, token and trivia piece in source order.</summary>
public abstract class SyntaxWalker : SyntaxVisitor<object?>
{
    /// <summary>Visits the children: nodes through <see cref="SyntaxVisitor{TResult}.Visit"/>, tokens through <see cref="VisitToken"/>.</summary>
    public override object? DefaultVisit(SyntaxNode node)
    {
        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode() is { } childNode)
            {
                Visit(childNode);
            }
            else
            {
                VisitToken(child.AsToken());
            }
        }

        return null;
    }

    /// <summary>Visits the leading, then the trailing trivia.</summary>
    public virtual void VisitToken(SyntaxToken token)
    {
        foreach (var trivia in token.LeadingTrivia)
        {
            VisitTrivia(trivia);
        }

        foreach (var trivia in token.TrailingTrivia)
        {
            VisitTrivia(trivia);
        }
    }

    public virtual void VisitTrivia(SyntaxTrivia trivia)
    {
    }
}
