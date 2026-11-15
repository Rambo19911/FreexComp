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

    public virtual TResult VisitFragment(FragmentSyntax node) => DefaultVisit(node);

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

    public virtual TResult VisitRoutineDeclaration(RoutineDeclarationSyntax node) => DefaultVisit(node);

    public virtual TResult VisitRoutineHeading(RoutineHeadingSyntax node) => DefaultVisit(node);

    public virtual TResult VisitDirective(DirectiveSyntax node) => DefaultVisit(node);

    public virtual TResult VisitMemberAccessExpression(MemberAccessExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitElementAccessExpression(ElementAccessExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitDereferenceExpression(DereferenceExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitInvocationExpression(InvocationExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitArgumentList(ArgumentListSyntax node) => DefaultVisit(node);

    public virtual TResult VisitArgument(ArgumentSyntax node) => DefaultVisit(node);

    public virtual TResult VisitAddressOfExpression(AddressOfExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitSetConstructor(SetConstructorSyntax node) => DefaultVisit(node);

    public virtual TResult VisitSetElement(SetElementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitInheritedExpression(InheritedExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitStringCastExpression(StringCastExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitPredefinedTypeExpression(PredefinedTypeExpressionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitAssignmentStatement(AssignmentStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitExpressionStatement(ExpressionStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitInheritedStatement(InheritedStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitGotoStatement(GotoStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitLabeledStatement(LabeledStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitRaiseStatement(RaiseStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitIfStatement(IfStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitCaseStatement(CaseStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitCaseArm(CaseArmSyntax node) => DefaultVisit(node);

    public virtual TResult VisitCaseLabel(CaseLabelSyntax node) => DefaultVisit(node);

    public virtual TResult VisitRepeatStatement(RepeatStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitWhileStatement(WhileStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitForStatement(ForStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitForInStatement(ForInStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitWithStatement(WithStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitTryFinallyStatement(TryFinallyStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitTryExceptStatement(TryExceptStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitExceptionHandler(ExceptionHandlerSyntax node) => DefaultVisit(node);

    public virtual TResult VisitInlineVarStatement(InlineVarStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitInlineConstStatement(InlineConstStatementSyntax node) => DefaultVisit(node);

    public virtual TResult VisitClassType(ClassTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitHeritage(HeritageSyntax node) => DefaultVisit(node);

    public virtual TResult VisitInterfaceType(InterfaceTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitGuid(GuidSyntax node) => DefaultVisit(node);

    public virtual TResult VisitHelperType(HelperTypeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitVisibilitySection(VisibilitySectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitMethodDeclaration(MethodDeclarationSyntax node) => DefaultVisit(node);

    public virtual TResult VisitMethodResolution(MethodResolutionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitPropertyDeclaration(PropertyDeclarationSyntax node) => DefaultVisit(node);

    public virtual TResult VisitPropertySpecifier(PropertySpecifierSyntax node) => DefaultVisit(node);

    public virtual TResult VisitClassVarSection(ClassVarSectionSyntax node) => DefaultVisit(node);

    public virtual TResult VisitGenericName(GenericNameSyntax node) => DefaultVisit(node);

    public virtual TResult VisitTypeArgumentList(TypeArgumentListSyntax node) => DefaultVisit(node);

    public virtual TResult VisitTypeParameterList(TypeParameterListSyntax node) => DefaultVisit(node);

    public virtual TResult VisitTypeParameter(TypeParameterSyntax node) => DefaultVisit(node);

    public virtual TResult VisitKeywordConstraint(KeywordConstraintSyntax node) => DefaultVisit(node);

    public virtual TResult VisitAttributeList(AttributeListSyntax node) => DefaultVisit(node);

    public virtual TResult VisitAttribute(AttributeSyntax node) => DefaultVisit(node);

    public virtual TResult VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => DefaultVisit(node);
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
